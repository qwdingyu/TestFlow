using System.Collections.Generic;

namespace ZL.DeviceLib.Models
{
    public class FlowConfig
    {
        public string Model { get; set; }
        public List<StepConfig> TestSteps { get; set; }
        public Dictionary<string, DeviceConfig> Devices { get; set; }
    }
}

