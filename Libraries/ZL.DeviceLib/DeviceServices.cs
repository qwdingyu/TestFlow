using System.Collections.Generic;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib
{
    public class DeviceServices
    {
        public static Devices.DeviceFactory Factory;
        public static Storage.IDatabaseService Db;
        public static Engine.StepContext Context;

        public static Dictionary<string, DeviceConfig> DevicesCfg { get; set; } = new Dictionary<string, DeviceConfig>();
        
    }
}
