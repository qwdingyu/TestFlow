using System;
using System.IO;
using ZL.DeviceLib;

namespace Cli.Commands
{
    /// <summary>
    /// 负责实现 scaffold-plugin 命令，生成独立插件项目的基础代码。
    /// </summary>
    internal static class ScaffoldPluginCommand
    {
        /// <summary>
        /// 创建插件项目文件以及设备示例代码。
        /// </summary>
        /// <param name="pluginName">插件名称，同时作为项目与类名前缀。</param>
        /// <param name="ns">代码使用的命名空间。</param>
        /// <returns>返回 0 表示生成成功，返回 1 表示生成失败。</returns>
        public static int Execute(string pluginName, string ns)
        {
            try
            {
                string repo = CommandHelper.FindRepoRoot();
                string srcDir = Path.Combine(repo, "PluginsSrc", pluginName);
                Directory.CreateDirectory(srcDir);

                string csproj = "" +
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

                string devPath = Path.Combine(srcDir, pluginName + "Device.cs");
                string regPath = Path.Combine(srcDir, pluginName + "Registrar.cs");
                string typeName = pluginName;
                // 使用插值字符串生成设备示例源码，确保模板结构清晰易读。
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

                // 同样使用插值字符串生成注册器模板代码，避免后续维护风险。
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
                File.WriteAllText(regPath, regSrc);

                LogHelper.Info("Plugin skeleton generated at: " + srcDir);
                LogHelper.Info("构建后将生成的 DLL 复制到 程序目录/Plugins 下，即可自动加载。");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Scaffold plugin failed: " + ex.Message);
                return 1;
            }
        }
    }
}
