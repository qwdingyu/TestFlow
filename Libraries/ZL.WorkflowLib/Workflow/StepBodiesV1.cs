using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using ZL.DeviceLib;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib.Workflow
{
    public class InitStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowData)context.Workflow.Data;
            data.Done = false; data.LastSuccess = true; data.Model = DeviceServices.Config.Model;
            foreach (var s in DeviceServices.Config.TestSteps)
            {
                if (s.DependsOn == null || s.DependsOn.Count == 0) { data.Current = s.Name; break; }
            }
            if (string.IsNullOrEmpty(data.Current)) data.Done = true;
            data.SessionId = DeviceServices.Db.StartTestSession(data.Model, data.Sn);
            UiEventBus.PublishLog($"[Init] 产品={data.Model}, SN={data.Sn}, SessionId={data.SessionId}, 起始步骤={data.Current}");
            return ExecutionResult.Next();
        }
    }

    public class DeviceExecStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowData)context.Workflow.Data;
            if (data.Done || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();
            var stepCfg = DeviceServices.Config.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[DeviceExec] 未找到步骤配置: {data.Current}");
                return ExecutionResult.Next();
            }
            UiEventBus.PublishLog($"--[Flow] 开始 {stepCfg.Name}, 设备【{stepCfg.Device}】, 描述【{stepCfg.Description}】, 下一步【{stepCfg.OnSuccess}】");
            var started = DateTime.Now;
            var pooledResult = StepResultPool.Instance.Get();
            try
            {
                var execStep = StepUtils.BuildExecutableStep(stepCfg, data);
                DeviceConfig devConf;
                if (!DeviceServices.Config.Devices.TryGetValue(execStep.Device, out devConf))
                    throw new Exception("Device not found: " + execStep.Device);

                // 步骤级超时：与全局取消令牌联动
                var baseToken = DeviceServices.Context != null ? DeviceServices.Context.Cancellation : System.Threading.CancellationToken.None;
                using (var linked = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(baseToken))
                {
                    if (execStep.TimeoutMs > 0)
                        linked.CancelAfter(execStep.TimeoutMs);
                    var stepCtx = DeviceServices.Context != null ? DeviceServices.Context.CloneWithCancellation(linked.Token)
                                                             : new ZL.DeviceLib.Engine.StepContext(data.Model, linked.Token);

                    var outputs = DeviceServices.Factory.UseDevice(execStep.Device, devConf, dev =>
                    {
                        var result = dev.Execute(execStep, stepCtx);
                        pooledResult.Success = result.Success; pooledResult.Message = result.Message; pooledResult.Outputs = result.Outputs ?? new Dictionary<string, object>();
                        return pooledResult.Outputs;
                    });
                }
                string reason;
                bool passExpected = ResultEvaluator.Evaluate(execStep.ExpectedResults, pooledResult.Outputs, execStep.Parameters, out reason);
                if (!passExpected)
                {
                    pooledResult.Success = false;
                    pooledResult.Message = (pooledResult.Message ?? "") + " | expected mismatch: " + reason;
                }
                DeviceServices.Db.AppendStep(data.SessionId, data.Model, data.Sn, execStep.Name, execStep.Description, execStep.Device, execStep.Command,
                    JsonConvert.SerializeObject(execStep.Parameters), JsonConvert.SerializeObject(execStep.ExpectedResults), JsonConvert.SerializeObject(pooledResult.Outputs),
                    pooledResult.Success ? 1 : 0, pooledResult.Message, started, DateTime.Now);
                data.LastSuccess = pooledResult.Success;
                UiEventBus.PublishLog($"[Step] {execStep.Name} | 设备={execStep.Device} | Success={pooledResult.Success} | Msg={pooledResult.Message}");
            }
            catch (Exception ex)
            {
                data.LastSuccess = false;
                DeviceServices.Db.AppendStep(data.SessionId, data.Model, data.Sn, stepCfg.Name, stepCfg.Description, stepCfg.Device, stepCfg.Command,
                    JsonConvert.SerializeObject(stepCfg.Parameters), JsonConvert.SerializeObject(stepCfg.ExpectedResults), null, 0, "Exception: " + ex.Message, started, DateTime.Now);
                UiEventBus.PublishLog($"[Step-Exception] {stepCfg.Name} | 错误={ex.Message}");
            }
            finally
            {
                StepResultPool.Instance.Return(pooledResult);
            }
            return ExecutionResult.Next();
        }
    }

    public class SubFlowExecutor
    {
        public void RunSubFlow(StepConfig stepCfg, FlowData data, StepConfig parentStepCfg)
        {
            UiEventBus.PublishLog($"[SubFlow] 开始 {stepCfg.Name}, 子步骤共有【{stepCfg.Steps.Count}】步");
            foreach (var sub in stepCfg.Steps)
            {
                UiEventBus.PublishLog($"---[SubFlow] 开始 {sub.Name}, 设备【{sub.Device}】, 描述【{sub.Description}】");
                var started = DateTime.Now;
                var pooledResult = StepResultPool.Instance.Get();
                try
                {
                    var execSub = StepUtils.BuildExecutableStep(sub, data);
                    DeviceConfig devConf; if (!DeviceServices.Config.Devices.TryGetValue(execSub.Device, out devConf)) throw new Exception("Device not found: " + execSub.Device);

                    var baseToken = DeviceServices.Context != null ? DeviceServices.Context.Cancellation : System.Threading.CancellationToken.None;
                    using (var linked = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(baseToken))
                    {
                        if (execSub.TimeoutMs > 0) linked.CancelAfter(execSub.TimeoutMs);
                        var stepCtx = DeviceServices.Context != null ? DeviceServices.Context.CloneWithCancellation(linked.Token)
                                                                 : new ZL.DeviceLib.Engine.StepContext(data.Model, linked.Token);

                        var outputs = DeviceServices.Factory.UseDevice(execSub.Device, devConf, dev =>
                        {
                            var result = dev.Execute(execSub, stepCtx);
                            pooledResult.Success = result.Success;
                            pooledResult.Message = result.Message;
                            pooledResult.Outputs = result.Outputs ?? new Dictionary<string, object>();
                            return pooledResult.Outputs;
                        });
                    }
                    string reason;
                    bool passExpected = ResultEvaluator.Evaluate(execSub.ExpectedResults, pooledResult.Outputs, execSub.Parameters, out reason);
                    if (!passExpected)
                    {
                        pooledResult.Success = false;
                        pooledResult.Message = (pooledResult.Message ?? "") + " | expected mismatch: " + reason;
                    }
                    DeviceServices.Db.AppendStep(data.SessionId, data.Model, data.Sn, execSub.Name, execSub.Description, execSub.Device, execSub.Command,
                        JsonConvert.SerializeObject(execSub.Parameters), JsonConvert.SerializeObject(execSub.ExpectedResults), JsonConvert.SerializeObject(pooledResult.Outputs),
                        pooledResult.Success ? 1 : 0, pooledResult.Message, started, DateTime.Now);
                    data.LastSuccess = pooledResult.Success;
                    UiEventBus.PublishLog($"[SubStep] {execSub.Name} | Success={pooledResult.Success} | Msg={pooledResult.Message}");
                    if (!pooledResult.Success)
                    {
                        UiEventBus.PublishLog($"[SubFlow] 子步骤 {execSub.Name} 失败，中断子流程");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    data.LastSuccess = false;
                    DeviceServices.Db.AppendStep(data.SessionId, data.Model, data.Sn, sub.Name, sub.Description, sub.Device, sub.Command,
                        JsonConvert.SerializeObject(sub.Parameters), JsonConvert.SerializeObject(sub.ExpectedResults), null, 0, "Exception: " + ex.Message, started, DateTime.Now);
                    UiEventBus.PublishLog($"[SubStep-Exception] {sub.Name} | 错误={ex.Message}");
                    break;
                }
                finally { StepResultPool.Instance.Return(pooledResult); }
            }
            UiEventBus.PublishLog($"[SubFlow] 结束 {stepCfg.Name}, Success={data.LastSuccess}, 下一步【{parentStepCfg.OnSuccess}】");
        }
    }

    public class UnifiedExecStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowData)context.Workflow.Data;
            if (data.Done || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();
            var stepCfg = DeviceServices.Config.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[UnifiedExec] 未找到步骤配置: {data.Current}");
                return ExecutionResult.Next();
            }
            try
            {
                if (stepCfg.Type == "SubFlow")
                    new SubFlowExecutor().RunSubFlow(stepCfg, data, stepCfg);
                else if (stepCfg.Type == "SubFlowRef")
                {
                    if (string.IsNullOrEmpty(stepCfg.Ref))
                    {
                        data.LastSuccess = false;
                        UiEventBus.PublishLog($"[UnifiedExec] 步骤 {stepCfg.Name} 缺少 Ref 字段");
                    }
                    else
                    {
                        StepConfig subDef;
                        if (WorkflowServices.Subflows != null && WorkflowServices.Subflows.TryGet(stepCfg.Ref, out subDef))
                        {
                            UiEventBus.PublishLog($"[UnifiedExec] 执行子流程引用 {stepCfg.Ref} (from {stepCfg.Name})");
                            new SubFlowExecutor().RunSubFlow(subDef, data, stepCfg);
                        }
                        else
                        {
                            data.LastSuccess = false;
                            UiEventBus.PublishLog($"[UnifiedExec] 未找到子流程引用: {stepCfg.Ref} (from {stepCfg.Name})");
                        }
                    }
                }
                else
                {
                    new DeviceExecStep().Run(context);
                }
            }
            catch (Exception ex)
            {
                data.LastSuccess = false;
                UiEventBus.PublishLog($"[UnifiedExec] 执行步骤 {stepCfg.Name} 异常: {ex.Message}");
            }
            return ExecutionResult.Next();
        }
    }

    public class RouteStep : StepBody
    {
        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = (FlowData)context.Workflow.Data;
            if (data.Done || string.IsNullOrEmpty(data.Current))
                return ExecutionResult.Next();
            var stepCfg = DeviceServices.Config.TestSteps.Find(x => x.Name == data.Current);
            if (stepCfg == null)
            {
                data.Done = true; UiEventBus.PublishLog("[Route] 找不到当前步骤配置，强制结束");
                return ExecutionResult.Next();
            }

            string next = data.LastSuccess ? stepCfg.OnSuccess : stepCfg.OnFailure;
            UiEventBus.PublishLog($"[Route] {stepCfg.Name} -> {(string.IsNullOrEmpty(next) ? "(结束)" : next)} | LastSuccess={data.LastSuccess}");
            if (string.IsNullOrEmpty(next))
                data.Done = true;
            else
                data.Current = next;
            if (data.Done)
            {
                // 老版本流程同样需要根据最终结果写回状态，保持数据一致性
                DeviceServices.Db.FinishTestSession(data.SessionId, data.LastSuccess ? 1 : 0);
                UiEventBus.PublishCompleted(data.SessionId.ToString(), data.Model);
            }
            return ExecutionResult.Next();
        }
    }
}
