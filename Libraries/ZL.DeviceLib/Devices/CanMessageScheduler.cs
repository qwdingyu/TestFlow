using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Transport;

namespace ZL.DeviceLib.Devices
{
    public sealed class CanMessageScheduler : IDisposable
    {
        private readonly ICanTransport _can;   // 复用你的传输
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<string, PeriodicJob> _periodics = new ConcurrentDictionary<string, PeriodicJob>();
        private readonly ConcurrentQueue<EventBurst> _events = new ConcurrentQueue<EventBurst>();
        private readonly Task _loopTask;

        public CanMessageScheduler(ICanTransport can)
        {
            _can = can;
            _loopTask = Task.Run(LoopAsync);
        }

        // ===== 周期任务 =====
        public void UpsertPeriodic(string id, byte[] data, int periodMs, bool enabled = true)
        {
            _periodics[id] = new PeriodicJob
            {
                Id = id,
                Data = data,
                PeriodMs = Math.Max(10, periodMs),
                Enabled = enabled,
                NextDue = DateTime.UtcNow
            };
        }

        public void EnablePeriodic(string id, bool enabled)
        {
            PeriodicJob job; if (_periodics.TryGetValue(id, out job)) job.Enabled = enabled;
        }

        public void RemovePeriodic(string id) => _periodics.TryRemove(id, out _);

        // ===== 事件突发 =====
        public Task EnqueueEventBurstAsync(string id, byte[] control, byte[] clear, int intervalMs, int controlCount = 3, int clearCount = 3)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _events.Enqueue(new EventBurst
            {
                Id = id,
                Control = control,
                Clear = clear,
                IntervalMs = Math.Max(10, intervalMs),
                ControlCount = Math.Max(1, controlCount),
                ClearCount = Math.Max(0, clearCount),
                Tcs = tcs
            });
            return tcs.Task;
        }

        // ===== 主循环 =====
        private async Task LoopAsync()
        {
            var token = _cts.Token;

            while (!token.IsCancellationRequested)
            {
                // 1) 先处理事件（突发优先）
                EventBurst burst;
                if (_events.TryDequeue(out burst))
                {
                    try
                    {
                        for (int i = 0; i < burst.ControlCount && !token.IsCancellationRequested; i++)
                        {
                            _can.Send(new CanMessage { Id = burst.Id, Data = burst.Control, Timestamp = DateTime.Now });
                            await Task.Delay(burst.IntervalMs, token);
                        }
                        if (burst.Clear != null)
                        {
                            for (int i = 0; i < burst.ClearCount && !token.IsCancellationRequested; i++)
                            {
                                _can.Send(new CanMessage { Id = burst.Id, Data = burst.Clear, Timestamp = DateTime.Now });
                                await Task.Delay(burst.IntervalMs, token);
                            }
                        }
                        burst.Tcs.TrySetResult(true);
                    }
                    catch (Exception ex)
                    {
                        burst.Tcs.TrySetException(ex);
                    }
                    continue;
                }

                // 2) 周期任务（到期就发一次）
                var now = DateTime.UtcNow;
                foreach (var kv in _periodics)
                {
                    var job = kv.Value;
                    if (!job.Enabled) continue;
                    if (now >= job.NextDue)
                    {
                        _can.Send(new CanMessage { Id = job.Id, Data = job.Data, Timestamp = DateTime.Now });
                        job.NextDue = now.AddMilliseconds(job.PeriodMs);
                    }
                }

                // 3) 微睡眠（避免CPU忙等；时序精度100ms场景足够）
                await Task.Delay(5, token);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _loopTask.Wait(200); } catch { }
        }

        private sealed class PeriodicJob
        {
            public string Id;
            public byte[] Data;
            public int PeriodMs;
            public bool Enabled;
            public DateTime NextDue;
        }

        private sealed class EventBurst
        {
            public string Id;
            public byte[] Control;
            public byte[] Clear;
            public int IntervalMs;
            public int ControlCount;
            public int ClearCount;
            public TaskCompletionSource<bool> Tcs;
        }
    }

}
