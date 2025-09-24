using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Events;

namespace ZL.DeviceLib.Devices.Transport
{
    /// <summary>
    /// 串口传输器：支持基本 Send/Receive
    /// </summary>
    public sealed class SerialTransport : TransportBaseWithState
    {
        private readonly SerialPort _port;
        public SerialTransport(string connectionString, string deviceKey) : base(string.IsNullOrEmpty(deviceKey) ? connectionString : deviceKey + " - " + connectionString)
        {
            // 格式： "COM3:9600,N,8,1"
            try
            {
                var settings = SerialPortParser.Parse(connectionString);
                _port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits);
                _port.ReadTimeout = 1000;
                _port.WriteTimeout = 1000;
                _port.Open();

                SetState(DeviceState.Connected);
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, ex.Message);
                throw new InvalidOperationException($"{_deviceKey} 串口初始化失败: {ex.Message}", ex);
            }
        }
        protected override Task<int> WriteAsync(byte[] data, CancellationToken token)
        {
            return Task.Run(() =>
            {
                try
                {
                    _port.Write(data, 0, data.Length);
                    SetState(DeviceState.Connected);
                    return data.Length;
                }
                catch (Exception ex)
                {
                    SetState(DeviceState.Disconnected, "串口写入失败: " + ex.Message);
                    throw;
                }
            }, token);
        }
        protected override int ReadChunk(byte[] buffer, int offset, int count, int timeoutMs, out bool timedOut)
        {
            timedOut = false;
            try
            {
                _port.ReadTimeout = timeoutMs <= 0 ? 1 : timeoutMs;
                return _port.Read(buffer, offset, count);
            }
            catch (TimeoutException)
            {
                timedOut = true;
                return 0;
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "串口读取异常: " + ex.Message);
                throw;
            }
        }

        protected override void DoFlush()
        {
            _port.DiscardInBuffer();
        }

        public override void Dispose()
        {
            base.Dispose();
            try
            {
                if (_port != null)
                {
                    if (_port.IsOpen) _port.Close();
                    _port.Dispose();
                }
            }
            catch { }
        }
    }
}
