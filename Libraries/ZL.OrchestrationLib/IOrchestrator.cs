using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Orchestration
{
    /// <summary>
    ///     编排器接口，负责根据编排计划按需调度任务。
    /// </summary>
    public interface IOrchestrator
    {
        /// <summary>
        ///     执行给定的编排计划。
        /// </summary>
        /// <param name="plan">需要执行的编排计划，不能为空。</param>
        /// <param name="cancellationToken">用于整体取消的令牌。</param>
        /// <returns>封装执行结果与输出的对象。</returns>
        Task<OrchestrationResult> ExecuteAsync(OrchestrationPlan plan, CancellationToken cancellationToken = default(CancellationToken));
    }

    /// <summary>
    ///     任务执行器接口，由上层注入具体的设备或逻辑实现。
    /// </summary>
    public interface IOrchTaskExecutor
    {
        /// <summary>
        ///     执行单个任务，返回任务输出。若执行失败应抛出异常。
        /// </summary>
        /// <param name="task">要执行的任务定义。</param>
        /// <param name="cancellationToken">用于控制超时或全局取消的令牌。</param>
        /// <returns>任务执行完成后返回的键值输出。</returns>
        Task<IDictionary<string, object>> ExecuteAsync(OrchTask task, CancellationToken cancellationToken);
    }
}
