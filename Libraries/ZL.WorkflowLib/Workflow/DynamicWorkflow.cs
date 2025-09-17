using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib.Workflow
{
    public sealed class DynamicLoopWorkflow : IWorkflow<FlowData>
    {
        private readonly FlowConfig _config;
        private readonly string _id;

        public DynamicLoopWorkflow(FlowConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            if (config.TestSteps == null) config.TestSteps = new List<StepConfig>();
            _config = config;

            UiEventBus.PublishLog("[DEBUG] Build for " + _config.Model + ", steps=" + _config.TestSteps.Count);
            foreach (var step in _config.TestSteps)
            {
                UiEventBus.PublishLog("[DEBUG] Step: " + step.Name + ", Type=" + step.Type + ", OnSuccess=" + step.OnSuccess + ", OnFailure=" + step.OnFailure);
            }

            var model = string.IsNullOrWhiteSpace(config.Model) ? "unknown" : config.Model.Trim();
            _id = "dyn:" + model;
        }

        public string Id { get { return _id; } }
        public int Version { get { return 1; } }

        public void Build(IWorkflowBuilder<FlowData> builder)
        {
            if (builder == null)
                throw new ArgumentNullException("builder");

            var init = builder.StartWith<InitStep>();
            var stepList = _config.TestSteps;
            var pipelines = new Dictionary<string, StepPipeline>(StringComparer.OrdinalIgnoreCase);
            IStepBuilder<FlowData, TransitionStep> lastTransition = null;

            // 全局边表（去重用）
            var edges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) 构建 pipeline
            for (int i = 0; i < stepList.Count; i++)
            {
                var rawStep = stepList[i];
                if (rawStep == null || string.IsNullOrWhiteSpace(rawStep.Name))
                    continue;

                var stepDef = rawStep;
                StepPipeline pipeline = (lastTransition == null)
                    ? BuildPipeline(init, stepDef)
                    : BuildPipeline(lastTransition, stepDef);

                pipelines[pipeline.Config.Name] = pipeline;
                lastTransition = pipeline.Transition;
            }

            // 2) 起点
            var firstPipeline = ResolveFirstPipeline(stepList, pipelines);
            if (firstPipeline != null && firstPipeline.EntryId.HasValue)
            {
                int stepId = firstPipeline.EntryId.Value;
                string stepName = firstPipeline.Config != null ? firstPipeline.Config.Name : string.Empty;
                init.Input<int?>(s => s.FirstStepId, data => stepId);
                init.Input<string>(s => s.FirstStepName, data => stepName);

                AddOutcomeMapping(init, stepId, stepId);
                ClearNextStep(init);
            }
            else
            {
                init.Input<int?>(s => s.FirstStepId, d => (int?)null);
                init.Input<string>(s => s.FirstStepName, d => string.Empty);
            }

            // 3) 配置 Transition（成功/失败）
            foreach (var pipe in pipelines.Values)
            {
                ConfigureTransition(pipe, pipelines, edges);
            }

            // 4) 处理 DependsOn（多前驱）
            foreach (var pipe in pipelines.Values)
            {
                var cfg = pipe.Config;
                if (cfg.DependsOn == null || cfg.DependsOn.Count == 0)
                    continue;

                for (int i = 0; i < cfg.DependsOn.Count; i++)
                {
                    var dep = cfg.DependsOn[i];
                    if (string.IsNullOrWhiteSpace(dep)) continue;
                    if (!pipelines.ContainsKey(dep)) continue;

                    var depPipe = pipelines[dep];
                    if (depPipe.EntryId.HasValue && pipe.EntryId.HasValue)
                    {
                        // 替换为统一 AddEdge（自动去重 + 建立 Outcome 映射）
                        AddEdge(dep, cfg.Name, depPipe, pipe, edges);
                    }
                }
            }

            // 这里通过显式清空上一节点的 NextStep，确保每次链式构建都以手动配置的跳转为准
            ClearNextStep(previous);
            // 5) 检查入度
            foreach (var p in pipelines.Values)
            {
                int incoming = 0;
                foreach (var q in pipelines.Values)
                {
                    if (string.Equals(q.Config.OnSuccess, p.Config.Name, StringComparison.OrdinalIgnoreCase)) incoming++;
                    if (string.Equals(q.Config.OnFailure, p.Config.Name, StringComparison.OrdinalIgnoreCase)) incoming++;
                    if (p.Config.DependsOn != null && p.Config.DependsOn.Contains(q.Config.Name, StringComparer.OrdinalIgnoreCase)) incoming++;
                }
                if (incoming > 1)
                {
                    UiEventBus.PublishLog("[BuildCheck] Step=" + p.Config.Name + " 入边数=" + incoming + "，请检查 JSON 是否存在重复 DependsOn 与 OnSuccess/OnFailure");
                }
            }
        }

        private static StepPipeline BuildPipeline<TPrev>(IStepBuilder<FlowData, TPrev> previous, StepConfig stepConfig)
            where TPrev : StepBody
        {
            var entry = previous.Then<ResolveStepContextStep>();
            entry.Input<StepConfig>(step => step.StepConfig, data => stepConfig);

            string type = (stepConfig != null && !string.IsNullOrWhiteSpace(stepConfig.Type))
                ? stepConfig.Type.Trim()
                : "Normal";

            IStepBuilder<FlowData, TransitionStep> transition;

            if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
            {
                var sub = entry.Then<ExecuteSubFlowStep>();
                transition = sub.Then<TransitionStep>();
            }
            else if (string.Equals(type, "SubFlowRef", StringComparison.OrdinalIgnoreCase))
            {
                var subRef = entry.Then<ExecuteSubFlowReferenceStep>();
                transition = subRef.Then<TransitionStep>();
            }
            else
            {
                var prepare = entry.Then<PrepareDeviceExecutionStep>();

                // BeforeMain
                prepare.When(d => d.CurrentExecution != null && d.CurrentExecution.Specification.HasExtrasForPhase(ExtraDevicePhase.BeforeMain))
                .Do(extraBefore => extraBefore
                    .StartWith<ExtraDeviceStep>()
                        .Input(s => s.Phase, _ => ExtraDevicePhase.BeforeMain)
                        .WithRetry(BuildMainRetryOptions)
                        .WithDelay(_ => new DelayOptions())
                );

                // Parallel
                prepare.When(data => ShouldRunParallel(data))
                .Do(parallel => parallel
                    .Parallel()
                        .Do(mainBranch => mainBranch
                            .StartWith<DeviceLockStep>()
                                .Input<string>(s => s.DeviceName,
                                d => d.CurrentExecution != null && d.CurrentExecution.ExecutableStep != null ? d.CurrentExecution.ExecutableStep.Target : null)
                            .Then<MainDeviceStep>()
                                .WithRetry(BuildMainRetryOptions)
                                .WithDelay(BuildMainDelayOptions))
                        .Do(extraBranch => extraBranch
                            .StartWith<ExtraDeviceStep>()
                                .Input<ExtraDevicePhase>(s => s.Phase, _ => ExtraDevicePhase.WithMain)
                                .WithRetry(BuildMainRetryOptions)
                                .WithDelay(_ => new DelayOptions()))
                    .Join()
                );

                // Sequential
                prepare.When(data => ShouldRunMainSequentially(data))
                .Do(mainSeq => mainSeq
                    .StartWith<DeviceLockStep>()
                        .Input<string>(s => s.DeviceName,
                        d => d.CurrentExecution != null && d.CurrentExecution.ExecutableStep != null ? d.CurrentExecution.ExecutableStep.Target : null)
                    .Then<MainDeviceStep>()
                        .WithRetry(BuildMainRetryOptions)
                        .WithDelay(BuildMainDelayOptions)
                );

                // AfterMain
                prepare.When(d => d.CurrentExecution != null &&
                                  d.CurrentExecution.Specification.HasExtrasForPhase(ExtraDevicePhase.AfterMain))
                .Do(extraAfter => extraAfter
                    .StartWith<ExtraDeviceStep>()
                        .Input<ExtraDevicePhase>(s => s.Phase, _ => ExtraDevicePhase.AfterMain)
                        .WithRetry(BuildMainRetryOptions)
                        .WithDelay(_ => new DelayOptions())
                );

                var finalize = prepare.Then<FinalizeDeviceStep>();
                transition = finalize.Then<TransitionStep>();
            }

            return new StepPipeline
            {
                Config = stepConfig,
                Entry = entry,
                EntryId = TryGetStepId(entry),
                Transition = transition
            };
        }

        private static void ConfigureTransition(StepPipeline pipeline, IDictionary<string, StepPipeline> pipelines, HashSet<string> edges)
        {
            if (pipeline == null || pipeline.Transition == null)
                return;

            var cfg = pipeline.Config;
            pipeline.Transition.Input<StepConfig>(s => s.StepConfig, data => cfg);

            var success = ResolveTarget(cfg != null ? cfg.OnSuccess : null, pipelines);
            var failure = ResolveTarget(cfg != null ? cfg.OnFailure : null, pipelines);

            UiEventBus.PublishLog("[BuildWire] Step=" + cfg.Name + ", Success=" + success.StepName + "(" + (success.StepId.HasValue ? success.StepId.Value.ToString() : "-") + ") Failure=" + failure.StepName + "(" + (failure.StepId.HasValue ? failure.StepId.Value.ToString() : "-") + ")");

            // 去重：成功/失败同目标
            if (success.StepId.HasValue && failure.StepId.HasValue && success.StepId.Value == failure.StepId.Value)
            {
                UiEventBus.PublishLog("[BuildDedup] " + cfg.Name + " 成功/失败路由指向同一个节点 " + success.StepName + "，自动去重");
                failure.StepId = null;
            }
            // —— 新增：统一用 AddEdge 建立 Success / Failure 路由（并全局去重）
            if (success.StepId.HasValue)
            {
                StepPipeline toPipe;
                if (pipelines.TryGetValue(success.StepName, out toPipe))
                {
                    AddEdge(cfg.Name, success.StepName, pipeline, toPipe, edges);
                }
            // 成功配置完路由后再清理默认 NextStep，避免后续继续调用 Then 时出现意外跳转
            ClearNextStep(pipeline.Transition);
                // 保留原来的属性注入（不改动）
                pipeline.Transition.Input<int?>(s => s.SuccessStepId, data => success.StepId);
            }

            if (failure.StepId.HasValue)
            {
                StepPipeline toPipe2;
                if (pipelines.TryGetValue(failure.StepName, out toPipe2))
                {
                    AddEdge(cfg.Name, failure.StepName, pipeline, toPipe2, edges);
                }
                // 保留原来的属性注入（不改动）
                pipeline.Transition.Input<int?>(s => s.FailureStepId, data => failure.StepId);
            }

            pipeline.Transition.Input<string>(s => s.SuccessStepName, data => success.StepName);
            pipeline.Transition.Input<bool>(s => s.SuccessTargetExists, data => success.Exists);

            pipeline.Transition.Input<string>(s => s.FailureStepName, data => failure.StepName);
            pipeline.Transition.Input<bool>(s => s.FailureTargetExists, data => failure.Exists);
        }

        // 统一建边 + 去重 + 真正添加 Outcome 映射
        private static void AddEdge(string fromName, string toName, StepPipeline fromPipe, StepPipeline toPipe, HashSet<string> edges)
        {
            if (string.IsNullOrWhiteSpace(fromName)) return;
            if (string.IsNullOrWhiteSpace(toName)) return;
            if (fromPipe == null || toPipe == null) return;
            if (!fromPipe.EntryId.HasValue || !toPipe.EntryId.HasValue) return;

            string edgeKey = fromName + "->" + toName;
            if (edges.Add(edgeKey))
            {
                UiEventBus.PublishLog("[BuildWire] AddEdge " + fromName + " -> " + toName);
                // Outcome.Value 取“目标 StepId”，NextStep 也指向“目标 StepId”
                AddOutcomeMapping(fromPipe.Transition, toPipe.EntryId.Value, toPipe.EntryId.Value);
            }
            else
            {
                UiEventBus.PublishLog("[BuildDedup] 跳过重复边 " + fromName + " -> " + toName);
            }
        }
        private static StepPipeline ResolveFirstPipeline(IList<StepConfig> steps, IDictionary<string, StepPipeline> pipelines)
        {
            if (steps == null || pipelines == null)
                return null;

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null || string.IsNullOrWhiteSpace(step.Name)) continue;

                bool hasDepends = step.DependsOn != null && step.DependsOn.Count > 0;
                if (hasDepends) continue;

                StepPipeline p;
                if (pipelines.TryGetValue(step.Name, out p)) return p;
            }

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null || string.IsNullOrWhiteSpace(step.Name)) continue;
                StepPipeline p;
                if (pipelines.TryGetValue(step.Name, out p)) return p;
            }

            return null;
        }

        private static StepTarget ResolveTarget(string targetName, IDictionary<string, StepPipeline> pipelines)
        {
            if (string.IsNullOrWhiteSpace(targetName))
                return new StepTarget { StepId = null, StepName = string.Empty, Exists = true };

            StepPipeline p;
            if (pipelines.TryGetValue(targetName, out p) && p.EntryId.HasValue)
            {
                return new StepTarget
                {
                    StepId = p.EntryId.Value,
                    StepName = p.Config != null ? p.Config.Name : targetName,
                    Exists = true
                };
            }

            return new StepTarget { StepId = null, StepName = targetName, Exists = false };
        }

        private static void AddOutcomeMapping<TStep>(IStepBuilder<FlowData, TStep> from, int outcomeValue, int nextStepId) where TStep : StepBody
        {
            if (from == null) return;
            var stepProp = from.GetType().GetProperty("Step");
            if (stepProp == null) return;
            var stepObj = stepProp.GetValue(from, null);
            if (stepObj == null) return;
            var outcomesProp = stepObj.GetType().GetProperty("Outcomes");
            if (outcomesProp == null) return;
            var outcomes = outcomesProp.GetValue(stepObj, null) as IList<IStepOutcome>;
            if (outcomes == null) return;

            var vo = new ValueOutcome
            {
                Value = (Expression<Func<object, object>>)(data => outcomeValue),
                NextStep = nextStepId
            };

            outcomes.Add(vo);
        }

        private static void ClearNextStep<TStep>(IStepBuilder<FlowData, TStep> from) where TStep : StepBody
        {
            if (from == null) return;
            var stepProp = from.GetType().GetProperty("Step");
            if (stepProp == null) return;
            var stepObj = stepProp.GetValue(from, null);
            if (stepObj == null) return;
            var nextProp = stepObj.GetType().GetProperty("NextStep");
            if (nextProp != null)
            {
                // WorkflowCore 2.x/3.x 中 NextStep 可能是引用类型（StepBase）或可空值类型，统一置为默认值
                object nextValue = null;
                if (nextProp.PropertyType.IsValueType && Nullable.GetUnderlyingType(nextProp.PropertyType) == null)
                {
                    nextValue = Activator.CreateInstance(nextProp.PropertyType);
                }
                nextProp.SetValue(stepObj, nextValue, null);
            }

            var nextIdProp = stepObj.GetType().GetProperty("NextStepId");
            if (nextIdProp != null)
            {
                // WorkflowCore 3.15 将 NextStepId 独立成属性，这里同样清空以避免默认自动衔接
                object nextIdValue = null;
                if (nextIdProp.PropertyType.IsValueType && Nullable.GetUnderlyingType(nextIdProp.PropertyType) == null)
                {
                    nextIdValue = Activator.CreateInstance(nextIdProp.PropertyType);
                }
                nextIdProp.SetValue(stepObj, nextIdValue, null);
            }
        }

        private static int? TryGetStepId<TStep>(IStepBuilder<FlowData, TStep> builder) where TStep : StepBody
        {
            if (builder == null) return null;
            var stepProp = builder.GetType().GetProperty("Step");
            if (stepProp == null) return null;
            var stepObj = stepProp.GetValue(builder, null);
            if (stepObj == null) return null;
            var idProp = stepObj.GetType().GetProperty("Id");
            if (idProp == null) return null;
            var value = idProp.GetValue(stepObj, null);
            if (value == null) return null;
            return Convert.ToInt32(value);
        }

        private sealed class StepPipeline
        {
            public StepConfig Config { get; set; }
            public IStepBuilder<FlowData, ResolveStepContextStep> Entry { get; set; }
            public int? EntryId { get; set; }
            public IStepBuilder<FlowData, TransitionStep> Transition { get; set; }
        }

        private sealed class StepTarget
        {
            public int? StepId { get; set; }
            public string StepName { get; set; }
            public bool Exists { get; set; }
        }

        private static RetryOptions BuildMainRetryOptions(FlowData data)
        {
            var spec = data != null && data.CurrentExecution != null ? data.CurrentExecution.Specification : null;
            if (spec == null || spec.MainRetry == null)
                return new RetryOptions { Attempts = 1, DelayMs = 0 };
            return new RetryOptions { Attempts = spec.MainRetry.Attempts, DelayMs = spec.MainRetry.DelayMs };
        }

        private static DelayOptions BuildMainDelayOptions(FlowData data)
        {
            var spec = data != null && data.CurrentExecution != null ? data.CurrentExecution.Specification : null;
            if (spec == null)
                return new DelayOptions();
            return new DelayOptions { PreDelayMs = spec.PreDelayMs, PostDelayMs = spec.PostDelayMs };
        }

        private static bool ShouldRunParallel(FlowData data)
        {
            if (data == null || data.CurrentExecution == null || data.CurrentExecution.Specification == null)
                return false;

            var spec = data.CurrentExecution.Specification;
            if (!spec.HasExtrasForPhase(ExtraDevicePhase.WithMain))
                return false;

            return spec.Mode == ExecMode.Parallel || spec.Mode == ExecMode.ExtrasFirst;
        }

        private static bool ShouldRunMainSequentially(FlowData data)
        {
            if (data == null || data.CurrentExecution == null || data.CurrentExecution.Specification == null)
                return true;

            var spec = data.CurrentExecution.Specification;
            if (!spec.HasExtrasForPhase(ExtraDevicePhase.WithMain))
                return true;

            return spec.Mode == ExecMode.MainFirst;
        }
    }
}
