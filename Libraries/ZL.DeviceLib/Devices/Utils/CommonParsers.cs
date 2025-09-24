using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZL.DeviceLib.Devices.Utils
{
    // 命令配置项：优先用 ParserKey 引用公共解析器；必要时可用 Parser 覆盖
    public class CommandSpec
    {
        public bool ExpectResponse = true;
        public int DefaultTimeoutMs = 500;
        public string Description = "";
        public string ParserKey; // 引用 CommonParsers.ByKey 中的解析器
        public CommonParsers.FrameParser Parser; // 可选，覆盖 ParserKey
        public byte Delimiter = (byte)'\n';
        public bool KeepAllFrames = false; // 视你的 ReceiveAsync 定义
    }
    public static class CommonParsers 
    {
        // 解析器签名：输入一帧原始字节，返回已解析对象
        public delegate object FrameParser(ReadOnlyMemory<byte> frame);

        // 注册表：按 key 检索解析器，避免在 _commandMap 重复写匿名函数
        public static readonly Dictionary<string, FrameParser> ByKey = new Dictionary<string, FrameParser>(StringComparer.OrdinalIgnoreCase)
            {
                // 原样字符串（去掉尾部 CR/LF/空格/NUL）
                { "raw:string", ParseStringTrim },

                // 标准浮点（含科学计数法，适配 0.2601E-03 / 11.269E-03）
                { "scpi:double", ParseDouble },

                // CSV 浮点数组，如 "1.2,3.4,5.6"
                { "scpi:double[]", ParseDoubleArray },

                // 标准整数
                { "scpi:int", ParseInt },

                // 布尔：支持 "1/0/ON/OFF/TRUE/FALSE"
                { "std:bool", ParseBool },

                // 键值对为 double，如 "VOLT:5.12;CURR:0.456" 或 "VOLT=5.12,CURR=0.456"
                { "kv:double", ParseKeyDoubleMap },

                // CSV 字符串数组
                { "csv:string[]", ParseCsvStrings },

                // HEX 字节串：如 "01 0A FF" -> byte[]
                { "hex:bytes", ParseHexBytes }
            };

        // ===== 基础工具 =====
        private static string NormalizeAscii(ReadOnlyMemory<byte> frame)
        {
            var s = Encoding.ASCII.GetString(frame.ToArray());
            return s.TrimEnd('\r', '\n', '\0', ' ');
        }

        // ===== 具体解析器实现 =====

        private static object ParseStringTrim(ReadOnlyMemory<byte> frame)
        {
            return NormalizeAscii(frame);
        }

        private static object ParseDouble(ReadOnlyMemory<byte> frame)
        {
            var s = NormalizeAscii(frame);
            double v;
            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v))
                return v;
            // 解析失败时，降级返回原串，便于排查
            return s;
        }

        private static object ParseDoubleArray(ReadOnlyMemory<byte> frame)
        {
            var s = NormalizeAscii(frame);
            var parts = s.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<double>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                double v;
                if (double.TryParse(parts[i], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v))
                    list.Add(v);
            }
            return list.ToArray();
        }

        private static object ParseInt(ReadOnlyMemory<byte> frame)
        {
            var s = NormalizeAscii(frame);
            long v64;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v64))
                return v64;
            return s;
        }

        private static object ParseBool(ReadOnlyMemory<byte> frame)
        {
            var s = NormalizeAscii(frame);
            var up = s.Trim().ToUpperInvariant();
            if (up == "1" || up == "ON" || up == "TRUE") return true;
            if (up == "0" || up == "OFF" || up == "FALSE") return false;

            bool b;
            if (bool.TryParse(s, out b)) return b;
            // 不可判定时原样返回字符串
            return s;
        }

        private static object ParseKeyDoubleMap(ReadOnlyMemory<byte> frame)
        {
            var s = NormalizeAscii(frame);
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var items = s.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < items.Length; i++)
            {
                var kv = items[i].Split(new[] { ':', '=' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (kv.Length == 2)
                {
                    double v;
                    if (double.TryParse(kv[1].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v))
                        map[kv[0].Trim()] = v;
                }
            }
            return map;
        }

        private static object ParseCsvStrings(ReadOnlyMemory<byte> frame)
        {
            var s = NormalizeAscii(frame);
            var parts = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();
            return parts;
        }

        private static object ParseHexBytes(ReadOnlyMemory<byte> frame)
        {
            var s = NormalizeAscii(frame).Replace(" ", "").Replace("-", "");
            if (s.Length % 2 != 0) s = "0" + s;
            var len = s.Length / 2;
            var bytes = new byte[len];
            for (int i = 0; i < len; i++)
            {
                bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
    }
}
