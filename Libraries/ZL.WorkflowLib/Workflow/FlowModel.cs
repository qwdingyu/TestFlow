using System;
using System.Collections.Generic;
using System.Threading;
using ZL.DeviceLib.Models;

namespace ZL.WorkflowLib.Workflow
{
    public class FlowConfig
    {
        public string Id { get { return WorkflowServices.GetWorkflowId(this.Model); } }
        public string Model { get; set; }
        public List<StepConfig> TestSteps { get; set; }
    }
    /// <summary>
    /// WorkflowCore 中流转的共享数据对象，用于承载当前产品信息、执行状态及步骤上下文。
    /// </summary>
    public class FlowModel
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
        /// <summary>
        /// 动态生成的子流程 Id
        /// </summary>
        public string WorkflowId { get; set; }
        /// <summary>
        /// 当前流程运行时使用的完整配置对象引用。
        /// <para>在并行启动不同型号流程时，每个实例都持有独立的配置，避免互相污染。</para>
        /// <para>保持可空以兼容旧版序列化数据，框架在读取不到该字段时仍然可以正常运行。</para>
        /// </summary>
        public FlowConfig ActiveConfig { get; set; }
    }

    /// <summary>
    /// <para>FlowModel 的便捷扩展，统一获取当前流程配置。</para>
    /// <para>优先使用运行时注入的 <see cref="FlowModel.ActiveConfig"/>，仅在缺少上下文时回退到全局配置。</para>
    /// </summary>
    internal static class FlowModelExtensions
    {
        /// <summary>
        /// 获取当前流程上下文对应的配置对象。
        /// </summary>
        /// <param name="data">工作流运行时共享数据。</param>
        /// <returns>流程配置；若上下文为空则退回到全局配置。</returns>
        public static FlowConfig GetActiveConfig(this FlowModel data)
        {
            if (data != null && data.ActiveConfig != null)
                return data.ActiveConfig;

            var global = WorkflowServices.FlowCfg;
            if (data != null && global != null && data.ActiveConfig == null)
                data.ActiveConfig = global;

            return global;
        }
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


    /// <summary>
    /// <para>单个任务的执行结果。</para>
    /// </summary>
    public sealed class OrchTaskResult
    {
        /// <summary>任务 Id。</summary>
        public string TaskId { get; set; }

        /// <summary>是否成功。</summary>
        public bool Success { get; set; }

        /// <summary>是否因依赖或取消而被跳过。</summary>
        public bool Skipped { get; set; }

        /// <summary>是否因取消而终止。</summary>
        public bool Canceled { get; set; }

        /// <summary>执行尝试次数。</summary>
        public int Attempts { get; set; }

        /// <summary>描述性的消息。</summary>
        public string Message { get; set; }

        /// <summary>设备返回的输出。</summary>
        public Dictionary<string, object> Outputs { get; set; } = new Dictionary<string, object>();

        /// <summary>UTC 开始时间。</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>UTC 结束时间。</summary>
        public DateTime FinishedAt { get; set; }

        /// <summary>
        /// <para>执行耗时。</para>
        /// </summary>
        public TimeSpan Duration => FinishedAt > StartedAt ? FinishedAt - StartedAt : TimeSpan.Zero;
    }

    /// <summary>
    /// 编排执行的整体结果。
    /// </summary>
    public sealed class OrchestrationResult
    {
        /// <summary>
        /// 全局成功标记，只有所有已执行任务均成功才会为 true。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 当失败时的综合消息（例如第一个失败任务的错误原因）。
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 各任务执行结果，键为任务 Id。
        /// </summary>
        public Dictionary<string, OrchTaskResult> TaskResults { get; } = new Dictionary<string, OrchTaskResult>(StringComparer.OrdinalIgnoreCase);
    }



    //private class ExtraDeviceSpec
    //{
    //    public string Target;
    //    public string Command;
    //    public string Alias;
    //    public int TimeoutMs;
    //    public RetrySpec Retry;
    //    public ExtraStart Start;
    //    public ExtraJoin Join;
    //    public Dictionary<string, object> Parameters;
    //}

    //private class RetrySpec
    //{
    //    public int Attempts;
    //    public int DelayMs;
    //}

    //private enum ExecMode { MainFirst, ExtrasFirst, Parallel }
    //private enum AggregationMode { Namespace, Flat }
    //private enum ExtraStart { Before, WithMain, After }
    //private enum ExtraJoin { Wait, Forget }


}




