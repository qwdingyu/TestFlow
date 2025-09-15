```

快速运行

  - 列出可用流程
      - dotnet run --project Tools/FlowCli -- list
  - 校验全部流程与子流程引用、设备映射
      - dotnet run --project Tools/FlowCli -- validate-all
  - 校验单一型号（例如 E311）
      - dotnet run --project Tools/FlowCli -- validate E311

  切到子目录运行（等价）

  - cd Tools/FlowCli
  - dotnet run -- list
  - dotnet run -- validate-all
  - dotnet run -- validate E311


dotnet run --project Tools/FlowCli -- run E311-2309010001

dotnet run --project Tools/FlowCli -- run E311-TESTSN001 --timeout 120


脚手架与模板（支持独立驱动库）

  - 新增 CLI 命令
      - 生成独立驱动库模板
      - dotnet run --project Tools/FlowCli -- scaffold-plugin MyDriver --ns MyCompany.MyDriver
      - 生成位置：PluginsSrc/MyDriver/
        - MyDriver.csproj（TargetFrameworks: net48;net7.0，引用 DeviceLib）
        - MyDriverDevice.cs：示例设备类，已带 [DeviceType("MyDriver")]
        - MyDriverRegistrar.cs：集中注册器（可选，使用 [DeviceType] 也可）
      - 构建后，将生成的 MyDriver.dll 拷贝到 程序目录/Plugins 即可被自动加载。
  - 生成内置设备模板（保留）
      - dotnet run --project Tools/FlowCli -- scaffold-device MyDevice
      - 仅在仓库内生成模板类，不建议你长期使用此方法；推荐使用独立插件库（上述 scaffold-plugin）。



     dotnet build /Users/dingyuwang/Seafile/2-项目代码/0-座椅电检/TestFlowDemo/PluginsSrc/MyDriver/MyDriver.csproj
```
