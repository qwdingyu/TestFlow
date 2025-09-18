using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib.Workflow.Lite
{
    public class InitStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModels)context.Workflow.Data;
            data.WorkflowCompleted = false; data.LastSuccess = true; data.Model = WorkflowServices.FlowCfg.Model;
            foreach (var s in WorkflowServices.FlowCfg.TestSteps)
            {
                if (s.DependsOn == null || s.DependsOn.Count == 0) { data.Current = s.Name; break; }
            }
            if (string.IsNullOrEmpty(data.Current)) data.WorkflowCompleted = true;
            data.SessionId = DeviceServices.Db.StartTestSession(data.Model, data.Sn);
            UiEventBus.PublishLog($"[Init] 产品={data.Model}, SN={data.Sn}, SessionId={data.SessionId}, 起始步骤={data.Current}");
            return ExecutionResult.Next();
        }
    }
    /// <summary>
    /// 瑞士军刀版 DeviceExecStep
    /// - 支持任意节点的跨设备并发（ExtraDevices）
    /// - 并行/串行，等待/忽略策略，重试、超时、聚合模式、设备互斥锁、前后置延迟
    /// - 与原有数据库记录 / 期望评估逻辑保持一致
    /// </summary>
    public class DeviceExecStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModels)context.Workflow.Data;
            if (data.WorkflowCompleted || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();

            var stepCfg = WorkflowServices.FlowCfg.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[DeviceExec] 未找到步骤配置: {data.Current}");
                return ExecutionResult.Next();
            }
            UiEventBus.PublishLog($"--[Flow] 开始 {stepCfg.Name}, 设备【{stepCfg.Target}】, 描述【{stepCfg.Description}】, 下一步【{stepCfg.OnSuccess}】");
            var started = DateTime.Now;

            var pooledResult = StepResultPool.Instance.Get();  // 和原来一致：复用对象池承载输出
            try
            {
                // 解析执行用的 Step（支持 @from_db 等展开）
                var execStep = StepUtils.BuildExecutableStep(stepCfg, data);

                // 解析瑞士军刀配置（全部可选）
                var execSpec = ExecSpec.ParseFrom(execStep.Parameters);

                // 设备超时与全局取消联合
                var baseToken = DeviceServices.Context != null ? DeviceServices.Context.Cancellation : CancellationToken.None;
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(baseToken))
                {
                    if (execStep.TimeoutMs > 0) linkedCts.CancelAfter(execStep.TimeoutMs);
                    var stepCtx = DeviceServices.Context != null ? DeviceServices.Context.CloneWithCancellation(linkedCts.Token)
                                                              : new StepContext(data.Model, linkedCts.Token);

                    // traceId 贯穿日志
                    var traceId = Guid.NewGuid().ToString("N").Substring(0, 8);

                    // 前置延迟（对齐测量窗口）
                    if (execSpec.PreDelayMs > 0)
                        SafeDelay(execSpec.PreDelayMs, stepCtx.Cancellation);

                    // 构建主设备任务（可加重试）
                    Func<Dictionary<string, object>> runMain = () =>
                    {
                        return ExecuteWithRetry(execStep.Target, execStep.Command, execStep.Parameters, execStep.TimeoutMs,
                                                execSpec.MainRetry, stepCtx, traceId);
                    };

                    // 构建附属设备任务列表
                    var extraPlans = execSpec.Extras ?? new List<ExtraDeviceSpec>(0);
                    var extraTasks = new List<Task<KeyValuePair<string, Dictionary<string, object>>>>();

                    Action startExtras = () =>
                    {
                        for (int i = 0; i < extraPlans.Count; i++)
                        {
                            var ex = extraPlans[i];
                            if (ex.Start == ExtraStart.Before && execSpec.Mode != ExecMode.ExtrasFirst)
                                continue; // 不在此时机启动

                            if (ex.Start == ExtraStart.After && execSpec.Mode != ExecMode.MainFirst)
                                continue;

                            var alias = string.IsNullOrEmpty(ex.Alias) ? ex.Target : ex.Alias;

                            var t = Task.Run(() =>
                            {
                                var outputs = ExecuteWithRetry(ex.Target, ex.Command, ex.Parameters, ex.TimeoutMs,
                                                               ex.Retry, stepCtx, traceId);
                                return new KeyValuePair<string, Dictionary<string, object>>(alias, outputs);
                            }, stepCtx.Cancellation);

                            if (ex.Join == ExtraJoin.Wait)
                                extraTasks.Add(t);
                            else
                                _ = t.ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);
                        }
                    };

                    // 执行编排
                    Dictionary<string, object> mainOutputs = null;
                    bool mainSucceeded = false;
                    Exception mainError = null;

                    if (execSpec.Mode == ExecMode.MainFirst)
                    {
                        // 先主，再附属
                        mainOutputs = TryRun(runMain, out mainSucceeded, out mainError);
                        startExtras(); // 启动 Start=after/with_main 的任务
                    }
                    else if (execSpec.Mode == ExecMode.ExtrasFirst)
                    {
                        // 先附属（Start=before/with_main）
                        startExtras();
                        mainOutputs = TryRun(runMain, out mainSucceeded, out mainError);
                    }
                    else // parallel
                    {
                        // 并行：先启动 with_main 的附属，再启主
                        // 也允许 Extra.Start=with_main 的在并行时一起起
                        startExtras();
                        mainOutputs = TryRun(runMain, out mainSucceeded, out mainError);
                    }

                    // 等待附属（wait策略的）
                    bool extrasSucceeded = true;
                    var extrasOutputs = new Dictionary<string, Dictionary<string, object>>();

                    if (extraTasks.Count > 0)
                    {
                        try
                        {
                            Task.WaitAll(extraTasks.ToArray(), stepCtx.Cancellation);
                        }
                        catch (Exception)
                        {
                            // 至少一个任务异常或取消
                        }

                        for (int i = 0; i < extraTasks.Count; i++)
                        {
                            var t = extraTasks[i];
                            if (t.Status == TaskStatus.RanToCompletion)
                            {
                                extrasOutputs[t.Result.Key] = t.Result.Value ?? new Dictionary<string, object>();
                            }
                            else
                            {
                                extrasSucceeded = false;
                            }
                        }
                    }

                    // 聚合输出
                    var merged = MergeOutputs(execSpec.Aggregation, mainOutputs, extrasOutputs);

                    // 后置延迟
                    if (execSpec.PostDelayMs > 0)
                        SafeDelay(execSpec.PostDelayMs, stepCtx.Cancellation);

                    // 组装结果
                    pooledResult.Outputs = merged;

                    // 成功判定：主失败 = 整步失败；主成功 + 附属失败 → 看策略
                    bool finalSuccess = mainSucceeded &&
                                        (extrasSucceeded || execSpec.ContinueOnExtraFailure);

                    pooledResult.Success = finalSuccess;
                    pooledResult.Message = BuildMessage(finalSuccess, mainError, extrasSucceeded, execSpec);

                    // 期望值评估（沿用原有逻辑）
                    ExpectedResultEvaluator.ApplyToStepResult(execStep, pooledResult, logSuccess: false, logFailure: false);

                    // 写库（沿用原逻辑）
                    DeviceServices.Db.AppendStep(
                        data.SessionId, data.Model, data.Sn,
                        execStep.Name, execStep.Description, execStep.Target, execStep.Command,
                        JsonConvert.SerializeObject(execStep.Parameters),
                        JsonConvert.SerializeObject(execStep.ExpectedResults),
                        JsonConvert.SerializeObject(pooledResult.Outputs),
                        pooledResult.Success ? 1 : 0, pooledResult.Message, started, DateTime.Now);

                    data.LastSuccess = pooledResult.Success;
                    UiEventBus.PublishLog($"[Step] {execStep.Name} | 设备={execStep.Target} | Success={pooledResult.Success} | Msg={pooledResult.Message}");
                }
            }
            catch (Exception ex)
            {
                // 使用完整异常字符串确保日志中包含堆栈信息，便于后续排查
                var exceptionDetail = ex.ToString();
                data.LastSuccess = false;
                DeviceServices.Db.AppendStep(
                    data.SessionId, data.Model, data.Sn,
                    stepCfg.Name, stepCfg.Description, stepCfg.Target, stepCfg.Command,
                    JsonConvert.SerializeObject(stepCfg.Parameters),
                    JsonConvert.SerializeObject(stepCfg.ExpectedResults),
                    null, 0, "Exception: " + exceptionDetail, started, DateTime.Now);
                UiEventBus.PublishLog(
                    $"[Step-Exception] {stepCfg.Name} | SessionId={data.SessionId} | 模型={data.Model} | SN={data.Sn} | 错误详情={exceptionDetail}");
            }
            finally
            {
                StepResultPool.Instance.Return(pooledResult);
            }

            return ExecutionResult.Next();
        }

        /// <summary>
        /// 提供给子流程编排器复用的执行入口：
        /// 在不依赖 WorkflowCore 上下文的前提下，完成单个设备步骤的实际执行、超时控制与期望值校验。
        /// </summary>
        /// <param name="step">已经过参数展开后的步骤配置。</param>
        /// <param name="sharedCtx">子流程共享的步骤上下文，用于复用模型信息与取消令牌。</param>
        /// <returns>封装执行状态、输出以及时间戳的结果对象。</returns>
        internal static OrchTaskResult ExecuteSingleStep(StepConfig step, StepContext sharedCtx)
        {
            var now = DateTime.Now;
            if (step == null)
            {
                // 兜底返回：避免外部调用方因配置缺失而出现空引用异常。
                return new OrchTaskResult
                {
                    Success = false,
                    Message = "步骤配置为空",
                    Outputs = new Dictionary<string, object>(),
                    StartedAt = now,
                    FinishedAt = now
                };
            }

            var taskResult = new OrchTaskResult
            {
                StartedAt = now,
                Outputs = new Dictionary<string, object>()
            };

            var pooledResult = StepResultPool.Instance.Get();

            try
            {
                // 统一处理步骤级超时：与共享上下文中的取消令牌进行合并，保障超时后能及时退出。
                var baseToken = sharedCtx != null ? sharedCtx.Cancellation : CancellationToken.None;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(baseToken))
                {
                    if (step.TimeoutMs > 0)
                        linked.CancelAfter(step.TimeoutMs);

                    // 沿用全局上下文信息（模型、Bag 等），仅替换取消令牌。
                    var model = sharedCtx != null ? sharedCtx.Model : WorkflowServices.FlowCfg != null ? WorkflowServices.FlowCfg.Model : string.Empty;
                    var stepCtx = sharedCtx != null
                        ? sharedCtx.CloneWithCancellation(linked.Token)
                        : new StepContext(model, linked.Token);

                    DeviceConfig devConf;
                    if (!DeviceServices.Devices.TryGetValue(step.Target, out devConf))
                        throw new Exception("Device not found: " + step.Target);

                    var execResult = DeviceServices.Factory.UseDevice(step.Target, devConf, dev => dev.Execute(step, stepCtx));

                    pooledResult.Success = execResult.Success;
                    pooledResult.Message = execResult.Message;
                    pooledResult.Outputs = execResult.Outputs ?? new Dictionary<string, object>();
                }

                // 统一走期望值校验逻辑，保持与主流程一致的判断结果。
                ExpectedResultEvaluator.ApplyToStepResult(step, pooledResult, logSuccess: false, logFailure: false);

                taskResult.Success = pooledResult.Success;
                taskResult.Message = pooledResult.Message;
                taskResult.Outputs = new Dictionary<string, object>(pooledResult.Outputs ?? new Dictionary<string, object>());
            }
            catch (Exception ex)
            {
                taskResult.Success = false;
                taskResult.Message = ex.Message;
                taskResult.Outputs = new Dictionary<string, object>();
                UiEventBus.PublishLog($"[SubStep-Exception] {step.Name} | 错误={ex.Message}");
            }
            finally
            {
                taskResult.FinishedAt = DateTime.Now;
                StepResultPool.Instance.Return(pooledResult);
            }

            return taskResult;
        }

        // ===== 执行主/附属一次（带重试 + 设备锁 + 超时）=====
        private static Dictionary<string, object> ExecuteWithRetry(
            string deviceName, string command, IDictionary<string, object> parameters, int timeoutMs,
            RetrySpec retry, StepContext ctx, string traceId)
        {
            int attempts = retry != null && retry.Attempts > 0 ? retry.Attempts : 1;
            int delayMs = retry != null ? Math.Max(0, retry.DelayMs) : 0;

            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    // 针对该设备的互斥锁（串行化对同一设备的访问，避免串口/会话冲突）
                    return DeviceLockRegistry.WithLock(deviceName, () =>
                    {
                        // 为该次执行单独设置更紧的超时（如果有）
                        var baseToken = ctx.Cancellation;
                        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(baseToken))
                        {
                            if (timeoutMs > 0) linked.CancelAfter(timeoutMs);
                            var runCtx = new StepContext(ctx.Model, linked.Token);

                            DeviceConfig devConf;
                            if (!DeviceServices.Devices.TryGetValue(deviceName, out devConf))
                                throw new Exception("Device not found: " + deviceName);

                            var sc = new StepConfig
                            {
                                Name = $"{deviceName}:{command}",
                                Description = "",
                                Target = deviceName,
                                Command = command,
                                Parameters = parameters != null
                                    ? new Dictionary<string, object>(parameters)
                                    : new Dictionary<string, object>(),
                                ExpectedResults = new Dictionary<string, object>(),
                                TimeoutMs = timeoutMs
                            };

                            UiEventBus.PublishLog($"[Exec:{traceId}] -> {deviceName}.{command} (attempt {attempt}/{attempts})");
                            var outputs = DeviceServices.Factory.UseDevice(deviceName, devConf, dev =>
                            {
                                var res = dev.Execute(sc, runCtx);
                                if (!res.Success)
                                    throw new Exception("Device exec failed: " + res.Message);
                                return res.Outputs ?? new Dictionary<string, object>();
                            });
                            return outputs ?? new Dictionary<string, object>();
                        }
                    });
                }
                catch (Exception ex)
                {
                    if (attempt >= attempts) throw;
                    // 缓存完整异常文本，确保重试日志记录下详细的异常原因
                    var exceptionDetail = ex.ToString();
                    UiEventBus.PublishLog(
                        $"[Retry] {deviceName}.{command} 失败：{exceptionDetail}，{delayMs}ms 后重试（第 {attempt} 次，共 {attempts} 次）");
                    SafeDelay(delayMs, ctx.Cancellation);
                }
            }

            return new Dictionary<string, object>(); // 理论到不了
        }

        private static Dictionary<string, object> MergeOutputs(
            AggregationMode mode,
            Dictionary<string, object> main,
            Dictionary<string, Dictionary<string, object>> extras)
        {
            var root = main != null ? new Dictionary<string, object>(main) : new Dictionary<string, object>();

            if (extras == null || extras.Count == 0)
                return root;

            if (mode == AggregationMode.Namespace)
            {
                foreach (var kv in extras)
                    root[kv.Key] = kv.Value ?? new Dictionary<string, object>();
                return root;
            }

            // flat：展平，增加前缀
            foreach (var kv in extras)
            {
                var prefix = kv.Key;
                var dict = kv.Value ?? new Dictionary<string, object>();
                foreach (var kv2 in dict)
                {
                    var key = $"{prefix}.{kv2.Key}";
                    root[key] = kv2.Value;
                }
            }
            return root;
        }

        private static Dictionary<string, object> TryRun(Func<Dictionary<string, object>> f, out bool ok, out Exception err)
        {
            try { var r = f(); ok = true; err = null; return r; }
            catch (Exception ex) { ok = false; err = ex; return new Dictionary<string, object>(); }
        }

        private static string BuildMessage(bool finalSuccess, Exception mainError, bool extrasSucceeded, ExecSpec spec)
        {
            if (!finalSuccess)
            {
                if (mainError != null)
                    return "main failed: " + mainError.Message;
                if (!extrasSucceeded && !spec.ContinueOnExtraFailure)
                    return "extras failed";
            }
            return "ok";
        }

        private static void SafeDelay(int ms, CancellationToken token)
        {
            if (ms <= 0) return;
            try { Task.Delay(ms, token).Wait(token); } catch { }
        }

        // ========= 配置解析 & 支撑类型 =========

        private class ExecSpec
        {
            public ExecMode Mode;
            public AggregationMode Aggregation;
            public bool ContinueOnExtraFailure;
            public int PreDelayMs;
            public int PostDelayMs;
            public RetrySpec MainRetry;
            public List<ExtraDeviceSpec> Extras;

            public static ExecSpec ParseFrom(IDictionary<string, object> parameters)
            {
                var spec = new ExecSpec
                {
                    Mode = ExecMode.Parallel,
                    Aggregation = AggregationMode.Namespace,
                    ContinueOnExtraFailure = true,
                    PreDelayMs = 0,
                    PostDelayMs = 0,
                    MainRetry = new RetrySpec { Attempts = 1, DelayMs = 0 },
                    Extras = new List<ExtraDeviceSpec>()
                };

                if (parameters == null) return spec;

                object raw;
                if (!parameters.TryGetValue("__exec", out raw) || raw == null)
                    return spec;

                var jo = ToJObject(raw);
                if (jo == null) return spec;

                spec.Mode = ParseEnum<ExecMode>(jo.Value<string>("mode"), ExecMode.Parallel);
                spec.Aggregation = ParseEnum<AggregationMode>(jo.Value<string>("aggregation"), AggregationMode.Namespace);
                spec.ContinueOnExtraFailure = jo.Value<bool?>("continueOnExtraFailure") ?? true;
                spec.PreDelayMs = jo.Value<int?>("preDelayMs") ?? 0;
                spec.PostDelayMs = jo.Value<int?>("postDelayMs") ?? 0;

                var mainRetry = jo["mainRetry"] as JObject;
                if (mainRetry != null)
                {
                    spec.MainRetry = new RetrySpec
                    {
                        Attempts = (int?)mainRetry["attempts"] ?? 1,
                        DelayMs = (int?)mainRetry["delayMs"] ?? 0
                    };
                }

                var arr = jo["extras"] as JArray;
                if (arr != null)
                {
                    int extraIndex = 0;
                    foreach (var item in arr)
                    {
                        // 记录当前 extras 的下标，便于日志输出后自增计数
                        int currentIndex = extraIndex++;
                        var e = item as JObject;
                        if (e == null)
                        {
                            // 保障数组中的节点可被解析，若遇到非 JObject 则直接跳过
                            continue;
                        }
                        var ex = new ExtraDeviceSpec
                        {
                            Target = e.Value<string>("device"),
                            Command = e.Value<string>("command"),
                            Alias = e.Value<string>("alias"),
                            TimeoutMs = e.Value<int?>("timeoutMs") ?? 0,
                            Start = ParseEnum<ExtraStart>(e.Value<string>("start"), ExtraStart.WithMain),
                            Join = ParseEnum<ExtraJoin>(e.Value<string>("join"), ExtraJoin.Wait),
                            Parameters = ToDictionary(e["parameters"])
                        };
                        var r = e["retry"] as JObject;
                        if (r != null)
                        {
                            ex.Retry = new RetrySpec
                            {
                                Attempts = (int?)r["attempts"] ?? 1,
                                DelayMs = (int?)r["delayMs"] ?? 0
                            };
                        }
                        if (string.IsNullOrWhiteSpace(ex.Target) || string.IsNullOrWhiteSpace(ex.Command))
                        {
                            // 当附属设备配置缺少必填字段时，记录警告并跳过，避免后续执行阶段出现空引用
                            var warnMsg = $"[BuildPlan] extras[{currentIndex}] 缺少 device 或 command，已忽略该节点";
                            LogHelper.Warn(warnMsg);
                            UiEventBus.PublishLog(warnMsg);
                            continue;
                        }
                        spec.Extras.Add(ex);
                    }
                }

                return spec;
            }

            private static JObject ToJObject(object o)
            {
                if (o == null) return null;
                if (o is JObject) return (JObject)o;
                if (o is string)
                {
                    try { return JObject.Parse((string)o); } catch { return null; }
                }
                try
                {
                    var json = JsonConvert.SerializeObject(o);
                    return JObject.Parse(json);
                }
                catch { return null; }
            }

            private static T ParseEnum<T>(string s, T def) where T : struct
            {
                if (string.IsNullOrEmpty(s)) return def;
                try
                {
                    T v;
                    if (Enum.TryParse<T>(s, true, out v)) return v;
                }
                catch { }
                return def;
            }

            private static Dictionary<string, object> ToDictionary(JToken token)
            {
                if (token == null) return new Dictionary<string, object>();
                try
                {
                    return JsonConvert.DeserializeObject<Dictionary<string, object>>(token.ToString())
                           ?? new Dictionary<string, object>();
                }
                catch
                {
                    return new Dictionary<string, object>();
                }
            }
        }
    }

    /// <summary>
    /// 设备级互斥锁，防止多个并发步骤争用同一物理设备（串口、CAN通道等）
    /// </summary>
    internal static class DeviceLockRegistry
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        public static T WithLock<T>(string device, Func<T> fn)
        {
            var sem = _locks.GetOrAdd(device, _ => new SemaphoreSlim(1, 1));
            sem.Wait();
            try { return fn(); }
            finally { try { sem.Release(); } catch { } }
        }
    }
    public class UnifiedExecStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModels)context.Workflow.Data;
            if (data.WorkflowCompleted || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();
            var stepCfg = WorkflowServices.FlowCfg.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[UnifiedExec] 未找到步骤配置: {data.Current}");
                return ExecutionResult.Next();
            }
            try
            {
                if (stepCfg.Type == "SubFlow")
                    new SubFlowLiteExecutor().RunSubFlow(stepCfg, data, stepCfg);
                else if (stepCfg.Type == "SubFlowRef")
                {
                    if (string.IsNullOrEmpty(stepCfg.Ref))
                    {
                        data.LastSuccess = false;
                        UiEventBus.PublishLog($"[UnifiedExec] 步骤 {stepCfg.Name} 缺少 Ref 字段");
                    }
                    else
                    {
                        StepConfig subDef;
                        if (WorkflowServices.Subflows != null && WorkflowServices.Subflows.TryGet(stepCfg.Ref, out subDef))
                        {
                            UiEventBus.PublishLog($"[UnifiedExec] 执行子流程引用 {stepCfg.Ref} (from {stepCfg.Name})");
                            new SubFlowLiteExecutor().RunSubFlow(subDef, data, stepCfg);
                        }
                        else
                        {
                            data.LastSuccess = false;
                            UiEventBus.PublishLog($"[UnifiedExec] 未找到子流程引用: {stepCfg.Ref} (from {stepCfg.Name})");
                        }
                    }
                }
                else
                {
                    new DeviceExecStep().Run(context);
                }
            }
            catch (Exception ex)
            {
                // 统一执行节点的异常同样输出堆栈，方便跟踪具体来源
                var exceptionDetail = ex.ToString();
                data.LastSuccess = false;
                UiEventBus.PublishLog(
                    $"[UnifiedExec] 执行步骤 {stepCfg.Name} 异常: {exceptionDetail} | SessionId={data.SessionId} | 模型={data.Model} | SN={data.Sn} | 当前步骤={data.Current}");
            }
            return ExecutionResult.Next();
        }
    }

    public class RouteStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowModels)context.Workflow.Data;
            if (data.WorkflowCompleted || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();
            var stepCfg = WorkflowServices.FlowCfg.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.WorkflowCompleted = true; UiEventBus.PublishLog("[Route] 找不到当前步骤配置，强制结束");
                return ExecutionResult.Next();
            }

            string next = data.LastSuccess ? stepCfg.OnSuccess : stepCfg.OnFailure;
            UiEventBus.PublishLog($"[Route] {stepCfg.Name} -> {(string.IsNullOrEmpty(next) ? "(结束)" : next)} | LastSuccess={data.LastSuccess}");
            if (string.IsNullOrEmpty(next))
                data.WorkflowCompleted = true;
            else
                data.Current = next;
            if (data.WorkflowCompleted)
            {
                // 将最终的成功/失败状态写入数据库，避免流程失败却被标记为成功
                DeviceServices.Db.FinishTestSession(data.SessionId, data.LastSuccess ? 1 : 0);
                UiEventBus.PublishCompleted(data.SessionId.ToString(), data.Model);
            }
            return ExecutionResult.Next();
        }
    }
    public class WorkflowBuildLite : IWorkflow<FlowModels>
    {
        public string Id => "WorkflowBuildLite";
        public int Version => 1;
        public void Build(IWorkflowBuilder<FlowModels> builder)
        {
            builder
                .StartWith<InitStep>()
                .While(data => !data.WorkflowCompleted)
                .Do(seq => seq.StartWith<UnifiedExecStep>()
                                 .Then<RouteStep>());
        }
    }
}

