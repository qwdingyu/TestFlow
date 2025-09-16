using System;
using System.Collections.Generic;
using System.Threading;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Devices.Transport;

namespace ZL.DeviceLib.Devices
{
    public abstract class SerialDeviceBase : IDevice, IDisposable, IHealthyDevice
    {
        protected readonly ISerialTextTransport _transport;
        protected readonly DeviceConfig _cfg;

        protected SerialDeviceBase(DeviceConfig cfg)
        {
            // 保存原始配置以便获取 ResourceId 等信息
            _cfg = cfg;
            _transport = new SerialTextTransport(cfg.ConnectionString);
        }

        // 默认的资源标识，若配置未提供则退回到连接字符串
        public string ResourceId => _cfg.ResourceId ?? _cfg.ConnectionString;

        public DeviceExecResult Execute(StepConfig step, StepContext ctx)
        {
            var token = ctx.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                return HandleCommand(step, ctx, outputs, token);
            }
            catch (OperationCanceledException)
            {
                return new DeviceExecResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs };
            }
            catch (Exception ex)
            {
                return new DeviceExecResult { Success = false, Message = $"{GetType().Name} Exception: {ex.Message}", Outputs = outputs };
            }
        }

        protected abstract DeviceExecResult HandleCommand(StepConfig step, StepContext ctx, Dictionary<string, object> outputs, CancellationToken token);

        public bool IsHealthy() => _transport != null && _transport.IsConnected;
        public void Dispose()
        {
            var d = _transport as IDisposable;
            d?.Dispose();
        }
    }
}

