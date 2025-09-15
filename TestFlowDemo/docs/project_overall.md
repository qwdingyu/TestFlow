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
7. 在架构选择上，本次决定采用 **独立编排器 (Orchestrator)** 方案，以提升流程控制能力和可维护性。

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

## 三、独立编排器 (Orchestrator) 方案分析

### 3.1 目标与思路

- 从 `DeviceExecStep` 中抽离“编排责任”，让 Step 只负责**触发/提交计划**，具体执行由编排器完成：
  - 设备任务 DAG（依赖/并行/窗口/重复/对齐）
  - 全局资源锁（基于 `resourceId`）
  - 统一超时/取消/重试策略
  - 任务级日志与追踪（traceId）
  - 结果聚合与标准化

### 3.2 设计（接口草案）

```csharp
public interface IOrchestrator
{
    OrchestrationResult Execute(OrchestrationPlan plan, StepContext ctx);
}

public sealed class OrchestrationPlan
{
    public string Name;
    public List<OrchTask> Tasks = new List<OrchTask>();  // DAG 节点
}

public sealed class OrchTask
{
    public string Id;                 // 任务标识
    public string Device;             // 设备名
    public string Command;            // 命令
    public Dictionary<string, object> Parameters;
    public string ResourceId;         // 物理资源互斥键（如 "can://ch0"）
    public List<string> DependsOn;    // 依赖任务 Ids
    public int TimeoutMs;             // 单任务超时
    public RetrySpec Retry;           // 重试策略（查询类优先）
    public WindowSpec Window;         // 可选：对齐/重复窗口
}

public sealed class RetrySpec { public int Attempts; public int DelayMs; }
public sealed class WindowSpec { public int Repeat; public int IntervalMs; }

public sealed class OrchestrationResult
{
    public bool Success;
    public Dictionary<string, Dictionary<string, object>> Outputs; // Id -> 输出
    public string Message;
}
```

**DeviceExecStep 的最小改动**：将 `__exec` 转换为 `OrchestrationPlan`，调用 `IOrchestrator.Execute(...)`。

### 3.3 改动面与成本（粗估）

- **新逻辑**：OrchestrationLib（1~2k 行）+ `__exec` 适配器（200~400 行）。
- **修改点**：`DeviceExecStep`（~150 行以内变更）、引入 `resourceId`（设备配置变更）。
- **人力/时间**：MVP：1 人 2~3 周；集成与灰度：1 周。
- **兼容性**：保持 netstandard2.0，支持 .NET 4.8 / .NET 7；限定 C# 7 语法。

### 3.4 收益

- 编排可复用/可组合：跨步骤/跨场景的任务图能沉淀为“模板”。
- 清晰的职责分离：Step 只做“触发”，Orchestrator 专注“执行”。
- 更强的全局能力：多任务窗口化、全局资源锁、跨设备对时。
- 长期维护成本降低：复杂性从 DeviceExecStep 中抽出，集中治理。

### 3.5 风险与缓解

- **学习曲线**：需要团队理解 Plan/DAG 概念 → 通过 `__exec` 适配平滑过渡。
- **过度设计**：避免一开始把编排器做成“重量级引擎”，坚持 MVP（依赖/并行/超时/锁/聚合）。

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
- **降低风险**：流程顺序由编排器保证，避免子流程无序并行。
- **可扩展性**：未来加入更多测试项或子流程，只需扩展模板。

---

## 五、结论与方向

1. 已决定采用 **独立编排器** 方案，确保流程控制可维护、可扩展。
2. 动态测试 JSON 生成由后端模板拼接完成，前端仅负责勾选。
3. 项目已完成：
   - `ZL.DeviceLib` / `ZL.WorkflowLib` 的分层与依赖切分。
   - `DeviceServices` + `WorkflowServices` 的独立化。
4. 下一步：
   - 实现 Orchestrator MVP（2~3 周）。
   - 引入流程模板库，支持动态生成 JSON。
   - 确保流程执行顺序确定，减少风险。
5. 长期方向：
   - 通过编排器逐步沉淀可复用的流程模板。
   - 考虑未来扩展事件驱动（被动响应）模式，降低上位机主动控制的风险。
