using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ZL.DeviceLib.Devices.Plugin;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public class DeviceFactory : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _reportDir;

        private readonly ConcurrentDictionary<string, IDevice> _pool = new ConcurrentDictionary<string, IDevice>();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new ConcurrentDictionary<string, SemaphoreSlim>();

        // 可扩展的设备注册表（区分大小写不敏感）
        private readonly ConcurrentDictionary<string, Func<DeviceFactory, DeviceConfig, IDevice>> _registry =
            new ConcurrentDictionary<string, Func<DeviceFactory, DeviceConfig, IDevice>>(StringComparer.OrdinalIgnoreCase);

        public DeviceFactory(string dbPath, string reportDir, string pluginsDir = null)
        {
            _dbPath = dbPath;
            _reportDir = reportDir;

            // 默认注册内置设备/服务
            Register("scanner", (f, cfg) => new MockScanner(cfg));
            Register("power_supply", (f, cfg) => new MockPowerSupply(cfg));
            Register("current_meter", (f, cfg) => new MockCurrentMeter(cfg));
            Register("resistance_meter", (f, cfg) => new MockResistanceMeter(cfg));
            Register("noise_meter", (f, cfg) => new MockNoiseMeter(cfg));
            Register("can_bus", (f, cfg) => new CanAdapterDevice(cfg));
            Register("database", (f, cfg) => new MockDatabase(cfg, f._dbPath));
            Register("report_generator", (f, cfg) => new MockReportGenerator(cfg, f._reportDir));
            Register("system", (f, cfg) => new MockSystem(cfg));
            Register("resistor_box", (f, cfg) => new ResistorBoxDevice(cfg));
            Register("voltmeter", (f, cfg) => new VoltmeterDevice(cfg));
            // 通用动作提供者（语义泛化）
            Register("http", (f, cfg) => new Actions.HttpActionDevice(cfg));
            Register("shell", (f, cfg) => new Actions.ShellActionDevice(cfg));


            // 加载外部插件（可选）：将自定义设备驱动 DLL 放置于 程序目录/Plugins 下即可自动注册
            if (string.IsNullOrEmpty(pluginsDir))
                pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

            try
            {
                LoadPlugins(pluginsDir);
                LogHelper.Info("[Init] 插件目录加载完成: " + pluginsDir);
            }
            catch (Exception ex)
            {
                // 使用 ex.ToString() 输出完整异常文本（包含堆栈信息），确保日志能保留插件加载失败时的上下文细节
                var errorDetail = $"[Init] 插件加载异常：{ex}";
                LogHelper.Error(errorDetail);
                LogHelper.Error($"插件目录加载失败，原因：{ex.Message}{Environment.NewLine}插件路径：{pluginsDir}{Environment.NewLine}请检查插件文件是否完整或依赖是否齐全。");
            }
        }

        private string GetDeviceKey(string deviceName, DeviceConfig cfg)
        {
            return !string.IsNullOrEmpty(deviceName)
                ? deviceName
                : (cfg.Type + "|" + (cfg.ConnectionString ?? ""));
        }

        private IDevice CreateInternal(DeviceConfig cfg)
        {
            var t = (cfg.Type ?? "").Trim();
            if (_registry.TryGetValue(t, out var factory))
                return factory(this, cfg);
            // 同时尝试小写键（兼容旧配置）
            var tl = t.ToLowerInvariant();
            if (_registry.TryGetValue(tl, out var factory2))
                return factory2(this, cfg);
            throw new Exception("Unsupported device type: " + cfg.Type);
        }

        public void Register(string type, Func<DeviceFactory, DeviceConfig, IDevice> factory)
        {
            if (string.IsNullOrWhiteSpace(type) || factory == null) return;
            _registry[type] = factory;
        }

        public void LoadPlugins(string dir)
        {
            try { DevicePluginLoader.LoadFrom(this, dir); } catch { }
        }

        private IDevice GetOrCreate(string key, DeviceConfig cfg)
        {
            IDevice dev = _pool.GetOrAdd(key, _ => CreateInternal(cfg));
            var hd = dev as IHealthyDevice;
            if (hd != null && !hd.IsHealthy())
            {
                IDevice old;
                if (_pool.TryRemove(key, out old))
                    TryDispose(old);
                dev = _pool.GetOrAdd(key, _ => CreateInternal(cfg));
            }
            return dev;
        }

        public T UseDevice<T>(string deviceKey, DeviceConfig cfg, Func<IDevice, T> action, int waitMs = 10000)
        {
            var gate = _gates.GetOrAdd(deviceKey, _ => new SemaphoreSlim(1, 1));
            if (!gate.Wait(waitMs))
                throw new TimeoutException("设备忙: " + deviceKey);

            try
            {
                var dev = GetOrCreate(deviceKey, cfg);
                return action(dev);
            }
            finally
            {
                gate.Release();
            }
        }

        private void TryDispose(IDevice dev)
        {
            var d = dev as IDisposable;
            if (d != null)
            {
                try { d.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            foreach (var kv in _pool)
                TryDispose(kv.Value);
            _pool.Clear();

            foreach (var kv in _gates)
                kv.Value.Dispose();
            _gates.Clear();
        }
    }
}
