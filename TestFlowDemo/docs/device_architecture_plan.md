# 汽车电检设备管理与测试流程架构改进方案

## 📌 用户诉求

1. **设备多样性**

   - 当前系统涉及多种设备与接口：COM 串口、CAN、Modbus、TCP、UDP 等。
   - 需要一个统一的抽象层，减少冗余代码。

2. **配置驱动**

   - 设备与服务的清单通过 JSON 文件维护。
   - 希望通过配置文件即可新增/修改设备，而无需改动主程序。

3. **流程驱动**

   - 测试流程需要通过 JSON 定义，动态映射到设备，发送命令并验证响应。
   - 要求支持依赖关系、子流程、成功/失败分支。

4. **长期演进**

   - 系统需具备良好的扩展性：未来可能新增 10+ 设备类型和协议。
   - 插件化（DLL 动态加载），避免修改主程序。

5. **项目需要架构需要重新规划**

   - 系统需具备良好的分层或分项目（作为单独的 Libary，供使用方使用）。
   - 工作流一个 Libary
   - 设备层一个 Libary

6. **技术选型**
   - 保留现在已有的 WorkflowCore 3.15 版本作为工作流的内核，在此基础之上扩展应用的宗旨。
   - 单独的 Libary，请兼容 .net framework 和 .net core 版本；LangVersion 使用 7.0 兼容的语法；
   - 测试部分保留现有 winform + sqlite 数据库进行测试；

---

## ⚠️ 目前存在的问题

1. **Devices.json 语义不清晰**

   - 混合了「物理设备」和「环境服务」（database、report_generator、system）。
   - 不利于区分职责。

2. **工厂类 CreateInternal 过度硬编码**

   - 使用大量 `if/else` 来区分设备类型。
   - 随着设备增多，维护成本高，扩展性差。

3. **设备与协议耦合**

   - 不同协议的通讯逻辑（COM/CAN/TCP/UDP）直接写在设备类里。
   - 设备无法复用到不同的通讯方式。

4. **流程与设备绑定紧密**
   - 测试步骤中的 `Device` 硬编码为具体实例名。
   - 缺少更灵活的映射机制。

---

## ✅ 改进方案

> 范围收敛（小而美）：短期不引入 DLL 扫描与跨进程服务；优先用“注册表 + 单体可执行”的方式达成解耦与可扩展，待设备类型显著增长后再评估插件化。

### 1. 文件组织优化

拆分配置文件：

- **infrastructure.json**  
  存放数据库、报告生成器、系统服务等非物理设备。
- **devices.json**  
  存放实际的硬件/虚拟设备及其协议配置。
- **flows.json（一种型号一个 json 文件）**  
  存放测试流程定义。

### 2. 传输层抽象 ITransport

定义统一的通讯接口：

```csharp
public interface ITransport : IDisposable
{
    void Connect();
    void Disconnect();
    void Send(string message);
    string Receive(Func<string, bool> predicate, int timeout, CancellationToken token);
    bool IsConnected { get; }
}
```

- 每种协议（COM、TCP、UDP、CAN、Modbus）实现一个 `ITransport` 子类。
- 设备类不关心底层通讯方式，只负责业务命令。

### 3. 设备抽象 DeviceBase

```csharp
public abstract class DeviceBase : IDevice
{
    protected readonly ITransport _transport;

    protected DeviceBase(ITransport transport) => _transport = transport;

    public DeviceExecResult Execute(StepConfig step, StepContext ctx)
    {
        var outputs = new Dictionary<string, object>();
        try { return HandleCommand(step, ctx, outputs, ctx.Cancellation); }
        catch (Exception ex) { return new DeviceExecResult { Success = false, Message = ex.Message, Outputs = outputs }; }
    }

    protected abstract DeviceExecResult HandleCommand(StepConfig step, StepContext ctx, Dictionary<string, object> outputs, CancellationToken token);
}
```

子类只需要实现具体命令逻辑。

### 4. 工厂模式改造（注册表 + 插件化）

使用注册表或反射替代 if/else：

```csharp
registry.Register("resistor_box", cfg => new ResistorBoxDevice(cfg));
registry.Register("voltmeter", cfg => new VoltmeterDevice(cfg));
```

- 未来新增设备只需写类 + 注册，不需修改主程序。
- 已实现插件化：`Plugins/*.dll` 自动扫描，支持 `IDeviceRegistrar` 与 `[DeviceType]` 两种注册方式；可在不改主程序的情况下新增/升级驱动。

### 5. 流程引擎设计

- 支持 **子流程 (SubFlow)**、**条件分支 (OnSuccess/OnFailure)**、**并行/循环**。
- 执行时由 `FlowEngine` 调度，设备由 `DeviceManager` 提供。

### 6. 命名优化

- `Devices.json` → **devices.json**
- `infrastructure.json` → 环境服务配置
- `flows.json` → 测试流程定义
- `DeviceFactoryRegistry` → 插件化工厂注册表

---

## 🎯 最终目标

1. **配置驱动**

   - 新增设备/协议只需修改 JSON 和增加对应实现类。

2. **解耦设计**

   - 协议（Transport）与设备（Device）完全分离。

3. **插件化扩展**

   - 新设备 DLL 可直接加载，无需改动主程序。

4. **流程灵活性**

   - JSON 定义测试流程，支持依赖、分支、子流程。

5. **统一管理**
   - `DeviceManager`：负责设备生命周期管理。
   - `FlowEngine`：负责测试流程执行。
   - 清晰分层，职责单一，易于维护和扩展。

---

## 📊 架构图

```
+--------------------------------------------------+
|                  FlowEngine                      |
|             (流程驱动执行引擎)                    |
+--------------------------------------------------+
                     |
                     v
+--------------------------------------------------+
|                  DeviceManager                   |
|        (设备/服务加载与生命周期管理)              |
+--------------------------------------------------+
     |                        |
     v                        v
+------------+         +---------------+
|  IDevice   |         | Infrastructure|
| (物理设备) |         | (DB/Report..) |
+------------+         +---------------+
     |
     v
+-------------------+
|   ITransport      |
| (协议: COM/TCP..) |
+-------------------+
```

---

## 🚀 总结

- **短期改进**：拆分配置文件、引入 ITransport、重构工厂为注册表。
- **中期改进**：引入 FlowEngine，支持子流程、条件分支。
- **长期目标**：插件化、配置驱动，构建可扩展的测试执行平台。
