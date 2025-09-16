using System;
using System.Collections.Generic;

namespace ZL.Orchestration
{
    /// <summary>
    ///     描述一次编排执行所需的任务图信息。
    /// </summary>
    public sealed class OrchestrationPlan
    {
        /// <summary>
        ///     编排计划的名称，主要用于日志或追踪。
        /// </summary>
        public string Name;

        /// <summary>
        ///     需要执行的任务集合，使用 <see cref="OrchTask"/> 描述。
        /// </summary>
        public List<OrchTask> Tasks = new List<OrchTask>();
    }

    /// <summary>
    ///     描述单个任务的执行信息与依赖关系。
    /// </summary>
    public sealed class OrchTask
    {
        /// <summary>
        ///     唯一标识符，用于依赖解析及结果回填。
        /// </summary>
        public string Id;

        /// <summary>
        ///     关联的设备或执行器名称，可由外部执行器自行解释。
        /// </summary>
        public string Device;

        /// <summary>
        ///     具体命令或行为标识。
        /// </summary>
        public string Command;

        /// <summary>
        ///     附加参数，通过字典传递给具体执行器。
        /// </summary>
        public IDictionary<string, object> Parameters = new Dictionary<string, object>();

        /// <summary>
        ///     资源锁标识。若多个任务声明相同值，则将串行执行以避免资源冲突。
        /// </summary>
        public string ResourceId;

        /// <summary>
        ///     前置依赖的任务 Id 集合。只有当全部依赖执行成功后，当前任务才会排队执行。
        /// </summary>
        public List<string> DependsOn = new List<string>();

        /// <summary>
        ///     单个任务的超时时间（毫秒）。小于等于 0 表示不设置超时，由上层控制。
        /// </summary>
        public int TimeoutMs;

        /// <summary>
        ///     重试策略，允许在指定次数内进行重试。
        /// </summary>
        public RetrySpec Retry;

        /// <summary>
        ///     窗口化/重复执行的扩展配置，当前实现未使用但保留以兼容设计。
        /// </summary>
        public WindowSpec Window;
    }

    /// <summary>
    ///     描述重试次数及重试间隔。
    /// </summary>
    public sealed class RetrySpec
    {
        /// <summary>
        ///     最大尝试次数。若小于等于 0 将视为只尝试一次。
        /// </summary>
        public int Attempts;

        /// <summary>
        ///     每次重试之间的延迟（毫秒）。
        /// </summary>
        public int DelayMs;
    }

    /// <summary>
    ///     描述重复执行或采样窗口配置。
    /// </summary>
    public sealed class WindowSpec
    {
        /// <summary>
        ///     重复次数。
        /// </summary>
        public int Repeat;

        /// <summary>
        ///     每次重复之间的间隔（毫秒）。
        /// </summary>
        public int IntervalMs;
    }

    /// <summary>
    ///     汇总一次编排执行的整体结果。
    /// </summary>
    public sealed class OrchestrationResult
    {
        /// <summary>
        ///     是否全部任务执行成功。
        /// </summary>
        public bool Success;

        /// <summary>
        ///     若执行失败，用于描述失败原因的消息。
        /// </summary>
        public string Message;

        /// <summary>
        ///     当执行失败时记录首个失败任务的 Id，便于定位问题。
        /// </summary>
        public string FailedTaskId;

        /// <summary>
        ///     所有任务的执行摘要，包含输出、耗时及尝试次数等信息。
        /// </summary>
        public Dictionary<string, TaskExecutionSummary> TaskSummaries = new Dictionary<string, TaskExecutionSummary>();

        /// <summary>
        ///     便捷属性，返回每个任务的输出字典。
        /// </summary>
        public Dictionary<string, IDictionary<string, object>> Outputs
        {
            get
            {
                var dict = new Dictionary<string, IDictionary<string, object>>();
                foreach (var pair in TaskSummaries)
                {
                    dict[pair.Key] = pair.Value.Output;
                }

                return dict;
            }
        }
    }

    /// <summary>
    ///     记录单个任务的执行情况。
    /// </summary>
    public sealed class TaskExecutionSummary
    {
        /// <summary>
        ///     构造函数，要求传入必需的数据。
        /// </summary>
        public TaskExecutionSummary(IDictionary<string, object> output, int attempts, TimeSpan duration, bool timedOut)
        {
            Output = output;
            Attempts = attempts;
            Duration = duration;
            TimedOut = timedOut;
        }

        /// <summary>
        ///     任务执行后得到的输出数据。
        /// </summary>
        public IDictionary<string, object> Output { get; private set; }

        /// <summary>
        ///     实际尝试次数，至少为 1。
        /// </summary>
        public int Attempts { get; private set; }

        /// <summary>
        ///     任务执行耗时，便于排查性能问题。
        /// </summary>
        public TimeSpan Duration { get; private set; }

        /// <summary>
        ///     指示最终成功前是否经历过超时情况。
        /// </summary>
        public bool TimedOut { get; private set; }
    }
}
