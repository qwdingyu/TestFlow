using System;
using System.Collections.Generic;
using System.Threading;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public static class DeviceStepRouter
    {

        public static ExecutionResult Execute(StepConfig step, StepContext stepCtx)
        {
            string deviceKey = step.Target;
            DeviceConfig cfg = DeviceServices.Factory.GetDeviceCfg(deviceKey);
            if (cfg == null)
            {
                throw new ArgumentException();
            }
            return Execute(deviceKey, cfg, step, stepCtx);
        }
        public static ExecutionResult Execute(string deviceKey, StepConfig step, StepContext stepCtx)
        {
            DeviceConfig cfg = DeviceServices.Factory.GetDeviceCfg(deviceKey);
            if (cfg == null)
            {
                throw new ArgumentException();
            }
            return Execute(deviceKey, cfg, step, stepCtx);
        }
        public static ExecutionResult Execute(string deviceKey, DeviceConfig cfg, StepConfig step, StepContext stepCtx)
        {
            if (DeviceServices.Factory == null)
                throw new InvalidOperationException("DeviceServices.Factory 尚未初始化");
            using var cts = new CancellationTokenSource();
            if (step.TimeoutMs > 0)
                cts.CancelAfter(step.TimeoutMs);

            Dictionary<string, object> outputs = null;

            try
            {
                return DeviceServices.Factory.UseDevice(deviceKey, cfg, dev =>
                {
                    if (dev == null) return new ExecutionResult { Success = false, Message = $"未找到该设备【{deviceKey}】！", Outputs = outputs };
                    if (dev is ICapabilityDevice capDev)
                    {
                        var Parameters = step.Parameters ?? new Dictionary<string, object>();
                        outputs = capDev.CallAsync(step.Command, Parameters, stepCtx).GetAwaiter().GetResult();
                        return new ExecutionResult { Success = true, Message = "OK", Outputs = outputs };

                        //string mismatchReason;
                        //var success = ResultEvaluator.Evaluate(step.ExpectedResults, outputs, step.Parameters, out mismatchReason);
                        //return new ExecutionResult
                        //{
                        //    Success = success,
                        //    Outputs = outputs,
                        //    Message = success ? $"Capability {step.Command} executed" : $"Capability {step.Command} executed fail, {mismatchReason}"
                        //};
                    }
                    else
                    {
                        return dev.Execute(step, stepCtx);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                return new ExecutionResult { Success = false, Message = "Timeout/Cancelled", Outputs = new() };
            }
            catch (Exception ex)
            {
                LogHelper.Error($"[Error] {ex}");
                return new ExecutionResult { Success = false, Message = ex.Message, Outputs = outputs };
            }
        }
    }


}
