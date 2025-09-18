using System;
using System.Collections.Generic;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Devices.Plugin;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace MyCompany.MyDriver
{
    [DeviceType("MyDriver")]
    public class MyDriverDevice : IDevice
    {
        public MyDriverDevice(DeviceConfig cfg) { /* TODO 读取 cfg.Settings */ }
        public ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            var outputs = new Dictionary<string, object>();
            try
            {
                outputs["status"] = "ok";
                return new ExecutionResult { Success = true, Message = "MyDriver ok", Outputs = outputs };
            }
            catch (OperationCanceledException)
            {
                return new ExecutionResult { Success = false, Message = "cancelled", Outputs = outputs };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Message = ex.Message, Outputs = outputs };
            }
        }
    }
}
