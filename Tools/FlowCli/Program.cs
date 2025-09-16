using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Cli.Commands;
using ZL.DeviceLib;
using ZL.DeviceLib.Models;

namespace Cli
{
    /// <summary>
    /// 设备配置的简单模型，供流程校验等命令临时使用。
    /// </summary>
    internal class DeviceConfig
    {
        public string Type { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public Dictionary<string, object> Settings { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 流程配置的简单模型，包含步骤及设备映射等信息。
    /// </summary>
    internal class FlowConfig
    {
        public string Model { get; set; } = string.Empty;
        // 这里直接引用设备库中的 StepConfig，保证流程定义与核心库保持一致
        public List<StepConfig> TestSteps { get; set; } = new List<StepConfig>();
        public Dictionary<string, DeviceConfig> Devices { get; set; } = new Dictionary<string, DeviceConfig>();
    }

    /// <summary>
    /// CLI 程序入口，负责解析命令行参数并分发到对应的命令处理器。
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 应用程序入口方法。
        /// </summary>
        private static int Main(string[] args)
        {
            if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
            {
                PrintHelp();
                LogHelper.Info("  scaffold-device <TypeName> [--ns <Namespace>] [--out <dir>]  生成设备模板与注册器");
                // 说明 scaffold-plugin 用法，保持字符串在单行以避免语法错误
                LogHelper.Info("  scaffold-plugin <PluginName> [--ns <Namespace>]  生成独立驱动库模板（放入 Plugins 目录自动加载）");
                LogHelper.Info("  schema-validate  校验 devices/infrastructure/flows/subflows 的 JSON 结构");
                return 0;
            }

            string cmd = args[0].ToLowerInvariant();
            try
            {
                int exitCode;
                switch (cmd)
                {
                    case "list":
                        exitCode = ListFlowsCommand.Execute();
                        break;
                    case "validate-all":
                        exitCode = FlowValidationCommand.ValidateAll();
                        break;
                    case "validate":
                        if (args.Length < 2)
                        {
                            Console.Error.WriteLine("Missing model. Usage: validate <MODEL>");
                            exitCode = 2;
                        }
                        else
                        {
                            exitCode = FlowValidationCommand.ValidateOne(args[1]);
                        }

                        break;
                    case "schema-validate":
                        exitCode = SchemaValidateCommand.Execute();
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
                            string repo = CommandHelper.FindRepoRoot();
                            string outDir = Path.Combine(repo, "Libraries", "DeviceLib", "Devices", "Custom");
                            for (int i = 2; i < args.Length - 1; i++)
                            {
                                if (args[i] == "--ns")
                                {
                                    ns = args[i + 1];
                                }
                                if (args[i] == "--out")
                                {
                                    outDir = args[i + 1];
                                }
                            }

                            exitCode = ScaffoldDeviceCommand.Execute(typeName, ns, outDir);
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
                                if (args[i] == "--ns")
                                {
                                    ns = args[i + 1];
                                }
                            }

                            exitCode = ScaffoldPluginCommand.Execute(plugin, ns);
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
                                if (args[i] == "--timeout" && int.TryParse(args[i + 1], out var t))
                                {
                                    timeout = t;
                                    i++;
                                }
                            }

                            exitCode = RunFlowCommand.Execute(args[1], timeout);
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
            catch (Exception ex)
            {
                Console.Error.WriteLine("[FATAL] " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// 打印命令帮助信息。
        /// </summary>
        private static int PrintHelp()
        {
            LogHelper.Info("Flow CLI");
            LogHelper.Info("Commands:");
            LogHelper.Info("  list                 列出已复制到输出目录的流程");
            LogHelper.Info("  validate-all         校验全部流程与子流程引用、设备映射");
            LogHelper.Info("  validate <MODEL>     校验单一型号流程");
            LogHelper.Info("  run <BARCODE>        解析型号并执行完整流程（无 UI）");
            LogHelper.Info("  scaffold-device <TypeName> [--ns <Namespace>] [--out <dir>]  生成设备模板与注册器");
            // 同样保持字符串为单行，避免编译错误
            LogHelper.Info("  scaffold-plugin <PluginName> [--ns <Namespace>]  生成独立驱动库模板（放入 Plugins 目录自动加载）");
            return 0;
        }
    }
}

