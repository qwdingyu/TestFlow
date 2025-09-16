using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Orchestration
{
    /// <summary>
    ///     默认的编排器实现，负责处理依赖、资源锁、超时与重试等通用能力。
    /// </summary>
    public sealed class Orchestrator : IOrchestrator
    {
        private readonly IOrchTaskExecutor _executor;
        private readonly Dictionary<string, SemaphoreSlim> _resourceLocks = new Dictionary<string, SemaphoreSlim>();
        private readonly object _resourceLockGate = new object();

        /// <summary>
        ///     通过构造函数注入任务执行器。
        /// </summary>
        public Orchestrator(IOrchTaskExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException("executor");
            }

            _executor = executor;
        }

        /// <inheritdoc />
        public async Task<OrchestrationResult> ExecuteAsync(OrchestrationPlan plan, CancellationToken cancellationToken)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            ExecutionContext context = ValidateAndBuildContext(plan);
            OrchestrationResult result = new OrchestrationResult();
            CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Queue<OrchTask> readyQueue = new Queue<OrchTask>();
            Dictionary<string, Task<TaskExecutionSummary>> runningTasks = new Dictionary<string, Task<TaskExecutionSummary>>();
            int totalTasks = plan.Tasks.Count;
            int completedTasks = 0;

            try
            {
                // 初始化可执行队列
                foreach (KeyValuePair<string, int> pair in context.DependencyCount)
                {
                    if (pair.Value == 0)
                    {
                        readyQueue.Enqueue(context.TasksById[pair.Key]);
                    }
                }

                while (completedTasks < totalTasks)
                {
                    // 将所有就绪任务并发启动
                    while (readyQueue.Count > 0)
                    {
                        OrchTask nextTask = readyQueue.Dequeue();
                        runningTasks[nextTask.Id] = ExecuteTaskAsync(nextTask, linkedCts.Token);
                    }

                    if (runningTasks.Count == 0)
                    {
                        throw new InvalidOperationException("没有可执行的任务，可能存在循环依赖。");
                    }

                    Task<TaskExecutionSummary> finishedTask = await Task.WhenAny(runningTasks.Values).ConfigureAwait(false);
                    string finishedTaskId = FindTaskId(runningTasks, finishedTask);

                    if (finishedTaskId == null)
                    {
                        throw new InvalidOperationException("无法定位已完成任务的标识。");
                    }

                    TaskExecutionSummary summary;
                    try
                    {
                        summary = await finishedTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        runningTasks.Remove(finishedTaskId);
                        linkedCts.Cancel();
                        try
                        {
                            await Task.WhenAll(runningTasks.Values).ConfigureAwait(false);
                        }
                        catch
                        {
                            // 忽略并发取消过程中的异常，保留首个失败信息
                        }

                        result.Success = false;
                        result.FailedTaskId = finishedTaskId;
                        result.Message = BuildFailureMessage(finishedTaskId, ex);
                        return result;
                    }

                    runningTasks.Remove(finishedTaskId);
                    result.TaskSummaries[finishedTaskId] = summary;
                    completedTasks++;

                    List<string> dependentList;
                    if (context.Dependents.TryGetValue(finishedTaskId, out dependentList))
                    {
                        for (int i = 0; i < dependentList.Count; i++)
                        {
                            string dependentId = dependentList[i];
                            int remaining = context.DependencyCount[dependentId] - 1;
                            context.DependencyCount[dependentId] = remaining;
                            if (remaining == 0)
                            {
                                readyQueue.Enqueue(context.TasksById[dependentId]);
                            }
                        }
                    }
                }

                result.Success = true;
                result.Message = "全部任务执行成功。";
                return result;
            }
            finally
            {
                linkedCts.Dispose();
            }
        }

        /// <summary>
        ///     根据任务 Id 查找对应的运行中任务条目。
        /// </summary>
        private static string FindTaskId(Dictionary<string, Task<TaskExecutionSummary>> runningTasks, Task<TaskExecutionSummary> finishedTask)
        {
            foreach (KeyValuePair<string, Task<TaskExecutionSummary>> pair in runningTasks)
            {
                if (object.ReferenceEquals(pair.Value, finishedTask))
                {
                    return pair.Key;
                }
            }

            return null;
        }

        /// <summary>
        ///     统一构建失败消息，优先输出内部异常内容。
        /// </summary>
        private static string BuildFailureMessage(string taskId, Exception ex)
        {
            if (ex is OrchestrationTaskException)
            {
                return ex.Message;
            }

            if (ex == null)
            {
                return string.Format("任务 \"{0}\" 执行失败。", taskId);
            }

            return string.Format("任务 \"{0}\" 执行失败：{1}", taskId, ex.Message);
        }

        /// <summary>
        ///     对输入计划进行校验并生成执行上下文。
        /// </summary>
        private static ExecutionContext ValidateAndBuildContext(OrchestrationPlan plan)
        {
            if (plan.Tasks == null || plan.Tasks.Count == 0)
            {
                throw new ArgumentException("编排计划中未包含任何任务。", "plan");
            }

            Dictionary<string, OrchTask> tasksById = new Dictionary<string, OrchTask>();
            Dictionary<string, int> dependencyCount = new Dictionary<string, int>();
            Dictionary<string, List<string>> dependents = new Dictionary<string, List<string>>();

            for (int i = 0; i < plan.Tasks.Count; i++)
            {
                OrchTask task = plan.Tasks[i];
                if (task == null)
                {
                    throw new ArgumentException("计划中存在空任务。", "plan");
                }

                if (string.IsNullOrEmpty(task.Id))
                {
                    throw new ArgumentException("存在未设置 Id 的任务。", "plan");
                }

                if (tasksById.ContainsKey(task.Id))
                {
                    throw new ArgumentException(string.Format("任务 Id \"{0}\" 重复。", task.Id), "plan");
                }

                if (task.Parameters == null)
                {
                    task.Parameters = new Dictionary<string, object>();
                }

                if (task.DependsOn == null)
                {
                    task.DependsOn = new List<string>();
                }

                tasksById.Add(task.Id, task);
                dependencyCount.Add(task.Id, task.DependsOn.Count);
            }

            foreach (OrchTask task in plan.Tasks)
            {
                for (int i = 0; i < task.DependsOn.Count; i++)
                {
                    string dependencyId = task.DependsOn[i];
                    if (!tasksById.ContainsKey(dependencyId))
                    {
                        throw new ArgumentException(string.Format("任务 \"{0}\" 依赖的任务 \"{1}\" 不存在。", task.Id, dependencyId), "plan");
                    }

                    if (string.Equals(task.Id, dependencyId, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(string.Format("任务 \"{0}\" 不能依赖自身。", task.Id), "plan");
                    }

                    List<string> list;
                    if (!dependents.TryGetValue(dependencyId, out list))
                    {
                        list = new List<string>();
                        dependents.Add(dependencyId, list);
                    }

                    list.Add(task.Id);
                }
            }

            // 使用拓扑排序检测循环依赖
            Queue<string> queue = new Queue<string>();
            Dictionary<string, int> dependencyLeft = new Dictionary<string, int>();
            foreach (KeyValuePair<string, int> pair in dependencyCount)
            {
                dependencyLeft[pair.Key] = pair.Value;
                if (pair.Value == 0)
                {
                    queue.Enqueue(pair.Key);
                }
            }

            int visited = 0;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                visited++;

                List<string> list;
                if (dependents.TryGetValue(current, out list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        string dependentId = list[i];
                        int left = dependencyLeft[dependentId] - 1;
                        dependencyLeft[dependentId] = left;
                        if (left == 0)
                        {
                            queue.Enqueue(dependentId);
                        }
                    }
                }
            }

            if (visited != plan.Tasks.Count)
            {
                throw new ArgumentException("任务依赖存在环，无法确定执行顺序。", "plan");
            }

            return new ExecutionContext(tasksById, dependencyCount, dependents);
        }

        /// <summary>
        ///     执行单个任务，负责资源锁与重试控制。
        /// </summary>
        private async Task<TaskExecutionSummary> ExecuteTaskAsync(OrchTask task, CancellationToken cancellationToken)
        {
            SemaphoreSlim resourceLock = null;
            bool lockTaken = false;
            if (!string.IsNullOrEmpty(task.ResourceId))
            {
                resourceLock = GetResourceLock(task.ResourceId);
                await resourceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                lockTaken = true;
            }

            try
            {
                return await ExecuteWithRetryAsync(task, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (lockTaken && resourceLock != null)
                {
                    resourceLock.Release();
                }
            }
        }

        /// <summary>
        ///     根据资源标识获取互斥锁，确保同一资源串行执行。
        /// </summary>
        private SemaphoreSlim GetResourceLock(string resourceId)
        {
            lock (_resourceLockGate)
            {
                SemaphoreSlim semaphore;
                if (!_resourceLocks.TryGetValue(resourceId, out semaphore))
                {
                    semaphore = new SemaphoreSlim(1, 1);
                    _resourceLocks.Add(resourceId, semaphore);
                }

                return semaphore;
            }
        }

        /// <summary>
        ///     处理单个任务的重试与超时逻辑。
        /// </summary>
        private async Task<TaskExecutionSummary> ExecuteWithRetryAsync(OrchTask task, CancellationToken cancellationToken)
        {
            int maxAttempts = 1;
            int delayMs = 0;
            if (task.Retry != null && task.Retry.Attempts > 0)
            {
                maxAttempts = task.Retry.Attempts;
                if (task.Retry.DelayMs > 0)
                {
                    delayMs = task.Retry.DelayMs;
                }
            }

            Exception lastError = null;
            bool encounteredTimeout = false;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CancellationTokenSource timeoutCts = null;
                Stopwatch stopwatch = null;
                try
                {
                    CancellationToken effectiveToken = cancellationToken;
                    if (task.TimeoutMs > 0)
                    {
                        timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeoutCts.CancelAfter(task.TimeoutMs);
                        effectiveToken = timeoutCts.Token;
                    }

                    stopwatch = Stopwatch.StartNew();
                    IDictionary<string, object> output = await _executor.ExecuteAsync(task, effectiveToken).ConfigureAwait(false);
                    stopwatch.Stop();

                    if (timeoutCts != null)
                    {
                        timeoutCts.Dispose();
                    }

                    return new TaskExecutionSummary(output, attempt, stopwatch.Elapsed, encounteredTimeout);
                }
                catch (OperationCanceledException oce)
                {
                    if (timeoutCts != null)
                    {
                        bool isTimeout = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
                        timeoutCts.Dispose();
                        if (isTimeout)
                        {
                            encounteredTimeout = true;
                            lastError = new TimeoutException(string.Format("任务 \"{0}\" 在第 {1} 次尝试时超时。", task.Id, attempt), oce);
                        }
                        else
                        {
                            throw;
                        }
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    if (timeoutCts != null)
                    {
                        timeoutCts.Dispose();
                    }

                    lastError = ex;
                }

                if (attempt < maxAttempts && delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new OrchestrationTaskException(task.Id, lastError);
        }

        /// <summary>
        ///     内部用于携带编排上下文的不可变结构。
        /// </summary>
        private sealed class ExecutionContext
        {
            public ExecutionContext(Dictionary<string, OrchTask> tasksById, Dictionary<string, int> dependencyCount, Dictionary<string, List<string>> dependents)
            {
                TasksById = tasksById;
                DependencyCount = dependencyCount;
                Dependents = dependents;
            }

            public Dictionary<string, OrchTask> TasksById { get; private set; }

            public Dictionary<string, int> DependencyCount { get; private set; }

            public Dictionary<string, List<string>> Dependents { get; private set; }
        }
    }
}
