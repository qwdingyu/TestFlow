using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ZL.DeviceLib;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Storage;
using ZL.WorkflowLib;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Workflow;
using ZL.WorkflowLib.Workflow.Flows;

namespace Cli.Commands
{
    /// <summary>
    /// 负责实现 run 命令，根据条码解析型号并执行完整流程。
    /// </summary>
    internal static class RunFlowCommand
    {
        /// <summary>
        /// 执行流程运行命令，完成条码解析、基础服务初始化以及流程等待。
        /// </summary>
        /// <param name="barcode">需要执行的条码信息。</param>
        /// <param name="timeoutSeconds">流程最大等待时间，单位秒。</param>
        /// <returns>返回 0 表示运行完成，返回非 0 表示出现错误。</returns>
        public static int Execute(string barcode, int timeoutSeconds)
        {
            string model;
            string error;
            if (!TryParseModel(barcode, out model, out error))
            {
                Console.Error.WriteLine("条码解析失败: " + error);
                return 2;
            }

            string dbPath = "test_cli.db";
            string reportDir = "Reports";

            InfrastructureRegistry registry = new InfrastructureRegistry();
            // 注册默认的 sqlite 数据库工厂，参数使用 DbOptions
            registry.RegisterDatabase("sqlite", delegate(DbOptions opts)
            {
                // 解析 sqlite 路径，若未提供连接串则使用默认路径
                return new DatabaseService(DbPathUtil.ResolveSqlitePath(
                    opts != null ? opts.ConnectionString : null,
                    opts != null ? (opts.DefaultDbPath ?? dbPath) : dbPath,
                    CommandHelper.FindRepoRoot()));
            });

            try
            {
                registry.LoadPlugins(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"));
            }
            catch
            {
            }

            (string Provider, string ConnectionString, Dictionary<string, object> Settings) dbCfg = ReadInfraDb();
            DbOptions dbOptions = new DbOptions();
            dbOptions.ConnectionString = dbCfg.ConnectionString;
            dbOptions.DefaultDbPath = dbPath;
            dbOptions.Settings = dbCfg.Settings ?? new Dictionary<string, object>();
            string providerType = string.IsNullOrWhiteSpace(dbCfg.Provider) ? "sqlite" : dbCfg.Provider;
            if (string.Equals(providerType, "database", StringComparison.OrdinalIgnoreCase))
            {
                LogHelper.Info("[WARN] infrastructure.database.Type=\"database\" 含糊，按 sqlite 处理；建议改为 Type=\"sqlite\" 或插件提供者名。");
                providerType = "sqlite";
            }

            IDatabaseService db = registry.CreateDatabase(providerType, dbOptions);
            // 将数据库与设备工厂注册到全局服务
            DeviceServices.Db = db;
            DeviceServices.Factory = new DeviceFactory(dbPath, reportDir);
            WorkflowServices.ParamInjector = new ParamInjector(db, 300, "L1", "ST01");
            WorkflowServices.ParamInjector.PreloadAll();
            WorkflowServices.Subflows = new SubflowRegistry();
            SubflowDefinitionCatalog.Initialize(WorkflowServices.Subflows);
            //DeviceServices.Factory.LoadPlugins(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"));

            UiEventBus.Log += delegate(string msg) { LogHelper.Info(DateTime.Now.ToString("u") + " " + msg); };
            UiEventBus.WorkflowCompleted += delegate(string sid, string m) { LogHelper.Info("[Completed] SessionId=" + sid + ", Model=" + m); };

            ServiceCollection services = new ServiceCollection();
            services.AddLogging(delegate(ILoggingBuilder builder)
            {
                builder.AddConsole();
            });
            services.AddWorkflow();
            IServiceProvider provider = services.BuildServiceProvider();
            WorkflowCore.Interface.IWorkflowHost host = provider.GetService<WorkflowCore.Interface.IWorkflowHost>();
            WorkflowServices.WorkflowHost = host;
            host.RegisterWorkflow<DynamicLoopWorkflow, FlowData>();
            SubflowDefinitionCatalog.RegisterWorkflows(host, WorkflowServices.Subflows);
            host.Start();

            try
            {
                ConfigManager.Instance.GetFlowConfig(model);
                TestRunner runner = new TestRunner(host);
                string runId = runner.RunTest(model, barcode, timeoutSeconds);
                LogHelper.Info("[Run] started: RunId=" + runId);

                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < timeoutSeconds + 5)
                {
                    System.Threading.Thread.Sleep(500);
                }

                LogHelper.Info("[Run] exit wait");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("运行异常: " + ex.Message);
                return 1;
            }
            finally
            {
                try
                {
                    host.Stop();
                }
                catch
                {
                }
            }
        }

        private static bool TryParseModel(string barcode, out string model, out string error)
        {
            model = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(barcode))
            {
                error = "条码不能为空";
                return false;
            }

            int idx = barcode.IndexOf('-');
            model = idx > 0 ? barcode.Substring(0, idx) : barcode.Trim();
            if (string.IsNullOrWhiteSpace(model))
            {
                error = "无法从条码解析出型号";
                return false;
            }

            return true;
        }

        private static (string Provider, string ConnectionString, Dictionary<string, object> Settings) ReadInfraDb()
        {
            try
            {
                string baseDir = CommandHelper.FindRepoRoot();
                string path = Path.Combine(baseDir, "infrastructure.json");
                if (!File.Exists(path))
                {
                    return (null, null, null);
                }

                JObject root = JObject.Parse(File.ReadAllText(path));
                JObject infra = root["Infrastructure"] as JObject;
                if (infra == null)
                {
                    return (null, null, null);
                }

                JToken dbTok;
                if (infra.TryGetValue("database", StringComparison.OrdinalIgnoreCase, out dbTok) && dbTok is JObject)
                {
                    JObject db = (JObject)dbTok;
                    string type = db.Value<string>("Type");
                    string conn = db.Value<string>("ConnectionString");
                    JObject settings = db["Settings"] as JObject;
                    Dictionary<string, object> settingDict = settings != null ? settings.ToObject<Dictionary<string, object>>() : null;
                    return (type, conn, settingDict);
                }
            }
            catch
            {
            }

            return (null, null, null);
        }
    }
}
