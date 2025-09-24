using System;
using System.Collections.Generic;
using System.Threading;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Events;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Example
{
    public class SiampleRunner
    {
        public static void Run()
        {
            // 1. 初始化 DeviceFactory
            DeviceServices.DevicesCfg = new Dictionary<string, DeviceConfig>
            {                //{ "plc_1", new DeviceConfig { Name = "plc_1", Type = "plc", ConnectionString = "{ \"DeviceType\":\"S1200\",\"PlcIp\":\"127.0.0.1\",\"PlcRack\":0,\"PlcSlot\":0}\"" } },
                { "scanner_1", new DeviceConfig { Name = "scanner_1", Type = "scanner", ConnectionString = "COM3:9600,N,8,1" } },
                //{ "can_1", new DeviceConfig { Name = "can_1", Type = "can", ConnectionString = "PCAN:CH1" } }
            };
            DeviceServices.Factory = new DeviceFactory();
            DeviceNotifier.DeviceStateChangedEvent += (key, state) =>
            {
                LogHelper.Info($"设备[{key}] 状态 => {state}");
            };
            using var cts = new CancellationTokenSource();
            var ctx = new StepContext("SeatCheckModel", cts.Token);

            try
            {
                //// 2. 运行步骤：PLC 开机
                //var powerStep = new StepConfig
                //{
                //    Name = "power_on",
                //    Target = "plc_1",
                //    Command = "SetBit",
                //    Parameters = new Dictionary<string, object> { { "address", "Q0.0" }, { "value", true } }
                //};
                ////  Execute(string deviceKey, DeviceConfig cfg, StepConfig step, StepContext stepCtx)
                //var powerResult = DeviceStepRouter.Execute("plc_1", powerStep, ctx);
                //LogHelper.Info($"PLC 输出: {Serialize(powerResult.Outputs)}");

                // 3. 运行步骤：扫码

                var wScanStep = new StepConfig
                {
                    Name = "scan_barcode",
                    Target = "scanner_1",
                    Command = "WriteText",
                    Parameters = new Dictionary<string, object> { { "text", DateTime.Now.ToString() }, { "timeoutMs", 9000 } }
                };
                //var wResult = DeviceStepRouter.Execute(wScanStep, ctx);
                //LogHelper.Info($"读取条码: {wResult.Outputs["written"]}");

                var scanStep = new StepConfig
                {
                    Name = "scan_barcode",
                    Target = "scanner_1",
                    Command = "ReadLine",
                    Parameters = new Dictionary<string, object> { { "timeoutMs", 9000 } }
                };
                var scanResult = DeviceStepRouter.Execute(scanStep, ctx);
                LogHelper.Info($"条码: {scanResult.Outputs["result"]}");

                //// 4. 运行步骤：CAN 报文
                //var canStep = new StepConfig
                //{
                //    Name = "send_can",
                //    Target = "can_1",
                //    Command = "SendOnce",
                //    Parameters = new Dictionary<string, object> { { "id", "0x201" }, { "data", "00 00 00 80 00 00 00 00" } }
                //};
                //var canResult = DeviceStepRouter.Execute(canStep, ctx);
                //LogHelper.Info($"CAN 输出: {Serialize(canResult.Outputs)}");
            }
            catch (Exception ex)
            {
                LogHelper.Info($"执行失败: {ex.Message}");
            }
        }

        private static string Serialize(Dictionary<string, object> dict)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(dict);
        }
    }
}
