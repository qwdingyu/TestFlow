using System;
using System.IO;

namespace Cli.Commands
{
    /// <summary>
    /// 提供命令模块常用的辅助功能，例如查找仓库根目录，方便其他命令重复使用。
    /// </summary>
    internal static class CommandHelper
    {
        /// <summary>
        /// 查找当前运行目录所属的代码仓库根目录，优先匹配包含 Libraries 与 Tools 的目录结构。
        /// </summary>
        /// <returns>返回推测到的仓库根路径；若未找到，则退回当前应用程序的基础目录。</returns>
        public static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "sln"))
                    || (Directory.Exists(Path.Combine(dir, "Libraries")) && Directory.Exists(Path.Combine(dir, "Tools"))))
                {
                    return dir;
                }

                var parent = Directory.GetParent(dir);
                if (parent == null)
                {
                    break;
                }

                dir = parent.FullName;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
