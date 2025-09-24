using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ZL.DeviceLib.Devices.Actions;
using ZL.DeviceLib.Devices.Can;
using ZL.DeviceLib.Devices.Plugin;
using ZL.DeviceLib.Devices.Sp;
using ZL.DeviceLib.Events;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public class DeviceFactory : IDisposable
    {
        private readonly ConcurrentDictionary<string, IDevice> _pool = new ConcurrentDictionary<string, IDevice>();
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new ConcurrentDictionary<string, SemaphoreSlim>();

        // 可扩展的设备注册表（区分大小写不敏感）
        private readonly ConcurrentDictionary<string, Func<DeviceFactory, DeviceConfig, IDevice>> _registry =
            new ConcurrentDictionary<string, Func<DeviceFactory, DeviceConfig, IDevice>>(StringComparer.OrdinalIgnoreCase);

        public DeviceFactory(string pluginsDir = null)
        {
            // 默认注册内置设备/服务
            // 基础设施-Delay Log
            Register("system", (f, cfg) => new SystemDevice(cfg));
            // 通用动作提供者（语义泛化）
            Register("http", (f, cfg) => new HttpActionDevice(cfg));
            Register("shell", (f, cfg) => new ShellActionDevice(cfg));

            //Register("plc", (f, cfg) => PlcDeviceBuilder.Build(cfg)); //PLC
            Register("noise", (f, cfg) => new NoiseSerialDevice(cfg)); //噪音仪
            Register("resistance", (f, cfg) => new ResistanceSerialDevice(cfg)); // 电阻仪
            Register("ktdy", (f, cfg) => new KtdyUsbDevice(cfg));   // 可调电源
            Register("oscilloscope", (f, cfg) => new OscilloscopeUsbDevice(cfg));   // 示波器
            Register("can_bus", (f, cfg) => new CanDevice(cfg));                      // 能力版 CAN
            Register("scanner", (f, cfg) => new RawSerialDevice(cfg));                // 原生串口：Raw能力 

            //Register("current_meter", (f, cfg) => new ScpiCurrentMeter(cfg));              // SCPI包装能力
            //Register("oscilloscope", (f, cfg) => new ScpiOscilloscopeDevice(cfg));
            //Register("modbus", (f, cfg) => new ModbusDevice(cfg));
            //Register("serial_raw", (f, cfg) => new RawSerialDevice(cfg)); // 通用串口
            //Register("can_bus", (f, cfg) => new CanAdapterDevice(cfg));               // 兼容旧驱动（需要时）

            //Register("scanner", (f, cfg) => new MockScanner(cfg));
            //Register("power_supply", (f, cfg) => new MockPowerSupply(cfg));
            //Register("current_meter", (f, cfg) => new MockCurrentMeter(cfg));
            //Register("resistance_meter", (f, cfg) => new MockResistanceMeter(cfg));
            //Register("noise_meter", (f, cfg) => new MockNoiseMeter(cfg));
            //Register("can_bus", (f, cfg) => new CanAdapterDevice(cfg));
            //Register("database", (f, cfg) => new MockDatabase(cfg));
            //Register("report_generator", (f, cfg) => new MockReportGenerator(cfg, f._reportDir));
            //Register("system", (f, cfg) => new MockSystem(cfg));
            //Register("resistor_box", (f, cfg) => new ResistorBoxDevice(cfg));
            //Register("voltmeter", (f, cfg) => new VoltmeterDevice(cfg));

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
            try
            {
                var t = (cfg.Type ?? "").Trim();
                if (_registry.TryGetValue(t, out var factory))
                    return factory(this, cfg);
                // 同时尝试小写键（兼容旧配置）
                var tl = t.ToLowerInvariant();
                if (_registry.TryGetValue(tl, out var factory2))
                    return factory2(this, cfg);
            }
            catch (Exception ex)
            {
                throw new Exception("Unsupported device type: " + cfg.Type);
            }
            return null;
        }
        public bool IsRegistered(string type) => _registry.ContainsKey(type);

        public bool TryRegister(string type, Func<DeviceFactory, DeviceConfig, IDevice> factory)
        {
            if (string.IsNullOrWhiteSpace(type) || factory == null) return false;
            return _registry.TryAdd(type, factory); // 不覆盖，存在就返回 false
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
            // 确保设备能知道自己的 key（如果配置没写 Name，就用字典的 key）
            if (string.IsNullOrWhiteSpace(cfg.Name)) cfg.Name = key;
            IDevice dev = _pool.GetOrAdd(key, _ => CreateInternal(cfg));

            if (dev is IHealthyDevice hd && !hd.IsHealthy())
            {
                if (_pool.TryRemove(key, out var old)) TryDispose(old);
                dev = _pool.GetOrAdd(key, _ => CreateInternal(cfg));
            }
            return dev;
        }
        public DeviceConfig GetDeviceCfg(string deviceKey)
        {
            LogHelper.Info($"查找的资源为：--------【{deviceKey}】--------");
            if (string.IsNullOrWhiteSpace(deviceKey))
                throw new ArgumentNullException(nameof(deviceKey));

            if (!DeviceServices.DevicesCfg.TryGetValue(deviceKey, out var cfg))
                throw new InvalidOperationException($"Device not found: {deviceKey}");

            return cfg;
        }
        /// <summary>
        /// 根据 deviceKey 获取已注册的设备实例。---一定要提前注册
        /// 从 DeviceServices.Devices 查找配置，不需要手动传 cfg。
        /// </summary>
        public IDevice GetDevice(string deviceKey)
        {
            if (string.IsNullOrWhiteSpace(deviceKey))
                throw new ArgumentNullException(nameof(deviceKey));

            if (!DeviceServices.DevicesCfg.TryGetValue(deviceKey, out var cfg))
                throw new InvalidOperationException($"Device not found: {deviceKey}");

            return GetOrCreate(deviceKey, cfg);
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
        /// <summary>
        /// 可选：在 UI 订阅完 DeviceNotifier 后，主动推送所有设备的“当前快照”
        /// </summary>
        public void PublishInitialStates()
        {
            foreach (var kv in _pool)
            {
                var dev = kv.Value;
                if (dev is IHealthyDevice hd)
                {
                    var state = hd.IsHealthy() ? DeviceState.Connected : DeviceState.Disconnected;
                    DeviceNotifier.DeviceStateChangedEvent?.Invoke(kv.Key, state);
                }
            }
        }
    }
}
