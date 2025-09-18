using System;
using System.Threading;
using System.Threading.Tasks;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using WorkflowCore.Models.LifeCycleEvents;

namespace ZL.WorkflowLib.Workflow
{
    public static class WorkflowHostExtensions
    {
        // 便捷重载（避免可选参写法）
        public static Task<WorkflowInstance> WaitForWorkflowToCompleteAsync(
            this IWorkflowHost host,
            string workflowInstanceId)
        {
            return WaitForWorkflowToCompleteAsync(host, workflowInstanceId, default(CancellationToken));
        }

        /// <summary>
        /// 等待指定工作流实例完成（Complete 或 Terminated），基于 v3.15 生命周期事件。
        /// </summary>
        public static async Task<WorkflowInstance> WaitForWorkflowToCompleteAsync(
            this IWorkflowHost host,
            string workflowInstanceId,
            CancellationToken cancellationToken)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (string.IsNullOrWhiteSpace(workflowInstanceId)) throw new ArgumentNullException(nameof(workflowInstanceId));

            var tcs = new TaskCompletionSource<WorkflowInstance>();

            // 先查一次当前状态，避免已完成但尚未订阅事件的竞态
            var snap = await host.PersistenceStore.GetWorkflowInstance(workflowInstanceId).ConfigureAwait(false);
            if (snap != null && (snap.Status == WorkflowStatus.Complete || snap.Status == WorkflowStatus.Terminated))
                return snap;

            LifeCycleEventHandler handler = null;
            handler = (LifeCycleEvent evt) =>
            {
                if (evt == null) return;
                if (!string.Equals(evt.WorkflowInstanceId, workflowInstanceId, StringComparison.OrdinalIgnoreCase))
                    return;

                // 完成或终止两种事件
                if (evt is WorkflowCompleted || evt is WorkflowTerminated)
                {
                    // 异步取完整实例再完成 TCS
                    Task.Run(async () =>
                    {
                        try
                        {
                            var inst = await host.PersistenceStore.GetWorkflowInstance(workflowInstanceId).ConfigureAwait(false);
                            tcs.TrySetResult(inst);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                        finally
                        {
                            host.OnLifeCycleEvent -= handler; // 解除订阅
                        }
                    });
                }
            };

            host.OnLifeCycleEvent += handler;

            // 取消支持
            var ctr = cancellationToken.Register(() =>
            {
                host.OnLifeCycleEvent -= handler;
                tcs.TrySetCanceled(cancellationToken);
            });

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                ctr.Dispose(); // 清理取消注册
            }
        }
    }

}
