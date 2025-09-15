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
        public object Get(string key) { object v; return Bag.TryGetValue(key, out v) ? v : null; }

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
