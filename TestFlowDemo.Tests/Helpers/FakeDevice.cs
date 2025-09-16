using System;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace TestFlowDemo.Tests.Helpers
{
    /// <summary>
    ///     与生产代码的 <see cref="DeviceFactory"/> 配合使用的假设备，实现可控的执行逻辑。
    /// </summary>
    public sealed class FakeDevice : IDevice
    {
        private readonly string _deviceId;

        public FakeDevice(DeviceConfig cfg)
        {
            if (cfg?.Settings != null && cfg.Settings.TryGetValue("id", out var idObj))
            {
                _deviceId = Convert.ToString(idObj) ?? "unknown";
            }
            else
            {
                _deviceId = "unknown";
            }
        }

        public DeviceExecResult Execute(StepConfig step, StepContext context)
        {
            var runtime = FakeDeviceRegistry.Runtime ?? throw new InvalidOperationException("FakeDeviceRuntime 尚未初始化");
            var behavior = FakeDeviceRegistry.GetBehavior(step.Name);
            return runtime.Execute(_deviceId, step, context, behavior);
        }
    }
}
