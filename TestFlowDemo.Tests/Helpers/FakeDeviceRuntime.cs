using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace TestFlowDemo.Tests.Helpers
{
    /// <summary>
    ///     负责跟踪假设备的执行过程，统计并发度、顺序以及时间线，方便断言流程行为。
    /// </summary>
    public sealed class FakeDeviceRuntime
    {
        private int _running;
        private int _maxRunning;
        private readonly object _syncRoot = new object();
        private readonly List<StepExecutionRecord> _timeline = new List<StepExecutionRecord>();
        private readonly List<string> _order = new List<string>();

        /// <summary>
        ///     每次测试前重置内部状态，避免跨用例污染。
        /// </summary>
        public void Reset()
        {
            _running = 0;
            _maxRunning = 0;
            lock (_syncRoot)
            {
                _timeline.Clear();
                _order.Clear();
            }
        }

        /// <summary>
        ///     核心执行入口：根据配置模拟延迟/成功或失败，并记录时间线信息。
        /// </summary>
        public DeviceExecResult Execute(string deviceId, StepConfig step, StepContext context, FakeStepBehavior behavior)
        {
            behavior ??= FakeStepBehavior.Default;

            var start = DateTime.UtcNow;
            var current = Interlocked.Increment(ref _running);
            UpdateMax(current);
            lock (_syncRoot)
            {
                _order.Add(step.Name);
            }

            try
            {
                if (behavior.DelayMs > 0)
                {
                    Task.Delay(behavior.DelayMs, context.Cancellation).Wait(context.Cancellation);
                }

                if (!behavior.ShouldSucceed)
                {
                    return new DeviceExecResult
                    {
                        Success = false,
                        Message = behavior.FailureMessage,
                        Outputs = new Dictionary<string, object>
                        {
                            ["device"] = deviceId,
                            ["step"] = step.Name,
                            ["status"] = "failed"
                        }
                    };
                }

                var outputs = behavior.Outputs != null
                    ? new Dictionary<string, object>(behavior.Outputs)
                    : new Dictionary<string, object>();
                outputs["device"] = deviceId;
                outputs["step"] = step.Name;

                return new DeviceExecResult
                {
                    Success = true,
                    Message = "模拟设备执行成功",
                    Outputs = outputs
                };
            }
            catch (OperationCanceledException)
            {
                return new DeviceExecResult
                {
                    Success = false,
                    Message = "步骤被取消",
                    Outputs = new Dictionary<string, object>
                    {
                        ["device"] = deviceId,
                        ["step"] = step.Name,
                        ["status"] = "cancelled"
                    }
                };
            }
            finally
            {
                var end = DateTime.UtcNow;
                lock (_syncRoot)
                {
                    _timeline.Add(new StepExecutionRecord(step.Name, deviceId, start, end));
                }
                Interlocked.Decrement(ref _running);
            }
        }

        private void UpdateMax(int current)
        {
            int snapshot;
            while (true)
            {
                snapshot = _maxRunning;
                if (current <= snapshot) break;
                if (Interlocked.CompareExchange(ref _maxRunning, current, snapshot) == snapshot) break;
            }
        }

        /// <summary>
        ///     快照化当前的执行统计，便于测试断言。
        /// </summary>
        public RuntimeSnapshot CreateSnapshot()
        {
            lock (_syncRoot)
            {
                return new RuntimeSnapshot
                {
                    StepOrder = _order.ToArray(),
                    Timeline = _timeline.OrderBy(t => t.Start).ToArray(),
                    MaxConcurrency = _maxRunning
                };
            }
        }
    }

    /// <summary>
    ///     单步执行的时间片段记录，包含起止时间和设备信息。
    /// </summary>
    public sealed class StepExecutionRecord
    {
        public StepExecutionRecord(string stepName, string deviceId, DateTime start, DateTime end)
        {
            StepName = stepName;
            DeviceId = deviceId;
            Start = start;
            End = end;
        }

        /// <summary>
        ///     步骤名称，便于人工核对日志。
        /// </summary>
        public string StepName { get; }

        /// <summary>
        ///     执行该步骤的设备标识。
        /// </summary>
        public string DeviceId { get; }

        /// <summary>
        ///     步骤开始时间（UTC），用于判定是否串行。
        /// </summary>
        public DateTime Start { get; }

        /// <summary>
        ///     步骤结束时间（UTC）。
        /// </summary>
        public DateTime End { get; }
    }

    /// <summary>
    ///     将执行结果打包成结构体，测试用例可直接读取。
    /// </summary>
    public sealed class RuntimeSnapshot
    {
        public string[] StepOrder { get; set; } = Array.Empty<string>();
        public StepExecutionRecord[] Timeline { get; set; } = Array.Empty<StepExecutionRecord>();
        public int MaxConcurrency { get; set; }
    }
}
