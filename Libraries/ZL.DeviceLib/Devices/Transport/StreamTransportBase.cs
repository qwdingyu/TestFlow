using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.DeviceLib.Devices.Transport
{
    public abstract class StreamTransportBase : ITransport
    {
        private readonly ConcurrentQueue<ReadOnlyMemory<byte>> _pending = new ConcurrentQueue<ReadOnlyMemory<byte>>();
        private volatile bool _disposed;
        protected abstract Task<int> WriteAsync(byte[] data, CancellationToken token);
        /// <summary>
        /// 底层读取一个“块”。返回实际字节数；设定 out timedOut=true 表示读超时（不抛异常，便于统一逻辑）。
        /// </summary>
        protected abstract int ReadChunk(byte[] buffer, int offset, int count, int timeoutMs, out bool timedOut);
        protected abstract void DoFlush();
        public Task FlushAsync(CancellationToken t)
        {
            return Task.Run(() => DoFlush(), t);
        }

        public abstract bool IsHealthy();

        public virtual void Dispose()
        {
            _disposed = true;
        }

        /// <summary>底层写入</summary>
        public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            return await WriteAsync(data.ToArray(), token).ConfigureAwait(false);
        }

        public async Task SendAsync(byte[] data, CancellationToken token)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            await WriteAsync(data, token).ConfigureAwait(false);
        }

        /// <summary>
        /// 通用接收：使用给定 Splitter，支持“保留全部帧 or 只取第一帧”。
        /// </summary>
        public async Task<IList<ReadOnlyMemory<byte>>> ReceiveAsync(IFrameSplitter splitter, TimeSpan timeout, CancellationToken token, bool keepAllFrames)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            return await Task.Run(() =>
            {
                var result = new List<ReadOnlyMemory<byte>>();
                var buf = new byte[4096];
                var deadline = DateTime.UtcNow + timeout;

                // 先吐出 pending 队列里的
                if (!_pending.IsEmpty)
                {
                    if (keepAllFrames)
                    {
                        while (_pending.TryDequeue(out var p)) result.Add(p);
                        if (result.Count > 0) return result;
                    }
                    else if (_pending.TryDequeue(out var one))
                    {
                        result.Add(one);
                        return result;
                    }
                }

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    int waitMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (waitMs <= 0) return result;

                    int n = ReadChunk(buf, 0, buf.Length, waitMs, out bool timedOut);
                    if (timedOut) return result;

                    if (n > 0)
                    {
                        splitter.Append(buf, 0, n);
                        var frames = splitter.ExtractFrames();
                        if (frames != null && frames.Count > 0)
                        {
                            if (keepAllFrames)
                            {
                                result.AddRange(frames);
                            }
                            else
                            {
                                // 取第一帧返回，其余放入 pending 以免丢失
                                result.Add(frames[0]);
                                for (int i = 1; i < frames.Count; i++) _pending.Enqueue(frames[i]);
                                return result;
                            }
                        }
                    }
                }
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// 便捷重载：expectedLen == 0 → 用 DelimiterSplitter；否则用 FixedLengthSplitter(expectedLen)。
        /// </summary>
        public async Task<IList<ReadOnlyMemory<byte>>> ReceiveAsync(int expectedLen, TimeSpan timeout, CancellationToken token, bool keepAllFrames, byte delimiter = (byte)'\n')
        {
            IFrameSplitter splitter = expectedLen > 0 ? (IFrameSplitter)new FixedLengthSplitter(expectedLen) : new DelimiterSplitter(delimiter);
            IList<ReadOnlyMemory<byte>> result = await ReceiveAsync(splitter, timeout, token, keepAllFrames).ConfigureAwait(false);

            return result;
        }

        public async Task<IList<string>> ReceiveStringAsync(int expectedLen, TimeSpan timeout, CancellationToken token, bool keepAllFrames, byte delimiter = 10, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.ASCII;
            var result = new List<string>();

            try
            {
                var frames = await ReceiveAsync(expectedLen, timeout, token, keepAllFrames, delimiter).ConfigureAwait(false);
                foreach (var frame in frames)
                {
                    // 转换为字符串
                    string str = encoding.GetString(frame.ToArray());
                    result.Add(str);
                }
            }
            catch (Exception)
            {
                // 保留原始异常堆栈
                throw;
            }

            return result;
        }

        public async Task<IList<string>> ReceiveStringAsync(IFrameSplitter splitter, TimeSpan timeout, CancellationToken token, bool keepAllFrames, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.ASCII;
            var result = new List<string>();

            try
            {
                var frames = await ReceiveAsync(splitter, timeout, token, keepAllFrames).ConfigureAwait(false);
                foreach (var frame in frames)
                {
                    string str = encoding.GetString(frame.ToArray());
                    result.Add(str);
                }
            }
            catch (Exception)
            {
                throw;
            }

            return result;
        }

        /// <summary>
        /// 监听模式：持续接收并逐帧回调。适合推流型设备。
        /// </summary>
        public void StartListening(IFrameSplitter splitter, Action<byte[]> onFrame, CancellationToken token, Action<Exception> onError = null)
        {
            var buf = new byte[4096];

            Task.Run(() =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        bool timedOut;
                        int n = ReadChunk(buf, 0, buf.Length, 2000, out timedOut);
                        if (timedOut) continue; // 超时不break，持续监听

                        if (n > 0)
                        {
                            splitter.Append(buf, 0, n);
                            var frames = splitter.ExtractFrames();
                            for (int i = 0; i < frames.Count; i++)
                                onFrame?.Invoke(frames[i].ToArray());
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (onError != null) onError(ex);
                }
            }, token);
        }

    }
}
