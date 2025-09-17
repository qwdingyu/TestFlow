using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Transport;

namespace ZL.DeviceLib.Devices
{
    /// <summary>
    /// 目标：周期恒定、事件不挡路、发送串行且可控
    /// </summary>
    public sealed class CanMessageScheduler : IDisposable
    {
        private readonly ICanTransport _can;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        // 统一发送队列 & 发送线程（串行化所有 _can.Send 调用）
        private readonly BlockingCollection<CanMessage> _sendQueue = new BlockingCollection<CanMessage>(new ConcurrentQueue<CanMessage>(), 10_000);
        private readonly Task _senderTask;

        // 周期定时器集合（key = 报文ID）
        private readonly ConcurrentDictionary<string, Timer> _periodicTimers = new ConcurrentDictionary<string, Timer>();

        // 方便在 stop 时一次性移除
        private readonly object _periodicSetLock = new object();
        private readonly HashSet<string> _periodicSet = new HashSet<string>();

        public CanMessageScheduler(ICanTransport can)
        {
            _can = can ?? throw new ArgumentNullException(nameof(can));
            _senderTask = Task.Run(new Func<Task>(SenderLoopAsync));
        }

        // ========== 周期任务 ==========
        /// <summary>
        /// 新增或更新一个周期任务（不会重复发送：同 ID 覆盖旧 Timer）
        /// </summary>
        public void UpsertPeriodic(string id, byte[] data, int periodMs, bool enabled = true)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            if (data == null) throw new ArgumentNullException(nameof(data));

            var p = Math.Max(10, periodMs);
            // 先移除旧 Timer
            Timer old;
            if (_periodicTimers.TryRemove(id, out old))
            {
                try { old.Dispose(); } catch { }
            }

            if (!enabled) return;

            // 生成要发送的消息模板（每次 Tick 入队，Timestamp 运行时再填）
            var payload = (byte[])data.Clone();

            // Timer 回调：只负责入队，不直接 Send
            Timer timer = null;
            TimerCallback cb = state =>
            {
                if (_cts.IsCancellationRequested) return;
                var msg = new CanMessage
                {
                    Id = id,
                    Data = payload, // 周期数据一般固定；若要动态，可改为委托
                    Timestamp = DateTime.UtcNow
                };
                SafeEnqueue(msg);
            };

            // 立即触发一次（dueTime=0），之后按 period 触发
            timer = new Timer(cb, null, 0, p);
            _periodicTimers[id] = timer;

            lock (_periodicSetLock) { _periodicSet.Add(id); }
        }

        public void EnablePeriodic(string id, bool enabled)
        {
            if (enabled)
            {
                // 无法“复活”已知的 payload，这里只负责停用
                // 如需开/关，外层应持有数据并调用 UpsertPeriodic 以重建
                return;
            }
            RemovePeriodic(id);
        }

        public void RemovePeriodic(string id)
        {
            Timer t;
            if (_periodicTimers.TryRemove(id, out t))
            {
                try { t.Dispose(); } catch { }
            }
            lock (_periodicSetLock) { _periodicSet.Remove(id); }
        }

        // ========== 事件突发 ==========
        /// <summary>
        /// 事件：按 interval 发送 control N 帧，再发送 clear M 帧。返回完成任务。
        /// </summary>
        public Task EnqueueEventBurstAsync(string id, byte[] control, byte[] clear,
                                           int intervalMs, int controlCount, int clearCount)
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
                    // Control N 帧
                    for (int i = 0; i < cc; i++)
                    {
                        if (token.IsCancellationRequested) break;
                        SafeEnqueue(new CanMessage { Id = id, Data = controlCopy, Timestamp = DateTime.UtcNow });
                        await Task.Delay(iv, token).ConfigureAwait(false);
                    }
                    // Clear M 帧
                    if (clearCopy != null)
                    {
                        for (int i = 0; i < ec; i++)
                        {
                            if (token.IsCancellationRequested) break;
                            SafeEnqueue(new CanMessage { Id = id, Data = clearCopy, Timestamp = DateTime.UtcNow });
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
                    CanMessage msg;
                    // TryTake + 超时，让取消能及时生效
                    if (!_sendQueue.TryTake(out msg, 100, token)) continue;

                    try
                    {
                        // 统一在此串行调用底层发送
                        _can.Send(msg);
                    }
                    catch (Exception)
                    {
                        // TODO: 在此记录发送异常日志/计数；必要时可丢弃或重试
                    }

                    await Task.Yield(); // 让出调度，避免长时间占用
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            catch (Exception)
            {
                // TODO: 记录严重异常
            }
        }

        private void SafeEnqueue(CanMessage msg)
        {
            // 队列满时的策略：阻塞/丢弃/超时入队。此处尝试 50ms；失败则可选择丢弃以保周期稳定
            if (!_sendQueue.TryAdd(msg, 50))
            {
                // TODO: 记录丢包日志；对周期/事件可设置不同优先级（扩展：两个队列 + 合并器）
            }
        }

        // ========== 生命周期 ==========
        public void Dispose()
        {
            _cts.Cancel();

            // 停周期间隔器
            foreach (var kv in _periodicTimers)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            _periodicTimers.Clear();

            // 关闭发送队列，等待发送线程退出
            _sendQueue.CompleteAdding();
            try { _senderTask.Wait(1000); } catch { }

            _cts.Dispose();
            _sendQueue.Dispose();
        }

        // ========== 工具：按表格一键启停 ==========
        public void StartSeatEnv100ms()
        {
            // “千万不要搞重复”——Upsert 会先 Dispose 旧 Timer 再新建，天然防重复
            UpsertPeriodic("0x201", Hex("00-00-00-00-80-00-00-00"), 100, true);
            UpsertPeriodic("0x1F1", Hex("20-00-00-00-00-00-00-00"), 100, true);
            UpsertPeriodic("0x17D", Hex("00-00-00-00-00-00-00-00"), 100, true);
            UpsertPeriodic("0x120", Hex("00-00-00-00-01-00-00-00"), 100, true);
        }

        public void StopSeatEnv()
        {
            // 移除本批周期
            RemovePeriodic("0x201");
            RemovePeriodic("0x1F1");
            RemovePeriodic("0x17D");
            RemovePeriodic("0x120");
        }

        // 事件：加热档位 -> 3 控制 + 3 清空，间隔 100ms
        public Task SeatHeaterAsync(string level)
        {
            var ctrl = level == "low" ? Hex("00-00-00-12-00-00-00-00")
                     : level == "mid" ? Hex("00-00-00-24-00-00-00-00")
                     : level == "high" ? Hex("00-00-00-36-00-00-00-00")
                     : level == "off" ? Hex("00-00-00-7E-00-00-00-00")
                     : Hex("00-00-00-00-00-00-00-00");

            var clear = Hex("00-00-00-00-00-00-00-00");
            return EnqueueEventBurstAsync("0x434", ctrl, clear, 100, 3, 3);
        }

        // 简易 HEX 工具
        private static byte[] Hex(string s)
        {
            var parts = s.Split(new[] { '-', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var buf = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                buf[i] = Convert.ToByte(parts[i], 16);
            }
            return buf;
        }
    }

}
