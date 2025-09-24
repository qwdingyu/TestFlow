using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Events;

namespace ZL.DeviceLib.Devices.Transport
{
    /*
     * // TCP 示例
var tcpTransport = new NetworkTransport("192.168.1.100", 502, "PLC-TCP");
await tcpTransport.SendAsync(Encoding.ASCII.GetBytes("Hello"), CancellationToken.None);
var frames = await tcpTransport.ReceiveAsync(0, TimeSpan.FromSeconds(2), CancellationToken.None, false);

// UDP 示例
var udpTransport = new NetworkTransport("0.0.0.0", 1500, "192.168.1.200", 1501, "Sensor-UDP");
udpTransport.StartListening(
    FrameSplitterFactory.Create(SplitterType.Delimiter, 0, (byte)'\n'),
    frameBytes => Console.WriteLine("Recv: " + Encoding.ASCII.GetString(frameBytes)),
    CancellationToken.None
);
*/
    /// <summary>
    /// 基于 Socket 的网络传输器 (支持 TCP/UDP)，继承 TransportBaseWithState
    /// </summary>
    public sealed class NetworkTransport : TransportBaseWithState
    {
        private readonly Socket _socket;
        private readonly bool _isUdp;
        private EndPoint _remoteEndPoint;

        /// <summary>
        /// TCP 客户端模式
        /// </summary>
        public NetworkTransport(string host, int port, string deviceKey = null)
            : base(string.IsNullOrEmpty(deviceKey) ? $"TCP[{host}:{port}]" : deviceKey)
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.Connect(host, port);
                _socket.SendTimeout = 2000;
                _socket.ReceiveTimeout = 2000;
                _isUdp = false;

                SetState(DeviceState.Connected);
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "TCP初始化失败: " + ex.Message);
                throw;
            }
        }

        /// <summary>
        /// UDP 模式
        /// </summary>
        public NetworkTransport(string localHost, int localPort, string remoteHost, int remotePort, string deviceKey = null)
            : base(string.IsNullOrEmpty(deviceKey) ? $"UDP[{remoteHost}:{remotePort}]" : deviceKey)
        {
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Bind(new IPEndPoint(IPAddress.Parse(localHost), localPort));
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(remoteHost), remotePort);
                _isUdp = true;

                SetState(DeviceState.Connected);
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "UDP初始化失败: " + ex.Message);
                throw;
            }
        }

        public override bool IsHealthy()
        {
            try
            {
                return _socket != null && _socket.Connected && base.IsHealthy();
            }
            catch
            {
                return false;
            }
        }

        protected override Task<int> WriteAsync(byte[] data, CancellationToken token)
        {
            return Task.Run(() =>
            {
                try
                {
                    int written;
                    if (_isUdp)
                    {
                        written = _socket.SendTo(data, _remoteEndPoint);
                    }
                    else
                    {
                        written = _socket.Send(data);
                    }

                    SetState(DeviceState.Connected);
                    return written;
                }
                catch (Exception ex)
                {
                    SetState(DeviceState.Disconnected, "Socket写入异常: " + ex.Message);
                    throw new IOException($"{_deviceKey} 网络发送失败", ex);
                }
            }, token);
        }

        protected override int ReadChunk(byte[] buffer, int offset, int count, int timeoutMs, out bool timedOut)
        {
            timedOut = false;
            try
            {
                _socket.ReceiveTimeout = timeoutMs <= 0 ? 1 : timeoutMs;

                if (_isUdp)
                {
                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    return _socket.ReceiveFrom(buffer, offset, count, SocketFlags.None, ref remote);
                }
                else
                {
                    return _socket.Receive(buffer, offset, count, SocketFlags.None);
                }
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    timedOut = true;
                    return 0;
                }
                SetState(DeviceState.Disconnected, "Socket读取异常: " + ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "网络读取异常: " + ex.Message);
                throw;
            }
        }

        protected override void DoFlush()
        {
            try
            {
                // 网络socket一般没有硬件缓冲清空，只能清掉接收缓存中的数据
                while (_socket.Available > 0)
                {
                    byte[] buf = new byte[_socket.Available];
                    _socket.Receive(buf);
                }
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "Flush异常: " + ex.Message);
                throw;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            try
            {
                if (_socket != null)
                {
                    _socket.Close();
                    _socket.Dispose();
                }
            }
            catch (Exception ex)
            {
                SetState(DeviceState.Disconnected, "Dispose异常: " + ex.Message);
            }
            finally
            {
                SetState(DeviceState.Disconnected);
            }
        }
    }
}

