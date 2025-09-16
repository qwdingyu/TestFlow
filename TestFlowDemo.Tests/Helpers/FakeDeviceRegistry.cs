using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TestFlowDemo.Tests.Helpers
{
    /// <summary>
    ///     维护测试期间的设备行为映射，并提供运行时实例引用。
    /// </summary>
    public static class FakeDeviceRegistry
    {
        private static readonly ConcurrentDictionary<string, FakeStepBehavior> _behaviors =
            new ConcurrentDictionary<string, FakeStepBehavior>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     运行时模拟器的引用，由测试装置在运行前注入。
        /// </summary>
        public static FakeDeviceRuntime? Runtime { get; set; }
        /// <summary>
        ///     清空所有配置，通常在每个测试用例开始时调用。
        /// </summary>
        public static void Reset()
        {
            _behaviors.Clear();
            Runtime = null;
        }

        /// <summary>
        ///     为指定步骤名称注册行为；如重复注册将覆盖旧值。
        /// </summary>
        public static void Configure(string stepName, FakeStepBehavior behavior)
        {
            if (string.IsNullOrWhiteSpace(stepName)) return;
            _behaviors[stepName] = behavior?.Clone() ?? FakeStepBehavior.Default.Clone();
        }

        /// <summary>
        ///     获取某步骤的配置；若不存在则返回默认行为。
        /// </summary>
        public static FakeStepBehavior GetBehavior(string stepName)
        {
            if (string.IsNullOrWhiteSpace(stepName)) return FakeStepBehavior.Default;
            if (_behaviors.TryGetValue(stepName, out var behavior)) return behavior;
            return FakeStepBehavior.Default;
        }
    }
}
