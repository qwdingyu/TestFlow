using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Example
{
    public class SimpleCfgRunner
    {
        private readonly List<StepConfig> _steps;
        private readonly string _model;

        public SimpleCfgRunner(string model, List<StepConfig> steps)
        {
            _model = model;
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        }

        public void Run(CancellationToken token)
        {
            var ctx = new StepContext(_model, token);

            foreach (var step in _steps)
            {
                Console.WriteLine($"[RUN] 执行步骤 {step.Name}，目标设备 {step.Target}, 命令 {step.Command}");

                var result = DeviceStepRouter.Execute(step, ctx);

                if (!result.Success)
                {
                    Console.WriteLine($"[FAIL] 步骤 {step.Name} 失败：{result.Message}");
                    break; // 或者根据需求决定是否继续
                }

                Console.WriteLine($"[OK] 步骤 {step.Name} 成功 → 输出: {Newtonsoft.Json.JsonConvert.SerializeObject(result.Outputs)}");
            }
        }
    }

    public class Runner
    {
        static string json = """
[
  {
    "Name": "power_on",
    "Target": "plc_1",
    "Command": "SetBit",
    "Parameters": { "address": "Q0.0", "value": true },
    "TimeoutMs": 1000
  },
  {
    "Name": "scan_barcode",
    "Target": "scanner_1",
    "Command": "Read",
    "Parameters": { "timeoutMs": 9000 },
    "TimeoutMs": 3000
  },
  {
    "Name": "send_can",
    "Target": "can_1",
    "Command": "SendOnce",
    "Parameters": { "id": "0x201", "data": "00 00 00 80 00 00 00 00" },
    "TimeoutMs": 1000
  }
]
""";


        public static void Run()
        {
            // 1. 初始化 DeviceFactory
            DeviceServices.Factory = new DeviceFactory();
            DeviceServices.DevicesCfg = new Dictionary<string, DeviceConfig>
            {
                { "plc_1", new DeviceConfig { Name = "plc_1", Type = "plc", ConnectionString = "{ \"DeviceType\":\"S1200\",\"PlcIp\":\"127.0.0.1\",\"PlcRack\":0,\"PlcSlot\":0}\"" } },
                { "scanner_1", new DeviceConfig { Name = "scanner_1", Type = "scanner", ConnectionString = "COM3:9600,N,8,1" } },
                //{ "can_1", new DeviceConfig { Name = "can_1", Type = "can", ConnectionString = "PCAN:CH1" } }
            };

            // 从 JSON 加载步骤
            //var json = File.ReadAllText("flow.json");
            var steps = Newtonsoft.Json.JsonConvert.DeserializeObject<List<StepConfig>>(json);

            // 创建 runner
            var runner = new SimpleCfgRunner("SeatCheckModel", steps);

            // 运行
            using var cts = new CancellationTokenSource();
            runner.Run(cts.Token);

        }
    }

}
