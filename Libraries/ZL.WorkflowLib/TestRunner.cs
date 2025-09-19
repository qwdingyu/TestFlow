using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WorkflowCore.Interface;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.WorkflowLib.Engine;
using ZL.WorkflowLib.Utils;
using ZL.WorkflowLib.Workflow;
using ZL.WorkflowLib.Workflow.Flows;
using ZL.WorkflowLib.Workflow.Lite;

namespace ZL.WorkflowLib
{
    public class TestRunner
    {
        private readonly IWorkflowHost _host;
        /// <summary>
        /// 是否使用自己实现的DAG编排器
        /// </summary>
        private bool _useLite = false;
        private static readonly ConcurrentDictionary<string, bool> _registered = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _timeoutMap = new ConcurrentDictionary<string, CancellationTokenSource>();
        public TestRunner(IWorkflowHost host, bool useLite = true)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _useLite = useLite;
            WorkflowServices.WorkflowHost = _host; // 记录全局 Host，供子流程调度
            SubflowDefinitionCatalog.RegisterWorkflows(_host, WorkflowServices.Subflows);
            if (_useLite)
                _host.RegisterWorkflow<WorkflowBuildLite, FlowModel>();

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
        public void UseLite(bool useLite)
        {
            _useLite = useLite;
        }
        /// <summary>
        /// 确保某型号的工作流只注册一次
        /// </summary>
        private void EnsureWorkflowRegistered(FlowConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            string wfId = config.Id;

            // 已经注册过了，直接返回
            if (_registered.ContainsKey(wfId))
                return;

            // 查询 Registry 里是否已有
            var exists = _host.Registry.GetDefinition(wfId, WorkflowServices.WorkflowVersion);
            if (exists != null)
            {
                _registered[wfId] = true;
                return;
            }

            // 注册新的 WorkflowDefinition
            var wf = new WorkflowBuild(config);
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
            // 记录到 FlowModel.ActiveConfig，确保每个流程实例持有独立配置。
            var flowData = new FlowModel { Model = model, Sn = barcode, Cancellation = cts.Token , ActiveConfig = cfg };
            // 为兼容旧版代码路径，仍然同步一次全局引用，仅用于调试查看。
            WorkflowServices.FlowCfg = cfg;

            string runId = "";
            if (_useLite)
            {
                runId = _host.StartWorkflow("WorkflowBuildLite", flowData).Result;
            }
            else
            {
                // 2) 确保注册
                EnsureWorkflowRegistered(cfg);
                var def = _host.Registry.GetDefinition(cfg.Id, WorkflowServices.WorkflowVersion);
                if (def == null)
                {
                    UiEventBus.PublishLog($"[DEBUG] Workflow Registe异常！条码={barcode} 型号={model}, 请检查！");
                    return "工作流注册异常，无法继续！";
                }
                UiEventBus.PublishLog($"[DEBUG] Workflow Definition {def.Id} version={def.Version}, Steps={def.Steps.Count}");

                // 4) 启动
                runId = _host.StartWorkflow(cfg.Id, WorkflowServices.WorkflowVersion, flowData).GetAwaiter().GetResult();
            }

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
                finally
                {
                    CleanupTimeoutRegistration(runId, cts, dispose: true);
                }
            }, cts.Token);
            return runId;
        }
        public void CancelTimeout(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return;
            if (_timeoutMap.TryRemove(runId, out var cts))
            {
                try
                {
                    if (!cts.IsCancellationRequested)
                        cts.Cancel();
                }
                finally
                {
                    cts.Dispose();
                }
                UiEventBus.PublishLog($"[Guard] RunId={runId} 超时守护已解除");
            }
        }

        /// <summary>
        /// 统一清理超时监控注册，避免 ConcurrentDictionary 长期持有工作流配置引用。
        /// </summary>
        /// <param name="runId">工作流运行 Id。</param>
        /// <param name="cts">对应的取消源。</param>
        /// <param name="dispose">是否在移除后释放取消源。</param>
        private void CleanupTimeoutRegistration(string runId, CancellationTokenSource cts, bool dispose)
        {
            if (string.IsNullOrEmpty(runId) || cts == null)
                return;

            if (_timeoutMap.TryGetValue(runId, out var existing) && ReferenceEquals(existing, cts))
            {
                if (_timeoutMap.TryRemove(runId, out existing) && dispose)
                {
                    try { existing.Dispose(); } catch { }
                }
            }
            else if (dispose)
            {
                try { cts.Dispose(); } catch { }
            }
        }
    }
}

