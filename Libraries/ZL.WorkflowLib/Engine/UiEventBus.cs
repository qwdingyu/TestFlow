using System;

namespace ZL.WorkflowLib.Engine
{
    public static class UiEventBus
    {
        public static event Action<string> Log;
        public static event Action<string, string> WorkflowCompleted;

        public static void PublishLog(string msg)
            => Log?.Invoke(msg);

        public static void PublishCompleted(string sessionId, string model)
            => WorkflowCompleted?.Invoke(sessionId, model);
    }
}

