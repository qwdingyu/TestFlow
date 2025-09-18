using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using Xunit;
using ZL.DeviceLib;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;
using TestFlowDemo.Tests.Helpers;

namespace TestFlowDemo.Tests
{
    /// <summary>
    ///     验证编排计划在解析窗口配置后，能按照设定的次数与间隔重复执行设备步骤。
    /// </summary>
    public class PlanWorkflowWindowTests
    {
        [Fact]
        public async Task WindowSpecTriggersRepeatedExecutionWithInterval()
        {
            // 1. 准备 WorkflowCore 宿主与计划构建器。
            var services = new ServiceCollection()
                .AddLogging()
                .AddWorkflow();
            var provider = services.BuildServiceProvider();
            var host = provider.GetRequiredService<IWorkflowHost>();

            var planBuilder = new PlanWorkflowBuilder();
            var plan = BuildWindowPlan();
            var workflowId = "plan-window-" + Guid.NewGuid().ToString("N");
            var workflow = new InlinePlanWorkflow(workflowId, plan, planBuilder);
            host.RegisterWorkflow(workflow);
            host.Start();

            var factory = new DeviceFactory("sqlite", System.IO.Path.GetTempPath(), System.IO.Path.GetTempPath());
            var cts = new CancellationTokenSource();

            try
            {
                // 2. 准备假设备与上下文，确保步骤能顺利执行并被统计。
                factory.Register("fake_device", (f, cfg) => new FakeDevice(cfg));
                DeviceServices.Factory = factory;
                WorkflowServices.FlowCfg = new FlowConfig
                {
                    Model = "PLAN_MODEL",
                    Devices = new Dictionary<string, DeviceConfig>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["can_stub"] = new DeviceConfig
                        {
                            Type = "fake_device",
                            Settings = new Dictionary<string, object> { ["id"] = "can_stub" }
                        }
                    },
                    TestSteps = new List<StepConfig>()
                };

                var runtime = new FakeDeviceRuntime();
                FakeDeviceRegistry.Reset();
                FakeDeviceRegistry.Runtime = runtime;
                FakeDeviceRegistry.Configure("can_poll", new FakeStepBehavior { DelayMs = 10 });

                var database = new FakeDatabaseService();
                DeviceServices.Db = database;

                var sharedContext = new StepContext("PLAN_MODEL", cts.Token);
                DeviceServices.Context = sharedContext;

                // 3. 启动工作流并等待完成。
                var data = planBuilder.CreateData(plan, sharedContext);
                var runId = await host.StartWorkflow(workflowId, data);
                WorkflowInstance instance = await host.WaitForWorkflowToComplete(runId, cts.Token);

                // 4. 断言重复次数与间隔均符合窗口设定。
                var executions = runtime.StepOrder.Where(name => string.Equals(name, "can_poll", StringComparison.OrdinalIgnoreCase)).ToArray();
                Assert.Equal(3, executions.Length);

                var timeline = runtime.Timeline
                    .Where(record => string.Equals(record.StepName, "can_poll", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(record => record.Start)
                    .ToArray();
                Assert.Equal(3, timeline.Length);

                for (int i = 1; i < timeline.Length; i++)
                {
                    var delta = (timeline[i].Start - timeline[i - 1].Start).TotalMilliseconds;
                    Assert.True(delta >= 50, "相邻两次执行之间应至少等待窗口指定的间隔时间。");
                }

                var planData = instance != null ? instance.Data as PlanWorkflowData : null;
                Assert.NotNull(planData);
                Assert.NotNull(planData.FinalResult);
                Assert.True(planData.FinalResult.Success, "所有窗口内任务均应成功完成。");
            }
            finally
            {
                try { host.Stop(); } catch { }
                factory.Dispose();
                FakeDeviceRegistry.Reset();
                DeviceServices.Factory = null;
                WorkflowServices.FlowCfg = null;
                DeviceServices.Db = null;
                DeviceServices.Context = null;
                cts.Dispose();
                if (provider is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        private static OrchestrationPlan BuildWindowPlan()
        {
            return new OrchestrationPlan
            {
                Name = "CAN_Window_Demo",
                Tasks = new List<OrchTask>
                {
                    new OrchTask
                    {
                        Id = "can_poll",
                        Device = "can_stub",
                        Command = "noop",
                        Parameters = new Dictionary<string, object>(),
                        Window = new WindowSpec
                        {
                            Repeat = 3,
                            IntervalMs = 50
                        }
                    }
                }
            };
        }

        /// <summary>
        ///     内联工作流定义：使用传入的计划构建器即时拼装节点，便于在测试中复用。
        /// </summary>
        private sealed class InlinePlanWorkflow : IWorkflow<PlanWorkflowData>
        {
            private readonly string _id;
            private readonly OrchestrationPlan _plan;
            private readonly IPlanWorkflowBuilder _builder;

            public InlinePlanWorkflow(string id, OrchestrationPlan plan, IPlanWorkflowBuilder builder)
            {
                if (string.IsNullOrWhiteSpace(id))
                    throw new ArgumentNullException(nameof(id));
                if (plan == null)
                    throw new ArgumentNullException(nameof(plan));
                if (builder == null)
                    throw new ArgumentNullException(nameof(builder));

                _id = id;
                _plan = plan;
                _builder = builder;
            }

            public string Id
            {
                get { return _id; }
            }

            public int Version
            {
                get { return 1; }
            }

            public void Build(IWorkflowBuilder<PlanWorkflowData> builder)
            {
                _builder.Build(builder, _plan);
            }
        }
    }
}
