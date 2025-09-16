using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib
{
    public static class WorkflowServices
    {

        public static SubflowRegistry Subflows;
        public static ParamInjector ParamInjector;

        /// <summary>
        /// <para>编排器实例，通常通过 <see cref="Engine.BasicOrchestrator.RegisterAsDefault"/> 进行初始化。</para>
        /// </summary>
        public static IOrchestrator Orchestrator;
    }
}
