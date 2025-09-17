# 项目总体设计与方向规划

## 环境与技术说明

- **语言**: C#
- **框架版本**:
  - .NET Framework 4.8
  - .NET Core 7
- **语法限制**: C# 7.0（不使用更高版本的语法糖）
- **工程结构**:
  - `ZL.DeviceLib` (设备驱动/工厂/数据库接口)
  - `ZL.WorkflowLib` (流程编排/子流程/参数注入)
  - 静态入口已拆分为 `DeviceServices` 与 `WorkflowServices`
- **依赖管理**: Newtonsoft.Json, WorkflowCore
- **日志**: Serilog + 自研 UDP 扩展
- **设计目标**: 小巧、灵活、可扩展的瑞士军刀，避免重复修改与架构混乱，避免过度开发，过度工程化；容易理解，方便使用；提高开发效率。

---

## 一、项目诉求回顾

1. 构建一套 **汽车座椅电检测试系统**，支持多设备（CAN、功率计、噪音传感器、供电电源等）。
2. 核心目标：通过 **统一的工作流引擎 (ZL.WorkflowLib)** + **设备库 (ZL.DeviceLib)** 驱动测试步骤。
3. 需要支持 **跨设备并发**（例如：发 CAN 报文的同时采集电流、噪音），并能在数据库中保存统一结果。
4. 需要支持 **周期报文（运行环境）** 和 **事件报文（控制 3+3 模式）** 的调度，确保测试逻辑符合 ECU 实际运行特性。
5. 需要考虑 **前端 UI 选择测试项** → 动态生成流程 JSON 的能力，避免前端拼接复杂 JSON。
6. 流程控制应具备确定性，避免子流程步骤无序并发执行带来的风险。
7. 在架构选择上，本次决定采用 **WorkflowCore 原生步骤图** 搭配 JSON/YAML `StepConfig`，以提升流程控制能力和可维护性。

---

## 二、目前主要探讨的问题

1. **CAN 报文调度**
   - 周期报文如何持续发送？
   - 控制报文如何实现「3 帧动作 + 3 帧无控制帧」？
   - 如何通过过滤器 (`SetFilter`) 避免非必要噪音干扰？

2. **跨设备并发协同**
   - 在「加热控制」同时，需要同步获取电流与噪音数据。
   - 需要保证超时/取消/重试机制联动，结果能统一落库。

3. **架构解耦**
   - 已将 `AppServices` 拆分为 `DeviceServices` 与 `WorkflowServices`，避免了 ZL.DeviceLib 与 ZL.WorkflowLib 的循环依赖。
   - 后续可引入 `IAppContext` 接口，进一步提升灵活性。

4. **流程控制确定性**
   - 之前子流程内多步骤并行执行（无顺序），存在风险。
   - 未来需要确保 **步骤按定义顺序串行执行**，除非显式声明并行。
   - 减少上位机主动控制过多细节，降低风险。

5. **动态生成测试 JSON**
   - 用户在前端表格选择测试项（如 ECU 报文检查/加热/通风）。
   - 后端根据选择拼接预定义模板，生成完整 JSON，避免前端处理复杂逻辑。

---

## 三、WorkflowCore 节点映射方案

### 3.1 目标与思路

- 以 `StepConfig` 作为**唯一**的流程描述模型，避免额外维护 `OrchestrationPlan`/`OrchTask` 等中间结构。
- 支持从 JSON/YAML 文件直接反序列化为 `StepConfig`，并在 WorkflowCore 的 `Build` 阶段映射为节点。
- 子流程与主流程使用完全一致的模型，子流程注册后即可作为 `SubFlow`/`SubFlowRef` 节点复用。
- 所有控制能力（依赖、跳转、重试、资源锁、期望值判定）均由现有 StepBody 组合完成。

### 3.2 设计（代码草案）

```csharp
public void Build(IWorkflowBuilder<FlowData> builder)
{
    var init = builder.StartWith<InitStep>();
    var pipelines = new Dictionary<string, StepPipeline>(StringComparer.OrdinalIgnoreCase);
    IStepBuilder<FlowData, TransitionStep> lastTransition = null;

    foreach (var step in DeviceServices.Config.TestSteps)
    {
        var pipeline = BuildPipeline(lastTransition ?? init, step);
        pipelines[step.Name] = pipeline;
        lastTransition = pipeline.Transition;
    }

    foreach (var pipeline in pipelines.Values)
        ConfigureTransition(pipeline, pipelines);
}
```

> 注：实际实现位于 `DynamicLoopWorkflow`，会根据 `Type` 判断是否为子流程，并对并行/附属设备执行等场景追加节点。示例仅强调“直接由 `StepConfig` 生成 WorkflowCore 节点”。

### 3.3 数据转换流程

1. 从 `Flows/`、`Flows/Subflows/` 读取 JSON/YAML，反序列化为 `FlowConfig`/`StepConfig`。
2. `DynamicLoopWorkflow` 根据 `StepConfig` 列表构建主流程节点；`JsonSubFlowWorkflow` 为子流程包装单一节点。
3. 运行期由 `WorkflowHost` 执行节点，`SubFlowExecutor` 和 `DeviceExecStep` 负责实际设备调用与期望值校验。
4. 所有状态通过 `FlowData` 传递，数据库记录由 `DeviceServices.Db` 统一落库。

### 3.4 收益

- 模型统一：主流程、子流程、动态模板全部使用 `StepConfig`，无额外映射层。
- 维护简单：新增字段只需扩展 `StepConfig` 及对应 StepBody，无需同步更新计划类。
- 文档友好：配置示例直接对应运行时节点，便于培训与排查。
- 复用增强：JSON/YAML 模板可直接下发到 CLI 或上位机，无需额外转换工具。

### 3.5 风险与缓解

- **JSON/YAML 质量**：需通过 `FlowCli validate`、`FlowValidator` 保证必填字段与引用有效。
- **复杂场景**：并发/附属设备逻辑需在 StepBody 中持续沉淀范式，避免模板直接写自定义逻辑。
- **YAML 兼容性**：目前以内置 JSON 为主，后续若接入 YAML 需补充 Schema 与解析测试。

---

## 四、动态生成测试流程 JSON 的方案

### 4.1 背景

- 用户希望通过 **前端表格勾选** 来决定某型号测试哪些项目。
- 需要根据勾选动态生成测试 JSON，避免前端拼接复杂结构。

### 4.2 模板定义方式

在后端维护一个 **模板库**（例如 `flow_templates.json`）：

```json
{
  "templates": {
    "ecu_can_check": {
      "Type": "SubFlowRef",
      "Ref": "ecu_can_check",
      "Description": "CAN 报文测试"
    },
    "heater_test": {
      "Type": "SubFlowRef",
      "Ref": "heater_flow",
      "Description": "座椅加热测试"
    },
    "ventilation_test": {
      "Type": "SubFlowRef",
      "Ref": "ventilation_flow",
      "Description": "座椅通风测试"
    }
  }
}
```

> 模板字段与 `StepConfig` 完全一致，不再额外维护编排计划相关数据结构。

### 4.3 前端交互简化

前端只提交：

```json
{
  "model": "E311",
  "selectedTests": ["ecu_can_check", "heater_test"]
}
```

后端根据 `selectedTests` 拼接模板，生成完整（这里只是举例，请参考项目中的json和逻辑进行合理优化） JSON：

```json
{
  "Model": "E311",
  "TestSteps": [
    {
      "Name": "power_on",
      "Device": "power_supply_1",
      "Command": "set_voltage",
      "Parameters": { "voltage": 12.0 },
      "OnSuccess": "ecu_can_check_ref"
    },
    {
      "Name": "ecu_can_check_ref",
      "Type": "SubFlowRef",
      "Ref": "ecu_can_check",
      "DependsOn": ["power_on"],
      "OnSuccess": "heater_test_ref"
    },
    {
      "Name": "heater_test_ref",
      "Type": "SubFlowRef",
      "Ref": "heater_flow",
      "DependsOn": ["ecu_can_check_ref"],
      "OnSuccess": "power_off"
    },
    {
      "Name": "power_off",
      "Device": "power_supply_1",
      "Command": "set_voltage",
      "Parameters": { "voltage": 0.0 },
      "DependsOn": ["heater_test_ref"]
    }
  ]
}
```

### 4.4 收益

- **前端极简**：表格勾选即可，不处理复杂 JSON。
- **后端集中管理**：新增测试项只需扩展模板。
- **降低风险**：流程顺序由 WorkflowCore 步骤图保证，避免子流程无序并行。
- **可扩展性**：未来加入更多测试项或子流程，只需扩展模板。

---

## 五、结论与方向

1. 已决定采用 **WorkflowCore 节点映射** 方案，JSON/YAML `StepConfig` 直接转换为执行节点，不再维护额外计划模型。
2. 动态测试 JSON 生成由后端模板拼接完成，前端仅负责勾选。
3. 项目已完成：
   - `ZL.DeviceLib` / `ZL.WorkflowLib` 的分层与依赖切分。
   - `DeviceServices` + `WorkflowServices` 的独立化。
4. 下一步：
   - 持续完善模板库与校验工具，保证 StepConfig 字段与 WorkflowCore 节点一一对应。
   - 根据需要补充 YAML 解析与 Schema 校验示例。
   - 确保流程执行顺序与资源锁语义在 StepBody 中持续演进。
5. 长期方向：
   - 在通用 StepBody 中沉淀更多可复用的设备协同范式。
   - 考虑未来扩展事件驱动（被动响应）模式，降低上位机主动控制的风险。
