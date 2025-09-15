using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZL.DeviceLib.Devices
{
    public class SerialPortManager : IDisposable
    {
        private readonly SerialPort _port;
        private readonly ConcurrentQueue<string> _rxQueue = new ConcurrentQueue<string>();
        private readonly bool _simulated;

        public bool IsOpen => _simulated || (_port != null && _port.IsOpen);

        public SerialPortManager(string connStr)
        {
            var parts = connStr.Split(':');
            var portName = parts[0];
            var settings = parts.Length > 1 ? parts[1] : "9600,N,8,1";
            var sp = settings.Split(',');

            int baudRate = int.Parse(sp[0]);
            Parity parity = (sp[1] == "E") ? Parity.Even : (sp[1] == "O") ? Parity.Odd : Parity.None;
            int dataBits = int.Parse(sp[2]);
            StopBits stopBits = sp[3] == "2" ? StopBits.Two : StopBits.One;

            bool forceSim = connStr.StartsWith("SIM:", StringComparison.OrdinalIgnoreCase) || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            if (!forceSim)
            {
                try
                {
                    _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
                    _port.DataReceived += OnDataReceived;
                    _port.Open();
                }
                catch
                {
                    _simulated = true;
                }
            }
            else
            {
                _simulated = true;
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                string data = _port.ReadExisting();
                _rxQueue.Enqueue(data);
            }
            catch { }
        }

        public void Send(string cmd)
        {
            if (_simulated)
            {
                // 简单模拟：回传 OK
                _rxQueue.Enqueue("OK");
                return;
            }
            _port.WriteLine(cmd);
        }

        public string WaitForResponse(Func<string, bool> matcher, int timeoutMs, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                if (_rxQueue.TryDequeue(out var msg))
                {
                    if (matcher(msg)) return msg;
                    else HandleUnmatchedMessage(msg);
                }
                Thread.Sleep(10);
            }
            throw new TimeoutException("No matching response");
        }

        private void HandleUnmatchedMessage(string msg) => LogHelper.Info($"[SerialPort-Unmatched] {msg}");

        public void Dispose()
        {
            if (_simulated) return;
            if (_port != null)
            {
                _port.DataReceived -= OnDataReceived;
                if (_port.IsOpen) _port.Close();
                _port.Dispose();
            }
        }
    }
}
