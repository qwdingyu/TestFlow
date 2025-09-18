using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Utils;

namespace ZL.WorkflowLib.Workflow
{
    /// <summary>
    /// 初始化流程上下文，定位第一个可执行步骤并写入测试会话信息。
    /// </summary>
    public class InitStep : StepBody
    {
        /// <summary>
        /// Workflow 构建阶段解析出的首个可执行步骤 Id，用于直接跳转到起点。
        /// </summary>
        public int? FirstStepId { get; set; }

        /// <summary>
        /// 首个步骤的名称，便于初始化日志展示。
        /// </summary>
        public string FirstStepName { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModel)context.Workflow.Data;
            data.WorkflowCompleted = false;
            data.LastSuccess = true;
            data.Current = null;
            data.CurrentStepConfig = null;
            data.CurrentStepKind = StepExecutionKind.None;
            data.CurrentExecution = null;

            data.SessionId = DeviceServices.Db.StartTestSession(data.Model, data.Sn);

            var startName = string.IsNullOrWhiteSpace(FirstStepName) ? "(无)" : FirstStepName;
            UiEventBus.PublishLog($"[Init] 产品={data.Model}, SN={data.Sn}, SessionId={data.SessionId}, 起始步骤={startName}");

            if (FirstStepId.HasValue)
            {
                data.Current = FirstStepName;
                return ExecutionResult.Outcome(FirstStepId.Value);
            }

            if (!string.IsNullOrWhiteSpace(FirstStepName))
            {
                data.Current = FirstStepName;
                return ExecutionResult.Next();
            }

            data.Current = null;
            WorkflowCompletionHelper.CompleteWorkflow(data);
            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 根据构建器注入的步骤定义初始化运行期上下文，决定后续执行分支。
    /// </summary>
    public class ResolveStepContextStep : StepBody
    {
        /// <summary>
        /// Workflow 构建阶段注入的步骤定义，避免运行时再次查表。
        /// </summary>
        public StepConfig StepConfig { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModel)context.Workflow.Data;
            if (data.WorkflowCompleted)
                return ExecutionResult.Next();

            data.CurrentExecution = null;
            data.CurrentStepConfig = StepConfig;
            data.Current = StepConfig != null ? StepConfig.Name : null;

            if (StepConfig == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog("[Resolve] 当前步骤配置为空，无法继续执行");
                data.CurrentStepKind = StepExecutionKind.None;
                return ExecutionResult.Next();
            }

            var type = string.IsNullOrWhiteSpace(StepConfig.Type) ? "Normal" : StepConfig.Type.Trim();
            if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
            {
                data.CurrentStepKind = StepExecutionKind.SubFlow;
                return ExecutionResult.Next();
            }

            if (string.Equals(type, "SubFlowRef", StringComparison.OrdinalIgnoreCase))
            {
                data.CurrentStepKind = StepExecutionKind.SubFlowReference;
                return ExecutionResult.Next();
            }

            data.CurrentStepKind = StepExecutionKind.Device;
            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 针对设备步骤生成可执行的配置对象，并解析瑞士军刀扩展参数。
    /// </summary>
    public class PrepareDeviceExecutionStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            Dbg.WfDbg(context, "Prepare");
            var data = (FlowModel)context.Workflow.Data;
            if (data.WorkflowCompleted || data.CurrentStepKind != StepExecutionKind.Device)
                return ExecutionResult.Next();

            if (data.CurrentStepConfig == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog("[Prepare] 当前步骤配置缺失，无法执行设备指令");
                return ExecutionResult.Next();
            }

            var execStep = StepUtils.BuildExecutableStep(data.CurrentStepConfig, data);
            var spec = DeviceExecSpec.ParseFrom(execStep.Parameters);
            var execution = new DeviceExecutionContext
            {
                SourceStep = data.CurrentStepConfig,
                ExecutableStep = execStep,
                Specification = spec,
                TraceId = Guid.NewGuid().ToString("N").Substring(0, 8),
                StartedAt = DateTime.Now,
                MainSuccess = false
            };

            data.CurrentExecution = execution;

            UiEventBus.PublishLog($"--[Flow] 开始 {execStep.Name}, 设备【{execStep.Target}】, 描述【{execStep.Description}】, 下一步【{execStep.OnSuccess}】");
            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 在主设备执行前统一申请互斥锁，避免多个流程并发访问同一物理设备。
    /// </summary>
    public class DeviceLockStep : StepBody
    {
        /// <summary>
        /// 由 Workflow 构建器注入的目标设备名称。
        /// </summary>
        public string DeviceName { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModel)context.Workflow.Data;
            if (data.WorkflowCompleted || data.CurrentExecution == null)
                return ExecutionResult.Next();

            if (string.IsNullOrWhiteSpace(DeviceName) && data.CurrentExecution.ExecutableStep != null)
                DeviceName = data.CurrentExecution.ExecutableStep.Target;

            if (string.IsNullOrWhiteSpace(DeviceName))
                return ExecutionResult.Next();

            if (data.CurrentExecution.MainLockHandle != null)
                return ExecutionResult.Next();

            data.CurrentExecution.MainLockHandle = DeviceLockRegistry.Acquire(DeviceName);
            UiEventBus.PublishLog($"[Lock] 获取设备互斥锁：{DeviceName}");
            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 主设备执行 StepBody，支持构建期注入的重试与延迟策略。
    /// </summary>
    public class MainDeviceStep : StepBody, IRetryConfigurable, IDelayConfigurable
    {
        public int RetryAttempts { get; set; } = 1;
        public int RetryDelayMs { get; set; }
        public int PreDelayMs { get; set; }
        public int PostDelayMs { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModel)context.Workflow.Data;
            var exec = data.CurrentExecution;
            if (data.WorkflowCompleted || exec == null)
                return ExecutionResult.Next();

            try
            {
                DeviceExecutionHelper.SafeDelay(PreDelayMs, data.Cancellation);

                var retry = new RetrySpec { Attempts = RetryAttempts, DelayMs = RetryDelayMs };
                exec.MainOutputs = DeviceExecutionHelper.ExecuteDeviceCommand(
                    data,
                    exec.ExecutableStep.Target,
                    exec.ExecutableStep.Command,
                    exec.ExecutableStep.Parameters,
                    exec.ExecutableStep.TimeoutMs,
                    retry,
                    exec.TraceId,
                    useLock: false);

                exec.MainSuccess = true;
                exec.MainError = null;
            }
            catch (Exception ex)
            {
                exec.MainSuccess = false;
                exec.MainError = ex;
                exec.MainOutputs = new Dictionary<string, object>();
                UiEventBus.PublishLog($"[Main] {exec.ExecutableStep.Target}.{exec.ExecutableStep.Command} 失败：{ex.Message}");
            }
            finally
            {
                DeviceExecutionHelper.SafeDelay(PostDelayMs, data.Cancellation);

                if (exec.MainLockHandle != null)
                {
                    try
                    {
                        exec.MainLockHandle.Dispose();
                        UiEventBus.PublishLog($"[Lock] 释放设备互斥锁：{exec.ExecutableStep.Target}");
                    }
                    catch
                    {
                    }
                    finally
                    {
                        exec.MainLockHandle = null;
                    }
                }
            }

            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 附属设备执行 StepBody，根据阶段筛选需要运行的附属设备，并合并输出。
    /// </summary>
    public class ExtraDeviceStep : StepBody, IRetryConfigurable, IDelayConfigurable
    {
        public ExtraDevicePhase Phase { get; set; }
        public int RetryAttempts { get; set; } = 1;
        public int RetryDelayMs { get; set; }
        public int PreDelayMs { get; set; }
        public int PostDelayMs { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModel)context.Workflow.Data;
            var exec = data.CurrentExecution;
            if (data.WorkflowCompleted || exec == null)
                return ExecutionResult.Next();

            var extras = exec.Specification.SelectExtras(Phase).ToList();
            if (extras.Count == 0)
                return ExecutionResult.Next();

            DeviceExecutionHelper.SafeDelay(PreDelayMs, data.Cancellation);

            for (int i = 0; i < extras.Count; i++)
            {
                var extra = extras[i];
                var alias = string.IsNullOrWhiteSpace(extra.Alias) ? extra.Target : extra.Alias;
                var retry = extra.Retry ?? new RetrySpec { Attempts = RetryAttempts, DelayMs = RetryDelayMs };

                if (extra.Join == ExtraJoin.Forget)
                {
                    DeviceExecutionHelper.FireAndForgetExtra(data, extra, retry, exec.TraceId);
                    continue;
                }

                try
                {
                    var outputs = DeviceExecutionHelper.ExecuteDeviceCommand(
                        data,
                        extra.Target,
                        extra.Command,
                        extra.Parameters,
                        extra.TimeoutMs,
                        retry,
                        exec.TraceId,
                        useLock: true);

                    exec.ExtraOutputs[alias] = outputs ?? new Dictionary<string, object>();
                }
                catch (Exception ex)
                {
                    exec.ExtrasSuccess = false;
                    UiEventBus.PublishLog($"[Extra] {extra.Target}.{extra.Command} 失败：{ex.Message}");
                }
            }

            DeviceExecutionHelper.SafeDelay(PostDelayMs, data.Cancellation);
            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 汇总主/附属设备的执行结果，写入数据库并更新流程状态。
    /// </summary>
    public class FinalizeDeviceStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            Dbg.WfDbg(context, "Finalize");
            var data = (FlowModel)context.Workflow.Data;
            var exec = data.CurrentExecution;
            if (data.WorkflowCompleted || data.CurrentStepKind != StepExecutionKind.Device || exec == null)
                return ExecutionResult.Next();

            var pooledResult = StepResultPool.Instance.Get();
            try
            {
                exec.FinishedAt = DateTime.Now;
                pooledResult.Outputs = DeviceExecutionHelper.MergeOutputs(
                    exec.Specification.Aggregation,
                    exec.MainOutputs,
                    exec.ExtraOutputs);

                bool finalSuccess = exec.MainSuccess &&
                                    (exec.ExtrasSuccess || exec.Specification.ContinueOnExtraFailure);

                pooledResult.Success = finalSuccess;
                pooledResult.Message = DeviceExecutionHelper.BuildMessage(finalSuccess, exec.MainError, exec.ExtrasSuccess, exec.Specification);

                ExpectedResultEvaluator.ApplyToStepResult(exec.ExecutableStep, pooledResult, logSuccess: false, logFailure: false);

                DeviceServices.Db.AppendStep(
                    data.SessionId,
                    data.Model,
                    data.Sn,
                    exec.ExecutableStep.Name,
                    exec.ExecutableStep.Description,
                    exec.ExecutableStep.Target,
                    exec.ExecutableStep.Command,
                    JsonConvert.SerializeObject(exec.ExecutableStep.Parameters),
                    JsonConvert.SerializeObject(exec.ExecutableStep.ExpectedResults),
                    JsonConvert.SerializeObject(pooledResult.Outputs),
                    pooledResult.Success ? 1 : 0,
                    pooledResult.Message,
                    exec.StartedAt,
                    exec.FinishedAt);

                data.LastSuccess = pooledResult.Success;
                UiEventBus.PublishLog($"[Step] {exec.ExecutableStep.Name} | 设备={exec.ExecutableStep.Target} | Success={pooledResult.Success} | Msg={pooledResult.Message}");
            }
            catch (Exception ex)
            {
                var exceptionDetail = ex.ToString();
                data.LastSuccess = false;
                DeviceServices.Db.AppendStep(
                    data.SessionId,
                    data.Model,
                    data.Sn,
                    exec.SourceStep != null ? exec.SourceStep.Name : data.Current,
                    exec.SourceStep != null ? exec.SourceStep.Description : string.Empty,
                    exec.SourceStep != null ? exec.SourceStep.Target : string.Empty,
                    exec.SourceStep != null ? exec.SourceStep.Command : string.Empty,
                    JsonConvert.SerializeObject(exec.SourceStep != null ? exec.SourceStep.Parameters : null),
                    JsonConvert.SerializeObject(exec.SourceStep != null ? exec.SourceStep.ExpectedResults : null),
                    null,
                    0,
                    "Exception: " + exceptionDetail,
                    exec.StartedAt == default(DateTime) ? DateTime.Now : exec.StartedAt,
                    DateTime.Now);
                UiEventBus.PublishLog($"[Step-Exception] {data.Current} | SessionId={data.SessionId} | 模型={data.Model} | SN={data.Sn} | 错误详情={exceptionDetail}");
            }
            finally
            {
                StepResultPool.Instance.Return(pooledResult);

                if (exec.MainLockHandle != null)
                {
                    try { exec.MainLockHandle.Dispose(); } catch { }
                    exec.MainLockHandle = null;
                }

                data.CurrentExecution = null;
            }

            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 为子流程执行器保留的兼容入口，复用统一的设备执行逻辑。
    /// </summary>
    public static class DeviceExecStep
    {
        internal static OrchTaskResult ExecuteSingleStep(StepConfig step, StepContext sharedCtx)
        {
            var now = DateTime.Now;
            if (step == null)
            {
                return new OrchTaskResult
                {
                    Success = false,
                    Message = "步骤配置为空",
                    Outputs = new Dictionary<string, object>(),
                    StartedAt = now,
                    FinishedAt = now
                };
            }

            var taskResult = new OrchTaskResult
            {
                StartedAt = now,
                Outputs = new Dictionary<string, object>()
            };

            var pooledResult = StepResultPool.Instance.Get();
            try
            {
                var baseToken = sharedCtx != null ? sharedCtx.Cancellation : CancellationToken.None;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(baseToken))
                {
                    if (step.TimeoutMs > 0)
                        linked.CancelAfter(step.TimeoutMs);

                    var model = sharedCtx != null ? sharedCtx.Model : WorkflowServices.FlowCfg != null ? WorkflowServices.FlowCfg.Model : string.Empty;
                    var stepCtx = sharedCtx != null
                        ? sharedCtx.CloneWithCancellation(linked.Token)
                        : new StepContext(model, linked.Token);

                    DeviceConfig devConf;
                    if (!DeviceServices.Devices.TryGetValue(step.Target, out devConf))
                        throw new Exception("Device not found: " + step.Target);

                    var execResult = DeviceServices.Factory.UseDevice(step.Target, devConf, dev => dev.Execute(step, stepCtx));

                    pooledResult.Success = execResult.Success;
                    pooledResult.Message = execResult.Message;
                    pooledResult.Outputs = execResult.Outputs ?? new Dictionary<string, object>();
                }

                ExpectedResultEvaluator.ApplyToStepResult(step, pooledResult, logSuccess: false, logFailure: false);

                taskResult.Success = pooledResult.Success;
                taskResult.Message = pooledResult.Message;
                taskResult.Outputs = new Dictionary<string, object>(pooledResult.Outputs ?? new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                taskResult.Success = false;
                taskResult.Message = ex.Message;
                taskResult.Outputs = new Dictionary<string, object>();
                UiEventBus.PublishLog($"[SubStep-Exception] {step.Name} | 错误={ex.Message}");
            }
            finally
            {
                taskResult.FinishedAt = DateTime.Now;
                StepResultPool.Instance.Return(pooledResult);
            }

            return taskResult;
        }
    }

    /// <summary>
    /// 执行内嵌子流程（Type=SubFlow）。
    /// </summary>
    public class ExecuteSubFlowStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModel)context.Workflow.Data;
            if (data.WorkflowCompleted || data.CurrentStepKind != StepExecutionKind.SubFlow)
                return ExecutionResult.Next();

            if (data.CurrentStepConfig == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[SubFlow] 当前步骤配置缺失: {data.Current}");
                return ExecutionResult.Next();
            }

            try
            {
                // TODO 需要确认逻辑是否正确？？？
                new SubFlowExecutor().RunInlineSubFlow(data.CurrentStepConfig, data, data.CurrentStepConfig);
            }
            catch (Exception ex)
            {
                var exceptionDetail = ex.ToString();
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[SubFlow] 执行 {data.CurrentStepConfig.Name} 异常: {exceptionDetail}");
            }

            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 执行子流程引用节点（Type=SubFlowRef）。
    /// </summary>
    public class ExecuteSubFlowReferenceStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModel)context.Workflow.Data;
            if (data.WorkflowCompleted || data.CurrentStepKind != StepExecutionKind.SubFlowReference)
                return ExecutionResult.Next();

            if (data.CurrentStepConfig == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[SubFlowRef] 当前步骤配置缺失: {data.Current}");
                return ExecutionResult.Next();
            }

            if (string.IsNullOrWhiteSpace(data.CurrentStepConfig.Ref))
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[SubFlowRef] 步骤 {data.CurrentStepConfig.Name} 缺少 Ref 字段");
                return ExecutionResult.Next();
            }

            StepConfig subDef;
            if (WorkflowServices.Subflows != null && WorkflowServices.Subflows.TryGet(data.CurrentStepConfig.Ref, out subDef))
            {
                UiEventBus.PublishLog($"[SubFlowRef] 执行子流程引用 {data.CurrentStepConfig.Ref} (from {data.CurrentStepConfig.Name})");
                try
                {
                    // TODO 需要确认逻辑是否正确？？？
                    new SubFlowExecutor().RunInlineSubFlow(subDef, data, data.CurrentStepConfig);
                }
                catch (Exception ex)
                {
                    var exceptionDetail = ex.ToString();
                    data.LastSuccess = false;
                    UiEventBus.PublishLog($"[SubFlowRef] 执行 {data.CurrentStepConfig.Ref} 异常: {exceptionDetail}");
                }
            }
            else
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[SubFlowRef] 未找到子流程引用: {data.CurrentStepConfig.Ref} (from {data.CurrentStepConfig.Name})");
            }

            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 处理下一跳调度：根据执行结果返回目标 StepId，若不存在下一步则结束流程。
    /// </summary>
    public class TransitionStep : StepBody
    {
        /// <summary>
        /// 当前步骤的静态定义，主要用于日志输出。
        /// </summary>
        public StepConfig StepConfig { get; set; }

        /// <summary>
        /// 成功分支对应的 StepId（若为空则表示流程终止）。
        /// </summary>
        public int? SuccessStepId { get; set; }

        /// <summary>
        /// 失败分支对应的 StepId（若为空则表示流程终止）。
        /// </summary>
        public int? FailureStepId { get; set; }

        /// <summary>
        /// 为日志准备的成功分支名称，便于定位配置问题。
        /// </summary>
        public string SuccessStepName { get; set; }

        /// <summary>
        /// 为日志准备的失败分支名称。
        /// </summary>
        public string FailureStepName { get; set; }

        /// <summary>
        /// 成功分支目标是否真实存在，用于打印缺失提示。
        /// </summary>
        public bool SuccessTargetExists { get; set; }

        /// <summary>
        /// 失败分支目标是否真实存在。
        /// </summary>
        public bool FailureTargetExists { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModel)context.Workflow.Data;

            Dbg.WfDbg(context, "Transition", $"LastSuccess={data.LastSuccess}");

            string sourceName = StepConfig != null ? StepConfig.Name : data.Current;
            string nextName;
            int? nextId;
            bool nextExists;

            if (data.LastSuccess)
            {
                nextName = string.IsNullOrWhiteSpace(SuccessStepName) && StepConfig != null
                    ? StepConfig.OnSuccess
                    : SuccessStepName;
                nextId = SuccessStepId;
                nextExists = SuccessTargetExists || string.IsNullOrWhiteSpace(nextName);
            }
            else
            {
                nextName = string.IsNullOrWhiteSpace(FailureStepName) && StepConfig != null
                    ? StepConfig.OnFailure
                    : FailureStepName;
                nextId = FailureStepId;
                nextExists = FailureTargetExists || string.IsNullOrWhiteSpace(nextName);
            }

            if (!nextExists && !string.IsNullOrWhiteSpace(nextName))
            {
                UiEventBus.PublishLog($"[Route] {sourceName} -> {nextName} 未找到对应定义，终止流程 | LastSuccess={data.LastSuccess}");
            }
            else
            {
                UiEventBus.PublishLog($"[Route] {sourceName} -> {(string.IsNullOrEmpty(nextName) ? "(结束)" : nextName)} | LastSuccess={data.LastSuccess}");
            }

            data.CurrentStepConfig = null;
            data.CurrentExecution = null;
            data.CurrentStepKind = StepExecutionKind.None;

            if (nextId.HasValue)
            {
                data.Current = nextName;
                return ExecutionResult.Outcome(nextId.Value);
            }

            data.Current = null;
            WorkflowCompletionHelper.CompleteWorkflow(data);
            return ExecutionResult.Next();
        }
    }

    /// <summary>
    /// 提供统一的流程收尾逻辑，确保 FinishTestSession 与 UI 通知只触发一次。
    /// </summary>
    internal static class WorkflowCompletionHelper
    {
        public static void CompleteWorkflow(FlowModel data)
        {
            if (data == null)
                return;

            if (data.WorkflowCompleted)
                return;

            data.WorkflowCompleted = true;
            DeviceServices.Db.FinishTestSession(data.SessionId, data.LastSuccess ? 1 : 0);
            UiEventBus.PublishCompleted(data.SessionId.ToString(), data.Model);
        }
    }

    /// <summary>
    /// 承载设备执行相关的通用辅助方法，供多个 StepBody 复用。
    /// </summary>
    internal static class DeviceExecutionHelper
    {
        public static Dictionary<string, object> ExecuteDeviceCommand(
            FlowModel data,
            string deviceName,
            string command,
            IDictionary<string, object> parameters,
            int timeoutMs,
            RetrySpec retry,
            string traceId,
            bool useLock)
        {
            int attempts = retry != null && retry.Attempts > 0 ? retry.Attempts : 1;
            int delayMs = retry != null && retry.DelayMs > 0 ? retry.DelayMs : 0;

            var baseContext = CreateBaseContext(data);
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    Func<Dictionary<string, object>> runner = () =>
                    {
                        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(baseContext.Cancellation))
                        {
                            if (timeoutMs > 0)
                                linked.CancelAfter(timeoutMs);

                            var runCtx = baseContext.CloneWithCancellation(linked.Token);

                            DeviceConfig devConf;
                            if (!DeviceServices.Devices.TryGetValue(deviceName, out devConf))
                                throw new Exception("Device not found: " + deviceName);

                            var execStep = new StepConfig
                            {
                                Name = $"{deviceName}:{command}",
                                Description = string.Empty,
                                Target = deviceName,
                                Command = command,
                                Parameters = parameters != null
                                    ? new Dictionary<string, object>(parameters)
                                    : new Dictionary<string, object>(),
                                ExpectedResults = new Dictionary<string, object>(),
                                TimeoutMs = timeoutMs
                            };

                            UiEventBus.PublishLog($"[Exec:{traceId}] -> {deviceName}.{command} (attempt {attempt}/{attempts})");
                            var outputs = DeviceServices.Factory.UseDevice(deviceName, devConf, dev =>
                            {
                                var res = dev.Execute(execStep, runCtx);
                                if (!res.Success)
                                    throw new Exception("Device exec failed: " + res.Message);
                                return res.Outputs ?? new Dictionary<string, object>();
                            });

                            return outputs ?? new Dictionary<string, object>();
                        }
                    };

                    if (useLock)
                        return DeviceLockRegistry.RunWithLock(deviceName, runner);

                    return runner();
                }
                catch (Exception ex)
                {
                    if (attempt >= attempts)
                        throw;

                    var exceptionDetail = ex.ToString();
                    UiEventBus.PublishLog($"[Retry] {deviceName}.{command} 失败：{exceptionDetail}，{delayMs}ms 后重试（第 {attempt} 次，共 {attempts} 次）");
                    SafeDelay(delayMs, baseContext.Cancellation);
                }
            }

            return new Dictionary<string, object>();
        }

        public static void FireAndForgetExtra(FlowModel data, ExtraDeviceSpec spec, RetrySpec fallback, string traceId)
        {
            var retry = spec.Retry ?? fallback ?? new RetrySpec { Attempts = 1, DelayMs = 0 };
            var token = data != null ? data.Cancellation : CancellationToken.None;
            Task.Run(() =>
            {
                try
                {
                    ExecuteDeviceCommand(data, spec.Target, spec.Command, spec.Parameters, spec.TimeoutMs, retry, traceId, useLock: true);
                }
                catch (Exception ex)
                {
                    UiEventBus.PublishLog($"[Extra-Forget] {spec.Target}.{spec.Command} 执行异常：{ex.Message}");
                }
            }, token);
        }

        public static Dictionary<string, object> MergeOutputs(
            AggregationMode mode,
            Dictionary<string, object> main,
            Dictionary<string, Dictionary<string, object>> extras)
        {
            var root = main != null ? new Dictionary<string, object>(main) : new Dictionary<string, object>();

            if (extras == null || extras.Count == 0)
                return root;

            if (mode == AggregationMode.Namespace)
            {
                foreach (var kv in extras)
                    root[kv.Key] = kv.Value ?? new Dictionary<string, object>();
                return root;
            }

            foreach (var kv in extras)
            {
                var prefix = kv.Key;
                var dict = kv.Value ?? new Dictionary<string, object>();
                foreach (var kv2 in dict)
                {
                    root[$"{prefix}.{kv2.Key}"] = kv2.Value;
                }
            }

            return root;
        }

        public static string BuildMessage(bool finalSuccess, Exception mainError, bool extrasSucceeded, DeviceExecSpec spec)
        {
            if (!finalSuccess)
            {
                if (mainError != null)
                    return "main failed: " + mainError.Message;
                if (!extrasSucceeded && !spec.ContinueOnExtraFailure)
                    return "extras failed";
            }
            return "ok";
        }

        public static void SafeDelay(int ms, CancellationToken token)
        {
            if (ms <= 0)
                return;
            try
            {
                Task.Delay(ms, token).Wait(token);
            }
            catch
            {
            }
        }

        private static StepContext CreateBaseContext(FlowModel data)
        {
            var model = data != null && !string.IsNullOrWhiteSpace(data.Model)
                ? data.Model
                : WorkflowServices.FlowCfg != null ? WorkflowServices.FlowCfg.Model : string.Empty;
            var token = data != null ? data.Cancellation : CancellationToken.None;

            if (DeviceServices.Context != null)
                return DeviceServices.Context.CloneWithCancellation(token);

            return new StepContext(model, token);
        }
    }

    /// <summary>
    /// 设备级互斥锁注册表，负责为不同设备提供独立的信号量。
    /// </summary>
    internal static class DeviceLockRegistry
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        public static IDisposable Acquire(string device)
        {
            if (string.IsNullOrWhiteSpace(device))
                return new NoopLock();

            var sem = Locks.GetOrAdd(device, _ => new SemaphoreSlim(1, 1));
            sem.Wait();
            return new Releaser(sem);
        }

        public static T RunWithLock<T>(string device, Func<T> action)
        {
            using (Acquire(device))
            {
                return action();
            }
        }

        public static T WithLock<T>(string device, Func<T> action)
        {
            return RunWithLock(device, action);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _sem;
            private bool _disposed;

            public Releaser(SemaphoreSlim sem)
            {
                _sem = sem;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                try { _sem.Release(); }
                catch { }
            }
        }

        private sealed class NoopLock : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
