using System.Collections.Generic;

namespace TestFlowDemo.Tests.Helpers
{
    /// <summary>
    ///     测试用的设备行为描述，方便在不同步骤上配置执行延迟和结果。
    /// </summary>
    public sealed class FakeStepBehavior
    {
        /// <summary>
        ///     默认配置：快速成功，无额外输出。
        /// </summary>
        public static FakeStepBehavior Default { get; } = new FakeStepBehavior();

        /// <summary>
        ///     设备执行延迟（毫秒），用于模拟真实硬件等待时间。
        /// </summary>
        public int DelayMs { get; set; } = 80;

        /// <summary>
        ///     指示步骤是否应该返回成功；用于构造异常或失败场景。
        /// </summary>
        public bool ShouldSucceed { get; set; } = true;

        /// <summary>
        ///     自定义失败提示信息，便于断言日志内容。
        /// </summary>
        public string FailureMessage { get; set; } = "模拟设备失败";

        /// <summary>
        ///     设备执行完成后回写的输出数据，可被后续断言或调试查看。
        /// </summary>
        public Dictionary<string, object> Outputs { get; set; } = new Dictionary<string, object>();

        /// <summary>
        ///     复制一个新的行为配置，避免共享引用带来的串扰。
        /// </summary>
        public FakeStepBehavior Clone()
        {
            return new FakeStepBehavior
            {
                DelayMs = DelayMs,
                ShouldSucceed = ShouldSucceed,
                FailureMessage = FailureMessage,
                Outputs = Outputs != null ? new Dictionary<string, object>(Outputs) : new Dictionary<string, object>()
            };
        }
    }
}
