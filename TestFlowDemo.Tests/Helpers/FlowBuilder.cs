using System;
using System.Collections.Generic;
using System.Linq;
using ZL.DeviceLib.Models;

namespace TestFlowDemo.Tests.Helpers
{
    /// <summary>
    ///     用于快速构造流程配置的辅助类，方便在测试中复用典型流程。
    /// </summary>
    public static class FlowBuilder
    {
        public static FlowConfig CreatePowerHeaterFlow(string model = "UNIT_TEST_MODEL")
        {
            var devices = new Dictionary<string, DeviceConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["power"] = new DeviceConfig
                {
                    Type = "fake_device",
                    Settings = new Dictionary<string, object> { ["id"] = "power" }
                },
                ["heater"] = new DeviceConfig
                {
                    Type = "fake_device",
                    Settings = new Dictionary<string, object> { ["id"] = "heater" }
                }
            };

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    Name = "power_on",
                    Description = "模拟上电动作",
                    Target = "power",
                    Command = "set_output",
                    Parameters = new Dictionary<string, object>
                    {
                        ["voltage"] = 12,
                        ["current_limit"] = 5
                    },
                    ExpectedResults = new Dictionary<string, object>(),
                    TimeoutMs = 500,
                    DependsOn = new List<string>(),
                    OnSuccess = "heater_low",
                    OnFailure = "power_off"
                },
                new StepConfig
                {
                    Name = "heater_low",
                    Description = "模拟座椅加热低档",
                    Target = "heater",
                    Command = "set_level",
                    Parameters = new Dictionary<string, object> { ["level"] = "low" },
                    ExpectedResults = new Dictionary<string, object>(),
                    TimeoutMs = 500,
                    DependsOn = new List<string> { "power_on" },
                    OnSuccess = "heater_high",
                    OnFailure = "power_off"
                },
                new StepConfig
                {
                    Name = "heater_high",
                    Description = "模拟座椅加热高档",
                    Target = "heater",
                    Command = "set_level",
                    Parameters = new Dictionary<string, object> { ["level"] = "high" },
                    ExpectedResults = new Dictionary<string, object>(),
                    TimeoutMs = 500,
                    DependsOn = new List<string> { "heater_low" },
                    OnSuccess = "heater_off",
                    OnFailure = "power_off"
                },
                new StepConfig
                {
                    Name = "heater_off",
                    Description = "关闭加热输出",
                    Target = "heater",
                    Command = "shutdown",
                    Parameters = new Dictionary<string, object>(),
                    ExpectedResults = new Dictionary<string, object>(),
                    TimeoutMs = 500,
                    DependsOn = new List<string> { "heater_high" },
                    OnSuccess = "power_off",
                    OnFailure = "power_off"
                },
                new StepConfig
                {
                    Name = "power_off",
                    Description = "断电收尾",
                    Target = "power",
                    Command = "shutdown",
                    Parameters = new Dictionary<string, object>(),
                    ExpectedResults = new Dictionary<string, object>(),
                    TimeoutMs = 500,
                    DependsOn = new List<string> { "heater_off" },
                    OnSuccess = string.Empty,
                    OnFailure = string.Empty
                }
            };

            return new FlowConfig
            {
                Model = model,
                Devices = devices,
                TestSteps = steps
            };
        }

        public static FlowConfig CreateFlowWithMissingDependency()
        {
            var baseFlow = CreatePowerHeaterFlow("UNIT_TEST_MISCONFIG");
            var stepMap = baseFlow.TestSteps.ToDictionary(s => s.Name, s => CloneStep(s), StringComparer.OrdinalIgnoreCase);

            var misordered = new List<StepConfig>
            {
                stepMap["heater_high"],
                stepMap["power_on"],
                stepMap["heater_low"],
                stepMap["heater_off"],
                stepMap["power_off"]
            };

            misordered[0].DependsOn = new List<string>();

            baseFlow.TestSteps = misordered;
            return baseFlow;
        }

        public static Dictionary<string, FakeStepBehavior> CreateDefaultBehaviors(int delayMs = 80)
        {
            return new Dictionary<string, FakeStepBehavior>(StringComparer.OrdinalIgnoreCase)
            {
                ["power_on"] = new FakeStepBehavior { DelayMs = delayMs },
                ["heater_low"] = new FakeStepBehavior { DelayMs = delayMs },
                ["heater_high"] = new FakeStepBehavior { DelayMs = delayMs },
                ["heater_off"] = new FakeStepBehavior { DelayMs = delayMs },
                ["power_off"] = new FakeStepBehavior { DelayMs = delayMs }
            };
        }

        public static StepConfig CloneStep(StepConfig src)
        {
            return new StepConfig
            {
                Name = src.Name,
                Description = src.Description,
                Target = src.Target,
                Target = src.Target,
                Command = src.Command,
                Parameters = src.Parameters != null ? new Dictionary<string, object>(src.Parameters) : null,
                ExpectedResults = src.ExpectedResults != null ? new Dictionary<string, object>(src.ExpectedResults) : null,
                TimeoutMs = src.TimeoutMs,
                DependsOn = src.DependsOn != null ? new List<string>(src.DependsOn) : null,
                OnSuccess = src.OnSuccess,
                OnFailure = src.OnFailure,
                Type = src.Type,
                Steps = src.Steps,
                Ref = src.Ref
            };
        }
    }
}
