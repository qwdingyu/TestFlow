using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices.Can
{
    /// <summary>
    /// 旧驱动的写法
    /// </summary>
    public class CanAdapterDevice : IDevice, IDisposable
    {
        private readonly ICanTransport _can;

        private readonly CanMessageScheduler _sched;

        public CanAdapterDevice(DeviceConfig cfg)
        {
            _can = new CanTransport(cfg.ConnectionString);
            _sched = new CanMessageScheduler(_can);
            //// 设置过滤器：仅保留测试相关的 ID
            _can.SetFilter(msg =>
            {
                // 只保留本测试需要的 ID
                switch (msg.Id)
                {
                    case "0x201": // 高压状态
                    case "0x1F1": // 点火钥匙
                    case "0x17D": // 车速
                    case "0x120": // 里程无效
                    case "0x434": // 座椅加热控制
                    case "0x12D": // 特定回显/ACK
                    case "0x4C1": // 主驾按摩（兼容以前的测试项）
                        return true;
                    default:
                        return false; // 其余一律丢弃
                }
            });
            //_can.SetFilter(msg =>
            //{
            //    if (msg.Id == "0x434")
            //    {
            //        // 只保留座椅控制（Data[3] 在 0x12/0x24/0x36/0x7E 范围）
            //        return msg.Data.Length > 3 &&
            //               (msg.Data[3] == 0x12 || msg.Data[3] == 0x24 ||
            //                msg.Data[3] == 0x36 || msg.Data[3] == 0x7E || msg.Data[3] == 0x00);
            //    }
            //    return allowed.Contains(msg.Id);
            //});
            // 安全获取 can_filter_allowed_ids
            if (cfg.Settings != null && cfg.Settings.TryGetValue("can_filter_allowed_ids", out object canFilterObj))
            {
                string[] can_filter_allowed_ids = Array.Empty<string>();
                try
                {
                    if (canFilterObj is IEnumerable<object> objList)
                    {
                        // 情况 1: 来自 JSON 的数组（JArray → object[]）
                        can_filter_allowed_ids = objList.Select(x => x?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                    }
                    else if (canFilterObj is string str)
                    {
                        // 情况 2: 配置成了逗号分隔的字符串
                        can_filter_allowed_ids = str.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Warn($"解析 can_filter_allowed_ids 失败: {ex.Message}");
                }

                var allowed = new HashSet<string>(can_filter_allowed_ids, StringComparer.OrdinalIgnoreCase);

                _can.SetFilter(msg =>
                {
                    return allowed.Contains(msg.Id);
                });
            }
            else
            {
                // 没有配置时默认放行所有
                _can.SetFilter(msg => true);
            }

        }

        public ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            var token = ctx.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                LogHelper.Info($"--当前测试步骤【{step.Name}】设备【{step.Target}】, 参数【{JsonConvert.SerializeObject(step.Parameters)}】");

                string id = step.Parameters.ContainsKey("id") ? step.Parameters["id"].ToString() : "0x000";
                byte[] data = step.Parameters.ContainsKey("data") ? ((IEnumerable<object>)step.Parameters["data"]).Select(x => ParseHex(x.ToString())).ToArray() : new byte[0];

                // 1) 周期环境报文
                // 周期环境报文
                if (step.Command == "start_seat_env")
                {
                    _sched.StartSeatEnv100ms();
                    return Ok("Seat ENV started");
                }
                if (step.Command == "stop_seat_env")
                {
                    _sched.StopSeatEnv();
                    return Ok("Seat ENV stopped");
                }

                // 事件型 —— 异步触发，不要同步阻塞等待
                if (step.Command == "seat_heater_control")
                {
                    var level = step.Parameters.ContainsKey("level") ? step.Parameters["level"].ToString() : "low";
                    var _ = _sched.SeatHeaterAsync(level);
                    return Ok($"Heater {level} accepted");
                }

                // 2) 事件型报文
                if (step.Command == "seat_heater_control")
                {
                    string level = step.Parameters.ContainsKey("level") ? step.Parameters["level"].ToString() : "low";
                    string ctrlHex = level == "low" ? "00-00-00-12-00-00-00-00"
                                    : level == "mid" ? "00-00-00-24-00-00-00-00"
                                    : level == "high" ? "00-00-00-36-00-00-00-00"
                                    : level == "off" ? "00-00-00-7E-00-00-00-00"
                                    : "00-00-00-00-00-00-00-00";
                    var clear = Hex("00-00-00-00-00-00-00-00");

                    _sched.EnqueueEventBurstAsync("0x434", Hex(ctrlHex), clear, 100, 3, 3)
                          .GetAwaiter().GetResult();

                    return Ok($"Heater {level} burst done");
                }

                // 3) 默认 send_and_receive
                if (step.Command == "send_and_receive")
                {
                    var msg = new CanMessage { Id = id, Data = data, Timestamp = DateTime.Now };
                    _can.Send(msg);
                    var resp = _can.WaitForResponse(m => m.Id == id, 2000, token);
                    outputs["response"] = "ACK";
                    outputs["id"] = resp.Id;
                    outputs["data"] = BitConverter.ToString(resp.Data);

                    return new ExecutionResult
                    {
                        Success = true,
                        Message = $"CAN ACK (ID={resp.Id}, DATA={outputs["data"]})",
                        Outputs = outputs
                    };
                }
                // XHJC.json 循环交叉采集
                if (step.Command == "seat_heater_with_measure")
                {
                    string level = step.Parameters["level"].ToString();
                    int measureMs = step.Parameters.ContainsKey("measure_ms") ? Convert.ToInt32(step.Parameters["measure_ms"]) : 5000;

                    // 1) 启动 CAN 事件任务
                    var ctrlTask = Task.Run(async () =>
                    {
                        string ctrlHex = level == "low" ? "00-00-00-12-00-00-00-00"
                                        : level == "mid" ? "00-00-00-24-00-00-00-00"
                                        : level == "high" ? "00-00-00-36-00-00-00-00"
                                        : "00-00-00-7E-00-00-00-00"; // off

                        await _sched.EnqueueEventBurstAsync("0x434", Hex(ctrlHex), Hex("00-00-00-00-00-00-00-00"), 100, 3, 3);
                    }, ctx.Cancellation);

                    // 2) 并发任务：电流采样
                    var pmTask = Task.Run(() =>
                    {
                        var devConf = DeviceServices.DevicesCfg["power_meter_1"];
                        return DeviceServices.Factory.UseDevice("power_meter_1", devConf, dev =>
                        {
                            var sc = new StepConfig
                            {
                                Target = "power_meter_1",
                                Command = "measure_current",
                                Parameters = new Dictionary<string, object> { { "duration_ms", measureMs } }
                            };
                            return dev.Execute(sc, ctx).Outputs;
                        });
                    }, ctx.Cancellation);

                    // 3) 并发任务：噪音采样
                    var micTask = Task.Run(() =>
                    {
                        var devConf = DeviceServices.DevicesCfg["mic_1"];
                        return DeviceServices.Factory.UseDevice("mic_1", devConf, dev =>
                        {
                            var sc = new StepConfig
                            {
                                Target = "mic_1",
                                Command = "record_noise",
                                Parameters = new Dictionary<string, object> { { "duration_ms", measureMs } }
                            };
                            return dev.Execute(sc, ctx).Outputs;
                        });
                    }, ctx.Cancellation);

                    Task.WaitAll(new Task[] { ctrlTask, pmTask, micTask }, ctx.Cancellation);

                    outputs["heater_level"] = level;
                    outputs["power_meter"] = pmTask.Result;
                    outputs["noise"] = micTask.Result;

                    return new ExecutionResult
                    {
                        Success = true,
                        Message = $"Heater {level} with measure done",
                        Outputs = outputs
                    };
                }

                throw new Exception("Unsupported command: " + step.Command);
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Message = "CAN Exception: " + ex.Message, Outputs = outputs };
            }
        }

        private ExecutionResult Ok(string msg) => new ExecutionResult { Success = true, Message = msg };

        private byte ParseHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) throw new ArgumentException("Empty data element");
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return Convert.ToByte(s, 16);
        }

        private byte[] Hex(string hex)
        {
            return hex.Split('-').Select(x => Convert.ToByte(x, 16)).ToArray();
        }

        public void Dispose() { _sched.Dispose(); }
    }
}