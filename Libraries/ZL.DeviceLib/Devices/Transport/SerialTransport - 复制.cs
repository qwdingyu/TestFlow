using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Utils;
using ZL.DeviceLib.Events;

namespace ZL.DeviceLib.Devices.Transport
{
    /// <summary>
    /// 串口传输器：支持基本 Send/Receive
    /// </summary>
    public sealed class SerialTransport : ITransport, IHealthyDevice
    {
        private readonly SerialPort _port;
        private DeviceState _state = DeviceState.Unknown;
        private readonly string _connectionString;
        private string _deviceKey;
        public SerialTransport(string connectionString, string deviceKey)
        {
            // 格式： "COM3:9600,N,8,1"
            try
            {
                _connectionString = connectionString;
                var settings = SerialPortParser.Parse(connectionString);
                if (string.IsNullOrEmpty(deviceKey))
                    _deviceKey = _connectionString;
                else
                    _deviceKey = deviceKey + " - " + _connectionString;
                _port = new SerialPort(settings.PortName, settings.BaudRate, settings.Parity, settings.DataBits, settings.StopBits);
                _port.ReadTimeout = 1000;
                _port.WriteTimeout = 1000;
                _port.Open();

                SetState(DeviceState.Connected);
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected);
                DeviceNotifier.DeviceInfoChangedEvent?.Invoke(_deviceKey, ex.Message);
                throw new InvalidOperationException($"{_deviceKey} 串口初始化失败: {ex.Message}", ex);
            }
        }
        private void SetState(DeviceState newState)
        {
            if (_state == newState) return;
            _state = newState;

            // 直接推送全局 Action
            DeviceNotifier.DeviceStateChangedEvent?.Invoke(_deviceKey, newState);
        }

        public bool IsHealthy() => _state == DeviceState.Connected && _port?.IsOpen == true;

        public async Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken t)
        {
            return await Task.Run(() =>
            {
                try
                {
                    _port.Write(data.ToArray(), 0, data.Length);
                    SetState(DeviceState.Connected);
                    return data.Length;
                }
                catch (Exception ex)
                {
                    SetState(DeviceState.Disconnected);
                    throw new IOException($"{_deviceKey}串口发送失败", ex);
                }
            }, t);
        }
        /// <summary>
        /// 接收数据帧
        /// </summary>
        public async Task<IList<ReadOnlyMemory<byte>>> ReceiveAsync(IFrameSplitter splitter, TimeSpan timeout, CancellationToken token, bool keepAllFrames)
        {
            return await Task.Run(() =>
            {
                var results = new List<ReadOnlyMemory<byte>>();
                var buffer = new byte[4096];
                var deadline = DateTime.UtcNow + timeout;

                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    int n = 0;
                    try
                    {
                        _port.ReadTimeout = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                        if (_port.ReadTimeout <= 0) throw new TimeoutException();

                        n = _port.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        return results; // 超时 → 返回已有帧（可能为空）
                    }

                    if (n > 0)
                    {
                        splitter.Append(new ReadOnlySpan<byte>(buffer, 0, n));
                        var frames = splitter.ExtractFrames();
                        foreach (var f in frames)
                        {
                            results.Add(f);
                            if (!keepAllFrames) return results; // 只要一帧就返回
                        }
                    }

                    if (DateTime.UtcNow >= deadline)
                        return results;
                }
            }, token).ConfigureAwait(false);
        }
        public async Task<ReadOnlyMemory<byte>> ReceiveAsync(int expectLen, TimeSpan timeout, CancellationToken t)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var buf = new byte[expectLen > 0 ? expectLen : 4096];
                    _port.ReadTimeout = (int)timeout.TotalMilliseconds;

                    int n = _port.Read(buf, 0, buf.Length);
                    // 没有数据，返回空
                    if (n == 0) { return ReadOnlyMemory<byte>.Empty; }
                    SetState(DeviceState.Connected);
                    return new ReadOnlyMemory<byte>(buf, 0, n);
                }
                catch (TimeoutException)
                {
                    // 串口超时，不算致命错误
                    LogHelper.Warn($"[SerialTransport] {_deviceKey}读取超时");
                    return ReadOnlyMemory<byte>.Empty;
                }
                catch (OperationCanceledException)
                {
                    // 上层主动取消
                    throw;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    SetState(DeviceState.Disconnected);
                    // 串口硬件故障或被占用
                    LogHelper.Error($"[SerialTransport] {_deviceKey}串口异常: {ex.Message}");
                    throw new InvalidOperationException($"{_deviceKey}串口不可用", ex);
                }
            }, t).ConfigureAwait(false);
        }
        //public void StartListening(Action<string> onData, CancellationToken token)
        //{
        //    Task.Run(() =>
        //    {
        //        var buffer = new byte[4096];
        //        var sb = new StringBuilder(); // 用来拼接残留数据

        //        while (!token.IsCancellationRequested)
        //        {
        //            try
        //            {
        //                int n = _port.Read(buffer, 0, buffer.Length);
        //                if (n > 0)
        //                {
        //                    string chunk = Encoding.ASCII.GetString(buffer, 0, n);
        //                    sb.Append(chunk);

        //                    // 以 \r\n 或 \n 分隔
        //                    string data = sb.ToString();
        //                    string[] lines = data.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        //                    // 最后一段可能是不完整的，先不触发
        //                    for (int i = 0; i < lines.Length - 1; i++)
        //                    {
        //                        string line = lines[i].Trim();
        //                        if (!string.IsNullOrEmpty(line))
        //                            onData?.Invoke(line);
        //                    }

        //                    // 保留最后一段残缺的
        //                    sb.Clear();
        //                    sb.Append(lines[lines.Length - 1]);
        //                }
        //            }
        //            catch (TimeoutException)
        //            {
        //                // 忽略超时
        //            }
        //            catch
        //            {
        //                SetState(DeviceState.Disconnected);
        //                break;
        //            }
        //        }
        //    }, token);
        //}
        /// <summary>
        ///  // 文本协议 (SCPI)
        /// StartListening(FrameSplitterFactory.Create(SplitterType.Delimiter, delimiter: (byte)'\n'), frame => Console.WriteLine("Line: " + Encoding.ASCII.GetString(frame)), cts.Token);
        ///  // 定长协议 (固定8字节)
        /// StartListening(FrameSplitterFactory.Create(SplitterType.FixedLength, param: 8), frame => Console.WriteLine("Fixed: " + BitConverter.ToString(frame)), cts.Token);
        ///    // Modbus RTU 协议
        /// StartListening(FrameSplitterFactory.Create(SplitterType.ModbusRtu), frame => Console.WriteLine("Modbus: " + BitConverter.ToString(frame)), cts.Token);
        /// </summary>
        /// <param name="splitter"></param>
        /// <param name="onFrame"></param>
        /// <param name="token"></param>
        public void StartListening(IFrameSplitter splitter, Action<byte[]> onFrame, CancellationToken token)
        {
            Task.Run(() =>
            {
                var buffer = new byte[4096];
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        int n = _port.Read(buffer, 0, buffer.Length);
                        if (n > 0)
                        {
                            splitter.Append(new ReadOnlySpan<byte>(buffer, 0, n));
                            var frames = splitter.ExtractFrames();
                            foreach (var f in frames)
                            {
                                onFrame?.Invoke(f.ToArray());
                            }
                        }
                    }
                    catch (TimeoutException)
                    {
                        // 忽略超时，继续循环
                    }
                    catch
                    {
                        SetState(DeviceState.Disconnected);
                        break;
                    }
                }
            }, token);
        }

        public void StartListening(IFrameSplitter splitter, Action<string> onFrame, CancellationToken token)
        {
            Task.Run(() =>
            {
                var buffer = new byte[4096];
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        int n = _port.Read(buffer, 0, buffer.Length);
                        if (n > 0)
                        {
                            splitter.Append(new ReadOnlySpan<byte>(buffer, 0, n));
                            var frames = splitter.ExtractFrames();
                            foreach (var f in frames)
                            {
                                string chunk = Encoding.ASCII.GetString(f.ToArray());
                                onFrame?.Invoke(chunk);
                            }
                        }
                    }
                    catch (TimeoutException)
                    {
                        // 忽略超时，继续循环
                    }
                    catch
                    {
                        SetState(DeviceState.Disconnected);
                        break;
                    }
                }
            }, token);
        }
        public Task FlushAsync(CancellationToken t)
        {
            return Task.Run(() =>
            {
                try
                {
                    _port.DiscardInBuffer();
                }
                catch (Exception ex)
                {
                    SetState(DeviceState.Disconnected);
                    throw new IOException($"{_deviceKey} 清空串口缓存失败", ex);
                }
            }, t);
        }

        public void Dispose()
        {
            try
            {
                if (_port != null)
                {
                    if (_port.IsOpen) _port.Close();
                    _port.Dispose();
                    SetState(DeviceState.Disconnected);
                }
            }
            catch
            {
                SetState(DeviceState.Disconnected);
            }
        }
    }
}
