using System.Collections.Generic;

namespace ZL.DeviceLib.Models
{
    public class StepConfig
    {
        public string Name { get; set; }
        public string Description { get; set; }
        // 语义泛化：Device 改为 Target 
        public string Target { get; set; }
        public string Command { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        public Dictionary<string, object> ExpectedResults { get; set; }
        public int TimeoutMs { get; set; }
        public List<string> DependsOn { get; set; }
        public string OnSuccess { get; set; }
        public string OnFailure { get; set; }

        // 扩展：子流程
        public string Type { get; set; } // "Normal" | "SubFlow" | "SubFlowRef"
        public List<StepConfig> Steps { get; set; }
        public string Ref { get; set; } // for SubFlowRef
    }
}
