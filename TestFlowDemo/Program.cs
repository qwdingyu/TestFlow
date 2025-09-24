using CsvDataMaintenance;
using System;
using System.IO;
using System.Windows.Forms;
using ZL.DeviceLib;
using ZL.DeviceLib.Example;

namespace TestFlowDemo
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try
            {
                // 初始化日志记录组件，将调试及以上级别的日志写入本地文件，并通过 UDP 转发到日志收集服务
                // 其中 udpHost/udpPort 用于指定外部日志聚合器的地址；若需要切换到其他端口，只需调整 udpPort 并保证接收端监听相同端口
                LogHelper.Init(
                    logFilePath: "logs/app-.txt",
                    minimumLevel: Serilog.Events.LogEventLevel.Debug,
                    udpHost: "127.0.0.1",
                    udpPort: 2012 // UDP 日志转发端口示例：默认对接监听 2012 端口的收集服务，若无需转发可传入 0 关闭
                );

                //Application.EnableVisualStyles();
                //Application.SetCompatibleTextRenderingDefault(false);
                //Application.Run(new MainForm());

                Application.Run(new Frm_SeatTest());

                //Application.Run(new Frm_Csv());


                //SiampleRunner.Run();
                //PlcRunner.Run();

                //NoiseRunner.Run();

                //new SeatTestMain().Run();
            }
            catch (Exception ex)
            {
                // 顶层兜底：记录未处理异常并提醒用户检查日志
                HandleFatalException(ex);
            }
        }

        /// <summary>
        /// 兜底处理应用入口捕获到的未处理异常：优先写入 Serilog 日志，失败时退回本地文件
        /// </summary>
        private static void HandleFatalException(Exception ex)
        {
            bool logged = false;
            try
            {
                LogHelper.Fatal("应用发生未处理异常", ex);
                logged = true;
            }
            catch
            {
                try
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    var logDir = Path.Combine(baseDir, "logs");
                    Directory.CreateDirectory(logDir);
                    var fallbackPath = Path.Combine(logDir, "fatal-unhandled.log");
                    var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 未处理异常捕获：{ex}{Environment.NewLine}";
                    File.AppendAllText(fallbackPath, logLine);
                    logged = true;
                }
                catch
                {
                    // 如果连本地写文件都失败，只能忽略异常避免进一步崩溃
                }
            }

            var message = "程序发生未处理异常，已尝试写入日志文件。" +
                          Environment.NewLine +
                          "错误描述：" + ex.Message +
                          (logged ? Environment.NewLine + "请查看 logs 目录内最新日志获取详细堆栈。" : Environment.NewLine + "日志写入失败，请联系系统管理员。");
            MessageBox.Show(message, "程序错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
