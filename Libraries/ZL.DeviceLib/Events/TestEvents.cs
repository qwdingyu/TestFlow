using System;
using System.Collections.Generic;
using ZL.DeviceLib.Storage;

namespace ZL.DeviceLib.Events
{

    public enum DeviceState { Unknown, Connected, Disconnected }

    /// <summary>
    /// 全局设备状态通知器。
    /// 任何设备的状态变化都调用 Notify(deviceKey, newState)。
    /// UI 只需订阅一次 DeviceStateChangedEvent 即可。
    /// </summary>
    public static class DeviceNotifier
    {
        // 你指定的形式：静态 Action。支持多播（可 += / -=）。
        public static Action<string, DeviceState> DeviceStateChangedEvent { get; set; }
        public static Action<string, string> DeviceInfoChangedEvent { get; set; }
    }


    public static class TestEvents
    {
        // 单步开始
        public static Action<string> StepStarted { get; set; }

        // 单步完成（参数：步骤名，是否成功，耗时s，输出字典）
        public static Action<string, bool, double, Dictionary<string, object>> StepCompleted { get; set; }

        // 总体完成
        public static Action<SeatResults> TestCompleted { get; set; }

        // 状态变化（比如 Running / Stopped / Error）
        public static Action<string> StatusChanged { get; set; }
        // 通用日志（也可直接用 UiLogManager）
        public static Action<string, string> Log { get; set; } // (level, message)
    }

}
