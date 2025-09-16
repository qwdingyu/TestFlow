using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.WorkflowLib.Engine;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

namespace ZL.WorkflowLib.Workflow
{
    public class InitStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowData)context.Workflow.Data;
            data.Done = false; data.LastSuccess = true; data.Model = DeviceServices.Config.Model;
            foreach (var s in DeviceServices.Config.TestSteps)
            {
                if (s.DependsOn == null || s.DependsOn.Count == 0) { data.Current = s.Name; break; }
            }
            if (string.IsNullOrEmpty(data.Current)) data.Done = true;
            data.SessionId = DeviceServices.Db.StartTestSession(data.Model, data.Sn);
            UiEventBus.PublishLog($"[Init] 产品={data.Model}, SN={data.Sn}, SessionId={data.SessionId}, 起始步骤={data.Current}");
            return ExecutionResult.Next();
        }
    }

    //public class DeviceExecStep : StepBody
    //{
    //    public override ExecutionResult Run(IStepExecutionContext context)
    //    {
    //        var data = (FlowData)context.Workflow.Data;
    //        if (data.Done || string.IsNullOrEmpty(data.Current))
    //            return ExecutionResult.Next();
    //        var stepCfg = DeviceServices.Config.TestSteps.Find(x => x.Name == data.Current);
    //        if (stepCfg == null)
    //        {
    //            data.LastSuccess = false;
    //            UiEventBus.PublishLog($"[DeviceExec] 未找到步骤配置: {data.Current}");
    //            return ExecutionResult.Next();
    //        }
    //        UiEventBus.PublishLog($"--[Flow] 开始 {stepCfg.Name}, 设备【{stepCfg.Device}】, 描述【{stepCfg.Description}】, 下一步【{stepCfg.OnSuccess}】");
    //        var started = DateTime.Now;
    //        var pooledResult = StepResultPool.Instance.Get();
    //        try
    //        {
    //            var execStep = StepUtils.BuildExecutableStep(stepCfg, data);
    //            DeviceConfig devConf;
    //            if (!DeviceServices.Config.Devices.TryGetValue(execStep.Device, out devConf))
    //                throw new Exception("Device not found: " + execStep.Device);

    //            // 步骤级超时：与全局取消令牌联动
    //            var baseToken = DeviceServices.Context != null ? DeviceServices.Context.Cancellation : System.Threading.CancellationToken.None;
    //            using (var linked = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(baseToken))
    //            {
    //                if (execStep.TimeoutMs > 0)
    //                    linked.CancelAfter(execStep.TimeoutMs);
    //                var stepCtx = DeviceServices.Context != null ? DeviceServices.Context.CloneWithCancellation(linked.Token)
    //                                                         : new ZL.DeviceLib.Engine.StepContext(data.Model, linked.Token);

    //                var outputs = DeviceServices.Factory.UseDevice(execStep.Device, devConf, dev =>
    //                {
    //                    var result = dev.Execute(execStep, stepCtx);
    //                    pooledResult.Success = result.Success; pooledResult.Message = result.Message; pooledResult.Outputs = result.Outputs ?? new Dictionary<string, object>();
    //                    return pooledResult.Outputs;
    //                });
    //            }
    //            string reason;
    //            bool passExpected = ResultEvaluator.Evaluate(execStep.ExpectedResults, pooledResult.Outputs, execStep.Parameters, out reason);
    //            if (!passExpected)
    //            {
    //                pooledResult.Success = false;
    //                pooledResult.Message = (pooledResult.Message ?? "") + " | expected mismatch: " + reason;
    //            }
    //            DeviceServices.Db.AppendStep(data.SessionId, data.Model, data.Sn, execStep.Name, execStep.Description, execStep.Device, execStep.Command,
    //                JsonConvert.SerializeObject(execStep.Parameters), JsonConvert.SerializeObject(execStep.ExpectedResults), JsonConvert.SerializeObject(pooledResult.Outputs),
    //                pooledResult.Success ? 1 : 0, pooledResult.Message, started, DateTime.Now);
    //            data.LastSuccess = pooledResult.Success;
    //            UiEventBus.PublishLog($"[Step] {execStep.Name} | 设备={execStep.Device} | Success={pooledResult.Success} | Msg={pooledResult.Message}");
    //        }
    //        catch (Exception ex)
    //        {
    //            data.LastSuccess = false;
    //            DeviceServices.Db.AppendStep(data.SessionId, data.Model, data.Sn, stepCfg.Name, stepCfg.Description, stepCfg.Device, stepCfg.Command,
    //                JsonConvert.SerializeObject(stepCfg.Parameters), JsonConvert.SerializeObject(stepCfg.ExpectedResults), null, 0, "Exception: " + ex.Message, started, DateTime.Now);
    //            UiEventBus.PublishLog($"[Step-Exception] {stepCfg.Name} | 错误={ex.Message}");
    //        }
    //        finally
    //        {
    //            StepResultPool.Instance.Return(pooledResult);
    //        }
    //        return ExecutionResult.Next();
    //    }
    //}

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
            var data = (FlowData)context.Workflow.Data;
            if (data.Done || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();

            var stepCfg = DeviceServices.Config.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[DeviceExec] 未找到步骤配置: {data.Current}");
                return ExecutionResult.Next();
            }

            UiEventBus.PublishLog($"--[Flow] 开始 {stepCfg.Name}, 设备, 描述, 下一步");
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
                        return ExecuteWithRetry(execStep.Device, execStep.Command, execStep.Parameters, execStep.TimeoutMs,
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

                            var alias = string.IsNullOrEmpty(ex.Alias) ? ex.Device : ex.Alias;

                            var t = Task.Run(() =>
                            {
                                var outputs = ExecuteWithRetry(ex.Device, ex.Command, ex.Parameters, ex.TimeoutMs,
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
                    string reason;
                    bool passExpected = ResultEvaluator.Evaluate(execStep.ExpectedResults, pooledResult.Outputs, execStep.Parameters, out reason);
                    if (!passExpected)
                    {
                        pooledResult.Success = false;
                        pooledResult.Message = (pooledResult.Message ?? "") + " | expected mismatch: " + reason;
                    }

                    // 写库（沿用原逻辑）
                    DeviceServices.Db.AppendStep(
                        data.SessionId, data.Model, data.Sn,
                        execStep.Name, execStep.Description, execStep.Device, execStep.Command,
                        JsonConvert.SerializeObject(execStep.Parameters),
                        JsonConvert.SerializeObject(execStep.ExpectedResults),
                        JsonConvert.SerializeObject(pooledResult.Outputs),
                        pooledResult.Success ? 1 : 0, pooledResult.Message, started, DateTime.Now);

                    data.LastSuccess = pooledResult.Success;
                    UiEventBus.PublishLog($"[Step] {execStep.Name} | 设备={execStep.Device} | Success={pooledResult.Success} | Msg={pooledResult.Message}");
                }
            }
            catch (Exception ex)
            {
                data.LastSuccess = false;
                DeviceServices.Db.AppendStep(
                    data.SessionId, data.Model, data.Sn,
                    stepCfg.Name, stepCfg.Description, stepCfg.Device, stepCfg.Command,
                    JsonConvert.SerializeObject(stepCfg.Parameters),
                    JsonConvert.SerializeObject(stepCfg.ExpectedResults),
                    null, 0, "Exception: " + ex.Message, started, DateTime.Now);
                UiEventBus.PublishLog($"[Step-Exception] {stepCfg.Name} | 错误={ex.Message}");
            }
            finally
            {
                StepResultPool.Instance.Return(pooledResult);
            }

            return ExecutionResult.Next();
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
                            if (!DeviceServices.Config.Devices.TryGetValue(deviceName, out devConf))
                                throw new Exception("Device not found: " + deviceName);

                            var sc = new StepConfig
                            {
                                Name = $"{deviceName}:{command}",
                                Description = "",
                                Device = deviceName,
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
                    UiEventBus.PublishLog($"[Retry] {deviceName}.{command} 失败：{ex.Message}，{delayMs}ms 后重试");
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
                            Device = e.Value<string>("device"),
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
                        if (string.IsNullOrWhiteSpace(ex.Device) || string.IsNullOrWhiteSpace(ex.Command))
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

        private class ExtraDeviceSpec
        {
            public string Device;
            public string Command;
            public string Alias;
            public int TimeoutMs;
            public RetrySpec Retry;
            public ExtraStart Start;
            public ExtraJoin Join;
            public Dictionary<string, object> Parameters;
        }

        private class RetrySpec
        {
            public int Attempts;
            public int DelayMs;
        }

        private enum ExecMode { MainFirst, ExtrasFirst, Parallel }
        private enum AggregationMode { Namespace, Flat }
        private enum ExtraStart { Before, WithMain, After }
        private enum ExtraJoin { Wait, Forget }
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
            var data = (FlowData)context.Workflow.Data;
            if (data.Done || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();
            var stepCfg = DeviceServices.Config.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[UnifiedExec] 未找到步骤配置: {data.Current}");
                return ExecutionResult.Next();
            }
            try
            {
                if (stepCfg.Type == "SubFlow")
                    new SubFlowExecutor().RunSubFlow(stepCfg, data, stepCfg);
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
                            new SubFlowExecutor().RunSubFlow(subDef, data, stepCfg);
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
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[UnifiedExec] 执行步骤 {stepCfg.Name} 异常: {ex.Message}");
            }
            return ExecutionResult.Next();
        }
    }

    public class RouteStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowData)context.Workflow.Data;
            if (data.Done || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();
            var stepCfg = DeviceServices.Config.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.Done = true; UiEventBus.PublishLog("[Route] 找不到当前步骤配置，强制结束");
                return ExecutionResult.Next();
            }

            string next = data.LastSuccess ? stepCfg.OnSuccess : stepCfg.OnFailure;
            UiEventBus.PublishLog($"[Route] {stepCfg.Name} -> {(string.IsNullOrEmpty(next) ? "(结束)" : next)} | LastSuccess={data.LastSuccess}");
            if (string.IsNullOrEmpty(next))
                data.Done = true;
            else
                data.Current = next;
            if (data.Done)
            {
                DeviceServices.Db.FinishTestSession(data.SessionId);
                UiEventBus.PublishCompleted(data.SessionId.ToString(), data.Model);
            }
            return ExecutionResult.Next();
        }
    }
}
