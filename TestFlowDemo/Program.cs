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
            // ��ʼ��ʱ����UDPת��
            LogHelper.Init(
                logFilePath: "logs/app-.txt",
                minimumLevel: Serilog.Events.LogEventLevel.Debug,
                udpHost: "127.0.0.1",
                udpPort: 2012 // ���� syslog/�Զ���ƽ̨�˿�
            );
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
