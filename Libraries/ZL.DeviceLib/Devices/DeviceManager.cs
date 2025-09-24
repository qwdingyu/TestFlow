using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public sealed class DeviceManager
    {
        private readonly DeviceFactory _factory;
        private readonly IDictionary<string, DeviceConfig> _cfgs;

        public DeviceManager(DeviceFactory factory, IDictionary<string, DeviceConfig> cfgs)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _cfgs = cfgs ?? throw new ArgumentNullException(nameof(cfgs));
        }

        public async Task InitializeAsync(CancellationToken token, int maxParallel = 4)
        {
            using var sem = new SemaphoreSlim(maxParallel, maxParallel);
            var tasks = new List<Task>();

            foreach (var kv in _cfgs)
            {
                var deviceKey = kv.Key;
                var cfg = kv.Value;

                tasks.Add(Task.Run(async () =>
                {
                    await sem.WaitAsync(token);
                    try
                    {
                        LogHelper.Info($"======InitializeAsync===={deviceKey}==========");
                        // 创建或取缓存
                        var dev = _factory.GetDevice(deviceKey);
                        // 仅对 onLoad=true 的设备，开机即握手
                        var hs = SettingsBinder.Bind<HandshakeSpec>(cfg.Settings, "handshake");
                        if (hs?.OnLoad == true && dev is DeviceBase db)
                            await db.EnsureReadyAsync(token).ConfigureAwait(false);
                    }
                    finally { sem.Release(); }
                }, token));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
}
