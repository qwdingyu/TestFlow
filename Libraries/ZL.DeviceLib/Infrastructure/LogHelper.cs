using Serilog;
using Serilog.Events;
using System;
using System.Net.Sockets;
using System.Text;

//Install-Package Serilog
// Install-Package Serilog.Sinks.Console
// Install-Package Serilog.Sinks.File
/*
// 初始化时启用UDP转发
        LogHelper.Init(
            logFilePath: "logs/app-.log",
            minimumLevel: Serilog.Events.LogEventLevel.Debug,
            udpHost: "192.168.1.200", // UDP 日志聚合服务器地址
            udpPort: 514 // UDP 日志端口示例：需与收集端监听端口保持一致，可按现场调整
        );

        LogHelper.Info("系统启动完成");
        LogHelper.Error("执行时出现错误", new Exception("测试异常"));
        LogHelper.SendMessage("关键步骤：完成数据下发");
*/
namespace ZL.DeviceLib
{
    public static class LogHelper
    {
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        // UDP配置：当同时指定 udpHost/udpPort 时，会额外通过 UDP 将日志推送到集中平台
        private static bool _udpEnabled = false;
        private static string _udpHost;
        private static int _udpPort; // UDP 日志发送端口，必须与日志聚合器监听端口一致
        private static UdpClient _udpClient;

        /// <summary>
        /// 初始化日志系统并可选启用 UDP 转发能力。
        /// </summary>
        /// <param name="logFilePath">本地滚动日志文件的输出路径模板。</param>
        /// <param name="minimumLevel">写入日志的最低级别。</param>
        /// <param name="udpHost">UDP 日志收集器地址，传 null 或空字符串表示关闭。</param>
        /// <param name="udpPort">UDP 日志端口，需与收集器监听端口一致；传 0 可完全禁用 UDP。</param>
        public static void Init(
            string logFilePath = "logs/log-.txt",
            LogEventLevel minimumLevel = LogEventLevel.Information,
            string udpHost = null,
            int udpPort = 0)
        {
            if (_isInitialized) return;

            lock (_lock)
            {
                if (_isInitialized) return;

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Is(minimumLevel)
                    .WriteTo.Console()
                    .WriteTo.File(
                        logFilePath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 10,
                        encoding: Encoding.UTF8)
                    .CreateLogger();

                if (!string.IsNullOrWhiteSpace(udpHost) && udpPort > 0)
                {
                    // 配置 UDP 转发：常用于对接如 ELK、syslog 等集中日志平台
                    _udpEnabled = true;
                    _udpHost = udpHost;
                    _udpPort = udpPort;
                    _udpClient = new UdpClient();
                }

                _isInitialized = true;
            }
        }

        public static void Info(string message)
        {
            EnsureInit();
            Log.Information(message);
            SendUdpIfEnabled("INFO", message);
        }

        public static void Debug(string message)
        {
            EnsureInit();
            Log.Debug(message);
            SendUdpIfEnabled("DEBUG", message);
        }

        public static void Warn(string message)
        {
            EnsureInit();
            Log.Warning(message);
            SendUdpIfEnabled("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            EnsureInit();
            if (ex != null)
            {
                Log.Error(ex, message);
                SendUdpIfEnabled("ERROR", $"{message} | {ex.Message}");
            }
            else
            {
                Log.Error(message);
                SendUdpIfEnabled("ERROR", message);
            }
        }

        public static void Fatal(string message, Exception ex = null)
        {
            EnsureInit();
            if (ex != null)
            {
                Log.Fatal(ex, message);
                SendUdpIfEnabled("FATAL", $"{message} | {ex.Message}");
            }
            else
            {
                Log.Fatal(message);
                SendUdpIfEnabled("FATAL", message);
            }
        }

        /// <summary>
        /// 用于关键日志的主动消息发送
        /// </summary>
        public static void SendMessage(string message)
        {
            EnsureInit();
            Log.Information("SendMessage: {Msg}", message);
            SendUdpIfEnabled("MESSAGE", message);
        }

        private static void EnsureInit()
        {
            if (!_isInitialized)
            {
                Init();
            }
        }

        private static void SendUdpIfEnabled(string level, string message)
        {
            if (!_udpEnabled) return;

            try
            {
                //string payload = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
                string payload = $"[{level}] {message}";
                //byte[] data = Encoding.UTF8.GetBytes(payload);
                byte[] data = Encoding.Default.GetBytes(payload);
                _udpClient.Send(data, data.Length, _udpHost, _udpPort);
            }
            catch (Exception ex)
            {
                Log.Warning("UDP发送失败: {Err}", ex.Message);
            }
        }
    }
}
