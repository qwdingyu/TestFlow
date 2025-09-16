using System;
using System.Linq;
using System.Threading.Tasks;
using TestFlowDemo.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace TestFlowDemo.Tests
{
    /// <summary>
    ///     通过集成宿主模拟典型的上电 + 座椅加热流程，验证依赖与超时行为。
    /// </summary>
    public class WorkflowSimulationTests
    {
        private readonly ITestOutputHelper _output;

        public WorkflowSimulationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task PowerAndHeaterFlowShouldExecuteSerially()
        {
            using var harness = new WorkflowTestHarness();
            var config = FlowBuilder.CreatePowerHeaterFlow();
            var behaviors = FlowBuilder.CreateDefaultBehaviors();

            var result = await harness.RunAsync(config, behaviors, timeoutSeconds: 5);
            DumpResult("串行执行校验", result);

            Assert.True(result.Completed && !result.TimedOut, "流程应在无异常情况下顺利完成。");
            Assert.Equal(new[] { "power_on", "heater_low", "heater_high", "heater_off", "power_off" }, result.Runtime.StepOrder);
            Assert.Equal(1, result.Runtime.MaxConcurrency);

            var timeline = result.Runtime.Timeline;
            for (int i = 1; i < timeline.Length; i++)
            {
                Assert.True(timeline[i].Start >= timeline[i - 1].End, $"步骤 {timeline[i - 1].StepName} 与 {timeline[i].StepName} 发生并发，违反串行约束。");
            }

            var heaterRecord = result.StepRecords.First(r => r.StepName == "heater_high");
            Assert.True(heaterRecord.Success, "加热高档步骤应成功结束。");
        }

        [Fact]
        public async Task StepWithoutDependencyBecomesUnexpectedRoot()
        {
            using var harness = new WorkflowTestHarness();
            var config = FlowBuilder.CreateFlowWithMissingDependency();
            var behaviors = FlowBuilder.CreateDefaultBehaviors();

            var result = await harness.RunAsync(config, behaviors, timeoutSeconds: 5);
            DumpResult("缺失 DependsOn 风险校验", result);

            Assert.True(result.Completed && !result.TimedOut, "流程虽完成，但顺序应已被破坏。");
            Assert.Equal("heater_high", result.Runtime.StepOrder.First());
            Assert.DoesNotContain("power_on", result.Runtime.StepOrder);
            Assert.Equal(new[] { "heater_high", "heater_off", "power_off" }, result.Runtime.StepOrder);
        }

        [Fact]
        public async Task HeaterHighTimeoutTriggersCancellation()
        {
            using var harness = new WorkflowTestHarness();
            var config = FlowBuilder.CreatePowerHeaterFlow("UNIT_TEST_TIMEOUT");
            config.TestSteps.First(s => s.Name == "heater_high").TimeoutMs = 200;

            var behaviors = FlowBuilder.CreateDefaultBehaviors();
            behaviors["heater_high"] = new FakeStepBehavior { DelayMs = 600 };

            var result = await harness.RunAsync(config, behaviors, timeoutSeconds: 5);
            DumpResult("步骤超时校验", result);

            Assert.True(result.Completed && !result.TimedOut, "局部超时后应能进入故障分支并收尾。");
            Assert.Contains("power_off", result.Runtime.StepOrder);
            var heaterRecord = result.StepRecords.First(r => r.StepName == "heater_high");
            Assert.False(heaterRecord.Success, "超时步骤应被标记为失败。");
            Assert.Contains("步骤被取消", heaterRecord.Message);
        }

        private void DumpResult(string title, FlowExecutionResult result)
        {
            _output.WriteLine($"==== {title} ====");
            _output.WriteLine($"RunId: {result.RunId}, Completed: {result.Completed}, TimedOut: {result.TimedOut}");
            _output.WriteLine("步骤顺序: " + string.Join(", ", result.Runtime.StepOrder));
            _output.WriteLine($"最大并发: {result.Runtime.MaxConcurrency}");
            foreach (var record in result.Runtime.Timeline)
            {
                _output.WriteLine($"  [{record.StepName}] {record.Start:HH:mm:ss.fff} -> {record.End:HH:mm:ss.fff}");
            }
            foreach (var log in result.Logs)
            {
                _output.WriteLine("LOG: " + log);
            }
        }
    }
}
