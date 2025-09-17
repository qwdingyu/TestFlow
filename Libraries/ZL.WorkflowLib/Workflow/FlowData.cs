using System.Threading;
using ZL.DeviceLib.Models;

namespace ZL.WorkflowLib.Workflow
{
    /// <summary>
    /// WorkflowCore 中流转的共享数据对象，用于承载当前产品信息、执行状态及步骤上下文。
    /// </summary>
    public class FlowData
    {
        public string Model { get; set; }
        public string Sn { get; set; }
        public bool LastSuccess { get; set; }
        public string Current { get; set; }
        /// <summary>
        /// 标记当前主流程是否已经完成，防止重复执行收尾逻辑。
        /// </summary>
        public bool WorkflowCompleted { get; set; }
        public long SessionId { get; set; }
        public CancellationToken Cancellation { get; set; }

        /// <summary>
        /// 当前步骤的原始配置对象，避免后续 StepBody 重复查找配置列表。
        /// </summary>
        public StepConfig CurrentStepConfig { get; set; }

        /// <summary>
        /// 当前步骤对应的执行类型（设备步骤、子流程、子流程引用等）。
        /// </summary>
        public StepExecutionKind CurrentStepKind { get; set; }

        /// <summary>
        /// 针对设备步骤额外维护的执行上下文（解析后的参数、重试配置、阶段性结果等）。
        /// </summary>
        public DeviceExecutionContext CurrentExecution { get; set; }
    }

    /// <summary>
    /// 用于在 Workflow 构建器中根据条件选择不同执行分支的枚举。
    /// </summary>
    public enum StepExecutionKind
    {
        None = 0,
        Device = 1,
        SubFlow = 2,
        SubFlowReference = 3
    }
}
