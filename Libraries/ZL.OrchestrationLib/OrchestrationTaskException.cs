using System;

namespace ZL.Orchestration
{
    /// <summary>
    ///     统一的任务执行异常封装，便于上层捕获并识别失败任务。
    /// </summary>
    public sealed class OrchestrationTaskException : Exception
    {
        /// <summary>
        ///     初始化异常并记录失败的任务 Id。
        /// </summary>
        public OrchestrationTaskException(string taskId, Exception inner)
            : base(CreateMessage(taskId, inner), inner)
        {
            TaskId = taskId;
        }

        /// <summary>
        ///     发生异常的任务 Id。
        /// </summary>
        public string TaskId { get; private set; }

        private static string CreateMessage(string taskId, Exception inner)
        {
            var reason = inner == null ? "未知错误" : inner.Message;
            return string.Format("任务 \"{0}\" 执行失败：{1}", taskId, reason);
        }
    }
}
