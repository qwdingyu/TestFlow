using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Transport;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Utils;

namespace ZL.DeviceLib.Devices.Sp
{
    public sealed class RawSerialDevice : ICapabilityDevice, IHealthyDevice, IDisposable
    {
        //private readonly ITransport _io;
        private readonly SerialTransport _io; // 注意这里改为 SerialTransport，而不是 ITransport

        public RawSerialDevice(DeviceConfig cfg)
        {
            try
            {
                _io = new SerialTransport(cfg.ConnectionString, cfg.Name);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void Dispose() => _io.Dispose();
        public bool IsHealthy() => _io.IsHealthy();
        public ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            var dict = Task.Run(() => CallAsync(step.Command, step.Parameters ?? new(), ctx)).GetAwaiter().GetResult();
            return new ExecutionResult { Success = true, Outputs = dict };
        }

        public async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
        {
            cap = cap ?? "";
            switch (cap)
            {
                case "WriteText":
                    // 参数: text
                    await _io.SendAsync(Encoding.ASCII.GetBytes(args["text"].ToString()), stepCtx.Cancellation).ConfigureAwait(false);
                    return new() { { "written", args["text"] } };

                case "WriteHex":
                    // 参数: hex
                    await _io.SendAsync(CodecUtils.Hex(args["hex"].ToString()), stepCtx.Cancellation).ConfigureAwait(false);
                    return new() { { "written", args["hex"] } };

                case "ReadLine":
                    // 参数: timeoutMs
                    {
                        var to = args.TryGetValue("timeoutMs", out var t) ? TimeSpan.FromMilliseconds(Convert.ToInt32(t)) : TimeSpan.FromMilliseconds(1000);
                        var result = await _io.ReceiveStringAsync(0, to, stepCtx.Cancellation,false).ConfigureAwait(false); // 具体实现中读到换行
                        var line = result[0].Trim();
                        return new() { { "result", line } };
                    }
                case "set_voltage":
                    var voltage = args.GetValue<double>("voltage", 0);
                    return new Dictionary<string, object> { { "result", true }, { "cmd", cap }, { "value", voltage } };

                default: throw new NotSupportedException($"RawSerial unsupported capability: {cap}");
            }
        }
    }

}
