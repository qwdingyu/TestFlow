using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkflowCore.Interface;
using ZL.DeviceLib;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Workflow;

namespace TestFlowDemo.Tests.Helpers
{
    /// <summary>
    ///     集成 WorkflowCore 宿主 + 假设备的测试装置，帮助在单元测试中驱动完整流程。
    /// </summary>
    public sealed class WorkflowTestHarness : IDisposable
    {
        private readonly IServiceProvider _provider;
        private readonly IWorkflowHost _host;
        private readonly DeviceFactory _factory;
        private readonly FakeDatabaseService _database;
        private readonly List<string> _logs = new List<string>();
        private readonly object _logSync = new object();

        public WorkflowTestHarness()
        {
            var services = new ServiceCollection()
                .AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug))
                .AddWorkflow();

            _provider = services.BuildServiceProvider();
            _host = _provider.GetRequiredService<IWorkflowHost>();
            _host.RegisterWorkflow<DynamicLoopWorkflow, FlowData>();
            _host.Start();

            _factory = new DeviceFactory(System.IO.Path.GetTempPath(), System.IO.Path.GetTempPath());
            _factory.Register("fake_device", (f, cfg) => new FakeDevice(cfg));

            _database = new FakeDatabaseService();
            DeviceServices.Factory = _factory;
            DeviceServices.Db = _database;

            UiEventBus.Log += OnLog;
        }

        /// <summary>
        ///     用于捕获执行过程的日志文本，便于人工排查。
        /// </summary>
        public IReadOnlyList<string> Logs
        {
            get
            {
                lock (_logSync)
                {
                    return _logs.ToList();
                }
            }
        }

        /// <summary>
        ///     便于断言的运行时模拟器，记录执行顺序与并发度。
        /// </summary>
        public FakeDeviceRuntime Runtime { get; } = new FakeDeviceRuntime();

        /// <summary>
        ///     暴露内存数据库的记录，测试用例可直接断言。
        /// </summary>
        public FakeDatabaseService Database => _database;

        private void OnLog(string message)
        {
            lock (_logSync)
            {
                _logs.Add(message);
            }
        }

        /// <summary>
        ///     驱动一次完整流程，支持配置步骤行为与超时。
        /// </summary>
        public async Task<FlowExecutionResult> RunAsync(FlowConfig config, IDictionary<string, FakeStepBehavior> behaviors, int timeoutSeconds = 5)
        {
            Runtime.Reset();
            _database.Reset();
            lock (_logSync) { _logs.Clear(); }

            FakeDeviceRegistry.Reset();
            FakeDeviceRegistry.Runtime = Runtime;
            foreach (var kv in behaviors)
            {
                FakeDeviceRegistry.Configure(kv.Key, kv.Value);
            }

            DeviceServices.Config = config;
            var cts = new CancellationTokenSource();
            DeviceServices.Context = new StepContext(config.Model, cts.Token);

            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void CompletedHandler(string sessionId, string model)
            {
                if (string.Equals(model, config.Model, StringComparison.OrdinalIgnoreCase))
                {
                    completion.TrySetResult(sessionId);
                }
            }
            UiEventBus.WorkflowCompleted += CompletedHandler;

            var data = new FlowData
            {
                Model = config.Model,
                Sn = $"{config.Model}-SN",
                Cancellation = cts.Token
            };

            var runId = await _host.StartWorkflow("DynamicLoopWorkflow", data);

            bool timedOut = false;
            try
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));
                var finished = await Task.WhenAny(completion.Task, timeoutTask);
                if (finished == timeoutTask)
                {
                    timedOut = true;
                    cts.Cancel();
                    try { await _host.TerminateWorkflow(runId); } catch { }
                }
                else
                {
                    await completion.Task;
                }
            }
            finally
            {
                UiEventBus.WorkflowCompleted -= CompletedHandler;
            }

            var snapshot = Runtime.CreateSnapshot();
            return new FlowExecutionResult
            {
                RunId = runId,
                Completed = !timedOut,
                TimedOut = timedOut,
                Runtime = snapshot,
                Logs = Logs,
                StepRecords = _database.StepRecords
            };
        }

        public void Dispose()
        {
            UiEventBus.Log -= OnLog;
            try { _host.Stop(); } catch { }
            _factory.Dispose();
            if (_provider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    ///     封装流程执行结果，包含日志、时间线等信息。
    /// </summary>
    public sealed class FlowExecutionResult
    {
        public string RunId { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public bool TimedOut { get; set; }
        public RuntimeSnapshot Runtime { get; set; } = new RuntimeSnapshot();
        public IReadOnlyList<string> Logs { get; set; } = Array.Empty<string>();
        public IReadOnlyList<FakeStepRecord> StepRecords { get; set; } = Array.Empty<FakeStepRecord>();
    }
}
