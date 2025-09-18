using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Workflow;

namespace ZL.WorkflowLib.Engine
{
    public static class StepUtils
    {
        public static StepConfig CloneWithParams(StepConfig src, Dictionary<string, object> effectiveParams)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            return new StepConfig
            {
                Name = src.Name,
                Description = src.Description,
                Target = src.Target,
                Command = src.Command,
                Parameters = effectiveParams,
                ExpectedResults = src.ExpectedResults,
                TimeoutMs = src.TimeoutMs,
                OnSuccess = src.OnSuccess,
                OnFailure = src.OnFailure,
                DependsOn = src.DependsOn != null ? new List<string>(src.DependsOn) : null,
                Type = src.Type,
                Steps = src.Steps,
                Ref = src.Ref
            };
        }

        public static Dictionary<string, object> ResolveParameters(StepConfig stepCfg, FlowModel data)
        {
            if (stepCfg == null) throw new ArgumentNullException(nameof(stepCfg));
            bool hasFromDb = (stepCfg.Parameters != null) && stepCfg.Parameters.ContainsKey("@from_db");
            if (hasFromDb)
            {
                return WorkflowServices.ParamInjector.GetParams(
                    WorkflowServices.ParamInjector.DefaultLine,
                    WorkflowServices.ParamInjector.DefaultStation,
                    data?.Model ?? "*",
                    stepCfg.Name
                );
            }
            if (stepCfg.Parameters != null && stepCfg.Parameters.Count > 0)
            {
                var json = JsonConvert.SerializeObject(stepCfg.Parameters);
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            }
            return new Dictionary<string, object>();
        }

        public static StepConfig BuildExecutableStep(StepConfig stepCfg, FlowModel data)
        {
            var effectiveParams = ResolveParameters(stepCfg, data);
            var step = CloneWithParams(stepCfg, effectiveParams);
            return step;
        }
    }
}
