using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ZL.DeviceLib.Devices.Utils
{
    public interface ICodec
    {
        /// <summary>把字符串负载编码为字节（例如 SCPI 文本、HEX 文本）</summary>
        byte[] Encode(string payload);

        /// <summary>
        /// 把收到的字节解码为对象。parser 可选：
        /// - TextCodec: "text"(默认) | "double" | "int"
        /// - HexCodec : "string"(默认：AA-BB-CC) | "bytes"（返回 byte[]）
        /// </summary>
        object Decode(ReadOnlyMemory<byte> data, string parser = null);
        object Decode(byte[] data, string parser = null);
    }

    public sealed class TextCodec : ICodec
    {
        private readonly Encoding _enc;
        private readonly string _eol;

        /// <param name="encoding">默认 ASCII；也可用 Encoding.UTF8 等</param>
        /// <param name="eol">行结束符，SCPI 一般为 "\n"</param>
        public TextCodec(Encoding encoding = null, string eol = "\n")
        {
            _enc = encoding ?? Encoding.ASCII;
            _eol = eol ?? "";
        }

        public byte[] Encode(string payload)
        {
            var s = payload ?? string.Empty;
            // 统一在末尾追加 EOL（SCPI 常见）
            return _enc.GetBytes(s + _eol);
        }

        public object Decode(byte[] data, string parser = null)
        {
            // 关键修正：对老框架用 ToArray()，避免 ReadOnlySpan<byte> 重载缺失导致 CS1503
            var s = _enc.GetString(data);
            // 常见仪器会带回车换行，规整去掉
            s = s.TrimEnd('\r', '\n', '\0', ' ');

            switch ((parser ?? "text").ToLowerInvariant())
            {
                case "double":
                    // 更宽松的数字解析
                    if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dv))
                        return dv;
                    throw new FormatException($"Cannot parse double from \"{s}\".");

                case "int":
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                        return iv;
                    throw new FormatException($"Cannot parse int from \"{s}\".");

                case "text":
                default:
                    return s;
            }
        }
        public object Decode(ReadOnlyMemory<byte> data, string parser)
        {
            return Decode(data.ToArray(), parser);
        }
    }

    public sealed class HexCodec : ICodec
    {
        public byte[] Encode(string hex)
        {
            // 支持多种写法：
            // "00 01 02", "00-01-02", "0x00,0x01,0x02", "000102"
            if (string.IsNullOrWhiteSpace(hex)) return Array.Empty<byte>();

            // 过滤出所有十六进制字符 0-9A-Fa-f
            var sb = new StringBuilder(hex.Length);
            foreach (var ch in hex)
            {
                if ((ch >= '0' && ch <= '9') ||
                    (ch >= 'a' && ch <= 'f') ||
                    (ch >= 'A' && ch <= 'F'))
                {
                    sb.Append(ch);
                }
            }

            if (sb.Length == 0) return Array.Empty<byte>();
            if ((sb.Length % 2) != 0)
                throw new FormatException($"Hex string has odd length: \"{hex}\".");

            var bytes = new byte[sb.Length / 2];
            for (int i = 0, j = 0; i < bytes.Length; i++, j += 2)
            {
                bytes[i] = Convert.ToByte(sb.ToString(j, 2), 16);
            }
            return bytes;
        }

        public object Decode(byte[] data, string parser)
        {
            var mode = (parser ?? "string").ToLowerInvariant();
            // 直接返回原始 byte[]
            if (mode == "bytes") return data;
            // 默认返回字符串 "AA-BB-CC"
            return BitConverter.ToString(data);
        }
        public object Decode(ReadOnlyMemory<byte> data, string parser)
        {
            return Decode(data.ToArray(), parser);
        }
    }
    public static class CodecFactory
    {
        public static ICodec Create(string name)
        {
            if (string.Equals(name, "hex", StringComparison.OrdinalIgnoreCase))
                return new HexCodec();
            else
                return new TextCodec();
        }
    }

    public static class CodecUtils
    {
        public static byte[] Hex(string s)
        {
            var parts = s.Split(new[] { ' ', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++) bytes[i] = Convert.ToByte(parts[i].Replace("0x", ""), 16);
            return bytes;
        }
    }
    public static class Format
    {
        public static string FormatValue(object value)
        {
            if (value == null) return "null";

            // 针对 IEnumerable，但排除 string
            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                return string.Join(";", enumerable.Cast<object>());
            }

            return value.ToString();
        }
    }
}
