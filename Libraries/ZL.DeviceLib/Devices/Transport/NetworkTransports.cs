using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ZL.DeviceLib.Devices.Transport
{
    public interface ITcpTransport : IDisposable
    {
        void Connect(string host, int port, int timeoutMs);
        void Send(byte[] data);
        byte[] Receive(int expected, int timeoutMs, CancellationToken token);
        bool Connected { get; }
    }

    public sealed class TcpTransport : ITcpTransport
    {
        private TcpClient _client;
        public bool Connected => _client?.Connected == true;
        public void Connect(string host, int port, int timeoutMs)
        {
            _client = new TcpClient();
            var ar = _client.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs)))
                throw new TimeoutException("TCP connect timeout");
            _client.EndConnect(ar);
        }
        public void Send(byte[] data) => _client.GetStream().Write(data, 0, data?.Length ?? 0);
        public byte[] Receive(int expected, int timeoutMs, CancellationToken token)
        {
            var ns = _client.GetStream();
            ns.ReadTimeout = timeoutMs;
            var buf = new byte[expected > 0 ? expected : 4096];
            int n = ns.Read(buf, 0, buf.Length);
            var ret = new byte[n]; Array.Copy(buf, ret, n); return ret;
        }
        public void Dispose() { try { _client?.Close(); } catch { } }
    }

    public interface IUdpTransport : IDisposable
    {
        void Bind(int port);
        void Send(string host, int port, byte[] data);
        byte[] Receive(int timeoutMs, CancellationToken token, out IPEndPoint remote);
    }

    public sealed class UdpTransport : IUdpTransport
    {
        private UdpClient _udp;
        public void Bind(int port) { _udp = new UdpClient(port); }
        public void Send(string host, int port, byte[] data) => _udp.Send(data, data.Length, host, port);
        public byte[] Receive(int timeoutMs, CancellationToken token, out IPEndPoint remote)
        {
            _udp.Client.ReceiveTimeout = timeoutMs;
            remote = new IPEndPoint(IPAddress.Any, 0);
            return _udp.Receive(ref remote);
        }
        public void Dispose() { try { _udp?.Close(); } catch { } }
    }

    public interface IModbusTransport
    {
        // 预留：根据后续选型接入第三方库，再封装成此接口
    }
}

