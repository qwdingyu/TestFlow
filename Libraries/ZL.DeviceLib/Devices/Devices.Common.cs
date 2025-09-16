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
        // 用于描述设备占用的物理资源，例如 "can://ch0"、"serial://COM1"
        string ResourceId { get; }

        // 执行步骤命令
        DeviceExecResult Execute(StepConfig step, StepContext context);
    }
    public interface IHealthyDevice
    {
        bool IsHealthy();
    }
}

