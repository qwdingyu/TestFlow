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
                .Do(seq => seq.StartWith<UnifiedExecStep>()
                                 .Then<RouteStep>());
        }
    }
}

