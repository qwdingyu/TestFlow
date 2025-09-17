using System;
using System.Collections.Generic;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.WorkflowLib.Engine
{
    /// <summary>
    /// <para>统一封装期望值判定逻辑，保证主流程与子流程复用同一套实现。</para>
    /// <para>该类型仅承担“对比 + 日志”职责，便于未来在不同上下文复用。</para>
    /// </summary>
    internal static class ExpectedResultEvaluator
    {
        /// <summary>
        /// <para>针对子流程执行器返回的 <see cref="Workflow.OrchTaskResult"/> 执行期望值判定。</para>
        /// <para>确保顺序子流程产出的结果与期望一致，并将判定信息写入日志。</para>
        /// </summary>
        /// <param name="step">步骤配置，包含期望值与参数。</param>
        /// <param name="pooledResult">设备执行后的原始输出。</param>
        public static void ApplyToStepResult(StepConfig step, StepResult pooledResult, bool logSuccess = false, bool logFailure = false)
        {
            if (step == null || pooledResult == null)
                return;

            string mismatchReason;
            bool passed = EvaluateInternal(step, pooledResult.Outputs, out mismatchReason);
            LogEvaluation(step.Name, passed, mismatchReason, logSuccess, logFailure);

            if (!passed)
            {
                pooledResult.Success = false;
                pooledResult.Message = AppendMessage(pooledResult.Message, mismatchReason);
            }
        }
        public static void ApplyToTaskResult(StepConfig step, Workflow.OrchTaskResult taskResult, string taskId, bool logSuccess = true, bool logFailure = true)
        {
            if (step == null || taskResult == null)
                return;

            string mismatchReason;
            bool passed = EvaluateInternal(step, taskResult.Outputs, out mismatchReason);
            LogEvaluation(step.Name ?? taskId, passed, mismatchReason, logSuccess, logFailure);

            if (!passed)
            {
                taskResult.Success = false;
                taskResult.Message = AppendMessage(taskResult.Message, mismatchReason);
            }
        }

        /// <summary>
        /// <para>实际调用 <see cref="ResultEvaluator"/> 的内部实现。</para>
        /// </summary>
        private static bool EvaluateInternal(StepConfig step, IDictionary<string, object> outputs, out string mismatchReason)
        {
            mismatchReason = null;
            var effectiveOutputs = outputs ?? new Dictionary<string, object>();
            return ResultEvaluator.Evaluate(step.ExpectedResults, effectiveOutputs, step.Parameters, out mismatchReason);
        }

        /// <summary>
        /// <para>统一记录期望值判定结果，成功写 Info，失败写 Warn，并同步到 UI 日志。</para>
        /// </summary>
        private static void LogEvaluation(string rawName, bool passed, string mismatchReason, bool logSuccess, bool logFailure)
        {
            var name = string.IsNullOrWhiteSpace(rawName) ? "<未命名步骤>" : rawName.Trim();
            if (passed)
            {
                if (!logSuccess)
                    return;
                var msg = $"[Expected] 步骤 {name} 期望校验通过";
                LogHelper.Info(msg);
                UiEventBus.PublishLog(msg);
            }
            else
            {
                if (!logFailure)
                    return;
                var msg = $"[Expected] 步骤 {name} 期望校验失败：{mismatchReason}";
                LogHelper.Warn(msg);
                UiEventBus.PublishLog(msg);
            }
        }

        /// <summary>
        /// <para>统一处理消息拼接，避免重复追加相同的 mismatch 信息。</para>
        /// </summary>
        private static string AppendMessage(string originalMessage, string mismatchReason)
        {
            var mismatch = $"expected mismatch: {mismatchReason}";
            if (string.IsNullOrWhiteSpace(originalMessage))
                return mismatch;

            if (originalMessage.IndexOf(mismatch, StringComparison.OrdinalIgnoreCase) >= 0)
                return originalMessage;

            return originalMessage + " | " + mismatch;
        }
    }
}
