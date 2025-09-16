using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ZL.DeviceLib.Engine
{
    /// <summary>
    /// 用于封装步骤执行结果的数据结构，提供基础的执行状态与输出内容。
    /// </summary>
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

        /// <summary>
        /// 重置复用对象的状态，确保返回对象池后不会残留旧数据。
        /// </summary>
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

    /// <summary>
    /// 面向步骤执行结果的对象池，避免频繁分配与回收所造成的 GC 压力。
    /// </summary>
    public class StepResultPool
    {
        private readonly ConcurrentBag<StepResult> _pool = new ConcurrentBag<StepResult>();
        private readonly int _maxSize;
        private static readonly Lazy<StepResultPool> _instance = new Lazy<StepResultPool>(() => new StepResultPool(200));
        private long _hits; private long _misses;
        /// <summary>
        /// 提供全局共享的对象池实例，避免在不同模块中重复创建池。
        /// </summary>
        public static StepResultPool Instance { get { return _instance.Value; } }

        /// <summary>
        /// 返回对象池当前的命中率；命中率越高，说明复用越充分。
        /// </summary>
        public double HitRate
        {
            get
            {
                long hits = Interlocked.Read(ref _hits);
                long misses = Interlocked.Read(ref _misses);
                return CalculateHitRate(hits, misses);
            }
        }

        /// <summary>
        /// 返回对象池内可立即复用的空闲对象数量。
        /// </summary>
        public int IdleCount
        {
            get { return _pool.Count; }
        }

        /// <summary>
        /// 获取对象池的容量上限，便于监控与调优。
        /// </summary>
        public int Capacity
        {
            get { return _maxSize; }
        }

        private StepResultPool(int maxSize)
        {
            _maxSize = maxSize;
        }

        /// <summary>
        /// 从对象池中获取一个 <see cref="StepResult"/> 实例，若无可用对象则新建。
        /// </summary>
        /// <returns>返回可供步骤使用的结果对象。</returns>
        public StepResult Get()
        {
            StepResult obj;
            if (_pool.TryTake(out obj))
            {
                Interlocked.Increment(ref _hits);
                obj.Reset();
                return obj;
            }

            // 新建对象时依赖属性的默认初始化逻辑，避免重复创建字典对象。
            Interlocked.Increment(ref _misses);
            return new StepResult();
        }

        /// <summary>
        /// 将使用完毕的结果对象归还对象池；当池已满时直接丢弃以控制占用。
        /// </summary>
        /// <param name="obj">需要回收的结果对象。</param>
        public void Return(StepResult obj)
        {
            if (obj == null)
            {
                return;
            }

            if (_pool.Count < _maxSize)
            {
                obj.Reset();
                _pool.Add(obj);
            }
        }

        /// <summary>
        /// 获取对象池的实时统计信息，便于在日志或监控中观测复用情况。
        /// </summary>
        /// <returns>包含容量、命中、未命中与命中率信息的统计结构体。</returns>
        public PoolStats GetStats()
        {
            long hits = Interlocked.Read(ref _hits);
            long misses = Interlocked.Read(ref _misses);
            double hitRate = CalculateHitRate(hits, misses);
            PoolStats stats = new PoolStats();
            stats.Capacity = _maxSize;
            stats.IdleCount = _pool.Count;
            stats.Hits = hits;
            stats.Misses = misses;
            stats.HitRate = hitRate;
            return stats;
        }

        private static double CalculateHitRate(long hits, long misses)
        {
            long total = hits + misses;
            if (total > 0)
            {
                return (double)hits / total;
            }

            return 0.0d;
        }
    }
    /// <summary>
    /// 描述对象池运行状态的统计信息，便于在日志与监控中展示关键指标。
    /// </summary>
    public class PoolStats
    {
        /// <summary>
        /// 对象池允许存储的最大对象数量，用于评估池的容量配置是否合理。
        /// </summary>
        public int Capacity { get; set; }

        /// <summary>
        /// 当前在池中的空闲对象数量，用于衡量对象复用的实时情况。
        /// </summary>
        public int IdleCount { get; set; }

        /// <summary>
        /// 从池中成功取出并复用的次数，数值越大说明复用效率越高。
        /// </summary>
        public long Hits { get; set; }

        /// <summary>
        /// 因池中无可用对象而触发新建的次数，可帮助判断池容量是否不足。
        /// </summary>
        public long Misses { get; set; }

        /// <summary>
        /// 对象池的命中率（Hits / (Hits + Misses)），在 0~1 之间浮动。
        /// </summary>
        public double HitRate { get; set; }
    }
}

