using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ZL.DeviceLib;
using ZL.DeviceLib.Models;

namespace Cli.Commands
{
    /// <summary>
    ///     build-flow 命令的实现类，负责解析命令行参数并触发模板流程的构建。
    /// </summary>
    internal static class BuildFlowCommand
    {
        /// <summary>
        ///     入口方法：解析 build-flow 命令并执行相应逻辑。
        /// </summary>
        /// <param name="args">来自 Program.Main 的原始参数数组。</param>
        /// <returns>0 表示成功，非 0 表示发生错误。</returns>
        public static int Run(string[] args)
        {
            // 由于 Program 会将命令本身作为第一个参数传入，这里至少需要 3 个参数（命令名、型号、一个测试编号）
            if (args == null || args.Length < 3)
            {
                Console.Error.WriteLine("用法: build-flow <MODEL> <TEST_ID...> [--out <路径>]");
                return 2;
            }

            // 第二个参数即型号信息，后续会传递给模板构建器
            string model = args[1];

            // testIds 用于收集所有测试编号，outputPath 存储可选的输出文件路径
            var testIds = new List<string>();
            string outputPath = null;

            // 从第三个参数开始遍历，识别 --out 选项并收集剩余的测试编号
            for (int i = 2; i < args.Length; i++)
            {
                string current = args[i];

                // 一旦遇到 --out 选项，则读取下一个参数作为输出路径
                if (string.Equals(current, "--out", StringComparison.OrdinalIgnoreCase))
                {
                    // --out 之后必须跟随路径，否则视为参数错误
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("参数 --out 需要紧跟输出路径。");
                        return 2;
                    }

                    outputPath = args[i + 1];
                    i++; // 跳过已消费的路径参数
                    continue;
                }

                // 普通位置参数视为测试编号，保存到列表中
                testIds.Add(current);
            }

            // 如果未收集到任何测试编号，同样视为参数错误
            if (testIds.Count == 0)
            {
                Console.Error.WriteLine("至少需要指定一个测试编号。用法: build-flow <MODEL> <TEST_ID...> [--out <路径>]");
                return 2;
            }

            // 调用模板构建器根据型号与测试编号生成流程配置
            FlowConfig flowConfig = TemplateFlowBuilder.BuildFlow(model, testIds);

            // 使用 Newtonsoft.Json 将流程配置转换为 JSON 字符串，便于后续输出或写入文件
            string json = JsonConvert.SerializeObject(flowConfig, Formatting.Indented);

            // 如果指定了输出路径，则确保目录存在并写入文件，否则直接输出到控制台
            if (!string.IsNullOrEmpty(outputPath))
            {
                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(outputPath, json);
                LogHelper.Info("流程模板已写入文件: " + outputPath);
            }
            else
            {
                Console.WriteLine(json);
            }

            // 返回 0 表示执行成功
            return 0;
        }
    }
}
