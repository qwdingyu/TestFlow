using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib.Workflow
{
    /// <summary>
    /// OrchestrationPlan 描述子流程中需要执行的一组任务（按照 DAG 编排）。
    /// </summary>
    public sealed class OrchestrationPlan
    {
        /// <summary>
        /// 计划名称，通常对应子流程名，仅用于日志展示。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 计划中的任务列表，执行顺序将由依赖关系决定（无依赖则按插入顺序）。
        /// </summary>
        public List<OrchTask> Tasks { get; } = new List<OrchTask>();
    }

    /// <summary>
    /// 编排任务（节点），描述单个设备指令的执行信息。
    /// </summary>
    public sealed class OrchTask
    {
        /// <summary>
        /// 任务唯一标识，经过前缀+清洗处理后保证在一个 Plan 内唯一。
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 实际需要执行的步骤（已完成参数展开）。
        /// </summary>
        public StepConfig Step { get; set; }

        /// <summary>
        /// 资源锁键，未显式设置时退化为 Step.Device。
        /// </summary>
        public string ResourceId { get; set; }

        /// <summary>
        /// 该任务依赖的其它任务 Id 列表，仅在需要显式等待前驱时使用。
        /// </summary>
        public List<string> DependsOn { get; set; }

        /// <summary>
        /// 内部存放原始的依赖名（带层级前缀），用于后续二次解析。
        /// </summary>
        internal List<string> RawDependsOn { get; set; }
    }

    /// <summary>
    /// 单个任务的执行结果。
    /// </summary>
    public sealed class OrchTaskResult
    {
        /// <summary>
        /// 是否成功。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 详细消息（包含期望值比对等信息）。
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 设备返回的输出字典，已复制一份，外部可安全序列化。
        /// </summary>
        public Dictionary<string, object> Outputs { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// 执行起始时间。
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 执行结束时间。
        /// </summary>
        public DateTime FinishedAt { get; set; }
    }

    /// <summary>
    /// 编排执行的整体结果。
    /// </summary>
    public sealed class OrchestrationResult
    {
        /// <summary>
        /// 全局成功标记，只有所有已执行任务均成功才会为 true。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 当失败时的综合消息（例如第一个失败任务的错误原因）。
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 各任务执行结果，键为任务 Id。
        /// </summary>
        public Dictionary<string, OrchTaskResult> TaskResults { get; } = new Dictionary<string, OrchTaskResult>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 编排器接口，后续可替换为更复杂的实现（并行/窗口化等）。
    /// </summary>
    public interface IOrchestrator
    {
        /// <summary>
        /// 执行给定的编排计划。
        /// </summary>
        /// <param name="plan">要执行的计划。</param>
        /// <param name="ctx">共享的步骤上下文（用于携带模型、取消令牌等信息）。</param>
        /// <returns>执行结果。</returns>
        OrchestrationResult Execute(OrchestrationPlan plan, StepContext ctx);
    }

    /// <summary>
    /// 默认的顺序编排器实现：按任务顺序依次执行，并在必要时检查依赖。
    /// </summary>
    internal sealed class SequentialOrchestrator : IOrchestrator
    {
        /// <inheritdoc />
        public OrchestrationResult Execute(OrchestrationPlan plan, StepContext ctx)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));

            var result = new OrchestrationResult { Success = true };
            if (plan.Tasks == null || plan.Tasks.Count == 0)
                return result;

            foreach (var task in plan.Tasks)
            {
                if (!CheckDependencies(task, result))
                {
                    result.Success = false;
                    result.Message = $"依赖未满足，任务 {task.Id} 无法执行";
                    break;
                }

                var taskResult = ExecuteTask(task, ctx);
                ExpectedResultEvaluator.ApplyToTaskResult(task.Step, taskResult, task.Id);
                result.TaskResults[task.Id] = taskResult;

                if (!taskResult.Success)
                {
                    // 保持与旧实现一致：遇到失败立即中断，并输出日志。
                    var stepName = task.Step != null ? task.Step.Name : task.Id;
                    UiEventBus.PublishLog($"[SubFlow] 子步骤 {stepName} 失败，中断子流程");
                    result.Success = false;
                    result.Message = taskResult.Message;
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// 执行单个任务，包含资源锁、超时控制与期望值判断。
        /// </summary>
        private static OrchTaskResult ExecuteTask(OrchTask task, StepContext sharedCtx)
        {
            if (task == null || task.Step == null)
            {
                var now = DateTime.Now;
                return new OrchTaskResult
                {
                    Success = false,
                    Message = "任务缺少 Step 配置",
                    Outputs = new Dictionary<string, object>(),
                    StartedAt = now,
                    FinishedAt = now
                };
            }

            var step = task.Step;
            UiEventBus.PublishLog($"---[SubFlow] 开始 {step.Name}, 设备【{step.Device}】, 描述【{step.Description}】");

            var resourceKey = !string.IsNullOrWhiteSpace(task.ResourceId)
                ? task.ResourceId
                : (!string.IsNullOrWhiteSpace(step.Device) ? step.Device : step.Target);

            if (string.IsNullOrWhiteSpace(resourceKey))
                resourceKey = step.Name ?? task.Id;

            var taskResult = DeviceLockRegistry.WithLock(resourceKey, () => DeviceExecStep.ExecuteSingleStep(step, sharedCtx));

            if (taskResult == null)
            {
                var now = DateTime.Now;
                taskResult = new OrchTaskResult
                {
                    Success = false,
                    Message = "执行返回空结果",
                    Outputs = new Dictionary<string, object>(),
                    StartedAt = now,
                    FinishedAt = now
                };
            }

            UiEventBus.PublishLog($"[SubStep] {step.Name} | Success={taskResult.Success} | Msg={taskResult.Message}");
            return taskResult;
        }

        /// <summary>
        /// 检查依赖任务是否已经成功执行，避免在依赖失败时继续运行后续任务。
        /// </summary>
        private static bool CheckDependencies(OrchTask task, OrchestrationResult result)
        {
            if (task?.DependsOn == null || task.DependsOn.Count == 0)
                return true;

            foreach (var dep in task.DependsOn)
            {
                OrchTaskResult depResult;
                if (!result.TaskResults.TryGetValue(dep, out depResult))
                    return false;
                if (!depResult.Success)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 子流程执行器：负责把 StepConfig.Steps 转换为编排计划并委托编排器执行。
    /// </summary>
    public class SubFlowExecutor
    {
        /// <summary>
        /// 子流程节点默认的超时时间（毫秒）。当配置未显式提供 TimeoutMs 时使用该值兜底。
        /// </summary>
        private const int DefaultStepTimeoutMs = 30000;

        private readonly IOrchestrator _orchestrator;

        /// <summary>
        /// 默认构造：使用顺序编排器实现。
        /// </summary>
        public SubFlowExecutor()
            : this(null)
        {
        }

        /// <summary>
        /// 允许外部注入自定义编排器，便于后续扩展或测试。
        /// </summary>
        internal SubFlowExecutor(IOrchestrator orchestrator)
        {
            _orchestrator = orchestrator ?? new SequentialOrchestrator();
        }

        /// <summary>
        /// 执行子流程，按照步骤配置生成 OrchestrationPlan 并交由编排器运行。
        /// </summary>
        /// <param name="stepCfg">子流程定义（Type=SubFlow 或实际展开后的引用）。</param>
        /// <param name="data">当前流程上下文数据。</param>
        /// <param name="parentStepCfg">父步骤配置，用于日志中输出下一步信息。</param>
        public void RunSubFlow(StepConfig stepCfg, FlowData data, StepConfig parentStepCfg)
        {
            if (stepCfg == null)
                throw new ArgumentNullException(nameof(stepCfg));

            UiEventBus.PublishLog($"[SubFlow] 开始 {stepCfg.Name}, 子步骤共有【{stepCfg.Steps?.Count ?? 0}】步");

            try
            {
                var plan = BuildPlan(stepCfg, data);
                if (plan.Tasks.Count == 0)
                {
                    UiEventBus.PublishLog($"[SubFlow] {stepCfg.Name} 未包含可执行子步骤，直接视为成功");
                    data.LastSuccess = true;
                    return;
                }

                var baseCtx = DeviceServices.Context ?? new StepContext(data?.Model ?? DeviceServices.Config?.Model ?? string.Empty,
                                                                         data != null ? data.Cancellation : CancellationToken.None);

                var orchestrationResult = _orchestrator.Execute(plan, baseCtx);

                foreach (var task in plan.Tasks)
                {
                    OrchTaskResult taskResult;
                    if (!orchestrationResult.TaskResults.TryGetValue(task.Id, out taskResult))
                        break; // 遇到未执行的任务（通常是前一步失败后中断），直接跳出

                    PersistTaskResult(task, taskResult, data);
                    // 如果某一步失败，后续计划已停止，这里无需额外中断
                }

                data.LastSuccess = orchestrationResult.Success;
                if (!orchestrationResult.Success && !string.IsNullOrEmpty(orchestrationResult.Message))
                    UiEventBus.PublishLog($"[SubFlow] {stepCfg.Name} 失败：{orchestrationResult.Message}");
            }
            catch (Exception ex)
            {
                // 记录详细异常，方便定位子流程执行失败的具体原因
                var exceptionDetail = ex.ToString();
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[SubFlow] 执行 {stepCfg.Name} 异常: {ex.Message}");
            }
            finally
            {
                UiEventBus.PublishLog($"[SubFlow] 结束 {stepCfg.Name}, Success={data.LastSuccess}, 下一步【{parentStepCfg?.OnSuccess}】");
            }
        }

        /// <summary>
        /// 将子流程配置展开为编排计划，支持嵌套子流程和子流程引用。
        /// </summary>
        /// <param name="stepCfg">原始的子流程定义（包含 Steps）。</param>
        /// <param name="data">流程运行时数据，用于参数解析与模型上下文。</param>
        private static OrchestrationPlan BuildPlan(StepConfig stepCfg, FlowData data)
        {
            var plan = new OrchestrationPlan { Name = stepCfg.Name };
            var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AppendSubSteps(stepCfg, data, plan, null, nameMap, usedIds);
            ResolveDependencies(plan, nameMap);
            return plan;
        }

        /// <summary>
        /// 递归展开子步骤，遇到嵌套子流程时继续下钻。
        /// </summary>
        /// <param name="container">当前需要展开的步骤容器（可能是子流程本身或引用）。</param>
        /// <param name="data">流程数据，用于参数注入与模型信息。</param>
        /// <param name="plan">正在构建的编排计划。</param>
        /// <param name="prefix">层级前缀，形如 parent.child。</param>
        /// <param name="nameMap">名称映射表，用于后续依赖解析。</param>
        /// <param name="usedIds">已使用的任务 Id 集，避免冲突。</param>
        private static void AppendSubSteps(StepConfig container, FlowData data, OrchestrationPlan plan, string prefix, Dictionary<string, string> nameMap, HashSet<string> usedIds)
        {
            if (container?.Steps == null)
                return;

            foreach (var sub in container.Steps)
            {
                if (sub == null)
                    continue;

                var type = (sub.Type ?? "Normal").Trim();
                if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
                {
                    var nextPrefix = CombinePrefix(prefix, sub.Name);
                    AppendSubSteps(sub, data, plan, nextPrefix, nameMap, usedIds);
                    continue;
                }

                if (string.Equals(type, "SubFlowRef", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(sub.Ref))
                        throw new Exception($"子流程 {container.Name} 的子步骤 {sub.Name} 缺少 Ref 字段");

                    StepConfig refCfg;
                    if (WorkflowServices.Subflows == null || !WorkflowServices.Subflows.TryGet(sub.Ref, out refCfg))
                        throw new Exception($"未找到子流程引用: {sub.Ref}");

                    var nextPrefix = CombinePrefix(prefix, string.IsNullOrWhiteSpace(sub.Name) ? sub.Ref : sub.Name);
                    AppendSubSteps(refCfg, data, plan, nextPrefix, nameMap, usedIds);
                    continue;
                }

                var execSub = StepUtils.BuildExecutableStep(sub, data);
                var originalKey = CombinePrefix(prefix, execSub.Name);
                var uniqueId = EnsureUniqueId(originalKey, usedIds, plan.Tasks.Count);
                var contextName = !string.IsNullOrEmpty(originalKey) ? originalKey : uniqueId;

                ApplyDefaultTimeoutIfNeeded(execSub, contextName);
                var resourceId = ResolveResourceId(execSub, contextName);

                if (!string.IsNullOrEmpty(originalKey))
                    nameMap[originalKey] = uniqueId;
                else
                    nameMap[uniqueId] = uniqueId;

                plan.Tasks.Add(new OrchTask
                {
                    Id = uniqueId,
                    Step = execSub,
                    ResourceId = resourceId, // 资源互斥标识：避免多个任务争用同一物理设备
                    RawDependsOn = NormalizeDepends(sub.DependsOn, prefix) // 暂存原始依赖名，待 ResolveDependencies 转换
                });
            }
        }

        /// <summary>
        /// 将 RawDependsOn（原始名）映射为实际任务 Id。
        /// </summary>
        /// <param name="plan">完整的编排计划，包含所有节点。</param>
        /// <param name="nameMap">名称到唯一 Id 的映射表。</param>
        private static void ResolveDependencies(OrchestrationPlan plan, Dictionary<string, string> nameMap)
        {
            if (plan?.Tasks == null)
                return;

            foreach (var task in plan.Tasks)
            {
                if (task.RawDependsOn == null || task.RawDependsOn.Count == 0)
                    continue;

                var resolved = new List<string>();
                foreach (var raw in task.RawDependsOn)
                {
                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    string actual;
                    if (nameMap.TryGetValue(raw, out actual))
                        resolved.Add(actual);
                }

                task.DependsOn = resolved.Count > 0 ? resolved : null; // 依赖列表：为空表示无前驱限制
                task.RawDependsOn = null; // 释放临时引用
            }
        }

        /// <summary>
        /// 将依赖名称添加层级前缀，保持与任务 Id 生成规则一致。
        /// </summary>
        /// <param name="dependsOn">原始的依赖列表。</param>
        /// <param name="prefix">当前层级的前缀。</param>
        private static List<string> NormalizeDepends(IList<string> dependsOn, string prefix)
        {
            if (dependsOn == null || dependsOn.Count == 0)
                return null;

            var list = new List<string>();
            foreach (var dep in dependsOn)
            {
                if (string.IsNullOrWhiteSpace(dep))
                    continue;
                var combined = CombinePrefix(prefix, dep);
                if (!string.IsNullOrEmpty(combined))
                    list.Add(combined);
            }
            return list.Count > 0 ? list : null;
        }

        /// <summary>
        /// 组合层级前缀，使用 '.' 作为分隔符，并在拼装前对每一段进行清洗。
        /// </summary>
        /// <param name="prefix">已有的层级前缀。</param>
        /// <param name="segment">当前步骤的名称片段。</param>
        private static string CombinePrefix(string prefix, string segment)
        {
            var sanitized = SanitizeSegment(segment);
            if (string.IsNullOrEmpty(prefix))
                return sanitized;
            if (string.IsNullOrEmpty(sanitized))
                return prefix;
            return prefix + "." + sanitized;
        }

        /// <summary>
        /// 清洗名称片段，避免出现空格等不适合作为 Id 的字符。
        /// </summary>
        /// <param name="value">原始名称片段。</param>
        private static string SanitizeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            var sb = new StringBuilder(trimmed.Length);
            foreach (var ch in trimmed)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                    sb.Append(ch);
                else
                    sb.Append('_');
            }

            var sanitized = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(sanitized) ? null : sanitized;
        }

        /// <summary>
        /// 生成唯一 Id，如果存在同名则自动追加序号后缀。
        /// </summary>
        /// <param name="candidate">建议的原始名称。</param>
        /// <param name="usedIds">已占用的 Id 集合。</param>
        /// <param name="index">当前任务在 Plan 中的序号，用于兜底命名。</param>
        private static string EnsureUniqueId(string candidate, HashSet<string> usedIds, int index)
        {
            var baseId = !string.IsNullOrEmpty(candidate) ? candidate : $"task_{index}";
            baseId = SanitizeSegment(baseId) ?? $"task_{index}";

            var unique = baseId;
            int suffix = 1;
            while (!usedIds.Add(unique))
            {
                unique = baseId + "_" + suffix;
                suffix++;
            }

            return unique;
        }

        /// <summary>
        /// 解析资源锁键，优先使用参数中的 __resourceId / resourceId，其次退化为设备名，并在使用默认值时输出日志。
        /// </summary>
        /// <param name="step">目标步骤配置。</param>
        /// <param name="contextName">用于日志的上下文名称（通常为层级化的步骤名）。</param>
        private static string ResolveResourceId(StepConfig step, string contextName)
        {
            if (step == null)
                return null;

            bool hasExplicit = false;
            string candidate = null;
            if (step.Parameters != null)
            {
                object value;
                if (step.Parameters.TryGetValue("__resourceId", out value) || step.Parameters.TryGetValue("resourceId", out value))
                {
                    hasExplicit = true;
                    candidate = value != null ? value.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(candidate))
                        return candidate.Trim();
                }
            }

            if (hasExplicit && string.IsNullOrWhiteSpace(candidate))
            {
                PublishPlanLog(
                    $"[BuildPlan] 步骤 {contextName} 提供的 ResourceId 为空字符串，建议补充有效值以避免资源争用", warn: true);
            }
            if (!string.IsNullOrWhiteSpace(step.Device))
            {
                // 当未提供 resourceId 或配置为空字符串时，默认使用设备名并记录提示日志，方便排查配置遗漏。
                PublishPlanLog(
                    $"[BuildPlan] 步骤 {contextName} 未显式配置 ResourceId，默认使用设备名 {step.Device}",
                    warn: false);
                return step.Device;
            }

            PublishPlanLog(
                $"[BuildPlan] 步骤 {contextName} 缺少 ResourceId 且无法从设备推断，将退化为使用设备锁", warn: true);
            return null;
        }

        /// <summary>
        /// 若步骤未提供 TimeoutMs，则填充默认值并输出提示日志，避免任务长时间无界等待。
        /// </summary>
        private static void ApplyDefaultTimeoutIfNeeded(StepConfig step, string contextName)
        {
            if (step == null)
                return;

            if (step.TimeoutMs > 0)
                return;

            step.TimeoutMs = DefaultStepTimeoutMs;
            PublishPlanLog(
                $"[BuildPlan] 步骤 {contextName} 未设置 TimeoutMs，已使用默认值 {DefaultStepTimeoutMs}ms",
                warn: false);
        }

        /// <summary>
        /// 统一封装计划构建阶段的日志输出，确保同时写入底层日志与 UI 日志。
        /// </summary>
        private static void PublishPlanLog(string message, bool warn)
        {
            if (warn)
                LogHelper.Warn(message);
            else
                LogHelper.Info(message);
            UiEventBus.PublishLog(message);
        }

        /// <summary>
        /// 将任务执行结果写入数据库（沿用旧逻辑）。
        /// </summary>
        /// <param name="task">当前编排任务（包含步骤配置）。</param>
        /// <param name="result">编排执行结果。</param>
        /// <param name="data">流程上下文，用于读取 Session/Model 等信息。</param>
        private static void PersistTaskResult(OrchTask task, OrchTaskResult result, FlowData data)
        {
            if (task?.Step == null || data == null)
                return;

            var step = task.Step;
            DeviceServices.Db.AppendStep(
                data.SessionId,
                data.Model,
                data.Sn,
                step.Name,
                step.Description,
                step.Device,
                step.Command,
                JsonConvert.SerializeObject(step.Parameters ?? new Dictionary<string, object>()),
                JsonConvert.SerializeObject(step.ExpectedResults ?? new Dictionary<string, object>()),
                JsonConvert.SerializeObject(result?.Outputs ?? new Dictionary<string, object>()),
                (result != null && result.Success) ? 1 : 0,
                result?.Message,
                result?.StartedAt ?? DateTime.Now,
                result?.FinishedAt ?? DateTime.Now);
        }
    }
}
