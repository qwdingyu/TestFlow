using System;
using System.Collections.Concurrent;
using System.Threading;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Devices.Plugin;

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

        public DeviceFactory(string dbPath, string reportDir)
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
            Register("http", (f, cfg) => new ZL.DeviceLib.Devices.Actions.HttpActionDevice(cfg));
            Register("shell", (f, cfg) => new ZL.DeviceLib.Devices.Actions.ShellActionDevice(cfg));
        }

        private string GetDeviceKey(string deviceName, DeviceConfig cfg)
        {
            // 优先使用配置中的 ResourceId 作为物理通道标识，确保互斥
            if (!string.IsNullOrEmpty(cfg.ResourceId))
                return cfg.ResourceId;

            // 兼容旧配置：若未设置 ResourceId，则回退到设备名或连接串
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
