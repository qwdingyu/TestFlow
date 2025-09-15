using System;
using System.IO;
using System.Text;
using System.Threading;
using ZL.DeviceLib.Engine;

namespace TestFlowDemo
{
    public class PoolMonitor : IDisposable
    {
        private readonly Timer _timer;
        private readonly string _logFile;
        private readonly object _lock = new object();

        // 上次快照
        private long _lastHits;
        private long _lastMisses;
        private int _lastIdle;
        private long _lastMem;

        public PoolMonitor(string logDir, int intervalSec = 30)
        {
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            _logFile = Path.Combine(logDir, $"PoolMonitor_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            // 写表头
            File.AppendAllText(_logFile,
                "Time | Capacity | Idle | Hits | Misses | HitRate | ΔHits | ΔMisses | ΔIdle | Mem(MB) | ΔMem(MB) | Gen0 | Gen1 | Gen2\n",
                Encoding.UTF8);

            // 初始内存快照
            _lastMem = GC.GetTotalMemory(false);

            _timer = new Timer(WriteLog, null, 0, intervalSec * 1000);
        }

        private void WriteLog(object state)
        {
            try
            {
                var stats = StepResultPool.Instance.GetStats();

                // Δ 计算
                long deltaHits = stats.Hits - _lastHits;
                long deltaMisses = stats.Misses - _lastMisses;
                int deltaIdle = stats.IdleCount - _lastIdle;

                long memNow = GC.GetTotalMemory(false);
                long deltaMem = memNow - _lastMem;

                int gen0 = GC.CollectionCount(0);
                int gen1 = GC.CollectionCount(1);
                int gen2 = GC.CollectionCount(2);

                string line =
                    $"{DateTime.Now:u} | " +
                    $"{stats.Capacity,3} | {stats.IdleCount,3} | {stats.Hits,5} | {stats.Misses,3} | {stats.HitRate,6:P1} | " +
                    $"{deltaHits,4} | {deltaMisses,3} | {deltaIdle,3} | " +
                    $"{memNow / 1024.0 / 1024.0,7:F2} | {deltaMem / 1024.0 / 1024.0,7:F2} | " +
                    $"{gen0,3} | {gen1,3} | {gen2,3}";

                lock (_lock)
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
                }

                // 更新快照
                _lastHits = stats.Hits;
                _lastMisses = stats.Misses;
                _lastIdle = stats.IdleCount;
                _lastMem = memNow;
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    File.AppendAllText(_logFile, $"{DateTime.Now:u} ERROR {ex.Message}\n", Encoding.UTF8);
                }
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
