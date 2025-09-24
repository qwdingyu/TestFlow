using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices.Actions
{
    /// <summary>
    /// 系统虚拟设备：提供 Delay、日志等通用能力
    /// </summary>
    public sealed class SystemDevice : ICapabilityDevice
    {
        public SystemDevice(DeviceConfig cfg)
        {
        }
        public void Dispose() { }

        public bool IsHealthy() => true; // 永远可用

        public ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            var dict = CallAsync(step.Command, step.Parameters ?? new(), ctx).GetAwaiter().GetResult();
            return new ExecutionResult { Success = true, Outputs = dict };
        }

        public async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext ctx)
        {
            switch (cap)
            {
                case "Shutdown":
                    int _ms = args.TryGetValue("ms", out var _v) ? Convert.ToInt32(_v) : 1000;
                    await Task.Delay(_ms, ctx.Cancellation);
                    return new() { { "delayedMs", _ms } };
                case "Delay":
                    int ms = args.TryGetValue("ms", out var v) ? Convert.ToInt32(v) : 1000;
                    await Task.Delay(ms, ctx.Cancellation);
                    return new() { { "delayedMs", ms } };

                case "Log":
                    string msg = args.TryGetValue("msg", out var m) ? m.ToString() : "no message";
                    Console.WriteLine($"[SystemDevice-Log] {msg}");
                    return new() { { "logged", msg } };

                default:
                    throw new NotSupportedException($"SystemDevice unsupported command: {cap}");
            }
        }
    }
}
