using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Transport;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices.Sp
{
    /*
        // 1) KtdyUsbDevice 构造（工厂内部会传入 cfg）
        var dev = new KtdyUsbDevice(cfg);

        // 2) 发送命令（例如设置电压）
        var args = new Dictionary<string, object> { { "Cmd", "VOLT 5\n" }, { "timeoutMs", 1000 } };
        var res = await dev.CallAsync("SetVolt", args, stepCtx);

        // 3) 生成噪声模拟报文
        string mock = MockMessageBuilder.BuildAwaaMock();             // "AWAA, 53.2dBA"
        string mock2 = MockMessageBuilder.BuildAwaaMock(30, 60, 1);   // "AWAA, 41.7dBA"
   */
    /// <summary>
    /// 
    /// </summary>
    public sealed class KtdyUsbDevice : DeviceBase
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

        // 命令配置表：只描述差异，复用公共解析器，避免重复
        private static readonly Dictionary<string, CommandSpec> _commandMap = new Dictionary<string, CommandSpec>(StringComparer.OrdinalIgnoreCase)
        {
            { "Handshake", new CommandSpec { ExpectResponse = false, Description = "设备握手，如 SYST:REM\\n" }},
            { "SetVolt", new CommandSpec { Description = "设置电压并回读，例如 VOLT 1\\n", ParserKey = "scpi:double", DefaultTimeoutMs = 800 }},
            { "SetCurrent", new CommandSpec { Description = "设置电流并回读，例如 CURR 2.5\\n", ParserKey = "scpi:double", DefaultTimeoutMs = 800 }},
            { "GetVolt", new CommandSpec { Description = "读取电压，例如 FETC:VOLT?\\n", ParserKey = "scpi:double" }},
            { "GetCurrent", new CommandSpec { Description = "读取电流，例如 FETC:CURR?\\n", ParserKey = "scpi:double" }},
            // 示例：返回 KV 对
            { "ReadKV", new CommandSpec { Description = "读取键值对，如 VOLT:5.1;CURR:0.3", ParserKey = "kv:double" }},
            // 示例：返回 CSV 数组
            { "ReadArray", new CommandSpec { Description = "读取数组，如 1,2,3,4", ParserKey = "scpi:double[]" }},
            // 示例：只要原始字符串
            { "ReadRaw", new CommandSpec { Description = "读取原始字符串", ParserKey = "raw:string" }},
        };
        //public KtdyUsbDevice(DeviceConfig cfg) : base(cfg, CreateUsbTransport(cfg))
        public KtdyUsbDevice(DeviceConfig cfg) : base(cfg, new SerialTransport(cfg.ConnectionString, cfg.Name))
        {
            try
            { 
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
        public override void Dispose() { _io.Dispose(); }


        /// <summary>
        /// 统一命令入口
        /// </summary>
        public override async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
        {
            // 设备所有命令执行前：确保已握手/初始化（由 DeviceBase 根据 Settings.handshake/initSequence 实现）
            await EnsureReadyAsync(stepCtx.Cancellation).ConfigureAwait(false);

            _cts = new CancellationTokenSource();
            // 查找命令配置
            if (!_commandMap.TryGetValue(cap, out var spec))
            {
                throw new NotSupportedException($"KtdyUsbDevice unsupported command: {cap}");
            }
            // 调用统一方法执行
            return await SendAndReceiveAsync(cap, args, stepCtx, spec);
        }

        /// <summary>
        /// 统一的命令发送/接收辅助方法
        /// </summary>
        /// <param name="cap">当前命令名称</param>
        /// <param name="args">参数字典，可能包含 "Cmd"、"timeoutMs"</param>
        /// <param name="stepCtx">步骤上下文（用于取消）</param>
        /// <param name="expectResponse">是否需要等待设备返回</param>
        /// <param name="defaultTimeoutMs">默认超时时间（毫秒）</param>
        private async Task<Dictionary<string, object>> SendAndReceiveAsync(string cap, Dictionary<string, object> args, StepContext stepCtx, CommandSpec spec)
        {
            // 取出指令参数
            string cmd = args != null && args.ContainsKey("Cmd") ? Convert.ToString(args["Cmd"]) : "";
            int timeout = (args != null && args.ContainsKey("timeoutMs")) ? Convert.ToInt32(args["timeoutMs"]) : spec.DefaultTimeoutMs;

            LogHelper.Info($"[CallAsync] 执行命令: {cap} | 描述: {spec.Description} | Cmd: {cmd} | Timeout: {timeout}ms | 需要响应: {spec.ExpectResponse}");

            if (!string.IsNullOrEmpty(cmd))
                await _io.SendAsync(Encoding.ASCII.GetBytes(cmd), _cts.Token);

            if (!spec.ExpectResponse)
            {
                LogHelper.Info($"[CallAsync] 命令 {cap} 执行完成（无需返回值）");
                return new Dictionary<string, object> { { "result", true }, { "cmd", cap } };
            }
            var t = stepCtx != null ? stepCtx.Cancellation : CancellationToken.None;
            var frames = await _io.ReceiveAsync(0, TimeSpan.FromMilliseconds(timeout), t, spec.KeepAllFrames, spec.Delimiter).ConfigureAwait(false);

            if (frames == null || frames.Count == 0)
            {
                LogHelper.Warn($"[CallAsync] 命令 {cap} 超时/无返回");
                return new Dictionary<string, object> { { "result", false }, { "cmd", cap } };
            }

            var frame = frames[0];
            object parsed = null;
            string rawString = Encoding.ASCII.GetString(frame.ToArray()).TrimEnd('\r', '\n', '\0', ' ');
            // 解析器选择：优先 spec.Parser；否则用 ParserKey 查公共解析器；再不行就原文返回
            try
            {
                if (spec.Parser != null)
                {
                    parsed = spec.Parser(frame);
                }
                else if (!string.IsNullOrEmpty(spec.ParserKey) && CommonParsers.ByKey.ContainsKey(spec.ParserKey))
                {
                    parsed = CommonParsers.ByKey[spec.ParserKey](frame);
                }
                else
                {
                    parsed = rawString; // 默认返回原始串
                }
            }
            catch (Exception ex)
            {
                LogHelper.Warn($"[CallAsync] 命令 {cap} 返回解析异常：{ex.Message}，原文回退");
                parsed = rawString;
            }

            LogHelper.Info($"[CallAsync] 命令 {cap} 成功 | Raw: {rawString} | Parsed: {parsed}");

            // 为了调试友好，既返回解析值，也返回原始串
            var dict = new Dictionary<string, object>();
            dict["result"] = true;
            dict["cmd"] = cap;
            dict["value"] = parsed;  // 解析后的值（double/int/dict/array/字符串）
            dict["raw"] = rawString; // 原始文本（便于日志与二次处理）
            return dict;
        }

        // /// <summary>
        // /// 设备命令执行入口
        // /// </summary>
        //public async Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx)
        // {
        //     _cts = new CancellationTokenSource();

        //     switch (cap)
        //     {
        //         case "Handshake":
        //             // 在系统初始化时发送，表示握手指令，必须先握手，否则后续指令无效
        //             // 典型指令如: "SYST:REM\n"
        //             return await SendAndReceiveAsync(cap, args, stepCtx, expectResponse: false);

        //         case "SetVolt":
        //             // 设置电压命令，例如: "VOLT 1\n"
        //             // 随后会查询电压: "FETC:VOLT?\n"
        //             return await SendAndReceiveAsync(cap, args, stepCtx);

        //         case "SetCurrent":
        //             // 设置电流命令，例如: "CURR 2.5\n"
        //             // 随后会查询电流: "FETC:CURR?\n"
        //             return await SendAndReceiveAsync(cap, args, stepCtx);

        //         case "GetVolt":
        //             // 查询电压命令: "FETC:VOLT?\n"
        //             return await SendAndReceiveAsync(cap, args, stepCtx);

        //         case "GetCurrent":
        //             // 查询电流命令: "FETC:CURR?\n"
        //             return await SendAndReceiveAsync(cap, args, stepCtx);
        //         case "set_voltage":
        //             var voltage = args.GetValue<double>("voltage", 0);
        //             return new Dictionary<string, object> { { "result", true }, { "cmd", cap }, { "value", voltage } };
        //         default:
        //             // 未支持的指令
        //             throw new NotSupportedException("KtdyUsbDevice unsupported: " + cap);
        //     }
        // }
    }
}
