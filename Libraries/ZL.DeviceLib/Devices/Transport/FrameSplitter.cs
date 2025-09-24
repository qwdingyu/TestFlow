using System;
using System.Collections.Generic;
using System.Text;

namespace ZL.DeviceLib.Devices.Transport
{/// <summary>
 /// 协议无关的分帧器：把流式字节切成一帧帧。
 /// 约定：ExtractFrames() 只返回“完整帧”，残缺数据留在内部缓冲，等待后续Append()补齐。
 /// </summary>
    public interface IFrameSplitter
    {
        void Append(byte[] buffer, int offset, int count);
        IList<ReadOnlyMemory<byte>> ExtractFrames();
        void Reset();
    }

    /// <summary>
    /// 定长帧分包器（Modbus/自定义定长）
    /// </summary>
    public class FixedLengthSplitter : IFrameSplitter
    {
        private readonly int _frameSize;
        private readonly List<byte> _buf = new List<byte>();

        public FixedLengthSplitter(int frameSize)
        {
            if (frameSize <= 0) throw new ArgumentException("frameSize must > 0");
            _frameSize = frameSize;
        }

        public void Append(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) _buf.Add(buffer[offset + i]);
        }

        public IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();
            while (_buf.Count >= _frameSize)
            {
                var arr = _buf.GetRange(0, _frameSize).ToArray();
                frames.Add(new ReadOnlyMemory<byte>(arr));
                _buf.RemoveRange(0, _frameSize);
            }
            return frames;
        }

        public void Reset() { _buf.Clear(); }
    }
    /// <summary>
    /// 分隔符分包器（SCPI/AT 等文本协议；支持 \n 或 \r\n）
    /// 注意：返回的帧含分隔符本身；上层可 TrimEnd('\r','\n')。
    /// </summary>
    public class DelimiterSplitter : IFrameSplitter
    {
        private readonly byte[] _delimiter;   // 可以是 1字节 或 多字节
        private readonly List<byte> _buf = new List<byte>();

        /// <summary>
        /// 使用单字节分隔符
        /// </summary>
        public DelimiterSplitter(byte delimiter) => _delimiter = new[] { delimiter };

        /// <summary>
        /// 使用字符串分隔符（会用 ASCII 编码转换）
        /// </summary>
        public DelimiterSplitter(string delimiter)
        {
            if (string.IsNullOrEmpty(delimiter))
                throw new ArgumentException("分隔符不能为空", nameof(delimiter));
            // TODO 是否需要根据 报文 是 ASCII 还是 HEX 进行调整？
            _delimiter = Encoding.ASCII.GetBytes(delimiter);
        }

        public void Append(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
                _buf.Add(buffer[offset + i]);
        }

        public IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();

            while (true)
            {
                int idx = IndexOf(_buf, _delimiter);
                if (idx < 0) break;

                int end = idx + _delimiter.Length;
                var arr = _buf.GetRange(0, end).ToArray();
                frames.Add(new ReadOnlyMemory<byte>(arr));
                _buf.RemoveRange(0, end);
            }

            return frames;
        }

        public void Reset() => _buf.Clear();

        // 工具方法：查找子数组位置
        private static int IndexOf(List<byte> buffer, byte[] pattern)
        {
            if (pattern.Length == 1)
                return buffer.IndexOf(pattern[0]);

            for (int i = 0; i <= buffer.Count - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (buffer[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }
    }


    /// <summary>
    /// 头长度CRC分包器（适用于“从头部推算总长度 + CRC校验”的帧，比如 Modbus RTU 响应等）
    /// 注意：这里把“长度解码器”和“CRC校验器”做成委托，以适配不同协议。
    /// </summary>
    public class HeaderLengthCrcSplitter : IFrameSplitter
    {
        private readonly int _headerMinSize;
        private readonly Func<byte[], int> _lengthDecoder;          // 输入当前缓冲（从 0 起），输出“需要的总帧长”；不够则返回 int.MaxValue
        private readonly Func<byte[], int, bool> _crcChecker;       // 输入完整帧 + 长度，返回 CRC 是否通过
        private readonly List<byte> _buf = new List<byte>();

        public HeaderLengthCrcSplitter(int headerMinSize, Func<byte[], int> lengthDecoder, Func<byte[], int, bool> crcChecker)
        {
            _headerMinSize = headerMinSize;
            _lengthDecoder = lengthDecoder ?? throw new ArgumentNullException("lengthDecoder");
            _crcChecker = crcChecker ?? throw new ArgumentNullException("crcChecker");
        }

        public void Append(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) _buf.Add(buffer[offset + i]);
        }

        public IList<ReadOnlyMemory<byte>> ExtractFrames()
        {
            var frames = new List<ReadOnlyMemory<byte>>();

            while (_buf.Count >= _headerMinSize)
            {
                var header = _buf.ToArray(); // 简便起见复制；如需性能可做环形缓冲
                int totalLen = _lengthDecoder(header);
                if (totalLen == int.MaxValue) break;     // 头不完整，等待更多字节
                if (totalLen <= 0)                       // 防御：异常长度，丢掉1字节继续
                {
                    _buf.RemoveAt(0);
                    continue;
                }
                if (_buf.Count < totalLen) break;        // 帧不完整

                var frame = _buf.GetRange(0, totalLen).ToArray();
                bool ok = _crcChecker(frame, totalLen);
                // 无论CRC是否通过，都丢弃这段；可根据需要把 CRC 失败的帧上报到错误事件
                if (ok) frames.Add(new ReadOnlyMemory<byte>(frame));
                _buf.RemoveRange(0, totalLen);
            }

            return frames;
        }

        public void Reset() { _buf.Clear(); }
    }

    /// <summary>
    /// 分包器工厂：统一创建常见分包器
    /// </summary>
    public enum SplitterType { FixedLength, Delimiter, ModbusRtu }

    public static class FrameSplitterFactory
    {
        public static IFrameSplitter Create(SplitterType type, int param = 0, byte delimiter = (byte)'\n')
        {
            switch (type)
            {
                case SplitterType.FixedLength:
                    return new FixedLengthSplitter(param);
                case SplitterType.Delimiter:
                    return new DelimiterSplitter(delimiter);
                case SplitterType.ModbusRtu:
                    // Modbus RTU: 以功能码和“字节数”字段推总长；CRC16(Modbus)
                    return new HeaderLengthCrcSplitter(
                        3,                                  // 至少要 Addr(1)+Func(1)+ByteCount(1)
                        ModbusLengthDecoder,
                        ModbusCrcChecker);
                default:
                    throw new NotSupportedException("Unsupported splitter type");
            }
        }

        // === Modbus RTU 辅助 ===
        private static int ModbusLengthDecoder(byte[] buf)
        {
            if (buf.Length < 2) return int.MaxValue;
            byte func = buf[1];

            // 以 0x03/0x04 响应为例：[Addr][Func][ByteCount][Data...][CRClo][CRChi]
            if (func == 0x03 || func == 0x04)
            {
                if (buf.Length < 3) return int.MaxValue;
                int bc = buf[2] & 0xFF;
                return 3 + bc + 2;
            }

            // 其他功能码可按需扩展；默认给个最小长度（例如异常响应也有5字节）
            return 5;
        }

        private static bool ModbusCrcChecker(byte[] frame, int len)
        {
            if (len < 3) return false;
            ushort calc = Crc16Modbus(frame, 0, len - 2);
            ushort got = (ushort)(frame[len - 2] | (frame[len - 1] << 8));
            return calc == got;
        }

        public static ushort Crc16Modbus(byte[] data, int offset, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= data[i];
                for (int b = 0; b < 8; b++)
                {
                    bool lsb = (crc & 0x0001) != 0;
                    crc >>= 1;
                    if (lsb) crc ^= 0xA001;
                }
            }
            return crc;
        }
    }

}
