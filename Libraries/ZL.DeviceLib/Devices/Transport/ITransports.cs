using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.DeviceLib.Devices.Transport
{
    public interface ITransport : IDisposable
    {
        /// <summary>��������</summary>
        Task<int> SendAsync(ReadOnlyMemory<byte> data, CancellationToken t);

        /// <summary>
        /// ��������֡
        /// expectedLen = 0 ��ʾ�÷ָ���ģʽ��DelimiterSplitter����>0 ��ʾ����֡��FixedLengthSplitter��
        /// </summary>
        Task<IList<ReadOnlyMemory<byte>>> ReceiveAsync(int expectedLen, TimeSpan timeout, CancellationToken token, bool keepAllFrames, byte delimiter = (byte)'\n');
        Task<IList<string>> ReceiveStringAsync(int expectedLen, TimeSpan timeout, CancellationToken token, bool keepAllFrames, byte delimiter = (byte)'\n', Encoding encoding = null);

        /// <summary>��������֡���Զ���Splitter��</summary>
        Task<IList<ReadOnlyMemory<byte>>> ReceiveAsync(IFrameSplitter splitter, TimeSpan timeout, CancellationToken token, bool keepAllFrames);
        Task<IList<string>> ReceiveStringAsync(IFrameSplitter splitter, TimeSpan timeout, CancellationToken token, bool keepAllFrames, Encoding encoding = null);

        /// <summary>��ս��ջ���</summary>
        Task FlushAsync(CancellationToken t);

        /// <summary>�豸�Ƿ񽡿�</summary>
        bool IsHealthy();
    }
}

