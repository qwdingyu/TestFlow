using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Transport;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices.Sp
{
    public sealed class OscilloscopeUsbDevice : DeviceBase
    {
        private CancellationTokenSource _cts;
        private Task _listenTask;
        private readonly SamplerDevice _sampler;

        // 设备级默认参数（可被每次调用 args 覆盖）
        private readonly int _pollingMsDefault;
        private readonly int _readTimeoutMsDefault;
        private readonly string _triggerCmdDefault;
        private readonly string _startCmdDefault;
        private readonly bool _mockDefault;

        private readonly ICodec _codec = new TextCodec();
        private DeviceConfig _cfg;

        //public OscilloscopeUsbDevice(DeviceConfig cfg) : base(cfg, CreateUsbTransport(cfg))
        public OscilloscopeUsbDevice(DeviceConfig cfg) : base(cfg, new SerialTransport(cfg.ConnectionString, cfg.Name))
        {
            try
            {
                _cfg=cfg;
                // 组合采样器（保持相同 deviceName，确保与本设备窗口一致）
                _sampler = new SamplerDevice(new DeviceConfig { Name = _deviceName, Type = "sampler" });

                // 读取设备级默认参数（大小写不敏感；点路径）
                _pollingMsDefault = SettingsBinder.Get<int>(cfg.Settings, "pollingMs", 500);
                _readTimeoutMsDefault = SettingsBinder.Get<int>(cfg.Settings, "readTimeoutMs", 600);
                _triggerCmdDefault = SettingsBinder.Get<string>(cfg.Settings, "triggerCmd", "trg\n");
                _startCmdDefault = SettingsBinder.Get<string>(cfg.Settings, "start.cmd", "");
                _mockDefault = SettingsBinder.Get<bool>(cfg.Settings, "mock", false);
            }
            catch (Exception)
            {
                throw;
            }
        }

        private static UsbTransport CreateUsbTransport(DeviceConfig cfg)
        {
            var o = UsbTransportOptions.FromSettings(cfg);
            return new UsbTransport(
                vendorId: o.VendorId,
                productId: o.ProductId,
                deviceKey: cfg.Name,
                readEndpoint: o.InEp,
                writeEndpoint: o.OutEp,
                configIndex: o.ConfigIndex,
                interfaceIndex: o.InterfaceIndex
            );
        }

        public override async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
        {
            //// 设备所有命令执行前：确保已握手/初始化（由 DeviceBase 根据 Settings.handshake/initSequence 实现）
            //await EnsureReadyAsync(stepCtx.Cancellation).ConfigureAwait(false);

            switch (cap)
            {
                case "Run":
                    await _io.SendAsync(_codec.Encode(":RUN"), stepCtx.Cancellation);
                    return new() { { "running", true } };

                case "Stop":
                    await _io.SendAsync(_codec.Encode(":STOP"), stepCtx.Cancellation);
                    return new() { { "stopped", true } };

                case "Acquire":   // 设置采样模式
                    var mode = args.TryGetValue("mode", out var m) ? m.ToString() : "NORM";
                    await _io.SendAsync(_codec.Encode($":ACQ:MODE {mode}"), stepCtx.Cancellation);
                    return new() { { "mode", mode } };

                case "MeasureVpp":  // 测量峰峰值
                    await _io.SendAsync(_codec.Encode(":MEAS:VPP?"), stepCtx.Cancellation);
                    //TODO 字节数组需要转换才能 获取峰值，还是说命令给出的就是峰值了
                    try
                    {
                        var frames = await _io.ReceiveAsync(expectedLen: 0, timeout: TimeSpan.FromMilliseconds(1000), token: stepCtx.Cancellation, keepAllFrames: false).ConfigureAwait(false);
                        if (frames == null || frames.Count == 0)
                            throw new TimeoutException("未收到任何数据帧 (ReceiveAsync 超时)。");
                        var decoded = _codec.Decode(frames[0], "double");

                        if (decoded is double val)
                        {
                            // 使用解析成功的值
                            LogHelper.Info($"解码成功: {val}");
                            return new() { { "Vpp", val } };
                        }
                        else
                        {
                            throw new InvalidCastException($"Decode 返回的结果不是 double 类型，而是 {decoded?.GetType().Name ?? "null"}。");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 被上层取消
                        LogHelper.Warn("操作被取消");
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
                        LogHelper.Error($"接收或解析数据时发生异常: {ex.Message}");
                        throw;
                    }
                default:
                    throw new NotSupportedException($"Oscilloscope capability not supported: {cap}");
            }
        }
    }
}
