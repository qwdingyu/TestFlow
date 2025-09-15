using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.DeviceLib.Devices
{
    public class CanMessage
    {
        public string Id { get; set; }
        public byte[] Data { get; set; }
        public DateTime Timestamp { get; set; }
        public override string ToString() => $"ID={Id}, DATA={BitConverter.ToString(Data)}";
    }

    public class CanBusManager : IDisposable
    {
        private readonly string _channel;
        private readonly BlockingCollection<CanMessage> _rxQueue = new BlockingCollection<CanMessage>(new ConcurrentQueue<CanMessage>());
        private bool _running;
        private Task _rxTask;
        private Func<CanMessage, bool> _acceptFilter;

        public CanBusManager(string connectionString)
        {
            _channel = connectionString;
            //StartReceiveLoop();
        }

        public void SetFilter(Func<CanMessage, bool> filter)
        {
            _acceptFilter = filter;
        }

        private void StartReceiveLoop()
        {
            _running = true;
            _rxTask = Task.Run(async () =>
            {
                var rnd = new Random();
                while (_running)
                {
                    try
                    {
                        await Task.Delay(500);
                        var msg = new CanMessage
                        {
                            Id = "0x12D",
                            Data = new byte[] { 0x00, 0x0C, 0x00, 0x00 },
                            Timestamp = DateTime.Now
                        };
                        OnReceive(msg);
                    }
                    catch { }
                }
            });
        }

        private void OnReceive(CanMessage msg)
        {
            if (_acceptFilter != null && !_acceptFilter(msg))
            {
                LogHelper.Info($"[CAN-Ignored] {msg}");
                return;
            }
            _rxQueue.Add(msg);
        }
        public void Send(CanMessage msg)
        {
            LogHelper.Info($"[CAN-Send] {msg}");
            // 为了测试稳定性：回显型应答，确保等待同一 ID 时能命中
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100);
                    OnReceive(new CanMessage
                    {
                        Id = msg.Id,
                        Data = (byte[])(msg.Data?.Clone() ?? Array.Empty<byte>()),
                        Timestamp = DateTime.Now
                    });
                }
                catch { }
            });
        }

        public CanMessage WaitForResponse(Func<CanMessage, bool> matcher, int timeoutMs, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();
                if (_rxQueue.TryTake(out var msg, 50))
                {
                    var isMatch = matcher(msg);
                    LogHelper.Info($"CAN -- 接受信息：{msg}； 匹配结果【{isMatch}】");
                    if (isMatch) return msg;
                    else HandleUnmatchedMessage(msg);
                }
            }
            throw new TimeoutException("No matching CAN response");
        }

        private void HandleUnmatchedMessage(CanMessage msg) => LogHelper.Info($"[CAN-Unmatched] {msg}");

        public void Dispose()
        {
            _running = false;
            _rxTask?.Wait(500);
            _rxQueue.Dispose();
        }
    }
}
