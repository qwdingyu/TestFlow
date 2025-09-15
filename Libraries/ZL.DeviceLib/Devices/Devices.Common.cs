using System.Collections.Generic;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public class DeviceExecResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Outputs { get; set; }
    }
    public interface IDevice
    {
        DeviceExecResult Execute(StepConfig step, StepContext context);
    }
    public interface IHealthyDevice
    {
        bool IsHealthy();
    }
}

