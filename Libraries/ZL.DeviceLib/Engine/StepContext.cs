using System;
using System.Collections.Generic;
using System.Threading;

namespace ZL.DeviceLib.Engine
{
    /// <summary>
    /// 表示步骤执行过程中的上下文容器，负责跨步骤传递模型名称、共享数据及执行状态。
    /// </summary>
    public class StepContext
    {
        /// <summary>
        /// 当前流程所属的模型名称；用于在执行链中识别不同的模型配置。
        /// </summary>
        public string Model { get; private set; }

        /// <summary>
        /// 存放跨步骤共享的数据字典；键为字符串，值为对象以便容纳不同类型的数据。
        /// </summary>
        public Dictionary<string, object> Bag { get; private set; }

        /// <summary>
        /// 记录已经执行完成的步骤集合；便于在长链路中避免重复执行同一逻辑。
        /// </summary>
        public HashSet<string> Completed { get; private set; }

        /// <summary>
        /// 用于统一管理步骤的取消信号；当任务超时或外部取消时可及时终止。
        /// </summary>
        public CancellationToken Cancellation { get; }

        /// <summary>
        /// 初始化上下文容器，并建立基础的共享存储与步骤完成状态集合。
        /// </summary>
        /// <param name="model">执行流程对应的模型名称。</param>
        /// <param name="cancellation">用于控制取消的 <see cref="CancellationToken"/>。</param>
        public StepContext(string model, CancellationToken cancellation)
        {
            Model = model;
            Bag = new Dictionary<string, object>();
            Completed = new HashSet<string>();
            Cancellation = cancellation;
        }

        /// <summary>
        /// 将数据写入上下文的共享字典中；若键已存在则覆盖旧值。
        /// 例如：<c>context.Set("RetryCount", 3)</c>。
        /// </summary>
        /// <param name="key">需要存储的键名。</param>
        /// <param name="value">待写入的值。</param>
        public void Set(string key, object value)
        {
            Bag[key] = value;
        }

        /// <summary>
        /// 尝试按照指定类型读取上下文中的数据，若读取或转换失败则返回 false。
        /// 例如：<c>if (context.TryGet&lt;int&gt;("RetryCount", out count))</c>。
        /// </summary>
        /// <typeparam name="T">希望获取的目标类型。</typeparam>
        /// <param name="key">需要读取的键名。</param>
        /// <param name="value">输出参数：成功时返回转换后的值，失败时为类型默认值。</param>
        /// <returns>若成功获取并转换为目标类型，则返回 true；否则返回 false。</returns>
        public bool TryGet<T>(string key, out T value)
        {
            value = default(T);

            object storedValue;
            if (!Bag.TryGetValue(key, out storedValue) || storedValue == null)
            {
                return false;
            }

            if (storedValue is T)
            {
                value = (T)storedValue;
                return true;
            }

            try
            {
                value = (T)Convert.ChangeType(storedValue, typeof(T));
                return true;
            }
            catch
            {
                value = default(T);
                return false;
            }
        }

        /// <summary>
        /// 根据键名尝试读取上下文中的数据，并转换为指定的类型；
        /// 若未找到或转换失败，则返回类型的默认值（值类型为默认值，引用类型为 null）。
        /// </summary>
        /// <typeparam name="T">期望返回的目标类型。</typeparam>
        /// <param name="key">存储在 Bag 中的键名。</param>
        /// <returns>转换成功后的值，或在失败/缺失时返回默认值。</returns>
        public T Get<T>(string key)
        {
            T value;
            if (TryGet<T>(key, out value))
            {
                return value;
            }

            return default(T);
        }

        /// <summary>
        /// 按照指定类型读取上下文中的数据；若不存在则返回指定的默认值。
        /// 例如：<c>var timeout = context.GetOrDefault("Timeout", 30);</c>。
        /// </summary>
        /// <typeparam name="T">希望获取的目标类型。</typeparam>
        /// <param name="key">需要读取的键名。</param>
        /// <param name="defaultValue">当键不存在或转换失败时返回的默认值。</param>
        /// <returns>成功获取时返回真实值，否则返回传入的默认值。</returns>
        public T GetOrDefault<T>(string key, T defaultValue)
        {
            T value;
            if (TryGet<T>(key, out value))
            {
                return value;
            }

            return defaultValue;
        }


        /// <summary>
        /// 读取原始对象，兼容旧有调用；内部委托给泛型版本以减少重复逻辑。
        /// </summary>
        /// <param name="key">需要读取的键名。</param>
        /// <returns>返回原始对象的引用；若不存在则返回 null。</returns>
        public object Get(string key)
        {
            return Get<object>(key);
        }

        /// <summary>
        /// 克隆一个新的上下文实例，共享原有数据字典与完成集合，但替换取消令牌。
        /// 常用于在单个步骤内设定独立的超时时间。
        /// </summary>
        /// <param name="newToken">新的取消令牌。</param>
        /// <returns>返回克隆后的上下文实例。</returns>
        public StepContext CloneWithCancellation(CancellationToken newToken)
        {
            var cloned = new StepContext(this.Model, newToken);
            cloned.Bag = this.Bag;
            cloned.Completed = this.Completed;
            return cloned;
        }
    }
}
