using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WorkflowCore.Interface;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Utils;
using ZL.WorkflowLib.Workflow;
using ZL.WorkflowLib.Workflow.Flows;

namespace ZL.WorkflowLib
{
    public class TestRunner
    {
        private readonly IWorkflowHost _host;
        private static readonly ConcurrentDictionary<string, bool> _registered = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _timeoutMap = new ConcurrentDictionary<string, CancellationTokenSource>();
        public TestRunner(IWorkflowHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));

            WorkflowServices.WorkflowHost = _host; // 记录全局 Host，供子流程调度
            SubflowDefinitionCatalog.RegisterWorkflows(_host, WorkflowServices.Subflows);

            _host.Start();  // 必须调用，而且要在 StartWorkflow 之前
            _host.OnStepError += (workflow, step, ex) =>
            {
                UiEventBus.PublishLog($"Step [Error] {step.Name} - {ex.Message}");
            };
            //_host.OnLifeCycleEvent += evt =>
            //{
            //    UiEventBus.PublishLog($"[LifeCycle] {evt.GetType().Name} - {evt.WorkflowInstanceId}");
            //};

            EventInspector.DumpEventHandlers(_host, "OnLifeCycleEvent");
            EventInspector.DumpEventHandlers(_host, "OnStepError");
        }
        /// <summary>
        /// 确保某型号的工作流只注册一次
        /// </summary>
        private void EnsureWorkflowRegistered(FlowConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            string wfId = "dyn:" + config.Model;
            int wfVer = 1;

            // 已经注册过了，直接返回
            if (_registered.ContainsKey(wfId))
                return;

            // 查询 Registry 里是否已有
            var exists = _host.Registry.GetDefinition(wfId, wfVer);
            if (exists != null)
            {
                _registered[wfId] = true;
                return;
            }

            // 注册新的 WorkflowDefinition
            var wf = new DynamicLoopWorkflow(config);
            _host.Registry.RegisterWorkflow(wf);

            _registered[wfId] = true;
        }
        public string RunTest(string model, string barcode, int timeoutSeconds = 60)
        {
            var cts = new CancellationTokenSource();
            var ctx = new StepContext(model, cts.Token);
            DeviceServices.Context = ctx;
            // 1) 加载配置
            var cfg = ConfigManager.Instance.GetFlowConfig(model);
            DeviceServices.Config = cfg;

            // 2) 确保注册
            EnsureWorkflowRegistered(cfg);
            var def = _host.Registry.GetDefinition("dyn:" + model, 1);
            UiEventBus.PublishLog($"[DEBUG] Workflow def exists? {def != null}");
            UiEventBus.PublishLog($"[DEBUG] Definition {def.Id} v{def.Version}, Steps={def.Steps.Count}");
            var flowData = new FlowData { Model = model, Sn = barcode, Cancellation = cts.Token };
            // 4) 启动
            string wfId = "dyn:" + model;
            var runId = _host.StartWorkflow(wfId, 1, flowData).GetAwaiter().GetResult();

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

