using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZL.DeviceLib;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib.Workflow
{
    /// <summary>
    /// 描述一次设备步骤在执行过程中的所有上下文信息，供多个 StepBody 共享。
    /// </summary>
    public class DeviceExecutionContext
    {
        public DeviceExecutionContext()
        {
            ExtraOutputs = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            ExtrasSuccess = true;
        }

        /// <summary>
        /// 原始的步骤配置，未进行参数展开。
        /// </summary>
        public StepConfig SourceStep { get; set; }

        /// <summary>
        /// 解析并展开参数后的实际执行步骤配置。
        /// </summary>
        public StepConfig ExecutableStep { get; set; }

        /// <summary>
        /// 瑞士军刀扩展配置（主/附属执行模式、重试、聚合等）。
        /// </summary>
        public DeviceExecSpec Specification { get; set; }

        /// <summary>
        /// 主设备执行结果的输出字典。
        /// </summary>
        public Dictionary<string, object> MainOutputs { get; set; }

        /// <summary>
        /// 主设备执行是否成功。
        /// </summary>
        public bool MainSuccess { get; set; }

        /// <summary>
        /// 主设备执行过程中捕获的异常，用于在最终消息中输出详细信息。
        /// </summary>
        public Exception MainError { get; set; }

        /// <summary>
        /// 按照别名归档的附属设备输出结果集合，仅统计 Join=Wait 的附属任务。
        /// </summary>
        public Dictionary<string, Dictionary<string, object>> ExtraOutputs { get; }

        /// <summary>
        /// 附属设备整体是否成功（仅统计需要等待的任务）。
        /// </summary>
        public bool ExtrasSuccess { get; set; }

        /// <summary>
        /// 用于日志追踪的 traceId，贯穿主/附属设备执行。
        /// </summary>
        public string TraceId { get; set; }

        /// <summary>
        /// 步骤开始时间，用于写入数据库记录。
        /// </summary>
        public DateTime StartedAt { get; set; }

        /// <summary>
        /// 步骤结束时间，便于统一持久化。
        /// </summary>
        public DateTime FinishedAt { get; set; }

        /// <summary>
        /// 主设备互斥锁的释放句柄，由 DeviceLockStep 负责设置。
        /// </summary>
        public IDisposable MainLockHandle { get; set; }
    }

    /// <summary>
    /// 描述瑞士军刀设备执行配置的实体，负责解析参数字典并提供阶段性过滤能力。
    /// </summary>
    internal class DeviceExecSpec
    {
        public ExecMode Mode { get; set; }
        public AggregationMode Aggregation { get; set; }
        public bool ContinueOnExtraFailure { get; set; }
        public int PreDelayMs { get; set; }
        public int PostDelayMs { get; set; }
        public RetrySpec MainRetry { get; set; }
        public List<ExtraDeviceSpec> Extras { get; set; }

        public DeviceExecSpec()
        {
            Extras = new List<ExtraDeviceSpec>();
            MainRetry = new RetrySpec { Attempts = 1, DelayMs = 0 };
        }

        /// <summary>
        /// 根据扩展参数解析出执行配置，兼容历史上在 Parameters 中塞入的 __exec 节点。
        /// </summary>
        public static DeviceExecSpec ParseFrom(IDictionary<string, object> parameters)
        {
            var spec = new DeviceExecSpec
            {
                Mode = ExecMode.Parallel,
                Aggregation = AggregationMode.Namespace,
                ContinueOnExtraFailure = true,
                PreDelayMs = 0,
                PostDelayMs = 0
            };

            if (parameters == null)
                return spec;

            object raw;
            if (!parameters.TryGetValue("__exec", out raw) || raw == null)
                return spec;

            var jo = ToJObject(raw);
            if (jo == null)
                return spec;

            spec.Mode = ParseEnum(jo.Value<string>("mode"), ExecMode.Parallel);
            spec.Aggregation = ParseEnum(jo.Value<string>("aggregation"), AggregationMode.Namespace);
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
                int index = 0;
                foreach (var item in arr)
                {
                    int currentIndex = index++;
                    var extraObj = item as JObject;
                    if (extraObj == null)
                        continue;

                    var extra = new ExtraDeviceSpec
                    {
                        Device = extraObj.Value<string>("device"),
                        Command = extraObj.Value<string>("command"),
                        Alias = extraObj.Value<string>("alias"),
                        TimeoutMs = extraObj.Value<int?>("timeoutMs") ?? 0,
                        Start = ParseEnum(extraObj.Value<string>("start"), ExtraStart.WithMain),
                        Join = ParseEnum(extraObj.Value<string>("join"), ExtraJoin.Wait),
                        Parameters = ToDictionary(extraObj["parameters"])
                    };

                    var retryObj = extraObj["retry"] as JObject;
                    if (retryObj != null)
                    {
                        extra.Retry = new RetrySpec
                        {
                            Attempts = (int?)retryObj["attempts"] ?? 1,
                            DelayMs = (int?)retryObj["delayMs"] ?? 0
                        };
                    }

                    if (string.IsNullOrWhiteSpace(extra.Device) || string.IsNullOrWhiteSpace(extra.Command))
                    {
                        var warnMsg = $"[BuildPlan] extras[{currentIndex}] 缺少 device 或 command，已忽略该节点";
                        LogHelper.Warn(warnMsg);
                        UiEventBus.PublishLog(warnMsg);
                        continue;
                    }

                    spec.Extras.Add(extra);
                }
            }

            return spec;
        }

        /// <summary>
        /// 判断指定阶段是否存在需要执行的附属设备。
        /// </summary>
        public bool HasExtrasForPhase(ExtraDevicePhase phase)
        {
            return Extras.Any(e => MatchPhase(e, phase));
        }

        /// <summary>
        /// 根据阶段过滤出对应的附属设备列表。
        /// </summary>
        public IEnumerable<ExtraDeviceSpec> SelectExtras(ExtraDevicePhase phase)
        {
            for (int i = 0; i < Extras.Count; i++)
            {
                var item = Extras[i];
                if (MatchPhase(item, phase))
                    yield return item;
            }
        }

        private bool MatchPhase(ExtraDeviceSpec extra, ExtraDevicePhase phase)
        {
            switch (phase)
            {
                case ExtraDevicePhase.BeforeMain:
                    return extra.Start == ExtraStart.Before;
                case ExtraDevicePhase.WithMain:
                    if (extra.Start != ExtraStart.WithMain)
                        return false;
                    // 当模式为 MainFirst 时，“with_main” 实际被延后执行，因此不纳入并发阶段
                    return Mode != ExecMode.MainFirst;
                case ExtraDevicePhase.AfterMain:
                    if (extra.Start == ExtraStart.After)
                        return true;
                    if (extra.Start == ExtraStart.WithMain && Mode == ExecMode.MainFirst)
                        return true;
                    return false;
                default:
                    return false;
            }
        }

        private static JObject ToJObject(object raw)
        {
            if (raw == null)
                return null;
            if (raw is JObject j)
                return j;
            if (raw is string s)
            {
                try { return JObject.Parse(s); }
                catch { return null; }
            }
            try
            {
                var json = JsonConvert.SerializeObject(raw);
                return JObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> ToDictionary(JToken token)
        {
            if (token == null)
                return new Dictionary<string, object>();
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

        private static TEnum ParseEnum<TEnum>(string value, TEnum defaultValue) where TEnum : struct
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;
            try
            {
                TEnum parsed;
                if (Enum.TryParse(value, true, out parsed))
                    return parsed;
            }
            catch
            {
            }
            return defaultValue;
        }
    }

    internal class ExtraDeviceSpec
    {
        public string Device { get; set; }
        public string Command { get; set; }
        public string Alias { get; set; }
        public int TimeoutMs { get; set; }
        public RetrySpec Retry { get; set; }
        public ExtraStart Start { get; set; }
        public ExtraJoin Join { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }

    internal class RetrySpec
    {
        public int Attempts { get; set; }
        public int DelayMs { get; set; }
    }

    internal enum ExecMode
    {
        MainFirst,
        ExtrasFirst,
        Parallel
    }

    internal enum AggregationMode
    {
        Namespace,
        Flat
    }

    internal enum ExtraStart
    {
        Before,
        WithMain,
        After
    }

    internal enum ExtraJoin
    {
        Wait,
        Forget
    }

    /// <summary>
    /// 在 Workflow 分支中指定附属设备执行阶段的枚举。
    /// </summary>
    public enum ExtraDevicePhase
    {
        BeforeMain,
        WithMain,
        AfterMain
    }
}
