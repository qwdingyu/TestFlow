using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZL.Orchestration;

namespace ZL.Orchestration.Tests
{
    /// <summary>
    ///     覆盖编排器的核心能力验证，确保依赖、锁、超时与重试逻辑可靠。
    /// </summary>
    public sealed class OrchestratorTests
    {
        /// <summary>
        ///     验证依赖解析能够保证顺序执行。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ShouldHonorDependencies()
        {
            List<string> executionOrder = new List<string>();
            DelegateExecutor executor = new DelegateExecutor(async delegate(OrchTask task, CancellationToken token)
            {
                // 记录执行顺序，便于断言依赖链路。
                lock (executionOrder)
                {
                    executionOrder.Add(task.Id);
                }

                await Task.Delay(20, token).ConfigureAwait(false);
                IDictionary<string, object> output = new Dictionary<string, object>();
                output["result"] = task.Id;
                return output;
            });

            OrchestrationPlan plan = new OrchestrationPlan();
            plan.Tasks.Add(new OrchTask { Id = "A" });
            plan.Tasks.Add(new OrchTask { Id = "B", DependsOn = new List<string> { "A" } });
            plan.Tasks.Add(new OrchTask { Id = "C", DependsOn = new List<string> { "B" } });

            Orchestrator orchestrator = new Orchestrator(executor);
            OrchestrationResult result = await orchestrator.ExecuteAsync(plan, CancellationToken.None).ConfigureAwait(false);

            Assert.True(result.Success);
            Assert.Equal(3, executionOrder.Count);
            Assert.True(executionOrder.IndexOf("A") < executionOrder.IndexOf("B"));
            Assert.True(executionOrder.IndexOf("B") < executionOrder.IndexOf("C"));
        }

        /// <summary>
        ///     验证资源锁能够防止共享资源上的并发。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ShouldEnforceResourceLocks()
        {
            int concurrentCounter = 0;
            int maxObserved = 0;
            DelegateExecutor executor = new DelegateExecutor(async delegate(OrchTask task, CancellationToken token)
            {
                int current = Interlocked.Increment(ref concurrentCounter);
                UpdateMax(ref maxObserved, current);

                await Task.Delay(50, token).ConfigureAwait(false);

                Interlocked.Decrement(ref concurrentCounter);
                IDictionary<string, object> output = new Dictionary<string, object>();
                output["task"] = task.Id;
                return output;
            });

            OrchestrationPlan plan = new OrchestrationPlan();
            plan.Tasks.Add(new OrchTask { Id = "T1", ResourceId = "shared" });
            plan.Tasks.Add(new OrchTask { Id = "T2", ResourceId = "shared" });

            Orchestrator orchestrator = new Orchestrator(executor);
            OrchestrationResult result = await orchestrator.ExecuteAsync(plan, CancellationToken.None).ConfigureAwait(false);

            Assert.True(result.Success);
            Assert.Equal(1, maxObserved);
        }

        /// <summary>
        ///     验证超时逻辑会导致执行失败并返回对应的任务信息。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ShouldFailOnTimeout()
        {
            DelegateExecutor executor = new DelegateExecutor(async delegate(OrchTask task, CancellationToken token)
            {
                await Task.Delay(200, token).ConfigureAwait(false);
                IDictionary<string, object> output = new Dictionary<string, object>();
                output["task"] = task.Id;
                return output;
            });

            OrchestrationPlan plan = new OrchestrationPlan();
            plan.Tasks.Add(new OrchTask { Id = "TimeoutTask", TimeoutMs = 50 });

            Orchestrator orchestrator = new Orchestrator(executor);
            OrchestrationResult result = await orchestrator.ExecuteAsync(plan, CancellationToken.None).ConfigureAwait(false);

            Assert.False(result.Success);
            Assert.Equal("TimeoutTask", result.FailedTaskId);
            Assert.Contains("超时", result.Message);
        }

        /// <summary>
        ///     验证重试能够在若干次失败后成功，并记录尝试次数。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ShouldRetryAfterFailures()
        {
            int attempt = 0;
            DelegateExecutor executor = new DelegateExecutor(async delegate(OrchTask task, CancellationToken token)
            {
                attempt++;
                if (attempt < 3)
                {
                    throw new InvalidOperationException("模拟失败");
                }

                IDictionary<string, object> output = new Dictionary<string, object>();
                output["attempt"] = attempt;
                return output;
            });

            OrchestrationPlan plan = new OrchestrationPlan();
            plan.Tasks.Add(new OrchTask
            {
                Id = "RetryTask",
                Retry = new RetrySpec { Attempts = 3, DelayMs = 10 }
            });

            Orchestrator orchestrator = new Orchestrator(executor);
            OrchestrationResult result = await orchestrator.ExecuteAsync(plan, CancellationToken.None).ConfigureAwait(false);

            Assert.True(result.Success);
            TaskExecutionSummary summary = result.TaskSummaries["RetryTask"];
            Assert.Equal(3, summary.Attempts);
            Assert.Equal(3, (int)summary.Output["attempt"]);
        }

        /// <summary>
        ///     验证若先发生超时再重试成功，会在摘要中记录曾经超时。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ShouldRememberTimeoutDuringRetry()
        {
            int attempt = 0;
            DelegateExecutor executor = new DelegateExecutor(async delegate(OrchTask task, CancellationToken token)
            {
                attempt++;
                if (attempt == 1)
                {
                    await Task.Delay(task.TimeoutMs + 50, token).ConfigureAwait(false);
                }

                IDictionary<string, object> output = new Dictionary<string, object>();
                output["attempt"] = attempt;
                return output;
            });

            OrchestrationPlan plan = new OrchestrationPlan();
            plan.Tasks.Add(new OrchTask
            {
                Id = "RetryAfterTimeout",
                TimeoutMs = 80,
                Retry = new RetrySpec { Attempts = 2, DelayMs = 10 }
            });

            Orchestrator orchestrator = new Orchestrator(executor);
            OrchestrationResult result = await orchestrator.ExecuteAsync(plan, CancellationToken.None).ConfigureAwait(false);

            Assert.True(result.Success);
            TaskExecutionSummary summary = result.TaskSummaries["RetryAfterTimeout"];
            Assert.True(summary.TimedOut);
            Assert.Equal(2, summary.Attempts);
        }

        /// <summary>
        ///     利用 Interlocked.CompareExchange 实现无锁的最大值更新。
        /// </summary>
        private static void UpdateMax(ref int target, int candidate)
        {
            int initial;
            int computed;
            do
            {
                initial = target;
                computed = initial > candidate ? initial : candidate;
            }
            while (Interlocked.CompareExchange(ref target, computed, initial) != initial);
        }

        /// <summary>
        ///     便捷的委托执行器，方便在测试内自定义行为。
        /// </summary>
        private sealed class DelegateExecutor : IOrchTaskExecutor
        {
            private readonly Func<OrchTask, CancellationToken, Task<IDictionary<string, object>>> _handler;

            public DelegateExecutor(Func<OrchTask, CancellationToken, Task<IDictionary<string, object>>> handler)
            {
                _handler = handler;
            }

            public Task<IDictionary<string, object>> ExecuteAsync(OrchTask task, CancellationToken cancellationToken)
            {
                return _handler(task, cancellationToken);
            }
        }
    }
}
