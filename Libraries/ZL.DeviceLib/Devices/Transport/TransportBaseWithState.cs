using System;
using System.Text;
using System.Threading;
using ZL.DeviceLib.Events;

namespace ZL.DeviceLib.Devices.Transport
{
    public abstract class TransportBaseWithState : StreamTransportBase, IHealthyDevice
    {
        private DeviceState _state = DeviceState.Unknown;
        protected readonly string _deviceKey;

        protected TransportBaseWithState(string deviceKey)
        {
            _deviceKey = deviceKey;
        }

        /// <summary>
        /// 当前状态
        /// </summary>
        public DeviceState State => _state;

        /// <summary>
        /// 设置状态并触发全局事件
        /// </summary>
        protected void SetState(DeviceState newState, string info = null)
        {
            if (_state == newState) return;
            _state = newState;

            DeviceNotifier.DeviceStateChangedEvent?.Invoke(_deviceKey, newState);
            if (!string.IsNullOrEmpty(info))
                DeviceNotifier.DeviceInfoChangedEvent?.Invoke(_deviceKey, info);
        }
        public override bool IsHealthy() => _state == DeviceState.Connected;

        protected override void DoFlush() { /* 默认无操作，子类实现 */ }

        public override void Dispose()
        {
            base.Dispose();
            SetState(DeviceState.Disconnected);
        }
        // ========== 保留 StartListening 的两个重载 ==========
        public void StartListening(IFrameSplitter splitter, Action<byte[]> onFrame, CancellationToken token)
        {
            base.StartListening(splitter, onFrame, token, ex =>
            {
                SetState(DeviceState.Disconnected, ex.Message);
            });
        }

        public void StartListening(IFrameSplitter splitter, Action<string> onFrame, CancellationToken token, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.ASCII;
            base.StartListening(splitter, bytes =>
            {
                string chunk = encoding.GetString(bytes);
                onFrame?.Invoke(chunk);
            }, token, ex =>
            {
                SetState(DeviceState.Disconnected, ex.Message);
            });
        }
    }
}
