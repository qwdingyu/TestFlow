using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ZL.DeviceLib.Storage;

namespace TestFlowDemo.Tests.Helpers
{
    /// <summary>
    ///     简易内存版数据库实现，记录测试过程中的步骤信息便于断言。
    /// </summary>
    public sealed class FakeDatabaseService : IDatabaseService
    {
        private long _sessionSeq = 1000;
        private readonly List<FakeStepRecord> _records = new List<FakeStepRecord>();
        private readonly object _sync = new object();

        public IReadOnlyList<FakeStepRecord> StepRecords
        {
            get
            {
                lock (_sync)
                {
                    return _records.ToList();
                }
            }
        }

        public void Reset()
        {
            lock (_sync)
            {
                _records.Clear();
            }
        }

        public IEnumerable<TestParamRow> GetAllActiveParams()
            => Array.Empty<TestParamRow>();

        public Dictionary<string, object> QueryParamsForModel(string model)
            => new Dictionary<string, object>();

        public long StartTestSession(string productModel, string barcode)
            => Interlocked.Increment(ref _sessionSeq);

        public void FinishTestSession(long sessionId, int finalStatus = 1)
        {
            // 本测试不需要记录最终状态，仅保留接口以满足依赖。
        }

        public void AppendStep(long sessionId, string productModel, string barcode, string stepName, string description,
            string device, string command, string parameters, string expected, string outputs, int success, string message,
            DateTime started, DateTime ended)
        {
            lock (_sync)
            {
                _records.Add(new FakeStepRecord
                {
                    SessionId = sessionId,
                    StepName = stepName,
                    Success = success == 1,
                    Message = message,
                    StartedAt = started,
                    EndedAt = ended
                });
            }
        }

        public void SaveReportPath(long sessionId, string reportPath)
        {
            // 报表输出与测试目标无关，留空实现即可。
        }
    }

    /// <summary>
    ///     用于断言的步骤记录快照。
    /// </summary>
    public sealed class FakeStepRecord
    {
        public long SessionId { get; set; }
        public string StepName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
    }
}
