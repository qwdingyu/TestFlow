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
            UiEventBus.PublishLog($"[Init] ��Ʒ={data.Model}, SN={data.Sn}, SessionId={data.SessionId}, ��ʼ����={data.Current}");
            return ExecutionResult.Next();
        }
    }
    /// <summary>
    /// ��ʿ������ DeviceExecStep
    /// - ֧������ڵ�Ŀ��豸������ExtraDevices��
    /// - ����/���У��ȴ�/���Բ��ԣ����ԡ���ʱ���ۺ�ģʽ���豸��������ǰ�����ӳ�
    /// - ��ԭ�����ݿ��¼ / ���������߼�����һ��
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
                UiEventBus.PublishLog($"[DeviceExec] δ�ҵ���������: {data.Current}");
                return ExecutionResult.Next();
            }
            UiEventBus.PublishLog($"--[Flow] ��ʼ {stepCfg.Name}, �豸��{stepCfg.Target}��, ������{stepCfg.Description}��, ��һ����{stepCfg.OnSuccess}��");
            var started = DateTime.Now;

            var pooledResult = StepResultPool.Instance.Get();  // ��ԭ��һ�£����ö���س������
            try
            {
                // ����ִ���õ� Step��֧�� @from_db ��չ����
                var execStep = StepUtils.BuildExecutableStep(stepCfg, data);

                // ������ʿ�������ã�ȫ����ѡ��
                var execSpec = ExecSpec.ParseFrom(execStep.Parameters);

                // �豸��ʱ��ȫ��ȡ������
                var baseToken = DeviceServices.Context != null ? DeviceServices.Context.Cancellation : CancellationToken.None;
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(baseToken))
                {
                    if (execStep.TimeoutMs > 0) linkedCts.CancelAfter(execStep.TimeoutMs);
                    var stepCtx = DeviceServices.Context != null ? DeviceServices.Context.CloneWithCancellation(linkedCts.Token)
                                                              : new StepContext(data.Model, linkedCts.Token);

                    // traceId �ᴩ��־
                    var traceId = Guid.NewGuid().ToString("N").Substring(0, 8);

                    // ǰ���ӳ٣�����������ڣ�
                    if (execSpec.PreDelayMs > 0)
                        SafeDelay(execSpec.PreDelayMs, stepCtx.Cancellation);

                    // �������豸���񣨿ɼ����ԣ�
                    Func<Dictionary<string, object>> runMain = () =>
                    {
                        return ExecuteWithRetry(execStep.Target, execStep.Command, execStep.Parameters, execStep.TimeoutMs,
                                                execSpec.MainRetry, stepCtx, traceId);
                    };

                    // ���������豸�����б�
                    var extraPlans = execSpec.Extras ?? new List<ExtraDeviceSpec>(0);
                    var extraTasks = new List<Task<KeyValuePair<string, Dictionary<string, object>>>>();

                    Action startExtras = () =>
                    {
                        for (int i = 0; i < extraPlans.Count; i++)
                        {
                            var ex = extraPlans[i];
                            if (ex.Start == ExtraStart.Before && execSpec.Mode != ExecMode.ExtrasFirst)
                                continue; // ���ڴ�ʱ������

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

                    // ִ�б���
                    Dictionary<string, object> mainOutputs = null;
                    bool mainSucceeded = false;
                    Exception mainError = null;

                    if (execSpec.Mode == ExecMode.MainFirst)
                    {
                        // �������ٸ���
                        mainOutputs = TryRun(runMain, out mainSucceeded, out mainError);
                        startExtras(); // ���� Start=after/with_main ������
                    }
                    else if (execSpec.Mode == ExecMode.ExtrasFirst)
                    {
                        // �ȸ�����Start=before/with_main��
                        startExtras();
                        mainOutputs = TryRun(runMain, out mainSucceeded, out mainError);
                    }
                    else // parallel
                    {
                        // ���У������� with_main �ĸ�����������
                        // Ҳ���� Extra.Start=with_main ���ڲ���ʱһ����
                        startExtras();
                        mainOutputs = TryRun(runMain, out mainSucceeded, out mainError);
                    }

                    // �ȴ�������wait���Եģ�
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
                            // ����һ�������쳣��ȡ��
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

                    // �ۺ����
                    var merged = MergeOutputs(execSpec.Aggregation, mainOutputs, extrasOutputs);

                    // �����ӳ�
                    if (execSpec.PostDelayMs > 0)
                        SafeDelay(execSpec.PostDelayMs, stepCtx.Cancellation);

                    // ��װ���
                    pooledResult.Outputs = merged;

                    // �ɹ��ж�����ʧ�� = ����ʧ�ܣ����ɹ� + ����ʧ�� �� ������
                    bool finalSuccess = mainSucceeded &&
                                        (extrasSucceeded || execSpec.ContinueOnExtraFailure);

                    pooledResult.Success = finalSuccess;
                    pooledResult.Message = BuildMessage(finalSuccess, mainError, extrasSucceeded, execSpec);

                    // ����ֵ����������ԭ���߼���
                    ExpectedResultEvaluator.ApplyToStepResult(execStep, pooledResult, logSuccess: false, logFailure: false);

                    // д�⣨����ԭ�߼���
                    DeviceServices.Db.AppendStep(
                        data.SessionId, data.Model, data.Sn,
                        execStep.Name, execStep.Description, execStep.Target, execStep.Command,
                        JsonConvert.SerializeObject(execStep.Parameters),
                        JsonConvert.SerializeObject(execStep.ExpectedResults),
                        JsonConvert.SerializeObject(pooledResult.Outputs),
                        pooledResult.Success ? 1 : 0, pooledResult.Message, started, DateTime.Now);

                    data.LastSuccess = pooledResult.Success;
                    UiEventBus.PublishLog($"[Step] {execStep.Name} | �豸={execStep.Target} | Success={pooledResult.Success} | Msg={pooledResult.Message}");
                }
            }
            catch (Exception ex)
            {
                // ʹ�������쳣�ַ���ȷ����־�а�����ջ��Ϣ�����ں����Ų�
                var exceptionDetail = ex.ToString();
                data.LastSuccess = false;
                DeviceServices.Db.AppendStep(
                    data.SessionId, data.Model, data.Sn,
                    stepCfg.Name, stepCfg.Description, stepCfg.Target, stepCfg.Command,
                    JsonConvert.SerializeObject(stepCfg.Parameters),
                    JsonConvert.SerializeObject(stepCfg.ExpectedResults),
                    null, 0, "Exception: " + exceptionDetail, started, DateTime.Now);
                UiEventBus.PublishLog(
                    $"[Step-Exception] {stepCfg.Name} | SessionId={data.SessionId} | ģ��={data.Model} | SN={data.Sn} | ��������={exceptionDetail}");
            }
            finally
            {
                StepResultPool.Instance.Return(pooledResult);
            }

            return ExecutionResult.Next();
        }

        /// <summary>
        /// �ṩ�������̱��������õ�ִ����ڣ�
        /// �ڲ����� WorkflowCore �����ĵ�ǰ���£���ɵ����豸�����ʵ��ִ�С���ʱ����������ֵУ�顣
        /// </summary>
        /// <param name="step">�Ѿ�������չ����Ĳ������á�</param>
        /// <param name="sharedCtx">�����̹���Ĳ��������ģ����ڸ���ģ����Ϣ��ȡ�����ơ�</param>
        /// <returns>��װִ��״̬������Լ�ʱ����Ľ������</returns>
        internal static OrchTaskResult ExecuteSingleStep(StepConfig step, StepContext sharedCtx)
        {
            var now = DateTime.Now;
            if (step == null)
            {
                // ���׷��أ������ⲿ���÷�������ȱʧ�����ֿ������쳣��
                return new OrchTaskResult
                {
                    Success = false,
                    Message = "��������Ϊ��",
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
                // ͳһ�����輶��ʱ���빲���������е�ȡ�����ƽ��кϲ������ϳ�ʱ���ܼ�ʱ�˳���
                var baseToken = sharedCtx != null ? sharedCtx.Cancellation : CancellationToken.None;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(baseToken))
                {
                    if (step.TimeoutMs > 0)
                        linked.CancelAfter(step.TimeoutMs);

                    // ����ȫ����������Ϣ��ģ�͡�Bag �ȣ������滻ȡ�����ơ�
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

                // ͳһ������ֵУ���߼���������������һ�µ��жϽ����
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
                UiEventBus.PublishLog($"[SubStep-Exception] {step.Name} | ����={ex.Message}");
            }
            finally
            {
                taskResult.FinishedAt = DateTime.Now;
                StepResultPool.Instance.Return(pooledResult);
            }

            return taskResult;
        }

        // ===== ִ����/����һ�Σ������� + �豸�� + ��ʱ��=====
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
                    // ��Ը��豸�Ļ����������л���ͬһ�豸�ķ��ʣ����⴮��/�Ự��ͻ��
                    return DeviceLockRegistry.WithLock(deviceName, () =>
                    {
                        // Ϊ�ô�ִ�е������ø����ĳ�ʱ������У�
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
                    // ���������쳣�ı���ȷ��������־��¼����ϸ���쳣ԭ��
                    var exceptionDetail = ex.ToString();
                    UiEventBus.PublishLog(
                        $"[Retry] {deviceName}.{command} ʧ�ܣ�{exceptionDetail}��{delayMs}ms �����ԣ��� {attempt} �Σ��� {attempts} �Σ�");
                    SafeDelay(delayMs, ctx.Cancellation);
                }
            }

            return new Dictionary<string, object>(); // ���۵�����
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

            // flat��չƽ������ǰ׺
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

        // ========= ���ý��� & ֧������ =========

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
                        // ��¼��ǰ extras ���±꣬������־�������������
                        int currentIndex = extraIndex++;
                        var e = item as JObject;
                        if (e == null)
                        {
                            // ���������еĽڵ�ɱ��������������� JObject ��ֱ������
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
                            // �������豸����ȱ�ٱ����ֶ�ʱ����¼���沢�������������ִ�н׶γ��ֿ�����
                            var warnMsg = $"[BuildPlan] extras[{currentIndex}] ȱ�� device �� command���Ѻ��Ըýڵ�";
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
    /// �豸������������ֹ���������������ͬһ�����豸�����ڡ�CANͨ���ȣ�
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
                UiEventBus.PublishLog($"[UnifiedExec] δ�ҵ���������: {data.Current}");
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
                        UiEventBus.PublishLog($"[UnifiedExec] ���� {stepCfg.Name} ȱ�� Ref �ֶ�");
                    }
                    else
                    {
                        StepConfig subDef;
                        if (WorkflowServices.Subflows != null && WorkflowServices.Subflows.TryGet(stepCfg.Ref, out subDef))
                        {
                            UiEventBus.PublishLog($"[UnifiedExec] ִ������������ {stepCfg.Ref} (from {stepCfg.Name})");
                            new SubFlowLiteExecutor().RunSubFlow(subDef, data, stepCfg);
                        }
                        else
                        {
                            data.LastSuccess = false;
                            UiEventBus.PublishLog($"[UnifiedExec] δ�ҵ�����������: {stepCfg.Ref} (from {stepCfg.Name})");
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
                // ͳһִ�нڵ���쳣ͬ�������ջ��������پ�����Դ
                var exceptionDetail = ex.ToString();
                data.LastSuccess = false;
                UiEventBus.PublishLog(
                    $"[UnifiedExec] ִ�в��� {stepCfg.Name} �쳣: {exceptionDetail} | SessionId={data.SessionId} | ģ��={data.Model} | SN={data.Sn} | ��ǰ����={data.Current}");
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
                data.WorkflowCompleted = true; UiEventBus.PublishLog("[Route] �Ҳ�����ǰ�������ã�ǿ�ƽ���");
                return ExecutionResult.Next();
            }

            string next = data.LastSuccess ? stepCfg.OnSuccess : stepCfg.OnFailure;
            UiEventBus.PublishLog($"[Route] {stepCfg.Name} -> {(string.IsNullOrEmpty(next) ? "(����)" : next)} | LastSuccess={data.LastSuccess}");
            if (string.IsNullOrEmpty(next))
                data.WorkflowCompleted = true;
            else
                data.Current = next;
            if (data.WorkflowCompleted)
            {
                // �����յĳɹ�/ʧ��״̬д�����ݿ⣬��������ʧ��ȴ�����Ϊ�ɹ�
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

