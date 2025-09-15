using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ZL.DeviceLib.Engine
{
    public class StepResult
    {
        public string StepName;
        public bool Success;
        public string Message;
        public Dictionary<string, object> Outputs;
        public void Reset()
        {
            StepName = string.Empty; Success = false; Message = string.Empty;
            if (Outputs == null) Outputs = new Dictionary<string, object>(); else Outputs.Clear();
        }
    }

    public class StepResultPool
    {
        private readonly ConcurrentBag<StepResult> _pool = new ConcurrentBag<StepResult>();
        private readonly int _maxSize;
        private static readonly Lazy<StepResultPool> _instance = new Lazy<StepResultPool>(() => new StepResultPool(200));
        private long _hits; private long _misses;
        public static StepResultPool Instance { get { return _instance.Value; } }
        private StepResultPool(int maxSize) { _maxSize = maxSize; }
        public StepResult Get()
        {
            StepResult obj; if (_pool.TryTake(out obj)) { Interlocked.Increment(ref _hits); obj.Reset(); return obj; }
            Interlocked.Increment(ref _misses); return new StepResult { Outputs = new Dictionary<string, object>() };
        }
        public void Return(StepResult obj)
        { if (_pool.Count < _maxSize) { obj.Reset(); _pool.Add(obj); } }
        public PoolStats GetStats()
        {
            long hits = Interlocked.Read(ref _hits); long misses = Interlocked.Read(ref _misses);
            double hitRate = (hits + misses) > 0 ? (double)hits / (hits + misses) : 0;
            return new PoolStats { Capacity = _maxSize, IdleCount = _pool.Count, Hits = hits, Misses = misses, HitRate = hitRate };
        }
    }
    public class PoolStats
    {
        public int Capacity { get; set; }
        public int IdleCount { get; set; }
        public long Hits { get; set; }
        public long Misses { get; set; }
        public double HitRate { get; set; }
    }
}

