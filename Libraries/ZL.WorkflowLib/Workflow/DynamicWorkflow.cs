using System;
using System.Collections.Generic;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.DeviceLib;
using ZL.DeviceLib.Models;
namespace ZL.WorkflowLib.Workflow
{
    public class DynamicLoopWorkflow : IWorkflow<FlowData>
    {
        public string Id => "DynamicLoopWorkflow";
        public int Version => 1;
        public void Build(IWorkflowBuilder<FlowData> builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            var init = builder.StartWith<InitStep>();

            var stepList = DeviceServices.Config != null && DeviceServices.Config.TestSteps != null
                ? DeviceServices.Config.TestSteps
                : new List<StepConfig>();

            var pipelines = new Dictionary<string, StepPipeline>(StringComparer.OrdinalIgnoreCase);
            IStepBuilder<FlowData, TransitionStep> lastTransition = null;

            foreach (var rawStep in stepList)
            {
                if (rawStep == null || string.IsNullOrWhiteSpace(rawStep.Name))
                    continue;

                var stepDef = rawStep;
                StepPipeline pipeline = lastTransition == null
                    ? BuildPipeline(init, stepDef)
                    : BuildPipeline(lastTransition, stepDef);

                pipelines[pipeline.Config.Name] = pipeline;
                lastTransition = pipeline.Transition;
            }

            var firstPipeline = ResolveFirstPipeline(stepList, pipelines);
            if (firstPipeline != null && firstPipeline.EntryId.HasValue)
            {
                init.Input(step => step.FirstStepId, data => (int?)firstPipeline.EntryId.Value);
                init.Input(step => step.FirstStepName, data => firstPipeline.Config != null ? firstPipeline.Config.Name : string.Empty);
            }
            else
            {
                init.Input(step => step.FirstStepId, data => (int?)null);
                init.Input(step => step.FirstStepName, data => string.Empty);
            }

            foreach (var pipeline in pipelines.Values)
            {
                ConfigureTransition(pipeline, pipelines);
            }
        }

        private static StepPipeline BuildPipeline<TStep>(IStepBuilder<FlowData, TStep> previous, StepConfig stepConfig)
            where TStep : StepBody
        {
            var entry = previous.Then<ResolveStepContextStep>();
            entry.Input(step => step.StepConfig, data => stepConfig);

            string type = stepConfig != null && !string.IsNullOrWhiteSpace(stepConfig.Type)
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

                prepare.When(data => data.CurrentExecution != null &&
                                      data.CurrentExecution.Specification.HasExtrasForPhase(ExtraDevicePhase.BeforeMain))
                    .Do(extraBefore => extraBefore
                        .StartWith<ExtraDeviceStep>()
                            .Input(step => step.Phase, _ => ExtraDevicePhase.BeforeMain)
                            .WithRetry(BuildMainRetryOptions)
                            .WithDelay(_ => new DelayOptions()));

                prepare.When(ShouldRunParallel)
                    .Do(parallel => parallel
                        .Parallel()
                            .Do(mainBranch => mainBranch
                                .StartWith<DeviceLockStep>()
                                    .Input(step => step.DeviceName,
                                           data => data.CurrentExecution != null && data.CurrentExecution.ExecutableStep != null
                                               ? data.CurrentExecution.ExecutableStep.Device
                                               : null)
                                .Then<MainDeviceStep>()
                                    .WithRetry(BuildMainRetryOptions)
                                    .WithDelay(BuildMainDelayOptions))
                            .Do(extraBranch => extraBranch
                                .StartWith<ExtraDeviceStep>()
                                    .Input(step => step.Phase, _ => ExtraDevicePhase.WithMain)
                                    .WithRetry(BuildMainRetryOptions)
                                    .WithDelay(_ => new DelayOptions()))
                        .Join());

                prepare.When(ShouldRunMainSequentially)
                    .Do(mainSeq => mainSeq
                        .StartWith<DeviceLockStep>()
                            .Input(step => step.DeviceName,
                                   data => data.CurrentExecution != null && data.CurrentExecution.ExecutableStep != null
                                       ? data.CurrentExecution.ExecutableStep.Device
                                       : null)
                        .Then<MainDeviceStep>()
                            .WithRetry(BuildMainRetryOptions)
                            .WithDelay(BuildMainDelayOptions));

                prepare.When(data => data.CurrentExecution != null &&
                                      data.CurrentExecution.Specification.HasExtrasForPhase(ExtraDevicePhase.AfterMain))
                    .Do(extraAfter => extraAfter
                        .StartWith<ExtraDeviceStep>()
                            .Input(step => step.Phase, _ => ExtraDevicePhase.AfterMain)
                            .WithRetry(BuildMainRetryOptions)
                            .WithDelay(_ => new DelayOptions()));

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

        private static StepPipeline ResolveFirstPipeline(IList<StepConfig> steps, IDictionary<string, StepPipeline> pipelines)
        {
            if (steps == null || pipelines == null)
                return null;

            foreach (var step in steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.Name))
                    continue;

                bool hasDepends = step.DependsOn != null && step.DependsOn.Count > 0;
                if (hasDepends)
                    continue;

                StepPipeline pipeline;
                if (pipelines.TryGetValue(step.Name, out pipeline))
                    return pipeline;
            }

            foreach (var step in steps)
            {
                if (step == null || string.IsNullOrWhiteSpace(step.Name))
                    continue;

                StepPipeline pipeline;
                if (pipelines.TryGetValue(step.Name, out pipeline))
                    return pipeline;
            }

            return null;
        }

        private static void ConfigureTransition(StepPipeline pipeline, IDictionary<string, StepPipeline> pipelines)
        {
            if (pipeline == null || pipeline.Transition == null)
                return;

            var config = pipeline.Config;
            pipeline.Transition.Input(step => step.StepConfig, data => config);

            var success = ResolveTarget(config != null ? config.OnSuccess : null, pipelines);
            pipeline.Transition.Input(step => step.SuccessStepId, data => success.StepId);
            pipeline.Transition.Input(step => step.SuccessStepName, data => success.StepName);
            pipeline.Transition.Input(step => step.SuccessTargetExists, data => success.Exists);

            var failure = ResolveTarget(config != null ? config.OnFailure : null, pipelines);
            pipeline.Transition.Input(step => step.FailureStepId, data => failure.StepId);
            pipeline.Transition.Input(step => step.FailureStepName, data => failure.StepName);
            pipeline.Transition.Input(step => step.FailureTargetExists, data => failure.Exists);
        }

        private static StepTarget ResolveTarget(string targetName, IDictionary<string, StepPipeline> pipelines)
        {
            if (string.IsNullOrWhiteSpace(targetName))
            {
                return new StepTarget
                {
                    StepId = null,
                    StepName = string.Empty,
                    Exists = true
                };
            }

            StepPipeline pipeline;
            if (pipelines != null && pipelines.TryGetValue(targetName, out pipeline) && pipeline.EntryId.HasValue)
            {
                return new StepTarget
                {
                    StepId = pipeline.EntryId.Value,
                    StepName = pipeline.Config != null ? pipeline.Config.Name : targetName,
                    Exists = true
                };
            }

            return new StepTarget
            {
                StepId = null,
                StepName = targetName,
                Exists = false
            };
        }

        private static int? TryGetStepId<TStep>(IStepBuilder<FlowData, TStep> builder)
            where TStep : StepBody
        {
            if (builder == null)
                return null;

            var stepProp = builder.GetType().GetProperty("Step");
            if (stepProp == null)
                return null;

            var stepObj = stepProp.GetValue(builder, null);
            if (stepObj == null)
                return null;

            var idProp = stepObj.GetType().GetProperty("Id");
            if (idProp == null)
                return null;

            var value = idProp.GetValue(stepObj, null);
            if (value == null)
                return null;

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

