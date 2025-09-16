using System;
using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace ZL.WorkflowLib.Workflow
{
    /// <summary>
    /// WorkflowCore 构建器的扩展方法，便于在构建阶段注入延迟与重试等通用配置。
    /// </summary>
    public static class WorkflowStepExtensions
    {
        public static IStepBuilder<FlowData, TStep> WithRetry<TStep>(
            this IStepBuilder<FlowData, TStep> builder,
            Func<FlowData, RetryOptions> selector)
            where TStep : StepBody, IRetryConfigurable
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            builder.Input(step => step.RetryAttempts, data =>
            {
                var normalized = RetryOptions.Normalize(selector != null ? selector(data) : null);
                return normalized.Attempts;
            });

            builder.Input(step => step.RetryDelayMs, data =>
            {
                var normalized = RetryOptions.Normalize(selector != null ? selector(data) : null);
                return normalized.DelayMs;
            });

            return builder;
        }

        public static IStepBuilder<FlowData, TStep> WithDelay<TStep>(
            this IStepBuilder<FlowData, TStep> builder,
            Func<FlowData, DelayOptions> selector)
            where TStep : StepBody, IDelayConfigurable
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            builder.Input(step => step.PreDelayMs, data =>
            {
                var normalized = DelayOptions.Normalize(selector != null ? selector(data) : null);
                return normalized.PreDelayMs;
            });

            builder.Input(step => step.PostDelayMs, data =>
            {
                var normalized = DelayOptions.Normalize(selector != null ? selector(data) : null);
                return normalized.PostDelayMs;
            });

            return builder;
        }
    }

    /// <summary>
    /// 供 StepBody 在执行阶段读取的重试配置。
    /// </summary>
    public interface IRetryConfigurable
    {
        int RetryAttempts { get; set; }
        int RetryDelayMs { get; set; }
    }

    /// <summary>
    /// 供 StepBody 在执行阶段读取的延迟配置。
    /// </summary>
    public interface IDelayConfigurable
    {
        int PreDelayMs { get; set; }
        int PostDelayMs { get; set; }
    }

    /// <summary>
    /// 构建器在注入重试策略时使用的数据承载类型。
    /// </summary>
    public class RetryOptions
    {
        public int Attempts { get; set; }
        public int DelayMs { get; set; }

        public static RetryOptions Normalize(RetryOptions options)
        {
            if (options == null)
                return new RetryOptions { Attempts = 1, DelayMs = 0 };

            return new RetryOptions
            {
                Attempts = options.Attempts > 0 ? options.Attempts : 1,
                DelayMs = options.DelayMs > 0 ? options.DelayMs : 0
            };
        }
    }

    /// <summary>
    /// 构建器在注入延迟策略时使用的数据承载类型。
    /// </summary>
    public class DelayOptions
    {
        public int PreDelayMs { get; set; }
        public int PostDelayMs { get; set; }

        public static DelayOptions Normalize(DelayOptions options)
        {
            if (options == null)
                return new DelayOptions { PreDelayMs = 0, PostDelayMs = 0 };

            return new DelayOptions
            {
                PreDelayMs = options.PreDelayMs > 0 ? options.PreDelayMs : 0,
                PostDelayMs = options.PostDelayMs > 0 ? options.PostDelayMs : 0
            };
        }
    }
}
