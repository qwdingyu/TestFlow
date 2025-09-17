using System;
using System.Reflection;
using WorkflowCore.Interface;
using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib.Utils
{

    public static class Dbg
    {
        public static void WfDbg(IStepExecutionContext ctx, string tag, string extra = null)
        {
            var ep = ctx.ExecutionPointer;
            UiEventBus.PublishLog(
                $"[WFDBG] {tag} | WFStepId={ctx.Step.Id} Name={ctx.Step.Name ?? ""} " +
                $"PtrId={(ep?.Id ?? "null")} Retry={(ep?.RetryCount ?? 0)} Active={(ep?.Active ?? false)} {extra ?? ""}");
        }
    }

    public static class EventInspector
    {
        /// <summary>
        /// 打印对象的指定事件订阅者
        /// </summary>
        /// <param name="target">事件所在的对象实例</param>
        /// <param name="eventName">事件名称（必须和声明一致）</param>
        public static void DumpEventHandlers(object target, string eventName)
        {
            if (target == null)
            {
                UiEventBus.PublishLog("[EventInspector] target 为空");
                return;
            }

            var type = target.GetType();
            // 在实例上找 backing field
            var field = type.GetField(eventName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy);

            if (field == null)
            {
                UiEventBus.PublishLog($"[EventInspector] 找不到事件 {eventName} 的字段");
                return;
            }

            var del = field.GetValue(target) as Delegate;
            if (del == null)
            {
                UiEventBus.PublishLog($"[EventInspector] {eventName} 没有订阅者");
                return;
            }

            var handlers = del.GetInvocationList();
            UiEventBus.PublishLog($"[EventInspector] {eventName} 订阅者数量: {handlers.Length}");
            foreach (var h in handlers)
            {
                UiEventBus.PublishLog($"  -> {h.Method.DeclaringType.FullName}.{h.Method.Name}");
            }
        }

    }

}
