using WorkflowCore.Interface;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Workflow;

namespace ZL.WorkflowLib
{
    public static class WorkflowServices
    {
        public static int WorkflowVersion = 1;
        public static string WorkflowIdPrefix = "dyn:";
        public static SubflowRegistry Subflows;
        public static ParamInjector ParamInjector;
        public static FlowConfig FlowCfg;

        /// <summary>
        /// <para>WorkflowCore Host 实例，便于在任意位置发起子流程。</para>
        /// </summary>
        public static IWorkflowHost WorkflowHost;


        /// <summary>
        /// <para>编排器实例，通常通过 <see cref="Engine.BasicOrchestrator.RegisterAsDefault"/> 进行初始化。</para>
        /// </summary>
        public static IOrchestrator Orchestrator;

        public static  string  GetWorkflowId(string workflowId)
        {
            return WorkflowIdPrefix + workflowId;
        }
    }
}
