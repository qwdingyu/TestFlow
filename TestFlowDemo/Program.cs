using System;
using System.Windows.Forms;
using ZL.DeviceLib;

namespace TestFlowDemo
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // 初始化日志记录组件，将调试及以上级别的日志写入本地文件，并通过 UDP 转发到日志收集服务
            LogHelper.Init(
                logFilePath: "logs/app-.txt",
                minimumLevel: Serilog.Events.LogEventLevel.Debug,
                udpHost: "127.0.0.1",
                udpPort: 2012 // UDP 日志发送端口，默认对接监听 2012 端口的本地日志接收程序
            );
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
