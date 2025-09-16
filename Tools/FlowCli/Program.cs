using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZL.DeviceLib;
using ZL.DeviceLib.Storage;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib;
using ZL.DeviceLib.Devices;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Workflow;

namespace Cli
{
    internal class DeviceConfig { public string Type { get; set; } = ""; public string ConnectionString { get; set; } = ""; public Dictionary<string, object> Settings { get; set; } = new(); }
    internal class FlowConfig
    {
        public string Model { get; set; } = "";
        // 这里直接引用设备库中的 StepConfig，保证流程定义与核心库保持一致
        public List<StepConfig> TestSteps { get; set; } = new();
        public Dictionary<string, DeviceConfig> Devices { get; set; } = new();
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
            {
                PrintHelp();
                LogHelper.Info("  scaffold-device <TypeName> [--ns <Namespace>] [--out <dir>]  生成设备模板与注册器");
                LogHelper.Info("  scaffold-plugin <PluginName> [--ns <Namespace>]  生成独立驱动库模板（放入 Plugins 目录自动加载）");
                LogHelper.Info("  schema-validate  校验 devices/infrastructure/flows/subflows 的 JSON 结构");
                return 0;
            }
            var cmd = args[0].ToLowerInvariant();
            try
            {
                int exitCode;
                switch (cmd)
                {
                    case "list":
                        exitCode = ListFlows();
                        break;
                    case "validate-all":
                        exitCode = ValidateAll();
                        break;
                    case "validate":
                        if (args.Length < 2)
                        {
                            Console.Error.WriteLine("Missing model. Usage: validate <MODEL>");
                            exitCode = 2;
                        }
                        else
                        {
                            exitCode = ValidateOne(args[1]);
                        }
                        break;
                    case "schema-validate":
                        exitCode = SchemaValidateAll();
                        break;
                    case "scaffold-device":
                        if (args.Length < 2)
                        {
                            Console.Error.WriteLine("Missing TypeName. Usage: scaffold-device <TypeName> [--ns <Namespace>] [--out <dir>]");
                            exitCode = 2;
                        }
                        else
                        {
                            string typeName = args[1];
                            string ns = "Custom";
                            string repo = FindRepoRoot();
                            string outDir = Path.Combine(repo, "Libraries", "DeviceLib", "Devices", "Custom");
                            for (int i = 2; i < args.Length - 1; i++)
                            {
                                if (args[i] == "--ns") ns = args[i + 1];
                                if (args[i] == "--out") outDir = args[i + 1];
                            }
                            exitCode = ScaffoldDevice(typeName, ns, outDir);
                        }
                        break;
                    case "scaffold-plugin":
                        if (args.Length < 2)
                        {
                            Console.Error.WriteLine("Missing PluginName. Usage: scaffold-plugin <PluginName> [--ns <Namespace>]");
                            exitCode = 2;
                        }
                        else
                        {
                            string plugin = args[1];
                            string ns = plugin;
                            for (int i = 2; i < args.Length - 1; i++)
                            {
                                if (args[i] == "--ns") ns = args[i + 1];
                            }
                            exitCode = ScaffoldPlugin(plugin, ns);
                        }
                        break;
                    case "run":
                        if (args.Length < 2)
                        {
                            Console.Error.WriteLine("Missing barcode. Usage: run <BARCODE> [--timeout <sec>]");
                            exitCode = 2;
                        }
                        else
                        {
                            int timeout = 90;
                            for (int i = 2; i < args.Length - 1; i++)
                            {
                                if (args[i] == "--timeout" && int.TryParse(args[i + 1], out var t)) { timeout = t; i++; }
                            }
                            exitCode = RunFlow(args[1], timeout);
                        }
                        break;
                    default:
                        Console.Error.WriteLine("Unknown command: " + cmd);
                        PrintHelp();
                        exitCode = 2;
                        break;
                }
                return exitCode;
            }
            catch (Exception ex) { Console.Error.WriteLine("[FATAL] " + ex.Message); return 1; }
        }

        private static int PrintHelp()
        {
            LogHelper.Info("Flow CLI");
            LogHelper.Info("Commands:");
            LogHelper.Info("  list                 列出已复制到输出目录的流程");
            LogHelper.Info("  validate-all         校验全部流程与子流程引用、设备映射");
            LogHelper.Info("  validate <MODEL>     校验单一型号流程");
            LogHelper.Info("  run <BARCODE>        解析型号并执行完整流程（无 UI）");
            LogHelper.Info("  scaffold-device <TypeName> [--ns <Namespace>] [--out <dir>]  生成设备模板与注册器");
            LogHelper.Info("  scaffold-plugin <PluginName> [--ns <Namespace>]  生成独立驱动库模板（放入 Plugins 目录自动加载）");
            return 0;
        }

        private static int ListFlows()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory; var flowsDir = Path.Combine(baseDir, "Flows");
            if (!Directory.Exists(flowsDir)) { LogHelper.Info("Flows 目录不存在"); return 0; }
            foreach (var f in Directory.GetFiles(flowsDir, "*.json").OrderBy(x => x))
                LogHelper.Info(Path.GetFileNameWithoutExtension(f));
            return 0;
        }

        private static int ValidateAll()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory; var flowsDir = Path.Combine(baseDir, "Flows");
            if (!Directory.Exists(flowsDir)) { LogHelper.Info("Flows 目录不存在"); return 1; }
            var files = Directory.GetFiles(flowsDir, "*.json").OrderBy(x => x).ToList(); if (files.Count == 0) { LogHelper.Info("未发现流程文件"); return 1; }
            int ok = 0, fail = 0;
            foreach (var f in files)
            {
                var model = Path.GetFileNameWithoutExtension(f);
                var rc = ValidateOne(model, quiet: true);
                if (rc == 0) { ok++; LogHelper.Info($"[OK] {model}"); } else { fail++; LogHelper.Info($"[ERR] {model}"); }
            }
            LogHelper.Info($"Summary: OK={ok}, ERR={fail}");
            return fail > 0 ? 1 : 0;
        }

        private static int ValidateOne(string model, bool quiet = false)
        {
            try
            {
                var cfg = LoadFlow(model);
                ValidateFlow(cfg);
                if (!quiet) LogHelper.Info($"[OK] {model} steps={cfg.TestSteps.Count}");
                return 0;
            }
            catch (Exception ex) { if (!quiet) Console.Error.WriteLine($"[ERR] {model}: {ex.Message}"); return 3; }
        }

        // ==== 运行完整流程（无 UI）====
        // 说明：为跨平台保留最小实现；依赖 WorkflowLib 在多目标下的 net7.0 或 net6.0 实现。
        private static int RunFlow(string barcode, int timeoutSeconds = 90)
        {
            if (!TryParseModel(barcode, out var model, out var err))
            {
                Console.Error.WriteLine("条码解析失败: " + err);
                return 2;
            }

            // 初始化全局服务（复用主程序思路）
            string dbPath = "test_cli.db";
            string reportDir = "Reports";

            var registry = new InfrastructureRegistry();
            registry.RegisterDatabase("sqlite", opts => new DatabaseService(
                DbPathUtil.ResolveSqlitePath(opts?.ConnectionString, opts?.DefaultDbPath ?? dbPath, FindRepoRoot())));
            try { registry.LoadPlugins(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins")); } catch { }
            var dbCfg = ReadInfraDb();
            var dbOptions = new DbOptions { ConnectionString = dbCfg.ConnectionString, DefaultDbPath = dbPath, Settings = dbCfg.Settings ?? new Dictionary<string, object>() };
            var providerType = string.IsNullOrWhiteSpace(dbCfg.Provider) ? "sqlite" : dbCfg.Provider;
            if (providerType.Equals("database", StringComparison.OrdinalIgnoreCase))
            {
                LogHelper.Info("[WARN] infrastructure.database.Type=\"database\" 含糊，按 sqlite 处理；建议改为 Type=\"sqlite\" 或插件提供者名。");
                providerType = "sqlite";
            }
            var db = registry.CreateDatabase(providerType, dbOptions);
            AppServices.Db = db;
            AppServices.Factory = new DeviceFactory(dbPath, reportDir);
            AppServices.ParamInjector = new ParamInjector(db, 300, "L1", "ST01");
            AppServices.ParamInjector.PreloadAll();
            AppServices.Subflows = new SubflowRegistry();
            AppServices.Subflows.LoadFromDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows", "Subflows"));
            // 加载插件（可选）：将自定义设备 DLL 放在输出目录 Plugins 下
            AppServices.Factory.LoadPlugins(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"));

            // 控制台日志绑定
            UiEventBus.Log += (msg) => LogHelper.Info($"{DateTime.Now:u} {msg}");
            UiEventBus.WorkflowCompleted += (sid, m) => LogHelper.Info($"[Completed] SessionId={sid}, Model={m}");

            // WorkflowCore Host
            var services = new ServiceCollection()
                .AddLogging(b => b.AddConsole())
                .AddWorkflow();
            var provider = services.BuildServiceProvider();
            var host = provider.GetService<WorkflowCore.Interface.IWorkflowHost>();
            host.RegisterWorkflow<DynamicLoopWorkflow, FlowData>();
            host.Start();

            try
            {
                // 触发加载与校验
                var cfg = ConfigManager.Instance.GetFlowConfig(model);

                // 启动一次运行
                var runner = new TestRunner(host);
                var runId = runner.RunTest(model, barcode, timeoutSeconds);
                LogHelper.Info($"[Run] started: RunId={runId}");

                // 等待完成：轮询 Host 状态（简单实现）
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < timeoutSeconds + 5)
                {
                    System.Threading.Thread.Sleep(500);
                    // 简单等待；真正完成时 WorkflowCompleted 会打印日志
                }
                LogHelper.Info("[Run] exit wait");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("运行异常: " + ex.Message);
                return 1;
            }
            finally { try { host.Stop(); } catch { } }
        }

        // 统一改用 DbPathUtil，保留方法名避免调用方改动
        private static string ResolveDbPath(string conn, string fallback)
            => DbPathUtil.ResolveSqlitePath(conn, fallback, FindRepoRoot());

        private static (string Provider, string ConnectionString, Dictionary<string, object> Settings) ReadInfraDb()
        {
            try
            {
                var baseDir = FindRepoRoot();
                var path = Path.Combine(baseDir, "infrastructure.json");
                if (!File.Exists(path)) return (null, null, null);
                var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                var infra = root["Infrastructure"] as Newtonsoft.Json.Linq.JObject; if (infra == null) return (null, null, null);
                if (infra.TryGetValue("database", StringComparison.OrdinalIgnoreCase, out var dbTok) && dbTok is Newtonsoft.Json.Linq.JObject db)
                {
                    var type = db.Value<string>("Type");
                    var conn = db.Value<string>("ConnectionString");
                    var settings = db["Settings"] as Newtonsoft.Json.Linq.JObject;
                    return (type, conn, settings != null ? settings.ToObject<Dictionary<string, object>>() : null);
                }
            }
            catch { }
            return (null, null, null);
        }

        private static bool TryParseModel(string barcode, out string model, out string error)
        {
            model = string.Empty; error = string.Empty;
            if (string.IsNullOrWhiteSpace(barcode)) { error = "条码不能为空"; return false; }
            int idx = barcode.IndexOf('-');
            model = (idx > 0) ? barcode.Substring(0, idx) : barcode.Trim();
            if (string.IsNullOrWhiteSpace(model)) { error = "无法从条码解析出型号"; return false; }
            return true;
        }

        private static int ScaffoldDevice(string typeName, string ns, string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                var devPath = Path.Combine(outDir, typeName + "Device.cs");
                var regPath = Path.Combine(outDir, typeName + "Registrar.cs");
                var devSrc = $@"using System;
using System.Collections.Generic;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Devices.Plugin;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace {ns}
{{
    [DeviceType(""{typeName}"")]
    public class {typeName}Device : IDevice
    {{
        public {typeName}Device(DeviceConfig cfg) {{ /* TODO: ctor with cfg */ }}
        public DeviceExecResult Execute(StepConfig step, StepContext ctx)
        {{
            var outputs = new Dictionary<string, object>();
            try
            {{
                outputs[""status""] = ""ok"";
                return new DeviceExecResult {{ Success = true, Message = ""{typeName} ok"", Outputs = outputs }};
            }}
            catch (OperationCanceledException)
            {{
                return new DeviceExecResult {{ Success = false, Message = ""cancelled"", Outputs = outputs }};
            }}
            catch (Exception ex)
            {{
                return new DeviceExecResult {{ Success = false, Message = ex.Message, Outputs = outputs }};
            }}
        }}
    }}
}}
";
                var regSrc = $@"using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Devices.Plugin;

namespace {ns}
{{
    public class {typeName}Registrar : IDeviceRegistrar
    {{
        public void Register(DeviceFactory factory)
        {{
            factory.Register(""{typeName}"", (_, cfg) => new {typeName}Device(cfg));
        }}
    }}
}}
";
                File.WriteAllText(devPath, devSrc);
                File.WriteAllText(regPath, regSrc);

                // 示例子流程
                var subDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows", "Subflows");
                Directory.CreateDirectory(subDir);
                var subJson = "{" +
"\n  \"Name\": \"" + typeName.ToLower() + "_sample\"," +
"\n  \"Type\": \"SubFlow\"," +
"\n  \"Description\": \"" + typeName + " 示例子流程\"," +
"\n  \"Steps\": [" +
"\n    {" +
"\n      \"Name\": \"demo\"," +
"\n      \"Device\": \"" + typeName + "\"," +
"\n      \"Command\": \"demo\"," +
"\n      \"Parameters\": { }," +
"\n      \"ExpectedResults\": { \"mode\": \"exists\", \"key\": \"status\" }" +
"\n    }" +
"\n  ]" +
"\n}\n";
                File.WriteAllText(Path.Combine(subDir, typeName.ToLower() + "_sample.json"), subJson);

                LogHelper.Info($"Generated:\n - {devPath}\n - {regPath}\n - Flows/Subflows/{typeName.ToLower()}_sample.json");
                LogHelper.Info("将编译输出中的 Plugins 目录放置编译好的 dll 即可自动加载（或直接将源码集成到独立插件工程）。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Scaffold failed: " + ex.Message);
                return 1;
            }
        }

        private static int ScaffoldPlugin(string pluginName, string ns)
        {
            try
            {
                var repo = FindRepoRoot();
                var srcDir = Path.Combine(repo, "PluginsSrc", pluginName);
                Directory.CreateDirectory(srcDir);

                var csproj = "" +
@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFrameworks>net48;net7.0</TargetFrameworks>
    <LangVersion>7.0</LangVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <AssemblyName>" + pluginName + @"</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\..\Libraries\ZL.DeviceLib\ZL.DeviceLib.csproj"" />
  </ItemGroup>
</Project>
";
                File.WriteAllText(Path.Combine(srcDir, pluginName + ".csproj"), csproj);

                var devPath = Path.Combine(srcDir, pluginName + "Device.cs");
                var regPath = Path.Combine(srcDir, pluginName + "Registrar.cs");
                var typeName = pluginName;
                var devSrc = $@"using System;
using System.Collections.Generic;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Devices.Plugin;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace {ns}
{{
    [DeviceType(""{typeName}"")]
    public class {typeName}Device : IDevice
    {{
        public {typeName}Device(DeviceConfig cfg) {{ /* TODO 读取 cfg.Settings */ }}
        public DeviceExecResult Execute(StepConfig step, StepContext ctx)
        {{
            var outputs = new Dictionary<string, object>();
            try
            {{
                outputs[""status""] = ""ok"";
                return new DeviceExecResult {{ Success = true, Message = ""{typeName} ok"", Outputs = outputs }};
            }}
            catch (OperationCanceledException)
            {{
                return new DeviceExecResult {{ Success = false, Message = ""cancelled"", Outputs = outputs }};
            }}
            catch (Exception ex)
            {{
                return new DeviceExecResult {{ Success = false, Message = ex.Message, Outputs = outputs }};
            }}
        }}
    }}
}}
";
                File.WriteAllText(devPath, devSrc);
                var regSrc = $@"using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Devices.Plugin;

namespace {ns}
{{
    public class {typeName}Registrar : IDeviceRegistrar
    {{
        public void Register(DeviceFactory factory)
        {{
            factory.Register(""{typeName}"", (_, cfg) => new {typeName}Device(cfg));
        }}
    }}
}}
";
                File.WriteAllText(regPath, regSrc);

                LogHelper.Info($"Plugin skeleton generated at: {srcDir}");
                LogHelper.Info("构建后将生成的 DLL 复制到 程序目录/Plugins 下，即可自动加载。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Scaffold plugin failed: " + ex.Message);
                return 1;
            }
        }

        private static FlowConfig LoadFlow(string model)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string devicesPath = Path.Combine(baseDir, "devices.json");
            if (!File.Exists(devicesPath)) devicesPath = Path.Combine(baseDir, "Devices.json");
            if (!File.Exists(devicesPath)) throw new FileNotFoundException("设备配置文件不存在: devices.json / Devices.json");
            var devicesRoot = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, DeviceConfig>>>(File.ReadAllText(devicesPath));
            var devs = devicesRoot.ContainsKey("Devices") ? (devicesRoot["Devices"] ?? new Dictionary<string, DeviceConfig>()) : new Dictionary<string, DeviceConfig>();

            string infraPath = Path.Combine(baseDir, "infrastructure.json");
            var infra = new Dictionary<string, DeviceConfig>();
            if (File.Exists(infraPath))
            {
                var infraRoot = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, DeviceConfig>>>(File.ReadAllText(infraPath));
                infra = infraRoot.ContainsKey("Infrastructure") ? (infraRoot["Infrastructure"] ?? new Dictionary<string, DeviceConfig>()) : new Dictionary<string, DeviceConfig>();
            }
            string flowPath = Path.Combine(baseDir, "Flows", model + ".json");
            if (!File.Exists(flowPath)) throw new FileNotFoundException("未找到该型号的流程配置: " + flowPath);
            var cfg = JsonConvert.DeserializeObject<FlowConfig>(File.ReadAllText(flowPath)) ?? new FlowConfig { Model = model };
            // 合并设备
            var merged = new Dictionary<string, DeviceConfig>(devs, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in infra) merged[kv.Key] = kv.Value;
            cfg.Devices = merged;
            return cfg;
        }

        private static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "sln"))
                    || (Directory.Exists(Path.Combine(dir, "Libraries")) && Directory.Exists(Path.Combine(dir, "Tools"))))
                    return dir;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static int SchemaValidateAll()
        {
            int ok = 0, err = 0;
            string baseDir = FindRepoRoot();
            // devices.json
            try
            {
                var devPath = File.Exists(Path.Combine(baseDir, "devices.json")) ? Path.Combine(baseDir, "devices.json") : Path.Combine(baseDir, "Devices.json");
                if (File.Exists(devPath))
                {
                    JsonSchemaValidator.ValidateDevicesFile(devPath);
                    LogHelper.Info("[OK] devices.json"); ok++;
                }
                else LogHelper.Info("[SKIP] devices.json 未找到");
            }
            catch (Exception ex) { Console.Error.WriteLine("[ERR] devices.json: " + ex.Message); err++; }

            // infrastructure.json
            try
            {
                var infraPath = Path.Combine(baseDir, "infrastructure.json");
                if (File.Exists(infraPath))
                {
                    JsonSchemaValidator.ValidateInfrastructureFile(infraPath);
                    // 额外提示：检查 database provider 语义
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(infraPath));
                    var infra = obj["Infrastructure"] as Newtonsoft.Json.Linq.JObject;
                    if (infra != null && infra.TryGetValue("database", StringComparison.OrdinalIgnoreCase, out var dbTok) && dbTok is Newtonsoft.Json.Linq.JObject db)
                    {
                        var type = db.Value<string>("Type");
                        if (string.IsNullOrWhiteSpace(type)) LogHelper.Info("[WARN] infrastructure.database.Type 缺失，默认按 sqlite 处理");
                        else if (string.Equals(type, "database", StringComparison.OrdinalIgnoreCase)) LogHelper.Info("[WARN] infrastructure.database.Type=\"database\" 含糊，建议改为 sqlite 或插件提供者名");
                    }
                    LogHelper.Info("[OK] infrastructure.json"); ok++;
                }
                else LogHelper.Info("[SKIP] infrastructure.json 未找到");
            }
            catch (Exception ex) { Console.Error.WriteLine("[ERR] infrastructure.json: " + ex.Message); err++; }

            // flows
            var flowsDir = Path.Combine(baseDir, "Flows");
            if (Directory.Exists(flowsDir))
            {
                foreach (var f in Directory.EnumerateFiles(flowsDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var fileName = Path.GetFileName(f);
                        var modelFromFile = Path.GetFileNameWithoutExtension(f);
                        var tok = Newtonsoft.Json.Linq.JToken.Parse(File.ReadAllText(f));
                        if (tok.Type != Newtonsoft.Json.Linq.JTokenType.Object) throw new Exception("根应为对象");
                        var obj = (Newtonsoft.Json.Linq.JObject)tok;
                        var declared = obj.Value<string>("Model");
                        if (string.IsNullOrWhiteSpace(declared))
                        {
                            LogHelper.Info($"[WARN] {fileName} 缺少 Model，校验时按文件名填充: {modelFromFile}");
                            var clone = (Newtonsoft.Json.Linq.JObject)obj.DeepClone();
                            clone["Model"] = modelFromFile;
                            JsonSchemaValidator.ValidateFlowToken(clone);
                        }
                        else
                        {
                            if (!string.Equals(declared, modelFromFile, StringComparison.Ordinal))
                                LogHelper.Info($"[WARN] {fileName} 中 Model=\"{declared}\" 与文件名不一致");
                            JsonSchemaValidator.ValidateFlowToken(obj);
                        }
                        LogHelper.Info("[OK] " + fileName); ok++;
                    }
                    catch (Exception ex) { Console.Error.WriteLine($"[ERR] {Path.GetFileName(f)}: {ex.Message}"); err++; }
                }
                var subDir = Path.Combine(flowsDir, "Subflows");
                if (Directory.Exists(subDir))
                {
                    foreach (var f in Directory.EnumerateFiles(subDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try { JsonSchemaValidator.ValidateSubflowFile(f); LogHelper.Info("[OK] Subflow " + Path.GetFileName(f)); ok++; }
                        catch (Exception ex) { Console.Error.WriteLine($"[ERR] Subflow {Path.GetFileName(f)}: {ex.Message}"); err++; }
                    }
                }
            }
            else LogHelper.Info("[SKIP] Flows 目录未找到");

            LogHelper.Info($"Summary: OK={ok}, ERR={err}");
            return err > 0 ? 1 : 0;
        }

        private static void ValidateFlow(FlowConfig cfg)
        {
            if (cfg.TestSteps == null || cfg.TestSteps.Count == 0) throw new Exception($"型号 {cfg.Model} 流程为空");
            if (cfg.Devices == null) throw new Exception("设备清单未注入（Devices 为空）");
            var stepsByName = new Dictionary<string, StepConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in cfg.TestSteps)
            {
                if (string.IsNullOrWhiteSpace(s.Name)) throw new Exception("存在未命名的步骤");
                if (stepsByName.ContainsKey(s.Name)) throw new Exception($"步骤名重复: {s.Name}");
                stepsByName[s.Name] = s;
            }
            int roots = cfg.TestSteps.Count(s => s.DependsOn == null || s.DependsOn.Count == 0);
            if (roots == 0) throw new Exception("未找到起始步骤（缺少无 DependsOn 的步骤）");
            foreach (var s in cfg.TestSteps)
            {
                if (s.DependsOn != null) foreach (var dep in s.DependsOn) if (!stepsByName.ContainsKey(dep)) throw new Exception($"步骤 {s.Name} 的 DependsOn 引用不存在: {dep}");
                if (!string.IsNullOrEmpty(s.OnSuccess) && !stepsByName.ContainsKey(s.OnSuccess)) throw new Exception($"步骤 {s.Name} 的 OnSuccess 指向不存在: {s.OnSuccess}");
                if (!string.IsNullOrEmpty(s.OnFailure) && !stepsByName.ContainsKey(s.OnFailure)) throw new Exception($"步骤 {s.Name} 的 OnFailure 指向不存在: {s.OnFailure}");
                var type = (s.Type ?? "Normal").Trim();
                if (string.Equals(type, "Normal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(type))
                {
                    var key = string.IsNullOrWhiteSpace(s.Device) ? s.Target : s.Device;
                    if (string.IsNullOrWhiteSpace(key)) throw new Exception($"普通步骤 {s.Name} 缺少 Device/Target 字段");
                    if (!cfg.Devices.ContainsKey(key)) throw new Exception($"普通步骤 {s.Name} 引用未知设备: {key}");
                }
                else if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
                {
                    if (s.Steps == null || s.Steps.Count == 0) throw new Exception($"子流程 {s.Name} 未包含任何子步骤");
                    ValidateSubStepsDevices(cfg, s.Name, s.Steps);
                }
                else if (string.Equals(type, "SubFlowRef", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(s.Ref)) throw new Exception($"子流程引用 {s.Name} 缺少 Ref 字段");
                    var subflowDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows", "Subflows");
                    var path = Path.Combine(subflowDir, s.Ref + ".json");
                    if (!File.Exists(path)) throw new Exception($"子流程引用未找到文件: {s.Ref}.json");
                    var subCfg = JsonConvert.DeserializeObject<StepConfig>(File.ReadAllText(path));
                    if (subCfg == null || subCfg.Steps == null || subCfg.Steps.Count == 0) throw new Exception($"子流程引用 {s.Ref} 文件内容无效（缺少 Steps）");
                    ValidateSubStepsDevices(cfg, s.Name + ":ref:" + s.Ref, subCfg.Steps);
                }
                else throw new Exception($"步骤 {s.Name} 类型不支持: {s.Type}");
            }
        }
        private static void ValidateSubStepsDevices(FlowConfig cfg, string owner, IList<StepConfig> subSteps)
        {
            foreach (var ss in subSteps)
            {
                if (string.IsNullOrEmpty(ss.Device)) throw new Exception($"子流程 {owner} 的子步骤 {ss.Name} 缺少 Device");
                if (!cfg.Devices.ContainsKey(ss.Device)) throw new Exception($"子流程 {owner} 的子步骤 {ss.Name} 引用未知设备: {ss.Device}");
            }
        }
    }
}
