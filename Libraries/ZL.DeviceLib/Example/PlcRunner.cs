using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Events;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Example
{
    public class PlcRunner
    {
        public static void Run()
        {
            // 1. 初始化 DeviceFactory
            DeviceServices.DevicesCfg = new Dictionary<string, DeviceConfig>
            {
                { "system", new DeviceConfig { Name="system", Type="system" } },                { "plc_1", new DeviceConfig { Name = "plc_1", Type = "plc", ConnectionString = "{ \"DeviceType\":\"S1200\",\"PlcIp\":\"127.0.0.1\",\"PlcRack\":0,\"PlcSlot\":0, \"TagFilePath\":\"LINE1_OP10_PlcTag.xml\"}"} },
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
                var steps = new List<StepConfig>
                {                    new StepConfig { Name = "Write", Target = "plc_1", Command = "Write", Parameters = new  Dictionary<string, object>{{"id",1},{"value",true}} },
                    //new StepConfig { Name = "delay", Target = "system", Command = "Delay", Parameters = new Dictionary<string, object>{{"ms", 9000}} },                    new StepConfig { Name = "Read", Target = "plc_1", Command = "Read", Parameters = new Dictionary<string, object>{{"id",1}} }
                };


                foreach (var step in steps)
                {
                    var result = DeviceStepRouter.Execute(step, ctx);
                    if (result == null) continue;
                    if (result.Outputs != null)
                    {
                        LogHelper.Info($"Step={step.Name}, Success={result.Success}, Outputs={string.Join(",", result.Outputs.Select(kv => $"{kv.Key}={kv.Value}"))}");
                    }
                    else
                    {
                        LogHelper.Info($"Step={step.Name}, Success={result.Success}");
                    }
                }

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
