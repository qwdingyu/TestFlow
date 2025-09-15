using System.Threading;
namespace ZL.WorkflowLib.Workflow
{
    public class FlowData
    {
        public string Model { get; set; }
        public string Sn { get; set; }
        public bool LastSuccess { get; set; }
        public string Current { get; set; }
        public bool Done { get; set; }
        public long SessionId { get; set; }
        public CancellationToken Cancellation { get; set; }
    }
}

