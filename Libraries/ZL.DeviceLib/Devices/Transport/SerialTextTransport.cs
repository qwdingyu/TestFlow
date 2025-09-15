using System;
using System.Threading;

namespace ZL.DeviceLib.Devices.Transport
{
    public sealed class SerialTextTransport : ISerialTextTransport, IDisposable
    {
        private readonly SerialPortManager _spm;

        public SerialTextTransport(string connectionString)
        {
            _spm = new SerialPortManager(connectionString);
        }

        public bool IsConnected => _spm.IsOpen;
        public void Send(string cmd) => _spm.Send(cmd);
        public string WaitForResponse(Func<string, bool> matcher, int timeoutMs, CancellationToken token)
            => _spm.WaitForResponse(matcher, timeoutMs, token);

        public void Dispose() => _spm?.Dispose();
    }
}

