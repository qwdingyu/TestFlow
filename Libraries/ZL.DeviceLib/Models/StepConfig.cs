using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZL.DeviceLib.Models
{
    public class StepConfig
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Target { get; set; }
        public string Command { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
        /// <summary>
        /// 启用 禁用
        /// </summary>
        public bool Enable { get; set; }
        /// <summary>
        /// 逻辑分组（比如动作、声音、风扇）
        /// </summary>
        public string Group { get; set; }
        /// <summary>
        /// 标识要并行的步骤（相同标识的步骤并行执行）
        /// </summary>
        public string ParallelGroup { get; set; }
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
