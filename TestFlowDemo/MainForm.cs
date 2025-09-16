using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Windows.Forms;
using WorkflowCore.Interface;
using ZL.DeviceLib;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Storage;
using ZL.WorkflowLib;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Workflow;

namespace TestFlowDemo
{
    public partial class MainForm : Form
    {
        private IWorkflowHost _host;
        private IDatabaseService _db;
        private DeviceFactory _factory;
        private TestRunner _runner;

        private string _currentRunId;     // 当前运行中的工作流 Id（为空表示空闲）
        private bool _isStarting;         // 启动防抖
        private bool _configReady;        // Devices.json 是否加载成功（影响是否允许启动）
        private Timer _poolTimer;
        private PoolMonitor _poolMonitor;
        public MainForm()
        {
            InitializeComponent();
            _poolTimer = new Timer();
            _poolTimer.Interval = 2000; // 2秒刷新一次
            _poolTimer.Tick += (s, ev) => UpdatePoolStatus();
            _poolTimer.Start();

            _poolMonitor = new PoolMonitor("Logs", 30); // 每 30 秒记录一次

            // 订阅 UI 事件总线（来自 StepBodies 内部发布）
            UiEventBus.Log += OnUiLog;
            UiEventBus.WorkflowCompleted += OnUiWorkflowCompleted;

        }

        // ====== 初始化阶段 ======

        private void MainForm_Load(object sender, EventArgs e)
        {
            // UI 初始状态
            lblStatus.Text = "状态：空闲";
            this.AcceptButton = btnStart;  // 回车=开始（可选）
            UpdateButtonStates();

            InitDbAndParams();
            // 初始化配置与 Host
            InitConfig();       // 明确初始化 ConfigManager（加载 Devices.json）
            InitWorkflowHost(); // 启动 WorkflowCore Host（常驻）
            UpdateButtonStates();
        }
        void InitDbAndParams()
        {
            string dbPath = "test_demo.db";
            string reportDir = "Reports";
            // 初始化数据库（注册内置 sqlite + 允许插件扩展）
            var registry = new InfrastructureRegistry();
            registry.RegisterDatabase("sqlite", opts => new DatabaseService(
                DbPathUtil.ResolveSqlitePath(opts?.ConnectionString, opts?.DefaultDbPath ?? dbPath, AppDomain.CurrentDomain.BaseDirectory)));
            try { registry.LoadPlugins(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins")); } catch { }

            var dbCfg = ReadInfraDb();
            var dbOptions = new DbOptions { ConnectionString = dbCfg.ConnectionString, DefaultDbPath = dbPath, Settings = dbCfg.Settings ?? new System.Collections.Generic.Dictionary<string, object>() };
            var providerType = string.IsNullOrWhiteSpace(dbCfg.Provider) ? "sqlite" : dbCfg.Provider;
            if (providerType.Equals("database", StringComparison.OrdinalIgnoreCase))
            {
                AddLog("[WARN] infrastructure.database.Type=\"database\" 含糊，已按 sqlite 处理。建议改为 Type=\"sqlite\" 或插件提供者名。");
                providerType = "sqlite";
            }
            _db = registry.CreateDatabase(providerType, dbOptions);
            DeviceServices.Db = _db;

            // 初始化设备工厂
            _factory = new DeviceFactory(dbPath, reportDir);
            DeviceServices.Factory = _factory;

            // 加载外部插件（可选）：将自定义设备驱动 DLL 放置于 程序目录/Plugins 下即可自动注册
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var pluginDir = Path.Combine(baseDir, "Plugins");
                DeviceServices.Factory.LoadPlugins(pluginDir);
                AddLog("[Init] 插件目录加载完成: " + pluginDir);
            }
            catch (Exception ex)
            {
                AddLog("[Init] 插件加载异常：" + ex.Message);
            }

            // 初始化参数注入器（缓存 + TTL 300s）
            WorkflowServices.ParamInjector = new ParamInjector(_db, 300, "L1", "ST01");
            WorkflowServices.ParamInjector.PreloadAll(); // 启动时加载全部 status=1 参数

            // 初始化子流程库
            WorkflowServices.Subflows = new SubflowRegistry();
            WorkflowServices.Subflows.LoadFromDirectory(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows", "Subflows")
            );

            UiEventBus.PublishLog("[Init] 全局服务初始化完成");
        }
        // 统一改用 DbPathUtil，保留方法名避免调用方改动
        private static string ResolveDbPath(string conn, string fallback)
            => DbPathUtil.ResolveSqlitePath(conn, fallback, AppDomain.CurrentDomain.BaseDirectory);
        private static (string Provider, string ConnectionString, System.Collections.Generic.Dictionary<string, object> Settings) ReadInfraDb()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = System.IO.Path.Combine(baseDir, "infrastructure.json");
                if (!System.IO.File.Exists(path)) return (null, null, null);
                var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
                var infra = root["Infrastructure"] as Newtonsoft.Json.Linq.JObject; if (infra == null) return (null, null, null);
                if (infra.TryGetValue("database", StringComparison.OrdinalIgnoreCase, out var dbTok) && dbTok is Newtonsoft.Json.Linq.JObject db)
                {
                    var type = db.Value<string>("Type");
                    var conn = db.Value<string>("ConnectionString");
                    var settings = db["Settings"] as Newtonsoft.Json.Linq.JObject;
                    return (type, conn, settings != null ? settings.ToObject<System.Collections.Generic.Dictionary<string, object>>() : null);
                }
            }
            catch { }
            return (null, null, null);
        }
        /// <summary>
        /// 显式初始化配置中心（此处只加载公共设备池 Devices.json）
        /// </summary>
        private void InitConfig()
        {
            try
            {
                // 第一次访问 Instance 会加载 Devices.json 并缓存
                var dummy = ConfigManager.Instance;
                _configReady = true;
                AddLog("[Init] 设备池加载完成（Devices.json）");
            }
            catch (Exception ex)
            {
                _configReady = false;
                AddLog("[Init] 加载设备池失败：" + ex.Message);
                MessageBox.Show("设备配置（Devices.json）加载失败：\n" + ex.Message,
                                "初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 启动 WorkflowCore Host（常驻）
        /// </summary>
        private void InitWorkflowHost()
        {
            var services = new ServiceCollection()
                .AddLogging(cfg => cfg.AddConsole())
                .AddWorkflow(); // 不使用不存在的生命周期扩展

            var provider = services.BuildServiceProvider();
            _host = provider.GetService<IWorkflowHost>();
            _host.RegisterWorkflow<DynamicLoopWorkflow, FlowData>();
            _host.Start();

            _runner = new TestRunner(_host);

            AddLog("[Init] WorkflowCore Host 已启动");
        }

        // ====== UI 交互与状态 ======

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                if (_poolTimer != null)
                {
                    _poolTimer.Stop();        // 先停止定时器，避免窗体关闭后仍然触发 Tick 事件
                    _poolTimer.Dispose();     // 释放定时器底层的 Win32 句柄，防止资源泄漏
                }
            }
            catch { }
            try { _poolMonitor?.Dispose(); } catch { }
            try { _host?.Stop(); } catch { }
            try { _factory?.Dispose(); } catch { }
        }

        private void txtBarcode_TextChanged(object sender, EventArgs e)
        {
            UpdateButtonStates();
        }

        private bool IsRunning()
        {
            return !string.IsNullOrEmpty(_currentRunId);
        }

        /// <summary>
        /// 按钮可用性：Start 仅受 条码是否为空 + 是否空闲 + 配置是否就绪 影响；
        /// Stop 仅在运行时可用。
        /// </summary>
        private void UpdateButtonStates()
        {
            string barcode = (txtBarcode.Text ?? "").Trim();
            bool hasBarcode = !string.IsNullOrEmpty(barcode);

            btnStart.Enabled = _configReady && hasBarcode && !IsRunning() && !_isStarting;
            btnStop.Enabled = IsRunning();
        }

        /// <summary>
        /// 条码→型号解析（可按需换成更严格的规则/正则）
        /// </summary>
        private bool TryParseModel(string barcode, out string model, out string error)
        {
            model = null;
            error = null;

            if (string.IsNullOrWhiteSpace(barcode))
            {
                error = "条码不能为空";
                return false;
            }

            int idx = barcode.IndexOf('-');
            model = (idx > 0) ? barcode.Substring(0, idx) : barcode.Trim();

            if (string.IsNullOrWhiteSpace(model))
            {
                error = "无法从条码解析出型号";
                return false;
            }
            return true;
        }

        // ====== 按钮事件 ======

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (_isStarting) return;
            _isStarting = true;
            UpdateButtonStates();

            try
            {
                if (!_configReady)
                {
                    MessageBox.Show("设备配置尚未就绪，请检查 Devices.json。", "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (IsRunning())
                {
                    AddLog("当前已有测试在运行，请先停止或等待完成。");
                    return;
                }

                string barcode = (txtBarcode.Text ?? "").Trim();
                string model, parseErr;
                if (!TryParseModel(barcode, out model, out parseErr))
                {
                    AddLog("启动失败：" + parseErr);
                    MessageBox.Show(parseErr, "无法启动", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 校验该型号流程是否存在（按需加载到缓存）
                try
                {
                    ConfigManager.Instance.GetFlowConfig(model);
                }
                catch (Exception exCfg)
                {
                    var msg = "未找到该型号的流程配置（Flows/" + model + ".json）：\n" + exCfg.Message;
                    AddLog("启动失败：" + msg);
                    MessageBox.Show(msg, "配置缺失", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 启动一次测试（TestRunner 内部负责把 SN 写入 FlowData.Sn，并做超时守护）
                _currentRunId = _runner.RunTest(model, barcode);
                lblStatus.Text = "正在测试: " + model + ", RunId=" + _currentRunId;
                AddLog("测试启动: " + model + ", Barcode=" + barcode + ", RunId=" + _currentRunId);
            }
            catch (Exception ex)
            {
                AddLog("启动异常：" + ex.Message);
                MessageBox.Show("启动异常：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // 未设置 _currentRunId，仍可再次尝试
            }
            finally
            {
                _isStarting = false;
                UpdateButtonStates(); // 始终恢复按钮状态
            }
        }
        private void btnStop_Click(object sender, EventArgs e)
        {
            try
            {
                if (IsRunning())
                {
                    _host.TerminateWorkflow(_currentRunId).Wait();
                    AddLog($"测试已终止: RunId={_currentRunId}");
                    lblStatus.Text = "已停止";
                    _runner.CancelTimeout(_currentRunId);   // 🔑 取消超时监控
                    _currentRunId = null;
                }
            }
            catch (Exception ex)
            {
                AddLog("停止异常：" + ex.Message);
                _currentRunId = null;
            }
            finally
            {
                UpdateButtonStates();
            }
        }
        private void btnClearLog_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
            AddLog("[System] 日志已清空");
        }

        private void btnRefreshParams_Click(object sender, EventArgs e)
        {
            WorkflowServices.ParamInjector.PreloadAll();
            UiEventBus.PublishLog("[Param] 参数缓存已刷新（全量 status=1）");
        }

        private void UpdatePoolStatus()
        {
            var stats = StepResultPool.Instance.GetStats();
            lblPoolStatus.Text =
                $"[对象池] 容量={stats.Capacity}, 空闲={stats.IdleCount}, " +
                $"命中={stats.Hits}, 未命中={stats.Misses}, 命中率={stats.HitRate:P1}";
        }


        // ====== UI 事件总线回调（由 StepBodies 内部发布） ======

        private void OnUiLog(string msg)
        {
            try
            {
                // 跨线程安全更新 UI
                if (this.IsHandleCreated)
                    this.BeginInvoke(new Action(delegate { AddLog(msg); }));
            }
            catch { /* ignore when closing */ }
        }
        private void OnUiWorkflowCompleted(string sessionId, string model)
        {
            try
            {
                if (this.IsHandleCreated)
                {
                    this.BeginInvoke(new Action(delegate
                    {
                        AddLog($"[Completed] SessionId={sessionId}, Model={model}（自然结束）");
                        lblStatus.Text = "已完成";
                        _runner.CancelTimeout(_currentRunId);   // 🔑 取消超时监控
                        _currentRunId = null;
                        UpdateButtonStates();
                    }));
                }
            }
            catch { }
        }


        // ====== 日志输出 ======

        private void AddLog(string msg)
        {
            LogHelper.Info(msg);
            // 显示本地时间；数据库仍保存 UTC
            txtLog.AppendText(DateTime.Now.ToString("u") + " " + msg + Environment.NewLine);
        }
    }
}
