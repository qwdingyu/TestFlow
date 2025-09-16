using System;
using System.Collections.Generic;
using System.Threading;

namespace ZL.DeviceLib.Engine
{
    public class StepContext
    {
        public string Model { get; private set; }
        public Dictionary<string, object> Bag { get; private set; }
        public HashSet<string> Completed { get; private set; }
        public CancellationToken Cancellation { get; }
        public StepContext(string model, CancellationToken cancellation)
        {
            Model = model;
            Bag = new Dictionary<string, object>();
            Completed = new HashSet<string>();
            Cancellation = cancellation;
        }
        public void Set(string key, object value) { Bag[key] = value; }

        /// <summary>
        /// 根据键名尝试读取上下文中的数据，并转换为指定的类型；
        /// 若未找到或转换失败，则返回类型的默认值（值类型为默认值，引用类型为 null）。
        /// </summary>
        /// <typeparam name="T">期望返回的目标类型。</typeparam>
        /// <param name="key">存储在 Bag 中的键名。</param>
        /// <returns>转换成功后的值，或在失败/缺失时返回默认值。</returns>
        public T Get<T>(string key)
        {
            if (!Bag.TryGetValue(key, out var value) || value == null)
                return default;

            if (value is T matched)
                return matched;

            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// 读取原始对象，兼容旧有调用；内部委托给泛型版本以减少重复逻辑。
        /// </summary>
        public object Get(string key) { return Get<object>(key); }

        // 克隆一个新的上下文，共享 Bag/Completed，但替换取消令牌（用于步骤级超时）
        public StepContext CloneWithCancellation(CancellationToken newToken)
        {
            var cloned = new StepContext(this.Model, newToken);
            cloned.Bag = this.Bag;
            cloned.Completed = this.Completed;
            return cloned;
        }
    }
}
