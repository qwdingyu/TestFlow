using System;
using System.Collections.Generic;

namespace ZL.DeviceLib.Storage
{
    public interface IDatabaseService
    {
        IEnumerable<TestParams> GetAllActiveParams();
        Dictionary<string, object> QueryParamsForModel(string model);

        long StartTestSession(string productModel, string barcode);
        void FinishTestSession(long sessionId, int finalStatus = 1);
        void AppendStep(
            long sessionId, string productModel, string barcode,
            string stepName, string description, string device, string command,
            string parameters, string expected, string outputs,
            int success, string message,
            DateTime started, DateTime ended);
        void SaveReportPath(long sessionId, string reportPath);
    }
}

