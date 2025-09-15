# 测试框架配置与执行说明

本说明文档详细描述了 **Devices.json**、型号配置 JSON、设备加载机制、参数传递方式以及 **ResultEvaluator** 的判定逻辑。

---

## 1. Devices.json 配置说明

配置已拆分：
- `devices.json` 定义物理设备（scanner、power_supply、current_meter、can_adapter 等）。
- `infrastructure.json` 定义基础设施（database、report_generator、system）。
加载时两者会合并注入到流程配置中，供步骤引用。

`devices.json` 用于定义测试系统中的所有“物理设备”，主要包含：

- **设备名**：唯一标识符，例如 `power_supply_1`
- **Type**：设备类型，对应设备实现类（如 `power_supply` → `PowerSupplyDevice`）
- **ConnectionString**：设备连接方式（如串口号、总线号、IP 地址）
- **Settings**：设备特定的配置参数（波特率、量程、分辨率等）

### 示例

```json
{
  "power_supply_1": {
    "Type": "power_supply",
    "ConnectionString": "COM3",
    "Settings": {
      "voltage": 12.0,
      "current_limit": 2.0
    }
  },
  "current_meter_1": {
    "Type": "current_meter",
    "ConnectionString": "COM5",
    "Settings": {
      "range": "auto",
      "unit": "A"
    }
  },
  "voltmeter_1": {
    "Type": "voltmeter",
    "ConnectionString": "COM6",
    "Settings": {
      "range": "auto",
      "unit": "V",
      "resolution": 0.001,
      "expected_nominal": 12.0
    }
  },
  "resistor_box_1": {
    "Type": "resistor_box",
    "ConnectionString": "COM7",
    "Settings": {
      "channels": 8,
      "unit": "Ohm"
    }
  },
  "can_adapter_1": {
    "Type": "can_bus",
    "ConnectionString": "PCAN_USBBUS1",
    "Settings": {
      "baud_rate": 500000,
      "mode": "normal"
    }
  },
  "report_generator": {
    "Type": "report_generator",
    "ConnectionString": "local",
    "Settings": {}
  },
  "system": {
    "Type": "system",
    "ConnectionString": "internal",
    "Settings": {}
  }
}
```

---

## 2. 型号配置 JSON

每个产品型号有对应的测试流程文件，例如 `E901.json`。

### 示例流程

```json
{
  "ProductModel": "E901",
  "TestSteps": [
    {
      "Name": "power_on",
      "Description": "上电",
      "Device": "power_supply_1",
      "Command": "set_voltage",
      "Parameters": { "voltage": 12.0 },
      "ExpectedResults": { "mode": "equals", "key": "status", "value": "ok" },
      "TimeoutMs": 2000,
      "OnSuccess": "ecu_can_check_ref",
      "OnFailure": "power_off"
    },
    {
      "Name": "ecu_can_check_ref",
      "Type": "SubFlow",
      "Ref": "ecu_can_check",
      "OnSuccess": "test_current",
      "OnFailure": "power_off"
    },
    {
      "Name": "test_current",
      "Description": "测试电流",
      "Device": "current_meter_1",
      "Command": "measure",
      "Parameters": { "range": "auto" },
      "ExpectedResults": {
        "mode": "range",
        "key": "current",
        "min": 1.0,
        "max": 2.0
      },
      "TimeoutMs": 2000,
      "OnSuccess": "power_off",
      "OnFailure": "power_off"
    },
    {
      "Name": "power_off",
      "Description": "下电",
      "Device": "power_supply_1",
      "Command": "set_voltage",
      "Parameters": { "voltage": 0.0 },
      "ExpectedResults": { "mode": "equals", "key": "status", "value": "ok" },
      "TimeoutMs": 2000,
      "OnSuccess": "generate_report",
      "OnFailure": "generate_report"
    },
    {
      "Name": "generate_report",
      "Description": "生成报告",
      "Device": "report_generator",
      "Command": "generate",
      "Parameters": { "format": "html" },
      "ExpectedResults": {},
      "TimeoutMs": 2000,
      "OnSuccess": "end_test",
      "OnFailure": "end_test"
    },
    {
      "Name": "end_test",
      "Description": "测试结束",
      "Device": "system",
      "Command": "shutdown",
      "Parameters": {},
      "ExpectedResults": {},
      "TimeoutMs": 1000,
      "OnSuccess": "",
      "OnFailure": ""
    }
  ]
}
```

### 子流程 JSON 示例

`ecu_can_check.json`：

```json
{
  "Name": "ecu_can_check",
  "Steps": [
    {
      "Name": "唤醒报文",
      "Device": "can_adapter_1",
      "Command": "send_and_receive",
      "Parameters": { "id": "0x12D", "data": ["0x00","0x00","0x0C"] },
      "ExpectedResults": { "mode": "contains", "key": "response", "value": "ACK" }
    },
    {
      "Name": "主驾按摩",
      "Device": "can_adapter_1",
      "Command": "send_and_receive",
      "Parameters": { "id": "0x4C1", "data": ["0x00","0x18","0x80"] },
      "ExpectedResults": { "mode": "contains", "key": "response", "value": "ACK" }
    }
  ]
}
```

`ecu_iv_check.json`：

```json
{
  "Name": "ecu_iv_check",
  "Steps": [
    {
      "Name": "apply_resistance",
      "Device": "resistor_box_1",
      "Command": "set_resistance",
      "Parameters": { "channel": 1, "value": 100 },
      "ExpectedResults": { "mode": "equals", "key": "status", "value": "ok" }
    },
    {
      "Name": "measure_current",
      "Device": "current_meter_1",
      "Command": "measure",
      "Parameters": { "range": "auto" },
      "ExpectedResults": { "mode": "range", "key": "current", "min": 1.0, "max": 2.0 }
    },
    {
      "Name": "measure_voltage",
      "Device": "voltmeter_1",
      "Command": "measure",
      "Parameters": { "range": "auto" },
      "ExpectedResults": { "mode": "tolerance", "key": "voltage", "target": 12.0, "tolerance": 0.5 }
    }
  ]
}
```

---

## 3. 设备加载机制

- 程序启动时，`Devices.json` 被读取。
- 每个条目映射到 `DeviceFactory`，根据 **Type** 生成对应的设备实现类：
  - `power_supply` → `PowerSupplyDevice`
  - `current_meter` → `CurrentMeterDevice`
  - `voltmeter` → `VoltmeterDevice`
  - `resistor_box` → `ResistorBoxDevice`
  - `can_bus` → `CanAdapterDevice`
  - `report_generator` → `ReportGeneratorDevice`
  - `system` → `SystemControlDevice`
- 设备放入设备池，后续流程执行时按 `Device` 名称取出。

---

## 4. 参数传递机制

- **Parameters**：每个步骤的输入参数（如电压值、电阻值、CAN 报文）。
- **Outputs**：设备执行后的输出结果（如测量的电流、电压、报文响应）。
- **ExpectedResults**：期望结果，用于 `ResultEvaluator` 判断是否通过。

执行流程：

1. `StepConfig.Parameters` 传入 `Device.Execute(step, context)`。  
2. 设备返回 `StepResult`，其中包含 `Outputs`。  
3. 调用 `ResultEvaluator.Evaluate(ExpectedResults, Outputs, Parameters, out reason)` 判定是否成功。  
4. 判定失败 → `LastSuccess = false`，并写入数据库。

---

## 5. ResultEvaluator 判定逻辑

支持以下模式：

- **equals**：值等于  
- **not_equals**：值不等于  
- **range**：数值在 `[min,max]` 范围内  
- **tolerance**：数值在 `target ± tolerance` 范围内  
- **contains**：字符串包含  
- **regex**：正则匹配  
- **in_set**：值在集合中  
- **exists**：只要求键存在  
- **gt/ge/lt/le**：数值比较

### 示例

```json
"ExpectedResults": [
  { "mode": "equals", "key": "status", "value": "ok" },
  { "mode": "range", "key": "current", "min": 1.0, "max": 2.0 },
  { "mode": "tolerance", "key": "voltage", "target": 12.0, "tolerance": 0.5 }
]
```

---

## 6. 总结

- **Devices.json** 定义设备种类和连接方式。  
- **型号 JSON** 定义测试流程、步骤、子流程引用。  
- **设备执行**：Parameters → Execute → Outputs。  
- **结果判定**：通过 ResultEvaluator 实现多模式灵活判断。  
- 支持 **子流程复用**，大幅减少重复配置，提高维护性。  
