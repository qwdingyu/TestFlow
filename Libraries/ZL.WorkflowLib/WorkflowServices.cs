using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib
{
    public static class WorkflowServices
    {

        public static SubflowRegistry Subflows;
        public static ParamInjector ParamInjector;

        /// <summary>
        ///     计划工作流构建器，默认提供 <see cref="Engine.PlanWorkflowBuilder"/> 实现，外部可按需替换。
        /// </summary>
        public static IPlanWorkflowBuilder Orchestrator = new PlanWorkflowBuilder();
    }
}
