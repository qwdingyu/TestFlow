using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Events;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public sealed class SamplerDevice : ICapabilityDevice, IDisposable
    {
        private CancellationTokenSource _cts;
        private DeviceConfig _cfg;
        private string _deviceName;
        public SamplerDevice(DeviceConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            if (string.IsNullOrWhiteSpace(cfg.Name))
                throw new ArgumentException("SamplerDevice 需要唯一的 cfg.Name");
            _deviceName = cfg.Name;
        }
        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
            }
            catch { /* 忽略 */ }
            finally
            {
                _cts?.Dispose();
            }
        }
        public bool IsHealthy() => true;
        /// <summary>
        /// 桥接方法：外部是同步接口，但内部调用保持 async。
        /// 通过 Task.Run + GetAwaiter().GetResult() 避免阻塞 UI 主线程。
        /// </summary>
        public ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            // 在独立线程里运行异步逻辑，避免 UI 死锁
            var dict = Task.Run(() => CallAsync(step.Command, step.Parameters ?? new(), ctx)).GetAwaiter().GetResult();
            return new ExecutionResult { Success = true, Outputs = dict };
        }

        public async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
        {
            switch (cap)
            {
                case "BeginWindow":
                    var ch = args["channel"].ToString();
                    MeasurementHub.Begin(_deviceName, ch, clear: true);
                    return new() { { "channel", ch }, { "active", true } };

                case "EndWindowAndCheckRange":
                    var channel = args["channel"].ToString();
                    double min = Convert.ToDouble(args["min"]);
                    double max = Convert.ToDouble(args["max"]);
                    var stats = MeasurementHub.End(_deviceName, channel, includeSamples: true);
                    bool pass = stats.Count > 0 && stats.Max >= min && stats.Max <= max;
                    return new() { { "channel", channel }, { "count", stats.Count }, { "max", stats.Max }, { "avg", stats.Avg }, { "samples", stats.Samples }, { "pass", pass } };
                case "RangeTest":
                    {
                        var _ch = args["channel"].ToString();
                        int duration = Convert.ToInt32(args["durationMs"]);
                        double _min = Convert.ToDouble(args["min"]);
                        double _max = Convert.ToDouble(args["max"]);

                        MeasurementHub.Begin(_deviceName, _ch, clear: true);
                        // 非阻塞等待，避免 UI 卡死
                        try
                        {
                            await Task.Delay(duration, stepCtx.Cancellation).ConfigureAwait(false);
                        }
                        catch (TaskCanceledException ex)
                        {
                            DeviceNotifier.DeviceInfoChangedEvent?.Invoke(_ch, $"测试被取消，通道【{_ch}】未完成采样");
                        }
                        // 可用于导出曲线 includeSamples
                        var _stats = MeasurementHub.End(_deviceName, _ch, includeSamples: true);
                        DeviceNotifier.DeviceInfoChangedEvent?.Invoke(_ch, $"设备【{_ch}】，采样数【{_stats.Count}】，最大值【{_stats.Max}】，最小值【{_stats.Min}】，平均值【{_stats.Avg}】");
                        bool _pass = _stats.Count > 0 && _stats.Max >= _min && _stats.Max <= _max;
                        return new() { { "channel", _ch }, { "durationMs", duration }, { "count", _stats.Count }, { "min", _stats.Min }, { "max", _stats.Max }, { "avg", _stats.Avg }, { "pass", _pass }, { "samples", _stats.Samples } };
                    }

                default: throw new NotSupportedException($"Sampler unsupported: {cap}");
            }
        }
    }
}
