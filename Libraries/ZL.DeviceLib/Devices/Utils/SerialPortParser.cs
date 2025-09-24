using System;
using System.IO.Ports;

namespace ZL.DeviceLib.Devices.Utils
{
    public static class SerialPortParser
    {
        /// <summary>
        /// 解析串口连接字符串（格式："COM3:9600,N,8,1"）
        /// </summary>
        /// <param name="connectionString">串口连接串</param>
        /// <returns>SerialPortSettings 对象</returns>
        public static SerialPortSettings Parse(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("串口连接串不能为空");

            var parts = connectionString.Split(':');
            if (parts.Length < 2)
                throw new ArgumentException("串口连接串格式错误，正确示例：COM3:9600,N,8,1");

            var portName = parts[0];
            var opts = parts[1].Split(',');

            int baud = int.Parse(opts[0]);
            Parity parity = (opts.Length > 1 && opts[1].ToUpper() == "E") ? Parity.Even :
                            (opts.Length > 1 && opts[1].ToUpper() == "O") ? Parity.Odd : Parity.None;
            int dataBits = (opts.Length > 2) ? int.Parse(opts[2]) : 8;
            StopBits stopBits = (opts.Length > 3 && opts[3] == "2") ? StopBits.Two : StopBits.One;

            return new SerialPortSettings
            {
                PortName = portName,
                BaudRate = baud,
                Parity = parity,
                DataBits = dataBits,
                StopBits = stopBits
            };
        }
    }

    /// <summary>
    /// 串口参数封装
    /// </summary>
    public class SerialPortSettings
    {
        public string PortName { get; set; }
        public int BaudRate { get; set; }
        public Parity Parity { get; set; }
        public int DataBits { get; set; }
        public StopBits StopBits { get; set; }
    }
}
