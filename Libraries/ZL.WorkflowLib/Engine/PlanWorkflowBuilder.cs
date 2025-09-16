using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Workflow;

namespace ZL.WorkflowLib.Engine
{
    /// <summary>
    ///     负责根据 <see cref="OrchestrationPlan"/> 动态拼装 WorkflowCore 的步骤图，
    ///     使得每一个编排节点都能通过宿主 <see cref="IWorkflowHost"/> 顺序执行。
    ///     该类型仅承担“构建拓扑 + 绑定重试参数”的职责，具体运行仍交给外层宿主。
    /// </summary>
    public interface IPlanWorkflowBuilder
    {
        /// <summary>
        ///     将编排计划映射为 WorkflowCore 的节点链路。
        /// </summary>
        /// <param name="builder">WorkflowCore 提供的链式构建器。</param>
        /// <param name="plan">已经解析好的编排计划。</param>
        void Build(IWorkflowBuilder<PlanWorkflowData> builder, OrchestrationPlan plan);

        /// <summary>
        ///     为一次计划执行准备初始数据对象，确保上下文、计划快照等信息就绪。
        /// </summary>
        /// <param name="plan">需要执行的计划。</param>
        /// <param name="context">步骤上下文（主要用于取消令牌与模型名）。</param>
        /// <returns>返回填充完毕的 <see cref="PlanWorkflowData"/> 实例。</returns>
        PlanWorkflowData CreateData(OrchestrationPlan plan, StepContext context);
    }

    /// <summary>
    ///     默认的计划构建器实现：逐个节点生成对应的 StepBody，并在最终节点汇总结果。
    /// </summary>
    public sealed class PlanWorkflowBuilder : IPlanWorkflowBuilder
    {
        /// <inheritdoc />
        public void Build(IWorkflowBuilder<PlanWorkflowData> builder, OrchestrationPlan plan)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            var snapshot = ClonePlan(plan);
            var planName = snapshot != null ? snapshot.Name : string.Empty;

            var init = builder
                .StartWith<InitializePlanStateStep>()
                .Input(step => step.Plan, data => snapshot)
                .Input(step => step.PlanName, data => planName)
                .Input(step => step.SharedContext, data => data.SharedContext);

            IStepBuilder<PlanWorkflowData, ExecutePlanTaskStep> lastExec = null;

            if (snapshot != null && snapshot.Tasks != null)
            {
                for (int i = 0; i < snapshot.Tasks.Count; i++)
                {
                    var task = snapshot.Tasks[i];
                    if (task == null)
                        continue;

                    var taskClone = CloneTask(task);
                    var execBuilder = lastExec == null
                        ? init.Then<ExecutePlanTaskStep>()
                        : lastExec.Then<ExecutePlanTaskStep>();

                    execBuilder
                        .Input(step => step.Task, data => taskClone)
                        .Input(step => step.PlanName, data => planName)
                        .Input(step => step.OrderIndex, data => i);

                    lastExec = execBuilder;
                }
            }

            if (lastExec == null)
            {
                init.Then<FinalizePlanStateStep>()
                    .Input(step => step.PlanName, data => planName);
            }
            else
            {
                lastExec.Then<FinalizePlanStateStep>()
                    .Input(step => step.PlanName, data => planName);
            }
        }

        /// <inheritdoc />
        public PlanWorkflowData CreateData(OrchestrationPlan plan, StepContext context)
        {
            var data = new PlanWorkflowData();
            data.Plan = ClonePlan(plan);
            data.PlanName = data.Plan != null ? data.Plan.Name : string.Empty;
            data.SharedContext = context;
            return data;
        }

        /// <summary>
        ///     深拷贝计划，避免后续步骤意外修改原始配置。
        /// </summary>
        private static OrchestrationPlan ClonePlan(OrchestrationPlan plan)
        {
            if (plan == null)
                return new OrchestrationPlan();

            var clone = new OrchestrationPlan
            {
                Name = plan.Name
            };

            if (plan.Tasks != null)
            {
                foreach (var task in plan.Tasks)
                {
                    if (task == null)
                        continue;
                    clone.Tasks.Add(CloneTask(task));
                }
            }

            return clone;
        }

        /// <summary>
        ///     深拷贝单个任务节点，确保重试策略与参数字典互不干扰。
        /// </summary>
        private static OrchTask CloneTask(OrchTask task)
        {
            if (task == null)
                return null;

            var clone = new OrchTask
            {
                Id = task.Id,
                Device = task.Device,
                Command = task.Command,
                Parameters = task.Parameters != null
                    ? new Dictionary<string, object>(task.Parameters)
                    : new Dictionary<string, object>(),
                ResourceId = task.ResourceId,
                DependsOn = task.DependsOn != null
                    ? new List<string>(task.DependsOn)
                    : new List<string>(),
                TimeoutMs = task.TimeoutMs,
                FireAndForget = task.FireAndForget
            };

            if (task.Retry != null)
            {
                clone.Retry = new RetrySpec
                {
                    Attempts = task.Retry.Attempts,
                    DelayMs = task.Retry.DelayMs
                };
            }

            if (task.Window != null)
            {
                clone.Window = new WindowSpec
                {
                    Repeat = task.Window.Repeat,
                    IntervalMs = task.Window.IntervalMs
                };
            }

            return clone;
        }
    }

    /// <summary>
    ///     工作流运行时的数据载体：记录计划、上下文以及执行结果。
    /// </summary>
    public sealed class PlanWorkflowData
    {
        public PlanWorkflowData()
        {
            TaskResults = new Dictionary<string, OrchTaskResult>(StringComparer.OrdinalIgnoreCase);
            TaskMap = new Dictionary<string, OrchTask>(StringComparer.OrdinalIgnoreCase);
            Errors = new List<string>();
        }

        /// <summary>当前执行的计划快照。</summary>
        public OrchestrationPlan Plan { get; set; }

        /// <summary>计划名称，方便日志输出。</summary>
        public string PlanName { get; set; }

        /// <summary>外层传入的步骤上下文，用于复用取消令牌与共享字典。</summary>
        public StepContext SharedContext { get; set; }

        /// <summary>任务执行结果表，键为任务 Id。</summary>
        public Dictionary<string, OrchTaskResult> TaskResults { get; private set; }

        /// <summary>任务定义索引表，便于按 Id 查询 Fire-And-Forget 属性。</summary>
        public Dictionary<string, OrchTask> TaskMap { get; private set; }

        /// <summary>计划执行过程中记录的错误信息。</summary>
        public List<string> Errors { get; private set; }

        /// <summary>计划是否因为关键任务失败而标记为失败。</summary>
        public bool PlanFailed { get; set; }

        /// <summary>计划级取消源，与外层上下文的取消令牌联动。</summary>
        public CancellationTokenSource PlanCancellation { get; set; }

        /// <summary>最终汇总的执行结果，可供外部查询。</summary>
        public OrchestrationResult FinalResult { get; set; }
    }

    /// <summary>
    ///     初始化步骤：写入计划、建立任务索引并准备取消令牌。
    /// </summary>
    internal sealed class InitializePlanStateStep : StepBody
    {
        /// <summary>外部注入的计划快照。</summary>
        public OrchestrationPlan Plan { get; set; }

        /// <summary>计划名称，仅用于日志展示。</summary>
        public string PlanName { get; set; }

        /// <summary>外部共享的步骤上下文。</summary>
        public StepContext SharedContext { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (PlanWorkflowData)context.Workflow.Data;

            var plan = Plan ?? data.Plan ?? new OrchestrationPlan();
            data.Plan = plan;
            data.PlanName = !string.IsNullOrWhiteSpace(PlanName)
                ? PlanName
                : (plan.Name ?? string.Empty);

            var baseCtx = SharedContext ?? data.SharedContext ?? DeviceServices.Context;
            if (baseCtx == null)
            {
                var model = DeviceServices.Config != null ? DeviceServices.Config.Model : string.Empty;
                baseCtx = new StepContext(model, CancellationToken.None);
            }
            data.SharedContext = baseCtx;

            if (data.PlanCancellation != null)
            {
                try { data.PlanCancellation.Dispose(); }
                catch { }
            }
            data.PlanCancellation = CancellationTokenSource.CreateLinkedTokenSource(baseCtx.Cancellation);

            data.TaskResults.Clear();
            data.TaskMap.Clear();
            data.Errors.Clear();
            data.PlanFailed = false;
            data.FinalResult = null;

            if (plan.Tasks != null)
            {
                foreach (var task in plan.Tasks)
                {
                    if (task == null || string.IsNullOrWhiteSpace(task.Id))
                        continue;

                    if (data.TaskMap.ContainsKey(task.Id))
                    {
                        var warn = "[Plan] 检测到重复的任务 Id: " + task.Id;
                        data.Errors.Add(warn);
                        UiEventBus.PublishLog(warn);
                        continue;
                    }
                    data.TaskMap[task.Id] = task;
                }
            }

            UiEventBus.PublishLog("[Plan] 初始化编排：" + data.PlanName);
            return ExecutionResult.Next();
        }
    }

    /// <summary>
    ///     核心执行步骤：负责校验依赖、执行设备指令并按需重试。
    /// </summary>
    internal sealed class ExecutePlanTaskStep : StepBody
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> ResourceLocks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前要执行的任务定义。</summary>
        public OrchTask Task { get; set; }

        /// <summary>任务在原始计划中的顺序，用于日志辅助排查。</summary>
        public int OrderIndex { get; set; }

        /// <summary>计划名称，仅用于日志拼接。</summary>
        public string PlanName { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (PlanWorkflowData)context.Workflow.Data;
            if (Task == null)
            {
                return ExecutionResult.Next();
            }

            var planToken = data.PlanCancellation != null ? data.PlanCancellation.Token : CancellationToken.None;

            if (planToken.IsCancellationRequested && !Task.FireAndForget)
            {
                var canceledResult = CreateSkippedResult(Task.Id,
                    "计划已取消，任务未执行", true);
                data.TaskResults[Task.Id] = canceledResult;
                data.PlanFailed = true;
                data.Errors.Add(canceledResult.Message);
                UiEventBus.PublishLog($"[Plan] 任务 {Task.Id} 因计划取消被跳过");
                return ExecutionResult.Next();
            }

            var depends = Task.DependsOn ?? new List<string>();
            string missingDep = null;
            string blockedDep = null;

            for (int i = 0; i < depends.Count; i++)
            {
                var depId = depends[i];
                if (string.IsNullOrWhiteSpace(depId))
                    continue;

                if (!data.TaskMap.ContainsKey(depId))
                {
                    missingDep = depId;
                    break;
                }

                OrchTaskResult depResult;
                if (!data.TaskResults.TryGetValue(depId, out depResult))
                {
                    blockedDep = depId;
                    break;
                }

                if (!depResult.Success)
                {
                    OrchTask depTask;
                    data.TaskMap.TryGetValue(depId, out depTask);
                    if (depTask == null || !depTask.FireAndForget)
                    {
                        blockedDep = depId;
                        break;
                    }
                }
            }

            if (missingDep != null)
            {
                var skipped = CreateSkippedResult(Task.Id,
                    "依赖任务不存在: " + missingDep, false);
                data.TaskResults[Task.Id] = skipped;
                data.PlanFailed = true;
                data.Errors.Add(skipped.Message);
                UiEventBus.PublishLog($"[Plan] 任务 {Task.Id} 因依赖缺失被跳过");
                return ExecutionResult.Next();
            }

            if (blockedDep != null)
            {
                var skipped = CreateSkippedResult(Task.Id,
                    "依赖任务失败: " + blockedDep, false);
                data.TaskResults[Task.Id] = skipped;
                data.PlanFailed = true;
                data.Errors.Add(skipped.Message);
                UiEventBus.PublishLog($"[Plan] 任务 {Task.Id} 因依赖失败被跳过");
                return ExecutionResult.Next();
            }

            UiEventBus.PublishLog($"[Plan] 执行任务 #{OrderIndex}: {Task.Id} ({PlanName})");

            var result = ExecuteWithRetry(Task, data);
            data.TaskResults[Task.Id] = result;

            if (!result.Success)
            {
                var message = "任务 " + Task.Id + " 执行失败：" + result.Message;
                data.Errors.Add(message);
                UiEventBus.PublishLog("[Plan] " + message);

                if (!Task.FireAndForget)
                {
                    data.PlanFailed = true;
                    if (data.PlanCancellation != null && !data.PlanCancellation.IsCancellationRequested)
                    {
                        try { data.PlanCancellation.Cancel(); }
                        catch { }
                    }
                }
            }

            return ExecutionResult.Next();
        }

        /// <summary>
        ///     封装重试流程，失败后根据配置等待指定时间再尝试。
        /// </summary>
        private OrchTaskResult ExecuteWithRetry(OrchTask task, PlanWorkflowData data)
        {
            var result = new OrchTaskResult
            {
                TaskId = task.Id,
                StartedAtUtc = DateTime.UtcNow,
                Outputs = new Dictionary<string, object>()
            };

            int attempts = task.Retry != null && task.Retry.Attempts > 0 ? task.Retry.Attempts : 1;
            int delayMs = task.Retry != null && task.Retry.DelayMs > 0 ? task.Retry.DelayMs : 0;

            Exception lastError = null;
            var planToken = data.PlanCancellation != null ? data.PlanCancellation.Token : CancellationToken.None;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                if (planToken.IsCancellationRequested && !task.FireAndForget)
                {
                    lastError = new OperationCanceledException("任务在开始前已被取消");
                    break;
                }

                try
                {
                    UiEventBus.PublishLog($"[Plan] 任务 {task.Id} 第 {attempt}/{attempts} 次尝试");
                    var outputs = ExecuteSingleAttempt(task, data);
                    result.Outputs = outputs ?? new Dictionary<string, object>();
                    result.Success = true;
                    result.Message = "成功";
                    result.Attempts = attempt;
                    result.CompletedAtUtc = DateTime.UtcNow;
                    return result;
                }
                catch (OperationCanceledException oce)
                {
                    lastError = oce;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    UiEventBus.PublishLog($"[Plan] 任务 {task.Id} 执行失败：{ex.Message}");
                    if (attempt < attempts)
                        SafeDelay(delayMs, planToken);
                }
            }

            result.Success = false;
            result.Attempts = attempts;
            result.CompletedAtUtc = DateTime.UtcNow;
            result.Message = lastError != null ? lastError.Message : "未知错误";
            result.Canceled = lastError is OperationCanceledException;
            return result;
        }

        /// <summary>
        ///     单次执行设备调用，负责处理资源锁与设备锁。
        /// </summary>
        private Dictionary<string, object> ExecuteSingleAttempt(OrchTask task, PlanWorkflowData data)
        {
            Func<Dictionary<string, object>> runCore = delegate
            {
                return DeviceLockRegistry.WithLock(task.Device ?? string.Empty,
                    delegate { return RunOnDevice(task, data); });
            };

            if (!string.IsNullOrWhiteSpace(task.ResourceId))
                return WithResourceLock(task.ResourceId, runCore);

            return runCore();
        }

        /// <summary>
        ///     实际向设备发起调用，并封装步骤上下文与超时控制。
        /// </summary>
        private Dictionary<string, object> RunOnDevice(OrchTask task, PlanWorkflowData data)
        {
            if (DeviceServices.Config == null)
                throw new InvalidOperationException("DeviceServices.Config 尚未初始化");
            if (DeviceServices.Factory == null)
                throw new InvalidOperationException("DeviceServices.Factory 尚未初始化");
            if (string.IsNullOrWhiteSpace(task.Device))
                throw new InvalidOperationException("任务 " + task.Id + " 未指定设备");

            DeviceConfig deviceConfig;
            if (!DeviceServices.Config.Devices.TryGetValue(task.Device, out deviceConfig))
                throw new InvalidOperationException("Device not found: " + task.Device);

            var planToken = data.PlanCancellation != null ? data.PlanCancellation.Token : CancellationToken.None;

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(planToken))
            {
                if (task.TimeoutMs > 0)
                    linked.CancelAfter(task.TimeoutMs);

                var baseCtx = data.SharedContext ?? DeviceServices.Context ??
                    new StepContext(DeviceServices.Config != null ? DeviceServices.Config.Model : string.Empty, planToken);
                var stepCtx = baseCtx.CloneWithCancellation(linked.Token);

                var stepConfig = new StepConfig
                {
                    Name = task.Id,
                    Description = string.Empty,
                    Device = task.Device,
                    Command = task.Command,
                    Parameters = task.Parameters != null
                        ? new Dictionary<string, object>(task.Parameters)
                        : new Dictionary<string, object>(),
                    ExpectedResults = new Dictionary<string, object>(),
                    TimeoutMs = task.TimeoutMs
                };

                return DeviceServices.Factory.UseDevice(task.Device, deviceConfig,
                    delegate(IDevice device)
                    {
                        var execResult = device.Execute(stepConfig, stepCtx);
                        if (!execResult.Success)
                            throw new Exception(execResult.Message ?? "设备执行失败");
                        return execResult.Outputs ?? new Dictionary<string, object>();
                    });
            }
        }

        /// <summary>
        ///     对 ResourceId 进行串行化保护，避免同一物理资源并发冲突。
        /// </summary>
        private static Dictionary<string, object> WithResourceLock(string resourceId, Func<Dictionary<string, object>> action)
        {
            var gate = ResourceLocks.GetOrAdd(resourceId, delegate { return new SemaphoreSlim(1, 1); });
            gate.Wait();
            try
            {
                return action();
            }
            finally
            {
                try { gate.Release(); }
                catch { }
            }
        }

        /// <summary>
        ///     在重试间隔内阻塞一段时间，并在取消令牌触发时提前退出。
        /// </summary>
        private static void SafeDelay(int delayMs, CancellationToken token)
        {
            if (delayMs <= 0)
                return;
            try
            {
                Task.Delay(delayMs, token).Wait(token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        ///     创建一个跳过结果，用于依赖缺失或取消场景。
        /// </summary>
        private static OrchTaskResult CreateSkippedResult(string taskId, string message, bool canceled)
        {
            return new OrchTaskResult
            {
                TaskId = taskId,
                Success = false,
                Message = message,
                Outputs = new Dictionary<string, object>(),
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
                Skipped = true,
                Canceled = canceled,
                Attempts = 0
            };
        }
    }

    /// <summary>
    ///     收尾步骤：整合执行结果并生成 <see cref="OrchestrationResult"/>。
    /// </summary>
    internal sealed class FinalizePlanStateStep : StepBody
    {
        /// <summary>计划名称，便于日志展示。</summary>
        public string PlanName { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (PlanWorkflowData)context.Workflow.Data;

            if (data.PlanCancellation != null)
            {
                try { data.PlanCancellation.Dispose(); }
                catch { }
                data.PlanCancellation = null;
            }

            var result = new OrchestrationResult
            {
                Success = !data.PlanFailed,
                Message = data.Errors.Count == 0
                    ? "编排完成"
                    : string.Join("; ", data.Errors)
            };

            var dict = new Dictionary<string, OrchTaskResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in data.TaskResults)
                dict[kv.Key] = kv.Value;
            result.TaskResults = dict;

            data.FinalResult = result;

            var status = result.Success ? "成功" : "失败";
            UiEventBus.PublishLog($"[Plan] 编排 {PlanName} 结束，结果：{status}");
            return ExecutionResult.Next();
        }
    }
}

