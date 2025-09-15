using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ZL.DeviceLib.Devices.Plugin
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class DeviceTypeAttribute : Attribute
    {
        public string Type { get; }
        public DeviceTypeAttribute(string type) { Type = type; }
    }

    public interface IDeviceRegistrar
    {
        void Register(DeviceFactory factory);
    }

    public static class DevicePluginLoader
    {
        public static void LoadFrom(DeviceFactory factory, string dir)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;

            var dlls = Directory.GetFiles(dir, "*.dll");
            foreach (var f in dlls)
            {
                try
                {
                    var asm = Assembly.LoadFrom(f);
                    // 1) 优先寻找 IDeviceRegistrar 实现
                    var regs = asm.GetTypes().Where(t => !t.IsAbstract && typeof(IDeviceRegistrar).IsAssignableFrom(t));
                    foreach (var r in regs)
                    {
                        var inst = (IDeviceRegistrar)Activator.CreateInstance(r);
                        inst.Register(factory);
                    }
                    // 2) 回退：扫描带 DeviceTypeAttribute 的 IDevice 实现，按特性注册
                    var devTypes = asm.GetTypes().Where(t => !t.IsAbstract && typeof(IDevice).IsAssignableFrom(t));
                    foreach (var t in devTypes)
                    {
                        var attrs = t.GetCustomAttributes(typeof(DeviceTypeAttribute), inherit: false).OfType<DeviceTypeAttribute>();
                        foreach (var a in attrs)
                        {
                            factory.Register(a.Type, (_, cfg) => (IDevice)Activator.CreateInstance(t, cfg));
                        }
                    }
                }
                catch { /* ignore bad plugin */ }
            }
        }
    }
}

