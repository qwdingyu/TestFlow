using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Events;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Storage;

namespace ZL.DeviceLib.Example
{
    public class NoiseRunner
    {
        public static void Run()
        {
            // 1. 初始化 DeviceFactory
            DeviceServices.DevicesCfg = new Dictionary<string, DeviceConfig>
            {
                { "system", new DeviceConfig { Name="system", Type="system" } },
                { "sampler_1", new DeviceConfig { Name="sampler_1", Type="sampler" } },
                { "noise_1", new DeviceConfig { Name="noise_1", Type="noise", ConnectionString="COM9:9600,N,8,1" } }
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
                string _dbTypeString = "MySql";
                string _connectionString = "server=127.0.0.1;port=3306;database=SeatTest;user=root;password=123456;charset=utf8mb4;SslMode=None";
                var _db = new DbServices(_dbTypeString, _connectionString);
                DeviceServices.Db = _db;
                //var steps = new List<StepConfig>
                //{
                //    new StepConfig { Name="start_listen", Target="noise_1", Command="StartListening", Parameters=new(){{"channel","noise_1"}} },
                //    new StepConfig { Name="begin", Target="sampler_1", Command="BeginWindow", Parameters=new(){{"channel","noise_1"}} },
                //    new StepConfig { Name="delay", Target="system", Command="Delay", Parameters=new(){{"ms",5000}} },
                //    new StepConfig { Name="end", Target="sampler_1", Command="EndWindowAndCheckRange", Parameters=new(){{"channel","noise_1"},{"min",20},{"max",60}} },
                //    new StepConfig { Name="stop_listen", Target="noise_1", Command="StopListening" }
                //};
                var steps = new List<StepConfig>
                {
                    new StepConfig { Name="start_listen", Target="noise_1", Command="StartListening", Parameters=new(){{"channel","noise_1"}, { "Cmd", "AWAA" } } },
                    new StepConfig { Name= "noise_range_test", Target= "sampler_1", Command="RangeTest",
                        Parameters= new(){ { "channel", "noise_1" }, {"durationMs", 5000}, {"min", 20},{ "max", 60}, { "Cmd", "AWAa" } } },
                    new StepConfig { Name = "stop_listen", Target = "noise_1", Command = "StopListening" }
                };
                var aggregator = new ResultAggregator();

                foreach (var step in steps)
                {
                    var result = DeviceStepRouter.Execute(step, ctx);
                    if (result == null) continue;
                    if (result.Outputs != null)
                    {
                        aggregator.AddStepResult(step.Name, result.Outputs);
                        //LogHelper.Info($"Step={step.Name}, Success={result.Success}, Outputs={string.Join(",", result.Outputs.Select(kv => $"{kv.Key}={kv.Value}"))}");
                        LogHelper.Info($"Step={step.Name}, Success={result.Success}, Outputs={string.Join(",", result.Outputs.Select(kv => $"{kv.Key}={Format.FormatValue(kv.Value)}"))}");
                    }
                    else
                    {
                        LogHelper.Info($"Step={step.Name}, Success={result.Success}");
                    }
                }
                //// 假步骤 1：高度上
                //var step1 = new { Name = "height_up", Outputs = new Dictionary<string, object> { { "value", 123.4f } } };
                //aggregator.AddStepResult(step1.Name, step1.Outputs);

                //// 假步骤 2：高度下
                //var step2 = new { Name = "height_down", Outputs = new Dictionary<string, object> { { "value", 98.7f } } };
                //aggregator.AddStepResult(step2.Name, step2.Outputs);

                //// 假步骤 3：声音
                //var step3 = new { Name = "sound", Outputs = new Dictionary<string, object> { { "left", 55.5f }, { "right", 54.8f } } };
                //aggregator.AddStepResult(step3.Name, step3.Outputs);

                //// 假步骤 4：终判（OK/NG）
                //var step4 = new { Name = "final_check", Outputs = new Dictionary<string, object> { { "pass", true } } };
                //aggregator.AddStepResult(step4.Name, step4.Outputs);

                var seatResults = aggregator.ToSeatResults("ModelX", "BARCODE123", 33, TabColMapping.SeatResultMapping);
                // 调用你已有的保存方法
                _db.SaveSeatResults(seatResults);
                //LogHelper.Info($"CAN 输出: {Serialize(canResult.Outputs)}");
            }
            catch (Exception ex)
            {
                LogHelper.Info($"执行失败: {ex.Message}");
            }
        }

    }
}
