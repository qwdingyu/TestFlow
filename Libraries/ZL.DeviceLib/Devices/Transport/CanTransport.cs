using System;
using System.Threading;

namespace ZL.DeviceLib.Devices.Transport
{
    public sealed class CanTransport : ICanTransport
    {
        private readonly CanBusManager _mgr;
        public CanTransport(string connectionString) => _mgr = new CanBusManager(connectionString);
        public void Send(CanMessage msg) => _mgr.Send(msg);
        public CanMessage WaitForResponse(Func<CanMessage, bool> matcher, int timeoutMs, CancellationToken token)
            => _mgr.WaitForResponse(matcher, timeoutMs, token);

        public void SetFilter(Func<CanMessage, bool> filter) => _mgr.SetFilter(filter);
    }
}

