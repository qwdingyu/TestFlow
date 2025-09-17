using WorkflowCore.Interface;
using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib
{
    public static class WorkflowServices
    {

        public static SubflowRegistry Subflows;
        public static ParamInjector ParamInjector;

        /// <summary>
        /// <para>WorkflowCore Host 实例，便于在任意位置发起子流程。</para>
        /// </summary>
        public static IWorkflowHost WorkflowHost;
    }
}
