# CLI 使用与本地测试指南
## 1. 环境准备
安装 .NET SDK 7.0（需要兼容 net48 的项目可以额外安装 .NET Framework 4.8 开发包）。

进入仓库根目录，例如：
```shell
cd /path/to/TestFlow
```
## 2. 编译 CLI
```shell
dotnet build Tools/FlowCli/FlowCli.csproj
```
构建后会在 Tools/FlowCli/bin/Debug/net7.0/ 下产生可执行程序，并自动拷贝 Flows/、devices.json、infrastructure.json 等运行所需文件。

## 3. 命令行基本用法
所有命令均可在仓库根目录直接执行，也可先 cd Tools/FlowCli 后省略 --project 参数。

语法格式
```shell
dotnet run --project Tools/FlowCli -- <命令> [参数]
或
cd Tools/FlowCli
dotnet run -- <命令> [参数]
```
## 4. 各命令功能与测试示例

```
命令	功能	测试示例
list	# 列出已复制到输出目录的流程 JSON 文件	dotnet run --project Tools/FlowCli -- list
validate-all	# 校验所有流程与子流程的引用、设备映射是否完整	dotnet run --project Tools/FlowCli -- validate-all
validate <MODEL>	# 校验指定型号流程，<MODEL> 为流程文件名（不带扩展名）	dotnet run --project Tools/FlowCli -- validate E311
schema-validate	# 校验 devices.json、infrastructure.json、Flows/ 和 Flows/Subflows/ 的 JSON 结构是否符合规范	dotnet run --project Tools/FlowCli -- schema-validate
run <BARCODE> [--timeout <sec>]	# 解析条码获取型号并执行完整流程；可指定超时时间（默认 90 秒）	1. dotnet run --project Tools/FlowCli -- run E311-2309010001 2. dotnet run --project Tools/FlowCli -- run E311-TESTSN001 --timeout 120
scaffold-device <TypeName> [--ns <Namespace>] [--out <dir>] # 	在仓库内生成设备模板及注册器，便于快速开发内置设备	dotnet run --project Tools/FlowCli -- scaffold-device MyDevice --ns Custom.Devices --out Libraries/DeviceLib/Devices/Custom
scaffold-plugin <PluginName> [--ns <Namespace>]	# 生成独立驱动插件模板（推荐使用方式），会在 PluginsSrc/<PluginName>/ 下生成项目骨架	dotnet run --project Tools/FlowCli -- scaffold-plugin MyDriver --ns MyCompany.MyDriver
提示

scaffold-plugin 生成的插件项目构建后需将 DLL 放到运行目录的 Plugins/ 下面才能被 CLI 自动加载。

运行 run 命令时会在当前目录生成 test_cli.db 和 Reports/ 等运行时数据。

```

## 5. 测试流程建议
按顺序执行 list → validate-all → schema-validate，确认基础配置完整。

针对某个型号执行 validate <MODEL> 确认单一流程无误。

使用 run <BARCODE> 执行完整流程，并观察日志输出。

如需扩展设备或插件，可分别测试 scaffold-device 与 scaffold-plugin，检查模板是否正确生成。

