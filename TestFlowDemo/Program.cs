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
            // 初始化时启用UDP转发
            LogHelper.Init(
                logFilePath: "logs/app-.txt",
                minimumLevel: Serilog.Events.LogEventLevel.Debug,
                udpHost: "127.0.0.1",
                udpPort: 2012 // 例如 syslog/自定义平台端口
            );
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
