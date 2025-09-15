using System;
using System.IO;

namespace ZL.DeviceLib.Storage
{
    public static class DbPathUtil
    {
        // 将 sqlite 连接配置解析为绝对文件路径。
        // 规则：
        // - null/"local-sqlite" -> baseDir + fallback
        // - "sqlite://<path>" -> <path>
        // - 以 .db 结尾的路径 -> 原样（若非绝对，则拼 baseDir）
        // 任何异常 -> baseDir + fallback
        public static string ResolveSqlitePath(string connectionString, string fallback, string baseDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrWhiteSpace(fallback)) fallback = "test.db";
                string BaseJoinPath(string rel) => Path.GetFullPath(Path.Combine(baseDir, rel));

                if (string.IsNullOrWhiteSpace(connectionString) || connectionString == "local-sqlite")
                    return BaseJoinPath(fallback);

                string path = null;
                if (connectionString.StartsWith("sqlite://", StringComparison.OrdinalIgnoreCase))
                    path = connectionString.Substring("sqlite://".Length);
                else if (connectionString.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                    path = connectionString;

                if (string.IsNullOrWhiteSpace(path)) return BaseJoinPath(fallback);
                if (!Path.IsPathRooted(path)) path = BaseJoinPath(path);
                return path;
            }
            catch { return Path.GetFullPath(Path.Combine(baseDir ?? AppDomain.CurrentDomain.BaseDirectory, fallback ?? "test.db")); }
        }
    }
}
