using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.DeviceLib.Devices.Can
{
    /// <summary>
    /// 通用 CAN 报文调度器：
    /// - 周期报文：Upsert / Remove
    /// - 事件突发：EnqueueEventBurstAsync
    /// - 串行发送：统一队列
    /// - 健康监控：队列长度、丢包数、任务数
    /// </summary>
    public sealed class CanMessageScheduler : IDisposable
    {
        private readonly ICanTransport _can;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly BlockingCollection<CanMessage> _sendQueue;
        private readonly Task _senderTask;

        // 周期定时器集合
        private readonly ConcurrentDictionary<string, Timer> _periodicTimers = new ConcurrentDictionary<string, Timer>();

        // 健康监控
        private long _sentCount = 0;
        private long _dropCount = 0;

        /// <summary>
        /// 构造调度器
        /// </summary>
        /// <param name="can">底层 CAN 传输接口</param>
        /// <param name="maxQueueSize">队列最大容量，默认 10,000</param>
        public CanMessageScheduler(ICanTransport can, int maxQueueSize = 10000)
        {
            _can = can ?? throw new ArgumentNullException(nameof(can));
            _sendQueue = new BlockingCollection<CanMessage>(new ConcurrentQueue<CanMessage>(), maxQueueSize);
            _senderTask = Task.Run(SenderLoopAsync);
        }

        // ========== 周期任务 ==========
        /// <summary>
        /// 新增或更新一个周期任务（同 ID 覆盖旧任务）
        /// </summary>
        public void UpsertPeriodic(string id, byte[] data, int periodMs, bool enabled = true)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (data == null) throw new ArgumentNullException(nameof(data));

            var p = Math.Max(10, periodMs);

            if (_periodicTimers.TryRemove(id, out var old))
            {
                try { old.Dispose(); } catch { }
            }

            if (!enabled) return;

            var payload = (byte[])data.Clone();

            Timer timer = new Timer(state =>
            {
                if (_cts.IsCancellationRequested) return;
                var msg = new CanMessage { Id = id, Data = payload, Timestamp = DateTime.Now };
                SafeEnqueue(msg, isPeriodic: true);
            }, null, 0, p);

            _periodicTimers[id] = timer;
        }

        /// <summary>
        /// 移除某个周期任务
        /// </summary>
        public void RemovePeriodic(string id)
        {
            if (_periodicTimers.TryRemove(id, out var t))
            {
                try { t.Dispose(); } catch { }
            }
        }

        // ========== 事件突发 ==========
        /// <summary>
        /// 事件突发：按 interval 发送 control N 帧，再发送 clear M 帧
        /// </summary>
        public Task EnqueueEventBurstAsync(string id, byte[] control, byte[] clear, int intervalMs, int controlCount, int clearCount)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (control == null) throw new ArgumentNullException(nameof(control));

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var token = _cts.Token;
            var iv = Math.Max(10, intervalMs);
            var cc = Math.Max(1, controlCount);
            var ec = Math.Max(0, clearCount);
            var controlCopy = (byte[])control.Clone();
            var clearCopy = clear != null ? (byte[])clear.Clone() : null;

            Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < cc; i++)
                    {
                        if (token.IsCancellationRequested) break;
                        SafeEnqueue(new CanMessage { Id = id, Data = controlCopy, Timestamp = DateTime.Now });
                        await Task.Delay(iv, token).ConfigureAwait(false);
                    }
                    if (clearCopy != null)
                    {
                        for (int i = 0; i < ec; i++)
                        {
                            if (token.IsCancellationRequested) break;
                            SafeEnqueue(new CanMessage { Id = id, Data = clearCopy, Timestamp = DateTime.Now });
                            await Task.Delay(iv, token).ConfigureAwait(false);
                        }
                    }
                    tcs.TrySetResult(true);
                }
                catch (OperationCanceledException)
                {
                    tcs.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, token);

            return tcs.Task;
        }

        // ========== 统一发送线程 ==========
        private async Task SenderLoopAsync()
        {
            var token = _cts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!_sendQueue.TryTake(out var msg, 100, token)) continue;

                    try
                    {
                        _can.Send(msg);
                        Interlocked.Increment(ref _sentCount);
                    }
                    catch (Exception ex)
                    {
                        // TODO: 日志记录异常
                        LogHelper.Warn($"[CAN-Send] 发送失败: {ex.Message}");
                    }

                    await Task.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            catch (Exception ex)
            {
                LogHelper.Error($"[CAN-SendLoop] 异常退出: {ex}");
            }
        }

        private void SafeEnqueue(CanMessage msg, bool isPeriodic = false)
        {
            if (!_sendQueue.TryAdd(msg, 50))
            {
                Interlocked.Increment(ref _dropCount);
                // 周期报文可容忍丢弃，事件报文则建议加日志
                if (!isPeriodic)
                {
                    LogHelper.Warn($"[CAN-Drop] 事件报文丢弃: {msg.Id}");
                }
            }
        }

        // ========== 健康监控 ==========
        public SchedulerStats GetStats()
        {
            return new SchedulerStats
            {
                QueueLength = _sendQueue.Count,
                PeriodicTaskCount = _periodicTimers.Count,
                SentCount = Interlocked.Read(ref _sentCount),
                DroppedCount = Interlocked.Read(ref _dropCount),
                IsRunning = !_cts.IsCancellationRequested
            };
        }

        // ========== 生命周期 ==========
        public void Dispose()
        {
            _cts.Cancel();

            foreach (var kv in _periodicTimers)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _periodicTimers.Clear();

            _sendQueue.CompleteAdding();
            try { _senderTask.Wait(1000); } catch { }

            _cts.Dispose();
            _sendQueue.Dispose();
        }
    }

    /// <summary>
    /// 调度器健康状态
    /// </summary>
    public class SchedulerStats
    {
        public int QueueLength { get; set; }
        public int PeriodicTaskCount { get; set; }
        public long SentCount { get; set; }
        public long DroppedCount { get; set; }
        public bool IsRunning { get; set; }
    }
}
