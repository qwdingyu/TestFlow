using System.Collections.Generic;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public interface IDevice
    {
        ExecutionResult Execute(StepConfig step, StepContext context);
    }
    public interface IHealthyDevice
    {
        bool IsHealthy();
    }
    public class ExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Outputs { get; set; }
    }
}

