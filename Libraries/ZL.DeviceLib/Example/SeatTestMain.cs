using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Events;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Example
{
    public class SeatTestMain
    {
        string _dbTypeString = "MySql";
        string _connectionString = "server=127.0.0.1;port=3306;database=SeatTest;user=root;password=123456;charset=utf8mb4;SslMode=None";
        SeatTestRunner seatTestRunner = null;
        CancellationTokenSource cts = new CancellationTokenSource();
        public SeatTestMain()
        {

            // 定义设备或资源
            DeviceServices.DevicesCfg = new Dictionary<string, DeviceConfig>
            {
                { "system", new DeviceConfig { Name="system", Type="system" } },
                { "sampler_1", new DeviceConfig { Name="sampler_1", Type="sampler" } },
                { "noise_1", new DeviceConfig { Name="noise_1", Type="noise", ConnectionString="COM3:9600,N,8,1" } }
            };
            // 注册设备状态事件
            DeviceNotifier.DeviceStateChangedEvent += (key, state) => { LogHelper.Info($"设备[{key}] 状态 => {state}"); };
            DeviceNotifier.DeviceInfoChangedEvent += (key, info) => { LogHelper.Info($"设备[{key}] 信息 => {info}"); };

            // 注册测试过程及状态事件
            TestEvents.StepStarted = stepName => { LogHelper.Info($"▶ 开始执行 {stepName}"); };
            TestEvents.StepCompleted = (stepName, success, ms, outputs) => { var resultStr = success ? "PASS" : "FAIL"; LogHelper.Info($"✅ {stepName} {resultStr}, 耗时={ms}ms, 数据={string.Join(",", outputs.Select(kv => kv.Key + "=" + kv.Value))}"); };
            TestEvents.StatusChanged = status => { LogHelper.Info($"状态变更: {status}"); };
            TestEvents.TestCompleted = result => { LogHelper.Info($"📊 测试完成: 条码={result.sn}, 总结果={result.test_result}, 总耗时={result.testing_time}"); };

        }
        public async void Run()
        {
            seatTestRunner = new SeatTestRunner(_dbTypeString, _connectionString);

            var selectedSteps = new List<StepConfig>
                {
                    new StepConfig { Name="start_listen", Target="noise_1", Command="StartListening", Parameters=new(){{"channel","noise_1"}} },
                    new StepConfig { Name= "noise_range_test", Target= "sampler_1", Command="RangeTest",
                        Parameters= new(){ { "channel", "noise_1" }, {"durationMs", 5000}, {"min", 20},{ "max", 60} } },
                    new StepConfig { Name = "stop_listen", Target = "noise_1", Command = "StopListening" }
                };
            await seatTestRunner.RunTestsAsync(selectedSteps, "ModelX", "BARCODE123", cts.Token);
        }

        //界面复测
        //var cts = new CancellationTokenSource();
        //var runner = new TestRunner();
        //btnStop.Click += (s, e) => cts.Cancel();
        //btnRetest.Click += async(s, e) =>
        //{
        //    cts = new CancellationTokenSource();
        //        await runner.RunTestsAsync(selectedSteps, "ModelX", "BARCODE123", cts.Token);
        // };

        /*
         [
              {
                "Id": "height_up",
                "Name": "高度向上",
                "Device": "plc_1",
                "Command": "SetBit",
                "Parameters": { "address": "Q0.0", "value": true },
                "Enabled": true,
                "Group": "motion",
                "ParallelGroup": null
              },
              {
                "Id": "sound_test",
                "Name": "声音测试",
                "Device": "noise_1",
                "Command": "RangeTest",
                "Parameters": { "durationMs": 3000, "min": 20, "max": 60 },
                "Enabled": true,
                "Group": "audio",
                "ParallelGroup": "P1"
              },
              {
                "Id": "blower_test",
                "Name": "风扇测试",
                "Device": "can_1",
                "Command": "SendAndReceive",
                "Parameters": { "id": "0x434", "data": "00 00 00 12 00 00 00 00" },
                "Enabled": true,
                "Group": "fan",
                "ParallelGroup": "P1"
              }
            ]
         */
    }
}
