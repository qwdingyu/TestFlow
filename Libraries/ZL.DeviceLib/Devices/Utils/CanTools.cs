using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ZL.DeviceLib.Devices.Utils
{
    /// <summary>
    /// CAN 帧（固定 8 字节）
    /// </summary>
    public class CanFrame
    {
        public int Id { get; set; }
        public byte[] Data { get; set; }

        public int Dlc { get { return Data?.Length ?? 0; } }

        public CanFrame(int id)
        {
            Id = id;
            Data = new byte[8];
        }

        public override string ToString()
        {
            return string.Format("ID=0x{0:X3} Data={1}", Id, BitConverter.ToString(Data));
        }
    }

    /// <summary>
    /// CAN 信号定义
    /// </summary>
    public class CanSignal
    {
        public string Name { get; set; }
        public int ByteIndex { get; set; }
        public int HighBit { get; set; }
        public int LowBit { get; set; }
        public double Factor { get; set; }
        public double Offset { get; set; }

        /// <summary>
        /// 是否有镜像
        /// </summary>
        public CanSignal Mirror { get; set; }

        /// <summary>
        /// 镜像规则：乘以一个因子（例如 ×2）
        /// </summary>
        public int MirrorFactor { get; set; }

        public int Length { get { return HighBit - LowBit + 1; } }

        public CanSignal(string name, int byteIndex, int highBit, int lowBit, double factor = 1, double offset = 0)
        {
            Name = name;
            ByteIndex = byteIndex;
            HighBit = highBit;
            LowBit = lowBit;
            Factor = factor;
            Offset = offset;
            MirrorFactor = 1;
        }
    }

    /// <summary>
    /// 报文定义
    /// </summary>
    public class CanMessageDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Dictionary<string, CanSignal> Signals { get; set; }

        public CanMessageDefinition(int id, string name)
        {
            Id = id;
            Name = name;
            Signals = new Dictionary<string, CanSignal>();
        }

        public void AddSignal(CanSignal signal)
        {
            Signals[signal.Name] = signal;
        }
    }

    /// <summary>
    /// 通用 CAN 解析工具
    /// </summary>
    public static class CanParser
    {
        public static double DecodeSignal(CanFrame frame, CanSignal signal)
        {
            int raw = ExtractBits(frame.Data, signal.ByteIndex, signal.HighBit, signal.LowBit);
            return raw * signal.Factor + signal.Offset;
        }

        public static void EncodeSignal(CanFrame frame, CanSignal signal, double physicalValue)
        {
            int raw = (int)((physicalValue - signal.Offset) / signal.Factor);
            InsertBits(frame.Data, signal.ByteIndex, signal.HighBit, signal.LowBit, raw);

            // 如果有镜像 → 自动写入镜像位
            if (signal.Mirror != null)
            {
                int mirrorVal = raw * signal.MirrorFactor;
                InsertBits(frame.Data, signal.Mirror.ByteIndex, signal.Mirror.HighBit, signal.Mirror.LowBit, mirrorVal);
            }
        }

        private static int ExtractBits(byte[] data, int startByte, int highBit, int lowBit)
        {
            int value = 0;
            int length = highBit - lowBit + 1;
            for (int i = 0; i < length; i++)
            {
                int bitIndex = lowBit + i;
                int mask = 1 << bitIndex;
                int bit = (data[startByte] & mask) >> bitIndex;
                value |= (bit << i);
            }
            return value;
        }

        private static void InsertBits(byte[] data, int startByte, int highBit, int lowBit, int value)
        {
            int length = highBit - lowBit + 1;
            for (int i = 0; i < length; i++)
            {
                int bitIndex = lowBit + i;
                int mask = 1 << bitIndex;

                if (((value >> i) & 1) == 1)
                    data[startByte] |= (byte)mask;
                else
                    data[startByte] &= (byte)~mask;
            }
        }
    }

    public static class CanDbLoader
    {
        public static Dictionary<int, CanMessageDefinition> LoadFromCsv(string filePath)
        {
            var messages = new Dictionary<int, CanMessageDefinition>();
            var signalCache = new Dictionary<string, CanSignal>(); // 临时保存信号，方便建立镜像关系

            using (var reader = new StreamReader(filePath))
            {
                string header = reader.ReadLine(); // 读掉表头
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split(',');
                    if (parts.Length < 12) continue;

                    int msgId = parts[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? int.Parse(parts[0].Substring(2), NumberStyles.HexNumber)
                        : int.Parse(parts[0]);

                    string msgName = parts[1];
                    string signalName = parts[2];
                    int startByte = int.Parse(parts[3]);
                    int highBit = int.Parse(parts[4]);
                    int lowBit = int.Parse(parts[5]);
                    int length = int.Parse(parts[6]);
                    double factor = double.Parse(parts[7], CultureInfo.InvariantCulture);
                    double offset = double.Parse(parts[8], CultureInfo.InvariantCulture);
                    string mirrorOf = parts[9];
                    int mirrorFactor = string.IsNullOrWhiteSpace(parts[10]) ? 1 : int.Parse(parts[10]);
                    string comment = parts[11];

                    if (!messages.ContainsKey(msgId))
                    {
                        messages[msgId] = new CanMessageDefinition(msgId, msgName);
                    }

                    var sig = new CanSignal(signalName, startByte, highBit, lowBit, factor, offset);
                    messages[msgId].AddSignal(sig);
                    signalCache[signalName] = sig;

                    // 如果是镜像信号，则延迟设置（需要主信号已存在）
                    if (!string.IsNullOrWhiteSpace(mirrorOf) && signalCache.ContainsKey(mirrorOf))
                    {
                        var master = signalCache[mirrorOf];
                        master.Mirror = sig;
                        master.MirrorFactor = mirrorFactor;
                    }
                }
            }

            return messages;
        }
    }
}
