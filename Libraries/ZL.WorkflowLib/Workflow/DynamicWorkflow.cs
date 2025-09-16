using WorkflowCore.Interface;
using WorkflowCore.Models;
namespace ZL.WorkflowLib.Workflow
{
    public class DynamicLoopWorkflow : IWorkflow<FlowData>
    {
        public string Id => "DynamicLoopWorkflow";
        public int Version => 1;
        public void Build(IWorkflowBuilder<FlowData> builder)
        {
            builder
                .StartWith<InitStep>()
                .While(data => !data.Done)
                .Do(loop => loop
                    .StartWith<ResolveStepContextStep>()
                    .When(data => data.CurrentStepKind == StepExecutionKind.SubFlow)
                        .Do(branch => branch.StartWith<ExecuteSubFlowStep>())
                    .When(data => data.CurrentStepKind == StepExecutionKind.SubFlowReference)
                        .Do(branch => branch.StartWith<ExecuteSubFlowReferenceStep>())
                    .When(data => data.CurrentStepKind == StepExecutionKind.Device)
                        .Do(device => device
                            .StartWith<PrepareDeviceExecutionStep>()
                            .When(data => data.CurrentExecution != null &&
                                          data.CurrentExecution.Specification.HasExtrasForPhase(ExtraDevicePhase.BeforeMain))
                                .Do(extraBefore => extraBefore
                                    .StartWith<ExtraDeviceStep>()
                                        .Input(step => step.Phase, _ => ExtraDevicePhase.BeforeMain)
                                        .WithRetry(BuildMainRetryOptions)
                                        .WithDelay(_ => new DelayOptions()))
                            .When(ShouldRunParallel)
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
                                    .Join())
                            .When(ShouldRunMainSequentially)
                                .Do(mainSeq => mainSeq
                                    .StartWith<DeviceLockStep>()
                                        .Input(step => step.DeviceName,
                                               data => data.CurrentExecution != null && data.CurrentExecution.ExecutableStep != null
                                                   ? data.CurrentExecution.ExecutableStep.Device
                                                   : null)
                                    .Then<MainDeviceStep>()
                                        .WithRetry(BuildMainRetryOptions)
                                        .WithDelay(BuildMainDelayOptions))
                            .When(data => data.CurrentExecution != null &&
                                          data.CurrentExecution.Specification.HasExtrasForPhase(ExtraDevicePhase.AfterMain))
                                .Do(extraAfter => extraAfter
                                    .StartWith<ExtraDeviceStep>()
                                        .Input(step => step.Phase, _ => ExtraDevicePhase.AfterMain)
                                        .WithRetry(BuildMainRetryOptions)
                                        .WithDelay(_ => new DelayOptions()))
                            .Then<FinalizeDeviceStep>())
                    .Then<RouteStep>());
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

