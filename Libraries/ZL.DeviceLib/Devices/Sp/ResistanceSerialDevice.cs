using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Mock;
using ZL.DeviceLib.Devices.Transport;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Utils;

namespace ZL.DeviceLib.Devices.Sp
{
    /// <summary>
    /// 电阻仪（串口）
    /// - 握手/初始化：由 DeviceBase 按 Settings.handshake/initSequence 保证（首次/开机）
    /// - 监听：支持真实串口与 mock（两路R通道，格式：R1:0.2601E-03,-1000.0,0 R2:11.269E-03,-1010.0,1）
    /// - 统计：透传给 SamplerDevice（RangeTest/BeginWindow/EndWindowAndCheckRange）
    /// - 采样隔离：MeasurementHub 按 deviceName + channel
    /// </summary>
    public sealed class ResistanceSerialDevice : DeviceBase
    {
        private CancellationTokenSource _cts;
        private Task _listenTask;
        private readonly SamplerDevice _sampler;
        private readonly ResistanceDataParser resistanceDataParser = new();

        // 设备级默认参数（可被每次调用 args 覆盖）
        private readonly int _pollingMsDefault;
        private readonly int _readTimeoutMsDefault;
        private readonly string _triggerCmdDefault;
        private readonly string _startCmdDefault;
        private readonly bool _mockDefault;

        public ResistanceSerialDevice(DeviceConfig cfg) : base(cfg, new SerialTransport(cfg.ConnectionString, cfg.Name))
        {
            try
            { // 组合采样器（保持相同 deviceName，确保与本设备窗口一致）
                _sampler = new SamplerDevice(new DeviceConfig { Name = _deviceName, Type = "sampler" });

                // 读取设备级默认参数（大小写不敏感；点路径）
                _pollingMsDefault = SettingsBinder.Get<int>(cfg.Settings, "pollingMs", 500);
                _readTimeoutMsDefault = SettingsBinder.Get<int>(cfg.Settings, "readTimeoutMs", 600);
                _triggerCmdDefault = SettingsBinder.Get<string>(cfg.Settings, "triggerCmd", "trg\n");
                _startCmdDefault = SettingsBinder.Get<string>(cfg.Settings, "start.cmd", "");
                _mockDefault = SettingsBinder.Get<bool>(cfg.Settings, "mock", false);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
        public override void Dispose()
        {
            try
            {
                _cts?.Cancel();
                _listenTask?.Wait(1000);
            }
            catch { }
            finally
            {
                _cts?.Dispose();
                _sampler.Dispose();
                _io.Dispose();
                base.Dispose();
            }
        }
        public override async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
        {
            //// 设备所有命令执行前：确保已握手/初始化（由 DeviceBase 根据 Settings.handshake/initSequence 实现）
            //await EnsureReadyAsync(stepCtx.Cancellation).ConfigureAwait(false);

            switch (cap)
            {
                case "StartListening":
                    return await StartListeningAsync(args ?? new(), stepCtx).ConfigureAwait(false);

                case "StopListening":
                    // 不需要发送命令，因为是被动响应的，只有发送命令才会有回应
                    _cts?.Cancel();
                    return new() { { "stopped", true } };

                case "RangeTest":
                case "BeginWindow":
                case "EndWindowAndCheckRange":
                    return await _sampler.CallAsync(cap, args, stepCtx).ConfigureAwait(false);

                case "Handshake":
                    return new() { { "ready", true }, { "device", _deviceName } };

                default:
                    throw new NotSupportedException($"ResistanceSerialDevice unsupported: {cap}");
            }
        }
        private async Task<Dictionary<string, object>> StartListeningAsync(Dictionary<string, object> args, StepContext stepCtx)
        {
            // trg\n 触发读取电阻，返回格式：R1:0.2601E-03,-1000.0,0 R2:11.269E-03,-1000.0,1
            var channel = args["channel"].ToString();
            var pollingMs = args.TryGetValue("pollingMs", out var p) ? Convert.ToInt32(p) : _pollingMsDefault;
            var readTimeout = args.TryGetValue("readTimeoutMs", out var rt) ? Convert.ToInt32(rt) : _readTimeoutMsDefault;
            var triggerCmd = args.TryGetValue("Trigger", out var t) ? t?.ToString() : _triggerCmdDefault;
            var startCmd = args.TryGetValue("Cmd", out var s) ? s?.ToString() : _startCmdDefault;
            var mock = args.TryGetValue("mock", out var m) ? DataConvertX.ToBool(m) : _mockDefault;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // 清空并打开采样窗口
            MeasurementHub.Begin(_deviceName, channel, clear: true);

            // 可选：开始命令（预热/切模式）
            if (!string.IsNullOrEmpty(startCmd))
                await _io.SendAsync(Encoding.ASCII.GetBytes(startCmd), token).ConfigureAwait(false);

            _listenTask = Task.Run(async () =>
            {
                var rnd = new Random();
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        string frameText;
                        if (mock) frameText = MockMessageBuilder.BuildResistanceFrame(min: 80.0, max: 480.0, decimals: 4, rnd: rnd);
                        else
                        {
                            // 大多数台式仪器：触发一次→返回一帧
                            if (!string.IsNullOrEmpty(triggerCmd))
                                await _io.SendAsync(Encoding.ASCII.GetBytes(triggerCmd), token).ConfigureAwait(false);

                            var frames = await _io.ReceiveAsync(expectedLen: 0, timeout: TimeSpan.FromMilliseconds(readTimeout), token: token, keepAllFrames: false, delimiter: (byte)'\n').ConfigureAwait(false);
                            if (frames.Count == 0)
                            {
                                await Task.Delay(pollingMs, token).ConfigureAwait(false);
                                continue;
                            }

                            frameText = Encoding.ASCII.GetString(frames[0].ToArray()).TrimEnd('\r', '\n', '\0', ' ');
                        }
                        LogHelper.Debug($"设备【{_deviceName}】 接收报文【{frameText}】");
                        // 原来的写法
                        //var ChannelDatas = ResistanceDataParser.ParseResistanceData(line);
                        //// 把采样值交给 MeasurementHub
                        //foreach (var d in ChannelDatas)
                        //{
                        //    MeasurementHub.Feed(_deviceName, _channel, d.Resistance);
                        //}
                        // 解析并投喂（R1/R2…）  //TODO 两个通道的值，根据待测步骤区分是哪个通道的值？？
                        var values = TryParseResistanceLine(frameText);
                        if (values.Length == 0)
                        {
                            LogHelper.Warn($"[{_deviceName}] 无法解析数据：'{frameText}'");
                        }
                        else
                        {
                            for (int i = 0; i < values.Length; i++)
                            {
                                LogHelper.Debug($"设备【{_deviceName}】 接收数据【{values[i]}】");
                                // 需要调整
                                MeasurementHub.Feed(_deviceName, channel, Math.Round(values[i], 3));
                            }
                        }

                        await Task.Delay(pollingMs, token).ConfigureAwait(false);
                    }
                }
                catch (TaskCanceledException) { /* 正常退出 */ }
                catch (OperationCanceledException)
                {
                    LogHelper.Warn("操作被上层取消");
                    throw;
                }
                catch (TimeoutException ex)
                {
                    LogHelper.Warn($"超时: {ex.Message}");
                    throw;
                }
                catch (FormatException ex)
                {
                    LogHelper.Error($"数据格式错误: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"[{_deviceName}] 监听异常: {ex}");
                    // 可选：触发上层掉线逻辑 → base.InvalidateReady();
                }
            }, token);

            return new() { { "channel", channel }, { "listening", true }, { "mock", mock } };
        }

        // =============== 解析 & 工具 ===============

        /// <summary>
        /// 解析 Rn:value 形式的科学计数/浮点，例：
        /// "R1:0.2601E-03,-1000.0,0 R2:11.269E-03,-1010.0,1" → [0.2601E-03, 11.269E-03]
        /// </summary>
        private static double[] TryParseResistanceLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return Array.Empty<double>();

            var matches = Regex.Matches(
                line,
                @"R\d+\s*:\s*([+\-]?\d+(?:\.\d+)?(?:[Ee][+\-]?\d+)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (matches.Count == 0) return Array.Empty<double>();

            var list = new List<double>(matches.Count);
            foreach (Match m in matches)
            {
                if (m.Groups.Count > 1 &&
                    double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    list.Add(v);
                }
            }
            return list.ToArray();
        }
    }
}
