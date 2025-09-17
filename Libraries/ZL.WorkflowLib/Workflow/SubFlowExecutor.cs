using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Workflow.Flows;

namespace ZL.WorkflowLib.Workflow
{
    /// <summary>
    ///     子流程执行结果模型，供 <see cref="SubFlowExecutor"/> 与 <see cref="DeviceExecStep.ExecuteSingleStep"/> 复用。
    /// </summary>
    public sealed class OrchTaskResult
    {
        /// <summary>执行是否成功。</summary>
        public bool Success { get; set; }

        /// <summary>执行信息，用于 UI 与日志展示。</summary>
        public string Message { get; set; }

        /// <summary>设备输出字典。</summary>
        public Dictionary<string, object> Outputs { get; set; } = new Dictionary<string, object>();

        /// <summary>执行开始时间。</summary>
        public DateTime StartedAt { get; set; }

        /// <summary>执行结束时间。</summary>
        public DateTime FinishedAt { get; set; }
    }

    /// <summary>
    ///     子流程执行器：负责将代码/配置中定义的子流程注册为 WorkflowCore 工作流，并在运行期调用。
    /// </summary>
    public class SubFlowExecutor
    {
        private const int DefaultStepTimeoutMs = 30000;

        private static readonly object _registerLock = new object();
        private static readonly HashSet<string> _registeredWorkflows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void MarkWorkflowRegistered(string workflowId)
        {
            if (string.IsNullOrEmpty(workflowId))
                return;
            lock (_registerLock)
            {
                _registeredWorkflows.Add(workflowId);
            }
        }

        private readonly IWorkflowHost _host;
        private readonly SubflowRegistry _registry;

        public SubFlowExecutor()
            : this(null, null)
        {
        }

        internal SubFlowExecutor(IWorkflowHost host, SubflowRegistry registry)
        {
            _host = host ?? WorkflowServices.WorkflowHost;
            _registry = registry ?? WorkflowServices.Subflows;
        }

        /// <summary>
        ///     执行行内子流程（主流程配置中直接展开 Steps）。
        /// </summary>
        public bool RunInlineSubFlow(StepConfig stepCfg, FlowData data, StepConfig parentStepCfg)
        {
            return RunSubFlowInternal(stepCfg, data, parentStepCfg);
        }

        /// <summary>
        ///     执行注册表中的子流程（通过 Ref 引用）。
        /// </summary>
        public bool RunRegisteredSubFlow(string subflowName, FlowData data, StepConfig parentStepCfg)
        {
            if (string.IsNullOrEmpty(subflowName))
            {
                if (data != null)
                    data.LastSuccess = false;
                UiEventBus.PublishLog("[SubFlow] 子流程引用名称为空，无法执行");
                return false;
            }

            StepConfig definition;
            if (_registry == null || !_registry.TryGet(subflowName, out definition))
            {
                if (data != null)
                    data.LastSuccess = false;
                UiEventBus.PublishLog("[SubFlow] 未找到子流程定义: " + subflowName);
                return false;
            }

            return RunSubFlowInternal(definition, data, parentStepCfg);
        }

        private bool RunSubFlowInternal(StepConfig definition, FlowData data, StepConfig parentStepCfg)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var host = EnsureHost();
            EnsureWorkflowRegistered(host, definition);

            var workflowId = JsonSubFlowWorkflow.BuildWorkflowId(definition.Name);
            var subData = CloneFlowData(data);

            UiEventBus.PublishLog(string.Format("[SubFlow] 开始 {0}, 子步骤共有【{1}】步", definition.Name, definition.Steps != null ? definition.Steps.Count : 0));

            try
            {
                var runId = host.StartWorkflow(workflowId, subData).GetAwaiter().GetResult();
                //WorkflowInstance instance = host.WaitForWorkflowToComplete(runId, data.Cancellation).GetAwaiter().GetResult();
                var instance = host.WaitForWorkflowToCompleteAsync(runId, data.Cancellation).GetAwaiter().GetResult();
                bool success = false;
                if (instance.Status == WorkflowStatus.Complete)
                {
                    // 成功
                    success = true;
                }
                else if (instance.Status == WorkflowStatus.Terminated)
                {
                    // 失败
                    success = false;
                }
                data.LastSuccess = success;

                if (!success)
                    UiEventBus.PublishLog(string.Format("[SubFlow] {0} 执行失败", definition.Name));
                return success;
            }
            catch (OperationCanceledException)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog(string.Format("[SubFlow] {0} 在等待期间被取消", definition.Name));
                return false;
            }
            catch (Exception ex)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog(string.Format("[SubFlow] 执行 {0} 异常: {1}", definition.Name, ex.Message));
                return false;
            }
            finally
            {
                UiEventBus.PublishLog(string.Format("[SubFlow] 结束 {0}, Success={1}, 下一步【{2}】", definition.Name, data.LastSuccess, parentStepCfg != null ? parentStepCfg.OnSuccess : string.Empty));
            }
        }

        private IWorkflowHost EnsureHost()
        {
            if (_host != null)
                return _host;
            if (WorkflowServices.WorkflowHost == null)
                throw new InvalidOperationException("WorkflowCore Host 尚未初始化，无法启动子流程");
            return WorkflowServices.WorkflowHost;
        }

        private void EnsureWorkflowRegistered(IWorkflowHost host, StepConfig definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Name))
                throw new ArgumentException("子流程缺少名称，无法注册", nameof(definition));
            if (definition.Steps == null || definition.Steps.Count == 0)
                throw new ArgumentException("子流程未包含任何步骤", nameof(definition));

            var workflowId = JsonSubFlowWorkflow.BuildWorkflowId(definition.Name);

            lock (_registerLock)
            {
                if (_registeredWorkflows.Contains(workflowId))
                    return;

                host.Registry.RegisterWorkflow(new JsonSubFlowWorkflow(definition));
                MarkWorkflowRegistered(workflowId);
            }
        }

        private static FlowData CloneFlowData(FlowData source)
        {
            if (source == null)
                return new FlowData();

            return new FlowData
            {
                Model = source.Model,
                Sn = source.Sn,
                SessionId = source.SessionId,
                Cancellation = source.Cancellation,
                LastSuccess = true,
                WorkflowCompleted = false,
                Current = null,
                CurrentStepConfig = null,
                CurrentStepKind = StepExecutionKind.None,
                CurrentExecution = null
            };
        }

        /// <summary>
        ///     WorkflowCore 子流程节点实际调用的执行函数。
        /// </summary>
        internal static bool ExecuteSequentialSubflow(string subflowName, IList<StepConfig> steps, FlowData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (steps == null || steps.Count == 0)
            {
                data.LastSuccess = true;
                UiEventBus.PublishLog(string.Format("[SubFlow] {0} 未包含可执行子步骤，直接视为成功", subflowName));
                return true;
            }

            var sharedCtx = CreateSharedContext(data);
            bool success = true;

            for (int i = 0; i < steps.Count; i++)
            {
                var rawStep = steps[i];
                if (rawStep == null)
                    continue;

                var execStep = StepUtils.BuildExecutableStep(rawStep, data);
                var contextName = !string.IsNullOrEmpty(execStep.Name) ? execStep.Name : string.Format("step_{0}", i);

                ApplyDefaultTimeoutIfNeeded(execStep, contextName);
                var resourceId = ResolveResourceId(execStep, contextName);
                var lockKey = NormalizeResourceKey(resourceId, execStep);

                OrchTaskResult taskResult = DeviceLockRegistry.WithLock(lockKey, delegate
                {
                    return DeviceExecStep.ExecuteSingleStep(execStep, sharedCtx);
                });

                ExpectedResultEvaluator.ApplyToTaskResult(execStep, taskResult, contextName, false, true);
                PersistTaskResult(execStep, taskResult, data);

                UiEventBus.PublishLog(string.Format("[SubStep] {0} | Success={1} | Msg={2}", execStep.Name, taskResult.Success, taskResult.Message));

                if (!taskResult.Success)
                {
                    success = false;
                    break;
                }
            }

            data.LastSuccess = success;
            return success;
        }

        private static StepContext CreateSharedContext(FlowData data)
        {
            var baseModel = data != null ? data.Model : DeviceServices.Config != null ? DeviceServices.Config.Model : string.Empty;
            var token = data != null ? data.Cancellation : CancellationToken.None;
            if (DeviceServices.Context != null)
                return DeviceServices.Context.CloneWithCancellation(token);
            return new StepContext(baseModel, token);
        }

        private static string NormalizeResourceKey(string resourceId, StepConfig step)
        {
            if (!string.IsNullOrEmpty(resourceId))
                return resourceId;
            if (!string.IsNullOrEmpty(step.Target))
                return step.Target;
            if (!string.IsNullOrEmpty(step.Target))
                return step.Target;
            return !string.IsNullOrEmpty(step.Name) ? step.Name : Guid.NewGuid().ToString("N");
        }

        private static void ApplyDefaultTimeoutIfNeeded(StepConfig step, string contextName)
        {
            if (step == null)
                return;
            if (step.TimeoutMs > 0)
                return;

            step.TimeoutMs = DefaultStepTimeoutMs;
            UiEventBus.PublishLog(string.Format("[SubFlow] 步骤 {0} 未设置 TimeoutMs，使用默认值 {1}ms", contextName, DefaultStepTimeoutMs));
        }

        private static string ResolveResourceId(StepConfig step, string contextName)
        {
            if (step == null)
                return null;

            if (step.Parameters != null)
            {
                object value;
                if (step.Parameters.TryGetValue("__resourceId", out value) || step.Parameters.TryGetValue("resourceId", out value))
                {
                    var candidate = value != null ? value.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(candidate))
                        return candidate.Trim();
                    UiEventBus.PublishLog(string.Format("[SubFlow] 步骤 {0} 提供的 ResourceId 为空字符串", contextName));
                }
            }

            if (!string.IsNullOrEmpty(step.Target))
            {
                UiEventBus.PublishLog(string.Format("[SubFlow] 步骤 {0} 默认使用设备名 {1} 作为资源锁", contextName, step.Target));
                return step.Target;
            }

            if (!string.IsNullOrEmpty(step.Target))
                return step.Target;

            UiEventBus.PublishLog(string.Format("[SubFlow] 步骤 {0} 未能推断资源锁，将退化为步骤名称", contextName));
            return step.Name;
        }

        private static void PersistTaskResult(StepConfig step, OrchTaskResult result, FlowData data)
        {
            if (step == null || data == null)
                return;

            DeviceServices.Db.AppendStep(
                data.SessionId,
                data.Model,
                data.Sn,
                step.Name,
                step.Description,
                step.Target,
                step.Command,
                JsonConvert.SerializeObject(step.Parameters ?? new Dictionary<string, object>()),
                JsonConvert.SerializeObject(step.ExpectedResults ?? new Dictionary<string, object>()),
                JsonConvert.SerializeObject(result != null ? result.Outputs ?? new Dictionary<string, object>() : new Dictionary<string, object>()),
                result != null && result.Success ? 1 : 0,
                result != null ? result.Message : null,
                result != null ? result.StartedAt : DateTime.Now,
                result != null ? result.FinishedAt : DateTime.Now);
        }
    }
}
