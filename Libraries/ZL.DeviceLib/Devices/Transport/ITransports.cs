using System;
using System.Threading;

namespace ZL.DeviceLib.Devices.Transport
{
    // 文本串口传输抽象
    public interface ISerialTextTransport
    {
        bool IsConnected { get; }
        void Send(string cmd);
        string WaitForResponse(Func<string, bool> matcher, int timeoutMs, CancellationToken token);
    }

    // CAN 传输抽象
    public interface ICanTransport
    {
        void Send(CanMessage msg);
        CanMessage WaitForResponse(Func<CanMessage, bool> matcher, int timeoutMs, CancellationToken token);

        void SetFilter(Func<CanMessage, bool> filter);
    }
}

