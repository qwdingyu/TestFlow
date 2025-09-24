using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ZL.DeviceLib.Models;

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
        public static void LoadFrom(DeviceFactory factory, string dir = null)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            if (!Directory.Exists(dir)) return;

            foreach (var f in Directory.GetFiles(dir, "*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(f);

                    // 1) 优先：通过 IDeviceRegistrar 统一注册（插件方可自定义更多逻辑）
                    foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && typeof(IDeviceRegistrar).IsAssignableFrom(t)))
                    {
                        try
                        {
                            var inst = (IDeviceRegistrar)Activator.CreateInstance(t);
                            inst.Register(factory);
                        }
                        catch (Exception exReg)
                        {
                            LogHelper.Warn($"[Plugin] Registrar 创建/注册失败: {t.FullName} | {exReg}");
                        }
                    }

                    // 2) 回退：扫描带 DeviceTypeAttribute 的 IDevice 实现（包含 DeviceBase/ICapabilityDevice）
                    foreach (var t in asm.GetTypes().Where(t => !t.IsAbstract && typeof(IDevice).IsAssignableFrom(t)))
                    {
                        var attrs = t.GetCustomAttributes(typeof(DeviceTypeAttribute), false).OfType<DeviceTypeAttribute>();
                        foreach (var a in attrs)
                        {
                            factory.Register(a.Type, (fac, cfg) =>
                            {
                                try { return (IDevice)CreateInstance(t, fac, cfg); }
                                catch (Exception exCtor)
                                {
                                    throw new InvalidOperationException($"创建设备失败: {t.FullName} ({a.Type}) | {exCtor.Message}", exCtor);
                                }
                            });
                            LogHelper.Info($"[Plugin] 已注册设备类型: {a.Type} -> {t.FullName}");
                        }
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    var msgs = string.Join("; ", rtle.LoaderExceptions.Select(e => e.Message));
                    LogHelper.Error($"[Plugin] 加载程序集失败: {f} | {rtle.Message} | {msgs}");
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"[Plugin] 跳过损坏/不兼容插件: {f} | {ex}");
                }
            }
        }

        private static object CreateInstance(Type t, DeviceFactory factory, DeviceConfig cfg)
        {
            // 支持两种常见构造器： (DeviceConfig) 或 (DeviceFactory, DeviceConfig)
            var ctor2 = t.GetConstructor(new[] { typeof(DeviceFactory), typeof(DeviceConfig) });
            if (ctor2 != null) return ctor2.Invoke(new object[] { factory, cfg });

            var ctor1 = t.GetConstructor(new[] { typeof(DeviceConfig) });
            if (ctor1 != null) return ctor1.Invoke(new object[] { cfg });

            // 最后回退：无参构造 + 反射注入（可选）
            var ctor0 = t.GetConstructor(Type.EmptyTypes);
            if (ctor0 != null)
            {
                var inst = ctor0.Invoke(null);
                // 如果插件类公开了可写的 Config/Factory 属性，可在此注入（按需）
                var pCfg = t.GetProperty("Config", BindingFlags.Public | BindingFlags.Instance);
                if (pCfg?.CanWrite == true && pCfg.PropertyType.IsAssignableFrom(typeof(DeviceConfig))) pCfg.SetValue(inst, cfg);

                var pFac = t.GetProperty("Factory", BindingFlags.Public | BindingFlags.Instance);
                if (pFac?.CanWrite == true && pFac.PropertyType.IsAssignableFrom(typeof(DeviceFactory))) pFac.SetValue(inst, factory);

                return inst;
            }

            throw new MissingMethodException($"{t.FullName} 缺少可用构造器。需要 (DeviceConfig) 或 (DeviceFactory, DeviceConfig)。");
        }
    }
}

