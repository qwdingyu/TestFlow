using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.DeviceLib.Devices.Can
{

    // CAN 传输抽象--底层传输
    public interface ICanTransport
    {
        void Send(CanMessage msg);
        CanMessage WaitForResponse(Func<CanMessage, bool> matcher, int timeoutMs, CancellationToken token);

        void SetFilter(Func<CanMessage, bool> filter);
    }
    // 调度器：周期 & 事件
    public interface ICanScheduler : IDisposable
    {
        void UpsertPeriodic(string id, byte[] data, int periodMs, bool enabled);
        void RemovePeriodic(string id);
        Task EnqueueEventBurstAsync(string id, byte[] control, byte[] clear, int intervalMs, int controlCount, int clearCount);
    }
}
