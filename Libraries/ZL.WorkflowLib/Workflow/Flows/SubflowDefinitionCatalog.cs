using System;
using System.Collections.Generic;
using WorkflowCore.Interface;
using WorkflowCore.Models;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Engine;

namespace ZL.WorkflowLib.Workflow.Flows
{
    /// <summary>
    ///     子流程定义集中营：将原本分散在 JSON 中的定义迁移到代码里，
    ///     统一由此处提供注册方法与 WorkflowCore 工作流实例，便于主流程直接调用。
    /// </summary>
    public static class SubflowDefinitionCatalog
    {
        /// <summary>
        ///     将所有内置子流程注册到 <see cref="SubflowRegistry"/> 中，供运行期查询。
        /// </summary>
        public static void Initialize(SubflowRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register(BuildEcuCanCheck());
            registry.Register(BuildEcuIvCheck());
            registry.Register(BuildEcuPowerOnCheck());
        }
        /// <summary>
        /// 按照当前注册表内容创建 WorkflowCore 工作流，供 <see cref="SubFlowExecutor"/> 启动。
        /// </summary>
        public static void RegisterWorkflows(IWorkflowHost host, SubflowRegistry subflowRegistry)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (subflowRegistry == null) throw new ArgumentNullException(nameof(subflowRegistry));

            IWorkflowRegistry wfRegistry = host.Registry;   // 关键：用 Host 的 Registry

            foreach (var subflow in subflowRegistry.GetAll())
            {
                wfRegistry.RegisterWorkflow(new JsonSubFlowWorkflow(subflow));

                SubFlowExecutor.MarkWorkflowRegistered(JsonSubFlowWorkflow.BuildWorkflowId(subflow.Name));
            }
        }
        private static StepConfig BuildEcuCanCheck()
        {
            var def = new StepConfig
            {
                Name = "ecu_can_check",
                Description = "CAN 报文测试（代码定义）",
                Type = "SubFlow",
                Steps = new List<StepConfig>()
            };

            var wake = new StepConfig
            {
                Name = "唤醒报文",
                Description = "唤醒报文",
                Target = "can_adapter_1",
                Command = "send_and_receive",
                Parameters = new Dictionary<string, object>(),
                ExpectedResults = new Dictionary<string, object>()
            };
            wake.Parameters["@from_db"] = true;
            def.Steps.Add(wake);

            var massage = new StepConfig
            {
                Name = "主驾按摩",
                Description = "主驾按摩",
                Target = "can_adapter_1",
                Command = "send_and_receive",
                Parameters = new Dictionary<string, object>(),
                ExpectedResults = new Dictionary<string, object>()
            };
            massage.Parameters["@from_db"] = true;
            def.Steps.Add(massage);

            return def;
        }

        private static StepConfig BuildEcuIvCheck()
        {
            var def = new StepConfig
            {
                Name = "ecu_iv_check",
                Description = "电流电阻联动检查",
                Type = "SubFlow",
                Steps = new List<StepConfig>()
            };

            var applyRes = new StepConfig
            {
                Name = "apply_resistance",
                Description = "设定电阻",
                Target = "resistor_box",
                Command = "set_resistance",
                Parameters = new Dictionary<string, object>(),
                ExpectedResults = new Dictionary<string, object>()
            };
            applyRes.Parameters["value"] = 100;
            applyRes.ExpectedResults["mode"] = "equals";
            applyRes.ExpectedResults["key"] = "status";
            applyRes.ExpectedResults["value"] = "ok";
            def.Steps.Add(applyRes);

            var measureCurrent = new StepConfig
            {
                Name = "measure_current",
                Description = "测量电流",
                Target = "current_meter_1",
                Command = "measure",
                Parameters = new Dictionary<string, object>(),
                ExpectedResults = new Dictionary<string, object>()
            };
            measureCurrent.Parameters["range"] = "auto";
            measureCurrent.ExpectedResults["mode"] = "range";
            measureCurrent.ExpectedResults["key"] = "current";
            measureCurrent.ExpectedResults["min"] = 1.0;
            measureCurrent.ExpectedResults["max"] = 2.0;
            def.Steps.Add(measureCurrent);

            var measureVoltage = new StepConfig
            {
                Name = "measure_voltage",
                Description = "测量电压",
                Target = "voltmeter_1",
                Command = "measure",
                Parameters = new Dictionary<string, object>(),
                ExpectedResults = new Dictionary<string, object>()
            };
            measureVoltage.Parameters["range"] = "auto";
            measureVoltage.ExpectedResults["mode"] = "range";
            measureVoltage.ExpectedResults["key"] = "current";
            measureVoltage.ExpectedResults["min"] = 11.5;
            measureVoltage.ExpectedResults["max"] = 12.5;
            def.Steps.Add(measureVoltage);

            return def;
        }

        private static StepConfig BuildEcuPowerOnCheck()
        {
            var def = new StepConfig
            {
                Name = "ecu_power_on_check",
                Description = "ECU 上电检测",
                Type = "SubFlow",
                Steps = new List<StepConfig>()
            };

            var setVoltage = new StepConfig
            {
                Name = "set_voltage",
                Description = "设置电流",
                Target = "power_supply_1",
                Command = "set_voltage",
                Parameters = new Dictionary<string, object>(),
                ExpectedResults = new Dictionary<string, object>()
            };
            setVoltage.ExpectedResults["mode"] = "tolerance";
            setVoltage.ExpectedResults["key"] = "voltage";
            setVoltage.ExpectedResults["target"] = 12;
            setVoltage.ExpectedResults["tolerance"] = 0.5;
            def.Steps.Add(setVoltage);

            var measureCurrent = new StepConfig
            {
                Name = "measure_current",
                Description = "设置电阻",
                Target = "current_meter_1",
                Command = "measure",
                Parameters = new Dictionary<string, object>(),
                ExpectedResults = new Dictionary<string, object>()
            };
            measureCurrent.ExpectedResults["mode"] = "range";
            measureCurrent.ExpectedResults["key"] = "current";
            measureCurrent.ExpectedResults["min"] = 0.5;
            measureCurrent.ExpectedResults["max"] = 2.0;
            def.Steps.Add(measureCurrent);

            return def;
        }
    }

    /// <summary>
    ///     基于 JSON/代码定义生成的 WorkflowCore 工作流包装器。
    ///     仅包含一个 <see cref="RunSubFlowStep"/>，真正的执行逻辑在 <see cref="SubFlowExecutor"/> 内部完成。
    /// </summary>
    internal sealed class JsonSubFlowWorkflow : IWorkflow<FlowData>
    {
        private readonly StepConfig _definition;

        public JsonSubFlowWorkflow(StepConfig definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrEmpty(definition.Name))
                throw new ArgumentException("子流程缺少名称，无法注册", nameof(definition));
            if (definition.Steps == null || definition.Steps.Count == 0)
                throw new ArgumentException("子流程未包含任何子步骤", nameof(definition));

            _definition = Clone(definition);
        }

        public string Id
        {
            get { return BuildWorkflowId(_definition.Name); }
        }

        public int Version
        {
            get { return 1; }
        }

        public void Build(IWorkflowBuilder<FlowData> builder)
        {
            builder
                .StartWith<RunSubFlowStep>()
                .Input(step => step.SubflowName, data => _definition.Name)
                .Input(step => step.StepConfigs, data => CloneSteps(_definition.Steps));
        }

        internal static string BuildWorkflowId(string name)
        {
            var trimmed = string.IsNullOrEmpty(name) ? "anonymous" : name.Trim();
            return "subflow:" + trimmed;
        }

        private static StepConfig Clone(StepConfig source)
        {
            if (source == null)
                return null;

            var copy = new StepConfig
            {
                Name = source.Name,
                Description = source.Description,
                Target = source.Target,
                Command = source.Command,
                Parameters = CloneDictionary(source.Parameters),
                ExpectedResults = CloneDictionary(source.ExpectedResults),
                TimeoutMs = source.TimeoutMs,
                DependsOn = source.DependsOn != null ? new List<string>(source.DependsOn) : null,
                OnSuccess = source.OnSuccess,
                OnFailure = source.OnFailure,
                Type = source.Type,
                Ref = source.Ref,
                Steps = CloneSteps(source.Steps)
            };
            return copy;
        }

        private static Dictionary<string, object> CloneDictionary(Dictionary<string, object> source)
        {
            if (source == null)
                return null;

            var copy = new Dictionary<string, object>();
            foreach (var kv in source)
                copy[kv.Key] = kv.Value;
            return copy;
        }

        private static List<StepConfig> CloneSteps(IList<StepConfig> steps)
        {
            if (steps == null)
                return new List<StepConfig>();

            var list = new List<StepConfig>();
            for (int i = 0; i < steps.Count; i++)
            {
                list.Add(Clone(steps[i]));
            }
            return list;
        }
    }

    /// <summary>
    ///     子流程真正的执行节点：接收一组 <see cref="StepConfig"/>，并调用 <see cref="SubFlowExecutor"/> 顺序执行。
    /// </summary>
    internal sealed class RunSubFlowStep : StepBody
    {
        /// <summary>运行期注入的子流程名称，仅用于日志。</summary>
        public string SubflowName { get; set; }

        /// <summary>运行期注入的子步骤集合。</summary>
        public IList<StepConfig> StepConfigs { get; set; }

        public override ExecutionResult Run(IStepExecutionContext context)
        {
            var data = context != null ? context.Workflow.Data as FlowData : null;
            if (data == null)
                throw new InvalidOperationException("子流程执行缺少 FlowData 数据上下文");

            SubFlowExecutor.ExecuteSequentialSubflow(SubflowName, StepConfigs, data);
            return ExecutionResult.Next();
        }
    }
}
