using System;
using System.Collections.Generic;

namespace ZL.WorkflowLib.Engine
{
    /// <summary>
    ///     编排计划的顶层定义，描述一次测试编排所需的所有任务节点。
    ///     通过自动属性便于后续序列化/反序列化和依赖注入。
    /// </summary>
    public sealed class OrchestrationPlan
    {
        /// <summary>
        ///     编排计划的唯一名称，通常用于日志标识与追踪定位。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        ///     任务节点集合，保持原有列表初始化，确保默认情况下为可操作的空集合。
        /// </summary>
        public List<OrchTask> Tasks { get; set; } = new List<OrchTask>();
    }

    /// <summary>
    ///     单个编排任务节点的定义，包含执行所需的所有上下文信息。
    /// </summary>
    public sealed class OrchTask
    {
        /// <summary>
        ///     任务的唯一标识符，用于依赖关系和日志输出。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        ///     关联的设备名称，指明任务需要调度的具体设备。
        /// </summary>
        public string Device { get; set; }

        /// <summary>
        ///     要执行的命令名称，用于在设备层查找具体动作。
        /// </summary>
        public string Command { get; set; }

        /// <summary>
        ///     任务执行参数，保持字典结构以兼容动态扩展的键值。
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        ///     全局资源锁标识，同一资源仅允许一个任务占用，避免冲突。
        /// </summary>
        public string ResourceId { get; set; }

        /// <summary>
        ///     依赖的上游任务列表，确保属性初始化后依旧可直接添加元素。
        /// </summary>
        public List<string> DependsOn { get; set; }

        /// <summary>
        ///     单个任务允许的最大执行时长，单位毫秒。
        /// </summary>
        public int TimeoutMs { get; set; }

        /// <summary>
        ///     任务的重试配置，封装重试次数与间隔等策略。
        /// </summary>
        public RetrySpec Retry { get; set; }

        /// <summary>是否为 Fire-And-Forget 任务：失败不会阻塞后续流程。</summary>
        public bool FireAndForget { get; set; }
        /// <summary>
        ///     窗口化执行配置，可选控制重复次数与间隔。
        /// </summary>
        public WindowSpec Window { get; set; }
    }

    /// <summary>
    ///     重试策略定义，描述任务失败后的重试次数与间隔。
    /// </summary>
    public sealed class RetrySpec
    {
        /// <summary>
        ///     最大重试次数，包含首轮执行在内的总尝试次数。
        /// </summary>
        public int Attempts { get; set; }

        /// <summary>
        ///     重试间隔，单位为毫秒。
        /// </summary>
        public int DelayMs { get; set; }
    }

    /// <summary>
    ///     窗口化执行策略定义，支持对齐或循环执行的参数配置。
    /// </summary>
    public sealed class WindowSpec
    {
        /// <summary>
        ///     重复执行次数，等于 1 时表示不进行额外重复。
        /// </summary>
        public int Repeat { get; set; }

        /// <summary>
        ///     每次重复之间的等待间隔，单位毫秒。
        /// </summary>
        public int IntervalMs { get; set; }
    }

    /// <summary>
    /// <para>编排整体的执行结果。</para>
    /// </summary>
    public sealed class OrchestrationResult
    {
        /// <summary>整体是否成功（所有非 Fire-And-Forget 任务成功完成）。</summary>
        public bool Success { get; set; }

        /// <summary>总结性消息。</summary>
        public string Message { get; set; }

        /// <summary>任务级输出，键为任务 Id。</summary>
        public Dictionary<string, OrchTaskResult> TaskResults { get; set; } =
            new Dictionary<string, OrchTaskResult>(StringComparer.OrdinalIgnoreCase);
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
        public DateTime StartedAtUtc { get; set; }

        /// <summary>UTC 结束时间。</summary>
        public DateTime CompletedAtUtc { get; set; }

        /// <summary>
        /// <para>执行耗时。</para>
        /// </summary>
        public TimeSpan Duration =>
            CompletedAtUtc > StartedAtUtc ? CompletedAtUtc - StartedAtUtc : TimeSpan.Zero;
    }
}
