using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Workflow;

namespace ZL.WorkflowLib.Engine
{
    /// <summary>
    /// <para>编排器的对外接口。</para>
    /// <para>外部只需传入计划以及步骤上下文，由编排器负责驱动任务的执行、聚合输出并返回结果。</para>
    /// </summary>
    public interface IOrchestrator
    {
        /// <summary>
        /// <para>执行给定的编排计划。</para>
        /// <para>实现需要负责：构建依赖拓扑、调度可执行的任务（串行或并发）、处理取消/超时/重试，并最终返回全量执行结果。</para>
        /// </summary>
        /// <param name="plan">需要执行的编排计划。</param>
        /// <param name="ctx">来自流程的上下文，主要用于传递取消令牌及共享数据。</param>
        /// <returns>编排的整体执行结果。</returns>
        OrchestrationResult Execute(OrchestrationPlan plan, StepContext ctx);
    }

    /// <summary>
    /// <para>最基础的编排器实现：仅依赖/并发、重试、超时、Fire-And-Forget。</para>
    /// <para>为了便于注入，提供 <see cref="RegisterAsDefault"/> 静态方法用于初始化 <see cref="WorkflowServices.Orchestrator"/>。</para>
    /// </summary>
    public sealed class BasicOrchestrator : IOrchestrator
    {
        /// <summary>
        /// <para>针对 ResourceId 的互斥锁集合。</para>
        /// <para>有些任务虽然命令在不同设备上执行，但可能争用同一物理资源（如 CAN 通道、治具等），因此需要额外的互斥控制。</para>
        /// </summary>
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _resourceLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// <para>注册默认编排器。</para>
        /// <para>通常在应用启动阶段调用一次，即可让 <see cref="WorkflowServices.Orchestrator"/> 获得默认实现。</para>
        /// </summary>
        public static void RegisterAsDefault()
        {
            WorkflowServices.Orchestrator = new BasicOrchestrator();
        }

        /// <inheritdoc />
        public OrchestrationResult Execute(OrchestrationPlan plan, StepContext ctx)
        {
            var result = new OrchestrationResult();
            if (plan == null || plan.Tasks == null || plan.Tasks.Count == 0)
            {
                result.Success = true;
                result.Message = "编排计划为空";
                return result;
            }

            // 计划级别的取消令牌：与外部 StepContext 共享，若某个关键任务失败会触发取消。
            var planCts = CancellationTokenSource.CreateLinkedTokenSource(
                ctx != null ? ctx.Cancellation : CancellationToken.None);
            var planToken = planCts.Token;

            // 统一校验任务集合（去除空任务、重复 Id 等），同时构建 Id -> 任务的索引。
            var taskMap = new Dictionary<string, OrchTask>(StringComparer.OrdinalIgnoreCase);
            var orderedTasks = new List<OrchTask>();
            var errors = new List<string>();

            foreach (var rawTask in plan.Tasks)
            {
                if (rawTask == null)
                {
                    errors.Add("检测到空任务定义，已忽略");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(rawTask.Id))
                {
                    errors.Add("存在未指定 Id 的任务，无法参与编排");
                    continue;
                }
                if (taskMap.ContainsKey(rawTask.Id))
                {
                    errors.Add($"任务 Id 重复：{rawTask.Id}");
                    continue;
                }
                taskMap[rawTask.Id] = rawTask;
                orderedTasks.Add(rawTask);
            }

            if (orderedTasks.Count == 0)
            {
                result.Success = false;
                result.Message = string.Join("; ", errors);
                return result;
            }

            // 状态表用于记录每个任务的生命周期：未开始/运行中/完成/跳过。
            var states = new Dictionary<string, TaskExecutionState>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in orderedTasks)
                states[task.Id] = TaskExecutionState.Pending;

            var runningTasks = new Dictionary<string, Task<OrchTaskResult>>(StringComparer.OrdinalIgnoreCase);
            var planSuccess = errors.Count == 0;

            while (true)
            {
                bool hasPending = false;
                bool progressed = false;

                foreach (var task in orderedTasks)
                {
                    var state = states[task.Id];
                    if (state != TaskExecutionState.Pending)
                        continue;

                    hasPending = true;

                    // 依赖检查：全部依赖必须已经完成，并且（非 Fire-And-Forget）依赖成功。
                    var depends = task.DependsOn ?? new List<string>();
                    bool ready = true;
                    string blockingDep = null;
                    bool missingDep = false;

                    foreach (var depId in depends)
                    {
                        if (!taskMap.ContainsKey(depId))
                        {
                            missingDep = true;
                            blockingDep = depId;
                            ready = false;
                            break;
                        }

                        OrchTaskResult depResult;
                        if (!result.TaskResults.TryGetValue(depId, out depResult))
                        {
                            ready = false;
                            continue;
                        }

                        if (!depResult.Success && !(taskMap[depId].FireAndForget))
                        {
                            blockingDep = depId;
                            ready = false;
                            break;
                        }
                    }

                    if (missingDep)
                    {
                        states[task.Id] = TaskExecutionState.Skipped;
                        var skip = CreateSkippedResult(task.Id,
                            $"依赖任务 {blockingDep} 不存在，跳过执行");
                        result.TaskResults[task.Id] = skip;
                        errors.Add(skip.Message);
                        planSuccess = false;
                        progressed = true;
                        continue;
                    }

                    if (blockingDep != null)
                    {
                        states[task.Id] = TaskExecutionState.Skipped;
                        var skip = CreateSkippedResult(task.Id,
                            $"依赖任务 {blockingDep} 失败，跳过执行");
                        result.TaskResults[task.Id] = skip;
                        errors.Add(skip.Message);
                        planSuccess = false;
                        progressed = true;
                        continue;
                    }

                    if (!ready)
                        continue;

                    if (planToken.IsCancellationRequested && !task.FireAndForget)
                    {
                        states[task.Id] = TaskExecutionState.Skipped;
                        var skip = CreateSkippedResult(task.Id,
                            "编排已取消，任务未执行", canceled: true);
                        result.TaskResults[task.Id] = skip;
                        errors.Add(skip.Message);
                        planSuccess = false;
                        progressed = true;
                        continue;
                    }

                    UiEventBus.PublishLog($"[Orchestrator] 准备执行任务 {task.Id}");
                    var execTask = Task.Run(() => ExecuteTask(task, ctx, planToken));
                    runningTasks[task.Id] = execTask;
                    states[task.Id] = TaskExecutionState.Running;
                    progressed = true;
                }

                if (runningTasks.Count == 0)
                {
                    if (!hasPending || !progressed)
                        break;
                    else
                        continue;
                }

                // 至少等待一个任务完成，随后写入结果再进入下一轮调度。
                var completed = Task.WhenAny(runningTasks.Values).GetAwaiter().GetResult();
                var pair = runningTasks.First(kv => kv.Value == completed);
                runningTasks.Remove(pair.Key);

                OrchTaskResult taskResult;
                try
                {
                    taskResult = completed.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    taskResult = new OrchTaskResult
                    {
                        TaskId = pair.Key,
                        Success = false,
                        Message = "任务执行过程中发生未捕获异常：" + ex.Message,
                        StartedAt = DateTime.Now,
                        FinishedAt = DateTime.Now,
                        Outputs = new Dictionary<string, object>()
                    };
                }

                states[pair.Key] = taskResult.Skipped ? TaskExecutionState.Skipped : TaskExecutionState.Completed;
                result.TaskResults[pair.Key] = taskResult;

                if (!taskResult.Success && !(taskMap[pair.Key].FireAndForget))
                {
                    planSuccess = false;
                    errors.Add($"任务 {pair.Key} 执行失败：{taskResult.Message}");
                    planCts.Cancel();
                }
            }

            // 若仍然存在 Pending 状态的任务，说明存在循环依赖或无法满足的条件，统一标记为跳过。
            foreach (var task in orderedTasks)
            {
                if (states[task.Id] == TaskExecutionState.Pending)
                {
                    var skip = CreateSkippedResult(task.Id,
                        "因存在循环依赖或前置任务失败，未能执行该任务");
                    result.TaskResults[task.Id] = skip;
                    states[task.Id] = TaskExecutionState.Skipped;
                    errors.Add(skip.Message);
                    planSuccess = false;
                }
            }

            result.Success = planSuccess;
            result.Message = planSuccess ? "编排完成" : string.Join("; ", errors);
            return result;
        }

        /// <summary>
        /// <para>执行单个编排任务，内部负责处理 ResourceId 锁、设备锁、重试、超时与取消。</para>
        /// <para>该方法在 Task.Run 中调用，不抛出异常而是将状态写入 <see cref="OrchTaskResult"/>。</para>
        /// </summary>
        private OrchTaskResult ExecuteTask(OrchTask task, StepContext baseCtx, CancellationToken planToken)
        {
            var taskResult = new OrchTaskResult
            {
                TaskId = task.Id,
                StartedAt = DateTime.Now,
                Outputs = new Dictionary<string, object>()
            };

            int attempts = task.Retry != null && task.Retry.Attempts > 0 ? task.Retry.Attempts : 1;
            int delayMs = task.Retry != null && task.Retry.DelayMs > 0 ? task.Retry.DelayMs : 0;

            Exception lastError = null;
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (planToken.IsCancellationRequested && !task.FireAndForget)
                {
                    lastError = new OperationCanceledException("任务在开始前已被取消");
                    break;
                }

                try
                {
                    UiEventBus.PublishLog($"[Orchestrator] 任务 {task.Id} 第 {attempt}/{attempts} 次执行");
                    var outputs = ExecuteOnce(task, baseCtx, planToken);
                    taskResult.Outputs = outputs ?? new Dictionary<string, object>();
                    taskResult.Success = true;
                    taskResult.Message = "成功";
                    taskResult.Attempts = attempt;
                    taskResult.FinishedAt = DateTime.Now;
                    return taskResult;
                }
                catch (OperationCanceledException oce)
                {
                    lastError = oce;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    UiEventBus.PublishLog($"[Orchestrator] 任务 {task.Id} 执行失败：{ex.Message}");
                    if (attempt < attempts)
                        SafeDelay(delayMs, planToken);
                }
            }

            taskResult.Success = false;
            taskResult.Attempts = attempts;
            taskResult.FinishedAt = DateTime.Now;
            taskResult.Message = lastError != null ? lastError.Message : "未知错误";
            taskResult.Canceled = lastError is OperationCanceledException;
            return taskResult;
        }

        /// <summary>
        /// <para>在单次尝试中执行设备调用，并处理 ResourceId 与设备锁。</para>
        /// </summary>
        private Dictionary<string, object> ExecuteOnce(OrchTask task, StepContext baseCtx, CancellationToken planToken)
        {
            Func<Dictionary<string, object>> runCore = () =>
                DeviceLockRegistry.WithLock(task.Target ?? string.Empty, () => RunOnDevice(task, baseCtx, planToken));

            if (!string.IsNullOrWhiteSpace(task.ResourceId))
                return WithResourceLock(task.ResourceId, runCore);

            return runCore();
        }

        ///// <summary>
        ///// <para>真正发起设备调用的核心逻辑。</para>
        ///// </summary>
        //private Dictionary<string, object> RunOnDevice(OrchTask task, StepContext baseCtx, CancellationToken planToken)
        //{
        //    //if (WorkflowServices.FlowCfg == null)
        //    //    throw new InvalidOperationException("WorkflowServices.FlowCfg 尚未初始化");
        //    if (DeviceServices.Factory == null)
        //        throw new InvalidOperationException("DeviceServices.Factory 尚未初始化");
        //    if (string.IsNullOrWhiteSpace(task.Target))
        //        throw new InvalidOperationException($"任务 {task.Id} 未指定设备");

        //    DeviceConfig deviceConfig;
        //    if (!DeviceServices.Devices.TryGetValue(task.Target, out deviceConfig))
        //        throw new InvalidOperationException("Device not found: " + task.Target);

        //    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(planToken))
        //    {
        //        if (task.TimeoutMs > 0)
        //            linked.CancelAfter(task.TimeoutMs);

        //        //var model = baseCtx != null ? baseCtx.Model : (WorkflowServices.FlowCfg.Model ?? string.Empty);
        //        var model = baseCtx != null && !string.IsNullOrWhiteSpace(baseCtx.Model) ? baseCtx.Model : string.Empty;
        //        var stepCtx = baseCtx != null ? baseCtx.CloneWithCancellation(linked.Token) : new StepContext(model, linked.Token);

        //        var stepConfig = new StepConfig
        //        {
        //            Name = task.Id,
        //            Description = string.Empty,
        //            Target = task.Target,
        //            Command = task.Command,
        //            Parameters = task.Parameters != null ? new Dictionary<string, object>(task.Parameters) : new Dictionary<string, object>(),
        //            ExpectedResults = new Dictionary<string, object>(),
        //            TimeoutMs = task.TimeoutMs
        //        };

        //        return DeviceServices.Factory.UseDevice(task.Target, deviceConfig, dev =>
        //        {
        //            var execResult = dev.Execute(stepConfig, stepCtx);
        //            if (!execResult.Success)
        //                throw new Exception(execResult.Message ?? "设备执行返回失败");
        //            return execResult.Outputs ?? new Dictionary<string, object>();
        //        });
        //    }
        //}
        /// <summary>
        /// 真正发起设备调用的核心逻辑（通过 DeviceStepRouter）。
        /// </summary>
        private Dictionary<string, object> RunOnDevice(OrchTask task, StepContext baseCtx, CancellationToken planToken)
        {
            if (DeviceServices.Factory == null)
                throw new InvalidOperationException("DeviceServices.Factory 尚未初始化");
            if (string.IsNullOrWhiteSpace(task.Target))
                throw new InvalidOperationException($"任务 {task.Id} 未指定设备");

            if (!DeviceServices.DevicesCfg.TryGetValue(task.Target, out var deviceConfig))
                throw new InvalidOperationException("Device not found: " + task.Target);

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(planToken))
            {
                if (task.TimeoutMs > 0)
                    linked.CancelAfter(task.TimeoutMs);

                var model = baseCtx != null && !string.IsNullOrWhiteSpace(baseCtx.Model) ? baseCtx.Model : string.Empty;
                var stepCtx = baseCtx != null ? baseCtx.CloneWithCancellation(linked.Token) : new StepContext(model, linked.Token);

                var stepConfig = new StepConfig
                {
                    Name = task.Id,
                    Description = string.Empty,
                    Target = task.Target,
                    Command = task.Command,
                    Parameters = task.Parameters != null ? new Dictionary<string, object>(task.Parameters) : new Dictionary<string, object>(),
                    ExpectedResults = new Dictionary<string, object>(),
                    TimeoutMs = task.TimeoutMs
                };

                // 关键改动：统一通过 DeviceStepRouter
                var execResult = DeviceStepRouter.Execute(task.Target, deviceConfig, stepConfig, stepCtx);

                if (!execResult.Success)
                    throw new Exception(execResult.Message ?? "设备执行返回失败");

                return execResult.Outputs ?? new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// <para>对 ResourceId 进行串行化保护。</para>
        /// </summary>
        private Dictionary<string, object> WithResourceLock(string resourceId, Func<Dictionary<string, object>> action)
        {
            var gate = _resourceLocks.GetOrAdd(resourceId, _ => new SemaphoreSlim(1, 1));
            gate.Wait();
            try
            {
                return action();
            }
            finally
            {
                try { gate.Release(); } catch { }
            }
        }

        /// <summary>
        /// <para>延迟指定毫秒数，若取消令牌触发则立即结束。</para>
        /// </summary>
        private static void SafeDelay(int delayMs, CancellationToken token)
        {
            if (delayMs <= 0)
                return;
            try
            {
                Task.Delay(delayMs, token).Wait(token);
            }
            catch
            {
                // 忽略取消或等待过程中的异常，保持主流程简洁。
            }
        }

        /// <summary>
        /// <para>构造跳过任务的统一结果体。</para>
        /// </summary>
        private static OrchTaskResult CreateSkippedResult(string taskId, string message, bool canceled = false)
        {
            return new OrchTaskResult
            {
                TaskId = taskId,
                Success = false,
                Skipped = true,
                Canceled = canceled,
                Message = message,
                Attempts = 0,
                Outputs = new Dictionary<string, object>(),
                StartedAt = DateTime.Now,
                FinishedAt = DateTime.Now
            };
        }

        /// <summary>
        /// <para>内部状态机枚举，描述任务在调度过程中的状态。</para>
        /// </summary>
        private enum TaskExecutionState
        {
            Pending,
            Running,
            Completed,
            Skipped
        }
    }
}
