using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Events;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Storage;
using ZL.DeviceLib.Utils;
using ZL.Forms.Extension;
using ZL.WorkflowLib;

namespace TestFlowDemo
{
    //public class TestRoot
    //{
    //    public string Model { get; set; }
    //    public List<StepConfig> TestSteps { get; set; }
    //}
    public partial class Frm_SeatTest : Form
    {
        private GridBinder<StepInfo> _stepBinder;
        private IDatabaseService _db;
        private SeatTestRunner _runner;

        private string _currentRunId;     // 当前运行中的工作流 Id（为空表示空闲）
        private bool _isStarting;         // 启动防抖
        private bool _configReady;        // Devices.json 是否加载成功（影响是否允许启动）
        //private System.Windows.Forms.Timer _poolTimer;
        private PoolMonitor _poolMonitor;
        CancellationTokenSource cts = new CancellationTokenSource();
        private readonly BindingList<StepInfo> _stepList = new(); // 自动行级刷新
        private readonly Dictionary<string, StepInfo> _stepLookup = new();
        ConfigManager configManager = null;
        public Frm_SeatTest()
        {
            InitializeComponent();
            //_poolTimer = new System.Windows.Forms.Timer();
            //_poolTimer.Interval = 2000; // 2秒刷新一次
            //_poolTimer.Tick += (s, ev) => UpdatePoolStatus();
            //_poolTimer.Start();

            //_poolMonitor = new PoolMonitor("Logs", 30); // 每 30 秒记录一次

            var columnMap = new Dictionary<string, ColumnConfig>
            {
                { "Name", new ColumnConfig { HeaderText = "步骤", Width = 120 } },
                { "Description", new ColumnConfig { HeaderText = "描述", Width = 150 } },
                { "Target", new ColumnConfig { HeaderText = "设备", Width = 100 } },
                { "Command", new ColumnConfig { HeaderText = "命令", Width = 100 } },
                { "TechRange", new ColumnConfig { HeaderText = "范围", Width = 120 } },
                { "Actual", new ColumnConfig { HeaderText = "实际结果", AutoSize = true } },
                { "Status", new ColumnConfig { HeaderText = "状态", Width = 80 } },
                { "ElapsedMs", new ColumnConfig { HeaderText = "耗时(s)", Width = 90 } }
            };
            // 初始化绑定器，自动生成列
            _stepBinder = new GridBinder<StepInfo>(dgv_StepInfoAndResult, columnMap, true);
            // 样式规则：状态列
            _stepBinder.AddCellStyleRule("Status", value =>
            {
                if (value == null) return null;
                var s = value.ToString();
                if (s == "PASS") return CellStyle.Both(Color.White, Color.Green);
                if (s == "FAIL") return CellStyle.Both(Color.White, Color.Red);
                if (s == "执行中") return CellStyle.Both(Color.Blue, Color.LightYellow);
                return null;
            });
            _stepBinder.SetRows(_stepList);

            DeviceNotifier.DeviceStateChangedEvent += (key, state) => { AddLog($"设备[{key}] 状态 => {state}"); };
            DeviceNotifier.DeviceInfoChangedEvent += (key, info) => { AddLog($"设备[{key}] 信息 => {info}"); };

            // 订阅 UI 事件总线
            TestEvents.StepStarted = stepName => { AddLog($"▶ 开始执行 {stepName}"); };
            TestEvents.StepCompleted = (stepName, success, ms, outputs) => { var resultStr = success ? "PASS" : "FAIL"; AddLog($"✅ {stepName} {resultStr}, 耗时={ms}ms, 数据={string.Join(",", outputs.Select(kv => kv.Key + "=" + kv.Value))}"); };
            TestEvents.StatusChanged = status => { AddLog($"状态变更: {status}"); };
            TestEvents.TestCompleted = result => { AddLog($"📊 测试完成: 条码={result.sn}, 总结果={result.test_result}, 总耗时={result.testing_time}"); };

        }

        // ====== 初始化阶段 ======
        private void WireStepEvents()
        {
            // 步骤开始
            TestEvents.StepStarted = stepName =>
            {
                var row = _stepBinder.FindRow(r => r.Name == stepName);
                if (row != null) row.Status = "执行中";
            };

            TestEvents.StepCompleted = (stepName, success, s, outputs) =>
            {
                var row = _stepBinder.FindRow(r => r.Name == stepName);
                if (row != null)
                {
                    row.Status = success ? "PASS" : "FAIL";
                    row.ElapsedMs = s;
                    row.Actual = outputs != null ? string.Join(",", outputs.Select(kv => kv.Key + "=" + kv.Value)) : "";
                }
            };
        }
        private async void MainForm_Load(object sender, EventArgs e)
        {
            WireStepEvents();

            // UI 初始状态
            lblStatus.Text = "状态：空闲";
            this.AcceptButton = btnStart;  // 回车=开始（可选）
            UpdateButtonStates();

            InitDbAndParams();
            // 初始化配置与 Host
            InitConfig();       // 明确初始化 ConfigManager（加载 Devices.json）
            _runner = new SeatTestRunner("MySql", "server=127.0.0.1;port=3306;database=SeatTest;user=root;password=123456;charset=utf8mb4;SslMode=None");
            await _runner.InitializeAsync();   // 别忘了这一步

            AddLog("[Init] SeatTestRunner 已启动");
            UpdateButtonStates();
        }
        void InitDbAndParams()
        {
            //var registry = new InfrastructureRegistry();
            //var dbOptions = new DbOptions { Type = "MySql", ConnectionString = "server=127.0.0.1;port=3306;database=SeatTest;user=root;password=123456;charset=utf8mb4;SslMode=None" };

            //registry.RegisterDatabase(dbOptions.Type, opts => new DbServices(dbOptions.Type, dbOptions.ConnectionString));
            //DeviceServices.Db = _db;
            //// 初始化参数注入器（缓存 + TTL 300s）
            //WorkflowServices.ParamInjector = new ParamInjector(_db, 300, "L1", "ST01");
            //WorkflowServices.ParamInjector.PreloadAll(); // 启动时加载全部 status=1 参数

            //AddLog("[Init] 全局服务初始化完成");
        }
        /// <summary>
        /// 显式初始化配置中心（此处只加载公共设备池 Devices.json）
        /// </summary>
        private void InitConfig()
        {
            try
            {
                // 第一次访问 Instance 会加载 Devices.json 并缓存
                configManager = ConfigManager.Instance;
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

        // ====== UI 交互与状态 ======

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            //try
            //{
            //    if (_poolTimer != null)
            //    {
            //        _poolTimer.Stop();        // 先停止定时器，避免窗体关闭后仍然触发 Tick 事件
            //        _poolTimer.Dispose();     // 释放定时器底层的 Win32 句柄，防止资源泄漏
            //    }
            //}
            //catch { }
            try { _poolMonitor?.Dispose(); } catch { }
            try
            {
                _runner?.Stop();
                _runner?.Dispose();
            }
            catch { }
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

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (_isStarting) return;
            _stepList.Clear();
            _stepLookup.Clear();
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
                FlowConfig flowConfig = null;
                // 校验该型号流程是否存在（按需加载到缓存）
                try
                {
                    flowConfig = configManager.GetFlowConfig(model);
                }
                catch (Exception exCfg)
                {
                    var msg = "未找到该型号的流程配置（Flows/" + model + ".json）：\n" + exCfg.Message;
                    AddLog("启动失败：" + msg);
                    MessageBox.Show(msg, "配置缺失", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var selectedSteps = flowConfig.TestSteps;
                foreach (var s in selectedSteps)
                {
                    //var ResourceConfig = configManager.GetResourceByKey(s.Target);
                    if (s != null && !_stepLookup.ContainsKey(s.Name))
                    {
                        var info = new StepInfo
                        {
                            Name = s.Name,
                            Description = s.Description,
                            Target = s.Target,
                            Command = s.Command,
                            Status = "未开始",
                            ElapsedMs = 0,
                            Actual = ""
                        };
                        //设置工艺范围
                        var p = s.Parameters;
                        if (p != null && p.ContainsKey("TechRange"))
                        {
                            info.TechRange = p.GetValue<string>("TechRange");
                        }
                        //if (configManager.TryGetResourceByKey(s.Target, out var deviceConfig))
                        //{
                        //    var Settings = deviceConfig.Settings;
                        //    if (Settings != null && Settings.ContainsKey("TechRange")) { 
                        //    info.TechRange = Settings.GetValue<string>("TechRange");
                        //    }
                        //}
                        _stepList.Add(info);
                        _stepLookup[s.Name] = info;// 快速索引
                    }
                }
                _stepBinder.SetRows(_stepList);
                // 启动一次测试（TestRunner 内部负责把 SN 写入 FlowData.Sn，并做超时守护）
                _currentRunId = Guid.NewGuid().ToString();
                lblStatus.Text = "正在测试: " + model + ", RunId=" + _currentRunId;
                AddLog("测试启动: " + model + ", Barcode=" + barcode + ", RunId=" + _currentRunId);
                await _runner.RunTestsAsync(selectedSteps, model, barcode, cts.Token);
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
                _stepLookup.Clear();
                UpdateButtonStates(); // 始终恢复按钮状态
            }
        }
        private void btnStop_Click(object sender, EventArgs e)
        {
            try
            {
                _stepLookup.Clear();
                _isStarting = false;
                if (IsRunning())
                {
                    AddLog($"测试已终止");
                    lblStatus.Text = "已停止";
                    _runner.Stop();
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
                        //_runner.CancelTimeout(_currentRunId);   // 取消超时监控
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
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => AddLog(msg))); // 保留参数
                return;
            }

            LogHelper.Info(msg);
            txtLog.AppendText($"{DateTime.Now:u} {msg}{Environment.NewLine}");
        }


        private void ck_isLite_CheckedChanged(object sender, EventArgs e)
        {
        }
    }
}
