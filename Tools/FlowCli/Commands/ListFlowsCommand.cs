using System;
using System.IO;
using System.Linq;
using ZL.DeviceLib;

namespace Cli.Commands
{
    /// <summary>
    /// 负责实现 list 命令，列出当前输出目录中可用的流程清单。
    /// </summary>
    internal static class ListFlowsCommand
    {
        /// <summary>
        /// 执行流程列表查询，并将结果输出到日志。
        /// </summary>
        /// <returns>命令执行状态码：始终返回 0 表示完成。</returns>
        public static int Execute()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string flowsDir = Path.Combine(baseDir, "Flows");
            if (!Directory.Exists(flowsDir))
            {
                LogHelper.Info("Flows 目录不存在");
                return 0;
            }

            foreach (string file in Directory.GetFiles(flowsDir, "*.json").OrderBy(x => x))
            {
                LogHelper.Info(Path.GetFileNameWithoutExtension(file));
            }

            return 0;
        }
    }
}
