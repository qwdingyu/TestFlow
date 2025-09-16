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
        private readonly DeviceConfig _cfg;

        public MyDriverDevice(DeviceConfig cfg)
        {
            _cfg = cfg; // 保存配置以便返回资源标识
            /* TODO 读取 cfg.Settings */
        }

        // 插件设备的资源标识
        public string ResourceId => _cfg.ResourceId ?? _cfg.ConnectionString;
        public DeviceExecResult Execute(StepConfig step, StepContext ctx)
        {
            var outputs = new Dictionary<string, object>();
            try
            {
                outputs["status"] = "ok";
                return new DeviceExecResult { Success = true, Message = "MyDriver ok", Outputs = outputs };
            }
            catch (OperationCanceledException)
            {
                return new DeviceExecResult { Success = false, Message = "cancelled", Outputs = outputs };
            }
            catch (Exception ex)
            {
                return new DeviceExecResult { Success = false, Message = ex.Message, Outputs = outputs };
            }
        }
    }
}
