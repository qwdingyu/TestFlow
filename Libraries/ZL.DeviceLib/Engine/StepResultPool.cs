using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ZL.DeviceLib.Engine
{
    public class StepResult
    {
        /// <summary>
        /// 当前步骤的名称，使用属性以便更好地进行封装与扩展。
        /// </summary>
        public string StepName { get; set; } = string.Empty;

        /// <summary>
        /// 当前步骤执行是否成功，使用属性便于外部读取与设置。
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 当前步骤的输出信息或错误提示。
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 当前步骤的输出字典，保存步骤执行产出的数据。
        /// </summary>
        public Dictionary<string, object> Outputs { get; set; } = new Dictionary<string, object>();

        public void Reset()
        {
            // 复用对象时重置所有属性，确保不会残留旧数据。
            StepName = string.Empty;
            Success = false;
            Message = string.Empty;
            if (Outputs == null)
            {
                Outputs = new Dictionary<string, object>();
            }
            else
            {
                Outputs.Clear();
            }
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
            // 新建对象时依赖属性的默认初始化逻辑，避免重复创建字典对象。
            Interlocked.Increment(ref _misses); return new StepResult();
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

