using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Devices;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Events;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Storage;

namespace ZL.DeviceLib
{
    public class SeatTestRunner
    {
        private readonly ResultAggregator _aggregator = new();
        string _dbTypeString = "MySql";
        string _connectionString = "server=127.0.0.1;port=3306;database=SeatTest;user=root;password=123456;charset=utf8mb4;SslMode=None";
        DbServices _db;
        public DeviceFactory factory { get; }
        public DeviceManager manager { get; }

        private CancellationTokenSource _cts;
        private bool _disposed;
        private readonly Stopwatch _totalSw = new();
        public  SeatTestRunner()
        {
            _db = (DbServices)DeviceServices.Db;

            // 1) 初始化工厂（内部完成内置注册 + 插件载入）
            factory = new DeviceFactory();
            manager = new DeviceManager(factory, DeviceServices.DevicesCfg);
            DeviceServices.Factory = factory;
        }
        public SeatTestRunner(string dbTypeString, string connectionString)
        {
            _dbTypeString = dbTypeString;
            _connectionString = connectionString;
            _db = new DbServices(_dbTypeString, _connectionString);
            DeviceServices.Db = _db;
            //  初始化 DeviceFactory
            factory = new DeviceFactory();
            manager = new DeviceManager(factory, DeviceServices.DevicesCfg);
            DeviceServices.Factory = factory;
        }
        public async Task InitializeAsync(CancellationToken ct = default)
        {
            // 3) 设备管理器：并发初始化（握手/初始化）
            await manager.InitializeAsync(ct, maxParallel: 4);
            factory.PublishInitialStates();
            DeviceServices.Factory = factory;
        }
        public async Task RunTestsAsync(IEnumerable<StepConfig> steps, string model, string barcode, CancellationToken token)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SeatTestRunner));
            _cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            TestEvents.StatusChanged?.Invoke("Running");
            TestEvents.Log?.Invoke("INFO", "测试开始");
            _totalSw.Restart();

            // 按 ParallelGroup 分组（null/空字符串为各自独立组，单步顺序执行）
            //var grouped = steps.GroupBy(s => string.IsNullOrWhiteSpace(s.ParallelGroup) ? Guid.NewGuid().ToString() : s.ParallelGroup).ToList();
            var grouped = steps.GroupBy(s => string.IsNullOrWhiteSpace(s.ParallelGroup) ? "XXXXXX" : s.ParallelGroup).ToList();

            foreach (var group in grouped)
            {
                if (_cts.IsCancellationRequested) break;
                // TODO 需要认真设计
                // 是否并行：同名 ParallelGroup -> 并行；否则该“组”仅1个元素，顺序执行
                bool isParallel = group.Count() > 1 || !string.IsNullOrEmpty(group.Key);
                //if (!isParallel)
                //{
                // 没有 ParallelGroup → 顺序执行
                foreach (var step in group)
                {
                    if (_cts.IsCancellationRequested) break;

                    using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    if (step.TimeoutMs > 0) stepCts.CancelAfter(step.TimeoutMs);

                    await RunStepAsync(step, model, stepCts.Token).ConfigureAwait(false);
                }
                //}
                //else
                //{
                //    // 相同 ParallelGroup → 并行执行
                //    var tasks = group.Select(step =>
                //    {
                //        var stepCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                //        if (step.TimeoutMs > 0)
                //            stepCts.CancelAfter(step.TimeoutMs);
                //        return RunStepAsync(step, model, stepCts.Token).ContinueWith(t => stepCts.Dispose()); // 确保 stepCts 最终释放
                //    }).ToList();
                //    await Task.WhenAll(tasks).ConfigureAwait(false);
                //}
            }
            _totalSw.Stop();
            // 汇总保存 & 触发完成事件
            TestEvents.StatusChanged?.Invoke("Completed");
            var testing_time = (float)(_totalSw.Elapsed.TotalSeconds);
            var seatResult = _aggregator.ToSeatResults(model, barcode, testing_time, TabColMapping.SeatResultMapping);
            if (seatResult == null)
                TestEvents.StatusChanged?.Invoke("Error");
            try
            {
                _db?.SaveSeatResults(seatResult);
            }
            catch (Exception ex)
            {
                TestEvents.Log?.Invoke("ERROR", $"[DB] 保存失败: {ex.Message}");
                LogHelper.Error($"[DB] 保存失败: {ex.Message}");
            }
            TestEvents.TestCompleted?.Invoke(seatResult);
            TestEvents.StatusChanged?.Invoke("Completed");
            TestEvents.Log?.Invoke("INFO", $"测试完成，总耗时：{_totalSw.ElapsedMilliseconds} ms");
        }

        private async Task RunStepAsync(StepConfig step, string model, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            TestEvents.StepStarted?.Invoke(step.Name);
            TestEvents.Log?.Invoke("INFO", $"开始步骤：{step.Name}");

            try
            {
                var ctx = new StepContext(model, token);
                // DeviceStepRouter.Run 是同步 → 用 Task.Run 包装为异步，避免阻塞调用线程
                var result = await Task.Run(() => DeviceStepRouter.Execute(step, ctx), token).ConfigureAwait(false);
                sw.Stop();

                _aggregator.AddStepResult(step.Name, result.Outputs);
                var _elapsed = Math.Round(sw.ElapsedMilliseconds / 1000.0, 2);
                TestEvents.StepCompleted?.Invoke(step.Name, true, _elapsed, result.Outputs);
                LogHelper.Info($"[PASS] {step.Name} 耗时={_elapsed}s");
                TestEvents.Log?.Invoke(result.Success ? "INFO" : "ERROR", $"步骤结束：{step.Name}, {(result.Success ? "PASS" : "FAIL")}, 耗时={_elapsed}s");
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                var outputs = new Dictionary<string, object> { { "pass", false }, { "error", "超时或取消" } };
                _aggregator.AddStepResult(step.Name, outputs);
                var _elapsed = Math.Round(sw.ElapsedMilliseconds / 1000.0, 2);
                TestEvents.StepCompleted?.Invoke(step.Name, false, _elapsed, outputs);
                TestEvents.Log?.Invoke("WARN", $"步骤取消/超时：{step.Name}, 耗时={_elapsed}s");
            }
            catch (Exception ex)
            {
                sw.Stop();
                var outputs = new Dictionary<string, object> { { "pass", false }, { "error", ex.Message } };
                _aggregator.AddStepResult(step.Name, outputs);
                var _elapsed = Math.Round(sw.ElapsedMilliseconds / 1000.0, 2);
                TestEvents.StepCompleted?.Invoke(step.Name, false, _elapsed, outputs);
                LogHelper.Info($"[FAIL] {step.Name} 耗时={_elapsed}s, 错误={ex.Message}");
                TestEvents.Log?.Invoke("ERROR", $"步骤异常：{step.Name}, {ex.Message}");
            }
        }
        /// <summary>外部调用：主动停止测试</summary>
        public void Stop()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                TestEvents.StatusChanged?.Invoke("Stopped");
                TestEvents.Log?.Invoke("WARN", "收到停止请求，测试流程取消");
            }
        }

        /// <summary>释放资源</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _cts?.Cancel(); _cts?.Dispose(); } catch { }
        }
    }

}
