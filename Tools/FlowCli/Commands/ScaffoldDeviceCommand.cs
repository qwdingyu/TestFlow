using System;
using System.Collections.Generic;
using System.IO;
using ZL.DeviceLib;

namespace Cli.Commands
{
    /// <summary>
    /// 负责实现 scaffold-device 命令，生成自定义设备模板及注册器。
    /// </summary>
    internal static class ScaffoldDeviceCommand
    {
        /// <summary>
        /// 生成设备模板源码以及示例子流程文件。
        /// </summary>
        /// <param name="typeName">设备类型名称，用于类名与标记。</param>
        /// <param name="ns">生成代码所在的命名空间。</param>
        /// <param name="outDir">设备代码输出目录。</param>
        /// <returns>返回 0 表示生成成功，返回 1 表示生成失败。</returns>
        public static int Execute(string typeName, string ns, string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                string devPath = Path.Combine(outDir, typeName + "Device.cs");
                string regPath = Path.Combine(outDir, typeName + "Registrar.cs");
                // 使用插值字符串生成设备模板源码，避免繁琐拼接导致的转义遗漏。
                string devSrc = $@"using System;
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
                // 使用插值字符串生成注册器模板源码，保证格式一致性。
                string regSrc = $@"using ZL.DeviceLib.Devices;
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

                string subDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows", "Subflows");
                Directory.CreateDirectory(subDir);
                string subJson = "{" +
"\n  \"Name\": \"" + typeName.ToLowerInvariant() + "_sample\"," +
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
                File.WriteAllText(Path.Combine(subDir, typeName.ToLowerInvariant() + "_sample.json"), subJson);

                LogHelper.Info("Generated:\n - " + devPath + "\n - " + regPath + "\n - Flows/Subflows/" + typeName.ToLowerInvariant() + "_sample.json");
                LogHelper.Info("将编译输出中的 Plugins 目录放置编译好的 dll 即可自动加载（或直接将源码集成到独立插件工程）。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Scaffold failed: " + ex.Message);
                return 1;
            }
        }
    }
}
