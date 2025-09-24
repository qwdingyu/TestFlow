using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices.Can
{
    public sealed class CanDevice : ICapabilityDevice, IHealthyDevice, IDisposable
    {
        private readonly CanTransport _transport;
        private readonly CanMessageScheduler _sched;

        public CanDevice(DeviceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            _transport = new CanTransport(cfg.ConnectionString, cfg.Name);
            _sched = new CanMessageScheduler(_transport);

            // 可选过滤器
            try
            {
                if (cfg.Settings != null && cfg.Settings.TryGetValue("can_filter_allowed_ids", out var o))
                {
                    var ids = (o is IEnumerable<object> ob ? ob.Select(x => x?.ToString()) :
                              (o is string s ? s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()) :
                               Array.Empty<string>()))
                              .Where(x => !string.IsNullOrWhiteSpace(x));
                    var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
                    _transport.SetFilter(m => set.Contains(m.Id));
                }
                else _transport.SetFilter(_ => true);
            }
            catch (Exception ex)
            {
                LogHelper.Warn($"[CAN] 过滤器初始化失败：{ex.Message}");
                _transport.SetFilter(_ => true);
            }
        }

        public void Dispose() => _sched.Dispose();

        public bool IsHealthy() => _transport.IsHealthy();
        public ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            try
            {
                // 这里不强行依赖 ExecContext，全量信息在 StepContext 即可
                var outputs = CallAsync(step?.Command ?? "", step?.Parameters ?? new Dictionary<string, object>(), ctx).GetAwaiter().GetResult();

                return new ExecutionResult { Success = true, Message = $"CAN {step?.Command} OK", Outputs = outputs };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Message = "CAN Exception: " + ex.Message, Outputs = new Dictionary<string, object>() };
            }
        }

        public async Task<Dictionary<string, object>> CallAsync(string capability, Dictionary<string, object> args, StepContext stepCtx)
        {
            if (capability == null) capability = string.Empty;
            var cap = capability.Trim();

            switch (cap)
            {
                case "StartPeriodic":
                    {
                        var id = Require(args, "id");
                        var data = Hex(Require(args, "data"));
                        var period = args.TryGetValue("periodMs", out var p) ? Convert.ToInt32(p) : 100;
                        _sched.UpsertPeriodic(id, data, period, enabled: true);
                        return new() { { "started", id }, { "periodMs", period } };
                    }

                case "StopPeriodic":
                    {
                        var id = Require(args, "id");
                        _sched.RemovePeriodic(id);
                        return new() { { "stopped", id } };
                    }

                case "TriggerEvent":
                    {
                        var id = Require(args, "id");
                        var ctrl = Hex(Require(args, "ctrlHex"));
                        var clear = Hex(args.TryGetValue("clearHex", out var z) ? z?.ToString() ?? "00 00 00 00 00 00 00 00" : "00 00 00 00 00 00 00 00");
                        var cnt = args.TryGetValue("count", out var c) ? Convert.ToInt32(c) : 3;
                        var iv = args.TryGetValue("intervalMs", out var i) ? Convert.ToInt32(i) : 100;

                        await _sched.EnqueueEventBurstAsync(id, ctrl, clear, iv, cnt, cnt);
                        return new() { { "triggered", id }, { "count", cnt }, { "intervalMs", iv } };
                    }

                case "SendOnce":
                    {
                        var id = Require(args, "id");
                        var data = Hex(Require(args, "data"));
                        await _sched.EnqueueEventBurstAsync(id, data, null, 0, 1, 0);
                        return new() { { "sent", id } };
                    }

                case "SendAndReceive":
                    {
                        var id = Require(args, "id");
                        var dat = ParseData(args.TryGetValue("data", out var d) ? d : null);
                        var to = args.TryGetValue("timeoutMs", out var t) ? Convert.ToInt32(t) : 2000;

                        _transport.Send(new CanMessage { Id = id, Data = dat, Timestamp = DateTime.Now });
                        var resp = _transport.WaitForResponse(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase), to, stepCtx.Cancellation);

                        return new() { { "response", "ACK" }, { "id", resp.Id }, { "data", BitConverter.ToString(resp.Data) } };
                    }

                default: throw new NotSupportedException($"CAN capability not supported: {cap}");
            }
        }

        // ========= 工具函数 =========
        private static string Require(Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var v) || v == null || string.IsNullOrWhiteSpace(v.ToString()))
                throw new ArgumentException($"Missing parameter '{key}'");
            return v.ToString();
        }

        private static byte[] ParseData(object val)
        {
            if (val == null) return Array.Empty<byte>();
            if (val is string s) return Hex(s);
            if (val is IEnumerable<object> seq)
            {
                var list = new List<byte>();
                foreach (var x in seq) list.Add(Convert.ToByte(x.ToString().Replace("0x", ""), 16));
                return list.ToArray();
            }
            throw new ArgumentException("Unsupported 'data' format");
        }

        private static byte[] Hex(string s)
        {
            var parts = s.Split(new[] { ' ', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var buf = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++) buf[i] = Convert.ToByte(parts[i].Replace("0x", ""), 16);
            return buf;
        }
    }
}
