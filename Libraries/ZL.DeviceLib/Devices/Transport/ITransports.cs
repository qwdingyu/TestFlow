using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.DeviceLib.Devices.Transport
{
    public interface ITransport : IDisposable
    {
        /// <summary>发送数据</summary>
        Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken t);

        /// <summary>
        /// 接收数据帧
        /// expectedLen = 0 表示用分隔符模式（DelimiterSplitter），>0 表示定长帧（FixedLengthSplitter）
        /// </summary>
        Task<IList<ReadOnlyMemory<byte>>> ReceiveAsync(int expectedLen, TimeSpan timeout, CancellationToken token, bool keepAllFrames, byte delimiter = (byte)'\n');
        Task<IList<string>> ReceiveStringAsync(int expectedLen, TimeSpan timeout, CancellationToken token, bool keepAllFrames, byte delimiter = (byte)'\n', Encoding encoding = null);

        /// <summary>接收数据帧（自定义Splitter）</summary>
        Task<IList<ReadOnlyMemory<byte>>> ReceiveAsync(IFrameSplitter splitter, TimeSpan timeout, CancellationToken token, bool keepAllFrames);
        Task<IList<string>> ReceiveStringAsync(IFrameSplitter splitter, TimeSpan timeout, CancellationToken token, bool keepAllFrames, Encoding encoding = null);

        /// <summary>清空接收缓冲</summary>
        Task FlushAsync(CancellationToken t);

        /// <summary>设备是否健康</summary>
        bool IsHealthy();
    }
}

