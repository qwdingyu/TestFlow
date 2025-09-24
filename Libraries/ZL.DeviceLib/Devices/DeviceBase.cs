using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices.Transport;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices
{
    public abstract class DeviceBase : ICapabilityDevice, IHealthyDevice, IDisposable
    {
        protected readonly ITransport _io;
        protected readonly DeviceConfig _cfg;

        // 握手/初始化配置
        protected readonly HandshakeSpec _handshake;
        protected readonly List<InitStepSpec> _initSteps;

        protected readonly string _deviceName;

        // 传输默认参数（可由 Settings 覆盖）
        private readonly Encoding _encoding;
        private readonly byte _delimiter;

        // 并发门禁（防止多线程同时握手）
        private readonly SemaphoreSlim _initGate = new(1, 1);
        private volatile bool _ready = false;
        private volatile bool _disposed = false;

        protected DeviceBase(DeviceConfig cfg, ITransport io)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _io = io ?? throw new ArgumentNullException(nameof(io));
            _deviceName = cfg.Name ?? throw new ArgumentException("DdeviceName需要唯一 Name");

            // 从 Settings 读取 handshake/initSequence（伪代码：按你的配置系统取值）
            _handshake = SettingsBinder.Bind<HandshakeSpec>(_cfg.Settings, "handshake");
            _initSteps = SettingsBinder.Bind<List<InitStepSpec>>(_cfg.Settings, "initSequence") ?? new List<InitStepSpec>();
            // 传输默认项：encoding + delimiter（大小写不敏感）
            var encName = SettingsBinder.Get<string>(_cfg.Settings, "transport.encoding", "ascii");
            _encoding = encName.Equals("utf8", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8 : Encoding.ASCII;

            var delimStr = SettingsBinder.Get<string>(_cfg.Settings, "transport.delimiter", "\n");
            _delimiter = string.IsNullOrEmpty(delimStr) ? (byte)'\n' : (byte)delimStr[0];
        }

        public bool IsHealthy() => !_disposed && _io.IsHealthy();

        public bool Ready => System.Threading.Volatile.Read(ref _ready);

        /// <summary>在传输层断线/重连后调用，强制下次命令重新握手</summary>
        public void InvalidateReady() => System.Threading.Volatile.Write(ref _ready, false);

        public async Task EnsureReadyAsync(CancellationToken token)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            if (_ready) return;

            await _initGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_ready) return;
                await RunHandshakeAndInitAsync(token).ConfigureAwait(false);
                _ready = true;
            }
            finally { _initGate.Release(); }
        }

        private async Task RunHandshakeAndInitAsync(CancellationToken token)
        {
            // 1) Handshake（可选）
            if (_handshake != null)
            {
                var ok = await RunHandshakeAsync(_handshake, token).ConfigureAwait(false);
                if (!ok) throw new InvalidOperationException($"Device '{_cfg.Name}' handshake failed.");

                if (_handshake.DelayAfterMs > 0)
                    await Task.Delay(_handshake.DelayAfterMs, token).ConfigureAwait(false);

                // 验证（可选）
                if (_handshake.Verify != null)
                {
                    var raw = await SendAndMaybeReceiveAsync(
                        _handshake.Verify.Cmd, expectResponse: true,
                        timeoutMs: Math.Max(200, _handshake.TimeoutMs), token).ConfigureAwait(false);

                    var text = _encoding.GetString(raw?.ToArray() ?? Array.Empty<byte>()).Trim();
                    if (string.IsNullOrEmpty(text)) throw new InvalidOperationException($"Device '{_cfg.Name}' verify failed: empty.");

                    // 忽略大小写；需要正则可扩展 Verify.Regex
                    if (!string.IsNullOrEmpty(_handshake.Verify.Contains) &&
                        text.IndexOf(_handshake.Verify.Contains, StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException($"Device '{_cfg.Name}' verify failed. Resp='{text}'");
                }
            }

            // 2) Init Sequence（可选）
            foreach (var step in _initSteps)
            {
                var data = await SendAndMaybeReceiveAsync(step.Cmd, step.ExpectResponse, step.TimeoutMs, token).ConfigureAwait(false);
                if (step.ExpectResponse && (data == null || data.Value.Span.Length == 0))
                    throw new TimeoutException($"Init step no response: {step.Cmd}");

                if (step.DelayAfterMs > 0)
                    await Task.Delay(step.DelayAfterMs, token).ConfigureAwait(false);
            }
        }

        private async Task<bool> RunHandshakeAsync(HandshakeSpec hs, CancellationToken token)
        {
            // 简单指数退避：200, 400, 800...
            int delay = Math.Max(50, hs.RetryDelayMs);
            for (int i = 0; i <= hs.Retries; i++)
            {
                try
                {
                    var data = await SendAndMaybeReceiveAsync(hs.Cmd, hs.ExpectResponse, hs.TimeoutMs, token).ConfigureAwait(false);
                    if (!hs.ExpectResponse || (data != null && data.Value.Span.Length > 0))
                        return true;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* ignore and retry */ }

                if (i < hs.Retries)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    delay = Math.Min(delay * 2, 2000); // 上限 2s
                }
            }
            return false;
        }

        /// <summary>底层发送/可选接收（统一走传输层）；默认使用基类配置的编码和分隔符</summary>
        protected async Task<ReadOnlyMemory<byte>?> SendAndMaybeReceiveAsync(
            string cmd, bool expectResponse, int timeoutMs, CancellationToken token, byte? delimiter = null)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            if (!string.IsNullOrEmpty(cmd))
                await _io.SendAsync(_encoding.GetBytes(cmd), token).ConfigureAwait(false);

            if (!expectResponse) return null;

            var frames = await _io.ReceiveAsync(
                expectedLen: 0,
                timeout: TimeSpan.FromMilliseconds(timeoutMs),
                token: token,
                keepAllFrames: false,
                delimiter: delimiter ?? _delimiter).ConfigureAwait(false);

            return frames.Count > 0 ? frames[0] : (ReadOnlyMemory<byte>?)null;
        }

        // ========== ICapabilityDevice 默认实现（子类只需实现 CallAsync） ==========
        public virtual ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            // 避免 UI 线程同步等待导致死锁
            var dict = Task.Run(() => CallAsync(step.Command, step.Parameters ?? new(), ctx)).GetAwaiter().GetResult();
            return new ExecutionResult { Success = true, Outputs = dict };
        }

        public abstract Task<Dictionary<string, object>> CallAsync(string cap, Dictionary<string, object> args, StepContext stepCtx);

        // ========== Dispose 模式 ==========
        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _initGate?.Dispose();
            }
            catch { /* ignore */ }

            try { _io?.Dispose(); } catch { /* ignore */ }
        }
    }
}