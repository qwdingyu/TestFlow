using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Mock;
using ZL.DeviceLib.Devices.Plugin;
using ZL.DeviceLib.Devices.Transport;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Utils;

namespace ZL.DeviceLib.Devices.Sp
{
    [DeviceType("noise")]
    public sealed class NoiseSerialDevice : DeviceBase
    {
        private CancellationTokenSource _cts;
        private Task _listenTask;
        private readonly SamplerDevice _sampler;
        private readonly DelimiterSplitter splitter = new DelimiterSplitter("\n");

        private DeviceConfig _cfg;

        // 设备级默认参数（可被每次调用 args 覆盖）
        private readonly int _pollingMsDefault;
        private readonly int _readTimeoutMsDefault;
        private readonly string _triggerCmdDefault;
        private readonly string _startCmdDefault;
        private readonly bool _mockDefault;

        public NoiseSerialDevice(DeviceConfig cfg) : base(cfg, new SerialTransport(cfg.ConnectionString, cfg.Name))
        {
            try
            {
                _cfg = cfg;
                // 组合采样器（保持相同 deviceName，确保与本设备窗口一致）
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
                _io.Dispose();
                _sampler.Dispose();
            }
        }

        public static (bool success, char dataType, string data, char checksum) ParseNoiseData(string input)
        {
            // 基本格式验证
            if (string.IsNullOrEmpty(input) || !input.StartsWith("AWA")) return (false, '\0', string.Empty, '\0');
            // 分割字符串
            string[] parts = input.Split(',');
            if (parts.Length < 3) return (false, '\0', string.Empty, '\0');
            try
            {
                // 提取数据类型 (AWAA 中的最后一个A)
                char dataType = 'x';// parts[0][3];
                // 假设格式总是 AWA+X            
                // 提取数据部分
                string data = parts[1]?.Replace("dBA", "");
                // 提取校验和
                char checksum = parts[2][0];
                // 取第一个字符作为校验和
                return (true, dataType, data, checksum);
            }
            catch (IndexOutOfRangeException)
            {
                return (false, '\0', string.Empty, '\0');
            }
        }
        public override async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
        {
            /*
             “AWAA”：主动上传瞬时值开
            “AWAa”：主动上传瞬时值关
            “AWA0”：读出瞬时值
            回答：AWA+数据类型+逗号+数据+逗号+校验和。
            数据类型：A
            数据：ASCII码
            例：输入AWAA，回答：AWAA, 53.2dBA
             */
            switch (cap)
            {
                case "StartListening":
                    // “AWAA”：主动上传瞬时值开
                    return await StartListeningAsync(args ?? new(), stepCtx).ConfigureAwait(false);

                case "StopListening":
                    {
                        // “AWAa”：主动上传瞬时值关
                        if (args.TryGetValue("Cmd", out var _m) && !string.IsNullOrEmpty(_m?.ToString()))
                            await _io.SendAsync(Encoding.UTF8.GetBytes(_m.ToString()), _cts?.Token ?? CancellationToken.None);

                        _cts?.Cancel();
                        return new() { { "stopped", true } };
                    }

                // 透传给 Sampler
                case "RangeTest":
                case "BeginWindow":
                case "EndWindowAndCheckRange":
                    return await _sampler.CallAsync(cap, args, stepCtx);

                default: throw new NotSupportedException($"NoiseSerial unsupported: {cap}");
            }
        }
        private async Task<Dictionary<string, object>> StartListeningAsync(Dictionary<string, object> args, StepContext stepCtx)
        {
            // “AWAA”：主动上传瞬时值开，返回格式：AWAA, 53.2dBA
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
                        if (mock) frameText = MockMessageBuilder.BuildAwaaMock(20, 50, 1, rnd);
                        else
                        {
                            var frames = await _io.ReceiveAsync(expectedLen: 0, timeout: TimeSpan.FromMilliseconds(readTimeout), token: token, keepAllFrames: false, delimiter: (byte)'\n').ConfigureAwait(false);
                            if (frames.Count == 0)
                            {
                                await Task.Delay(pollingMs, token).ConfigureAwait(false);
                                continue;
                            }

                            frameText = Encoding.ASCII.GetString(frames[0].ToArray()).TrimEnd('\r', '\n', '\0', ' ');
                        }
                        LogHelper.Debug($"设备【{_deviceName}】 接收报文【{frameText}】");
                        //TODO 根据实际返回值进行修改
                        var _d = ParseNoiseData(frameText);
                        //LogHelper.Warn($"[{_deviceName}] 无法解析数据：'{frameText}'");
                        if (double.TryParse(_d.data, out var v))
                        {
                            LogHelper.Debug($"设备【{_deviceName}】 接收数据【{v}】");
                            // 需要调整
                            MeasurementHub.Feed(_deviceName, channel, Math.Round(v, 3));
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
    }
}
