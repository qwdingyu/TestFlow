using System;
using System.Collections.Generic;
using System.Globalization;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices.Transport
{
    // =========================
    // 1) USB 选项解析（集中处理）
    // =========================
    public sealed class UsbTransportOptions
    {
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public byte InEp { get; set; } = 0x81;
        public byte OutEp { get; set; } = 0x01;
        public byte ConfigIndex { get; set; } = 1;
        public int InterfaceIndex { get; set; } = 0;

        public static UsbTransportOptions FromSettings(DeviceConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            var s = cfg.Settings ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            return new UsbTransportOptions
            {
                VendorId = ReadInt(s, new[] { "VID", "VendorId", "usb.vid" }, required: true),
                ProductId = ReadInt(s, new[] { "PID", "ProductId", "usb.pid" }, required: true),
                InEp = ReadByte(s, new[] { "InEp", "usb.inEp" }, 0x81),
                OutEp = ReadByte(s, new[] { "OutEp", "usb.outEp" }, 0x01),
                ConfigIndex = (byte)ReadInt(s, new[] { "ConfigIndex", "usb.configIndex" }, 1, required: false, allowHex: false),
                InterfaceIndex = ReadInt(s, new[] { "InterfaceIndex", "usb.interfaceIndex" }, 0, required: false, allowHex: false),
            };
        }

        private static int ReadInt(IDictionary<string, object> settings, string[] keys, int @default = 0, bool required = false, bool allowHex = true)
        {
            foreach (var k in keys)
            {
                if (settings.TryGetValue(k, out var v) && v != null)
                    return ParseInt(v, @default, allowHex);
            }
            if (required) throw new ArgumentException($"USB setting missing: {string.Join("/", keys)}");
            return @default;
        }

        private static byte ReadByte(IDictionary<string, object> settings, string[] keys, byte @default = 0)
        {
            foreach (var k in keys)
            {
                if (settings.TryGetValue(k, out var v) && v != null)
                    return ParseByte(v, @default);
            }
            return @default;
        }

        private static int ParseInt(object v, int @default, bool allowHex)
        {
            try
            {
                if (v is int i) return i;
                var s = v.ToString().Trim();
                if (allowHex && (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0X")))
                    return Convert.ToInt32(s.Substring(2), 16);
                return int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            catch { return @default; }
        }

        private static byte ParseByte(object v, byte @default)
        {
            try
            {
                if (v is byte b) return b;
                var s = v.ToString().Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0X"))
                    return Convert.ToByte(s.Substring(2), 16);
                return byte.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }
            catch { return @default; }
        }
    }
}