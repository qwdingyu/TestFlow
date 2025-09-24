using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ZL.DeviceLib.Devices.Utils
{
    public sealed class WindowStats
    {
        public long Count { get; internal set; }
        public double Sum { get; internal set; }
        public double Min { get; internal set; } = double.PositiveInfinity;
        public double Max { get; internal set; } = double.NegativeInfinity;
        public List<double> Samples { get; internal set; } = new();
        public double Avg => Count > 0 ? Math.Round(Sum / Count, 3) : 0.0;

        public WindowStats Clone(bool includeSamples = false)
        {
            return new WindowStats { Count = Count, Sum = Sum, Min = Min, Max = Max, Samples = includeSamples ? new List<double>(Samples) : new List<double>() };
        }
    }

    internal sealed class WindowState
    {
        public readonly object Sync = new();
        public bool Active;
        public WindowStats Stats = new();
    }

    /// <summary>
    /// 通用采样窗口中心，支持多设备多通道。
    /// 增强：增加 maxSamples 限制，避免内存泄漏。
    /// </summary>
    public static class MeasurementHub
    {
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WindowState>> _devices = new();

        /// <summary>
        /// 每个通道最多保留的样本数（避免内存无限增长）。
        /// 默认 10,000，可根据需要调整。
        /// </summary>
        public static int MaxSamples { get; set; } = 10_000;

        private static WindowState GetWindow(string deviceId, string channel, bool createIfMissing = false)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentNullException(nameof(deviceId));
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentNullException(nameof(channel));

            var device = _devices.GetOrAdd(deviceId, _ => new ConcurrentDictionary<string, WindowState>());
            if (createIfMissing)
                return device.GetOrAdd(channel, _ => new WindowState());
            else
                return device.TryGetValue(channel, out var win) ? win : null;
        }

        public static void Begin(string deviceId, string channel, bool clear = true)
        {
            var win = GetWindow(deviceId, channel, createIfMissing: true);
            lock (win.Sync)
            {
                if (clear) win.Stats = new WindowStats();
                win.Active = true;
            }
        }

        public static void Feed(string deviceId, string channel, double value)
        {
            var win = GetWindow(deviceId, channel);
            if (win == null) return;

            lock (win.Sync)
            {
                if (!win.Active) return;
                var s = win.Stats;

                s.Count++;
                s.Sum += value;
                if (value < s.Min) s.Min = value;
                if (value > s.Max) s.Max = value;

                try
                {
                    if (MaxSamples > 0)
                    {
                        if (s.Samples.Count >= MaxSamples)
                        {
                            // 丢弃最早的数据，保证内存不会无限增长
                            s.Samples.RemoveAt(0);
                        }
                        s.Samples.Add(value);
                    }
                }
                catch (Exception ex)
                {
                    // 捕获异常，避免采样线程崩溃
                    System.Diagnostics.Debug.WriteLine($"Feed error: {ex}");
                }
            }
        }

        public static WindowStats End(string deviceId, string channel, bool close = true, bool includeSamples = false)
        {
            var win = GetWindow(deviceId, channel);
            if (win == null) return new WindowStats();

            lock (win.Sync)
            {
                var snapshot = win.Stats.Clone(includeSamples);
                if (close) win.Active = false;
                return snapshot;
            }
        }

        public static WindowStats Snapshot(string deviceId, string channel, bool includeSamples = false)
        {
            var win = GetWindow(deviceId, channel);
            if (win == null) return new WindowStats();

            lock (win.Sync) return win.Stats.Clone(includeSamples);
        }

        public static bool IsActive(string deviceId, string channel)
        {
            var win = GetWindow(deviceId, channel);
            return win != null && win.Active;
        }
    }
}
