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
        public List<double> Samples { get; internal set; } = new(); // 新增：采样数据记录
        public double Avg => Count > 0 ? Sum / Count : 0.0;

        public WindowStats Clone(bool includeSamples = false)
        {
            return new WindowStats
            {
                Count = Count,
                Sum = Sum,
                Min = Min,
                Max = Max,
                Samples = includeSamples ? new List<double>(Samples) : new List<double>()
            };
        }
    }

    internal sealed class WindowState
    {
        public readonly object Sync = new object();
        public bool Active;
        public WindowStats Stats = new WindowStats();
    }

    /// <summary>
    /// 协议无关的采样窗口中心。设备在采样时调用 Feed(channel, value) 即可。
    /// 流程层通过 Begin / End 统一管理窗口生命周期。
    /// </summary>
    public static class MeasurementHub
    {
        private static readonly ConcurrentDictionary<string, WindowState> _windows = new();

        public static void Begin(string channel, bool clear = true)
        {
            if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentNullException(nameof(channel));
            var win = _windows.GetOrAdd(channel, _ => new WindowState());
            lock (win.Sync)
            {
                if (clear) win.Stats = new WindowStats();
                win.Active = true;
            }
        }

        public static void Feed(string channel, double value)
        {
            LogHelper.Info($"设备【{channel}】增加采样数据【{value}】");
            if (!_windows.TryGetValue(channel, out var win)) return;
            lock (win.Sync)
            {
                if (!win.Active) return;
                var s = win.Stats;
                s.Count++;
                s.Sum += value;
                if (value < s.Min) s.Min = value;
                if (value > s.Max) s.Max = value;
                s.Samples.Add(value); // 保存完整采样点
            }
        }

        public static WindowStats End(string channel, bool close = true, bool includeSamples = false)
        {
            if (!_windows.TryGetValue(channel, out var win)) return new WindowStats();
            lock (win.Sync)
            {
                var snapshot = win.Stats.Clone(includeSamples);
                if (close) win.Active = false;
                return snapshot;
            }
        }

        public static WindowStats Snapshot(string channel, bool includeSamples = false)
        {
            if (!_windows.TryGetValue(channel, out var win)) return new WindowStats();
            lock (win.Sync) return win.Stats.Clone(includeSamples);
        }

        public static bool IsActive(string channel) => _windows.TryGetValue(channel, out var win) && win.Active;
    }
}
