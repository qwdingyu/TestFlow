using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WorkflowCore.Interface;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Workflow;

namespace ZL.WorkflowLib
{
    public class TestRunner
    {
        private readonly IWorkflowHost _host;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _timeoutMap = new ConcurrentDictionary<string, CancellationTokenSource>();
        public TestRunner(IWorkflowHost host) { _host = host; }
        public string RunTest(string model, string barcode, int timeoutSeconds = 60)
        {
            var cts = new CancellationTokenSource();
            var ctx = new StepContext(model, cts.Token);
            DeviceServices.Config = ConfigManager.Instance.GetFlowConfig(model);
            DeviceServices.Context = ctx;
            var flowData = new FlowData { Model = model, Sn = barcode, Cancellation = cts.Token };
            var runId = _host.StartWorkflow("DynamicLoopWorkflow", flowData).Result;
            UiEventBus.PublishLog($"[Run] 启动工作流 RunId={runId}, Model={model}, Barcode={barcode}");
            _timeoutMap[runId] = cts;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cts.Token);
                    if (!cts.Token.IsCancellationRequested)
                    {
                        cts.Cancel();
                        _host.TerminateWorkflow(runId).Wait();
                        UiEventBus.PublishLog($"[Timeout] RunId={runId} 超过 {timeoutSeconds}s，已强制终止");
                    }
                }
                catch (TaskCanceledException) { }
            }, cts.Token);
            return runId;
        }
        public void CancelTimeout(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return;
            if (_timeoutMap.TryRemove(runId, out var cts)) { cts.Cancel(); cts.Dispose(); UiEventBus.PublishLog($"[Guard] RunId={runId} 超时守护已解除"); }
        }
    }
}

