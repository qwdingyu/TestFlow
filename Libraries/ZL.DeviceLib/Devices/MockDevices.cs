using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;
using ZL.DeviceLib.Storage;

namespace ZL.DeviceLib.Devices
{
    internal static class Util
    {
        public static double GetDouble(Dictionary<string, object> dict, string key, double defVal)
        {
            if (dict == null || !dict.ContainsKey(key)) return defVal;
            try { return Convert.ToDouble(dict[key], CultureInfo.InvariantCulture); } catch { return defVal; }
        }
    }

    public class MockScanner : IDevice
    {
        private readonly DeviceConfig _cfg;
        public MockScanner(DeviceConfig cfg) { _cfg = cfg; }
        public ExecutionResult Execute(StepConfig step, StepContext context)
        {
            var token = context.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(180, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                outputs["barcode"] = (context != null ? context.Model : "UNKNOWN") + "-SN001";
                return new ExecutionResult { Success = true, Message = "scan ok", Outputs = outputs };
            }
            catch (OperationCanceledException)
            { return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
            catch (Exception ex)
            { return new ExecutionResult { Success = false, Message = "Scanner Exception: " + ex.Message, Outputs = outputs }; }
        }
    }

    public class MockPowerSupply : IDevice
    {
        private readonly DeviceConfig _cfg;
        public MockPowerSupply(DeviceConfig cfg) { _cfg = cfg; }
        public ExecutionResult Execute(StepConfig step, StepContext context)
        {
            var token = context.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(180, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                double v = Util.GetDouble(step.Parameters, "voltage", 12.0);
                double cur = Util.GetDouble(step.Parameters, "current_limit", 2.0);
                outputs["status"] = "ok";
                outputs["set_voltage"] = v;
                outputs["current_limit"] = cur;
                return new ExecutionResult { Success = true, Message = $"power set {v}V, limit {cur}A", Outputs = outputs };
            }
            catch (OperationCanceledException)
            { return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
            catch (Exception ex)
            { return new ExecutionResult { Success = false, Message = "Power Exception: " + ex.Message, Outputs = outputs }; }
        }
    }

    public class MockCurrentMeter : IDevice
    {
        private readonly DeviceConfig _cfg;
        private readonly Random _rnd = new Random();
        public MockCurrentMeter(DeviceConfig cfg) { _cfg = cfg; }
        public ExecutionResult Execute(StepConfig step, StepContext context)
        {
            var token = context.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(180, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                outputs["status"] = "";
                var result = new ExecutionResult { Outputs = outputs };
                double value = step.Command == "measure" ? 1.7 + _rnd.NextDouble() * 0.2 : 1.8 + ((_rnd.NextDouble() - 0.5) * 0.2);
                result.Success = true;
                result.Message = $"measured current {value:F3}A";
                result.Outputs["current"] = value;
                result.Outputs["status"] = "ok";
                return result;
            }
            catch (OperationCanceledException)
            { return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
            catch (Exception ex)
            { return new ExecutionResult { Success = false, Message = "Current Exception: " + ex.Message, Outputs = outputs }; }
        }
    }

    public class MockResistanceMeter : IDevice
    {
        private readonly DeviceConfig _cfg;
        private readonly Random _rnd = new Random();
        public MockResistanceMeter(DeviceConfig cfg) { _cfg = cfg; }
        public ExecutionResult Execute(StepConfig step, StepContext context)
        {
            var token = context.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(180, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                double val = 10000.0 + ((_rnd.NextDouble() - 0.5) * 500.0);
                outputs["resistance"] = Math.Round(val, 1);
                return new ExecutionResult { Success = true, Message = $"resistance {outputs["resistance"]}Ω", Outputs = outputs };
            }
            catch (OperationCanceledException)
            { return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
            catch (Exception ex)
            { return new ExecutionResult { Success = false, Message = "Resistance Exception: " + ex.Message, Outputs = outputs }; }
        }
    }

    public class MockNoiseMeter : IDevice
    {
        private readonly DeviceConfig _cfg;
        private readonly Random _rnd = new Random();
        public MockNoiseMeter(DeviceConfig cfg) { _cfg = cfg; }
        public ExecutionResult Execute(StepConfig step, StepContext context)
        {
            var token = context.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(300, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                double val = 65.0 + ((_rnd.NextDouble() - 0.5) * 2.0);
                outputs["noise_level"] = Math.Round(val, 1);
                outputs["weighting"] = step.Parameters != null && step.Parameters.ContainsKey("weighting") ? step.Parameters["weighting"] : "A";
                return new ExecutionResult { Success = true, Message = $"noise {outputs["noise_level"]} dB(A)", Outputs = outputs };
            }
            catch (OperationCanceledException)
            { return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
            catch (Exception ex)
            { return new ExecutionResult { Success = false, Message = "Noise Exception: " + ex.Message, Outputs = outputs }; }
        }
    }

    //public class MockCanBus : IDevice
    //{
    //    private readonly DeviceConfig _cfg;
    //    private readonly Random _rnd = new Random();
    //    public MockCanBus(DeviceConfig cfg) { _cfg = cfg; }
    //    public DeviceExecResult Execute(StepConfig step, StepContext context)
    //    {
    //        var outputs = new Dictionary<string, object>();
    //        try
    //        {
    //            int timeout = 65000 + _rnd.Next(500);
    //            var token = context.Cancellation;
    //            Task.Delay(timeout, token).Wait(token);
    //            if (token.IsCancellationRequested)
    //                return new DeviceExecResult { Success = false, Message = "Cancelled", Outputs = outputs };

    //            string id = null;
    //            List<string> data = null;
    //            if (step.Parameters != null)
    //            {
    //                object idObj; if (step.Parameters.TryGetValue("id", out idObj)) id = idObj.ToString();
    //                object dataObj;
    //                if (step.Parameters.TryGetValue("data", out dataObj))
    //                {
    //                    if (dataObj is IEnumerable<object> enumerable)
    //                        data = enumerable.Select(d => d.ToString()).ToList();
    //                    else if (dataObj is string s)
    //                        data = new List<string> { s };
    //                }
    //            }

    //            string response;
    //            if (id == "0x12D") response = "WAKEUP_ACK";
    //            else if (id == "0x4C1") response = "MASSAGE_ACK";
    //            else if (id == "0x21" || id == "0x22") response = "SEAT_CTRL_ACK";
    //            else response = _rnd.Next(100) < 90 ? "ACK" : "NACK";

    //            string payload = data != null ? string.Join(" ", data) : "NO_DATA";
    //            outputs["response"] = response;
    //            outputs["payload"] = payload;
    //            outputs["id"] = id ?? "unknown";

    //            return new DeviceExecResult
    //            {
    //                Success = response.EndsWith("ACK"),
    //                Message = $"CAN {response} (ID={id}, DATA={payload})",
    //                Outputs = outputs
    //            };
    //        }
    //        catch (OperationCanceledException)
    //        { return new DeviceExecResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
    //        catch (Exception ex)
    //        { return new DeviceExecResult { Success = false, Message = "CAN Exception: " + ex.Message, Outputs = outputs }; }
    //    }
    //}

    public class MockDatabase : IDevice
    {
        private readonly DeviceConfig _cfg;
        private readonly string _dbTypeString;
        private readonly string _connectionString;
        /// <summary>
        /// 之所以传递  数据库类型和连接，是为了可以连接除主应用数据库外，还可以连接别的数据库
        /// 感觉在这种场景下 没有必要，后续需要慎重考虑
        /// </summary>
        /// <param name="cfg"></param>
        /// <param name="dbTypeString"></param>
        /// <param name="connectionString"></param>
        public MockDatabase(DeviceConfig cfg, string dbTypeString, string connectionString)
        {
            _cfg = cfg;
            _dbTypeString = dbTypeString;
            _connectionString = connectionString;
        }
        public ExecutionResult Execute(StepConfig step, StepContext context)
        {
            var token = context.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(120, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                var db = new DbServices(_dbTypeString, _connectionString);
                var dict = db.QueryParamsForModel(context != null ? context.Model : "ABC-123");
                return new ExecutionResult
                {
                    Success = true,
                    Message = dict.Count > 0 ? "db params loaded" : "no params found, continue",
                    Outputs = dict
                };
            }
            catch (OperationCanceledException)
            {
                return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Message = "DB Exception: " + ex.Message, Outputs = outputs };
            }
        }
    }

    public class MockReportGenerator : IDevice
    {
        private readonly DeviceConfig _cfg;
        private readonly string _reportDir;
        public MockReportGenerator(DeviceConfig cfg, string reportDir) { _cfg = cfg; _reportDir = reportDir; }
        public ExecutionResult Execute(StepConfig step, StepContext context)
        {
            var token = context.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(120, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                Directory.CreateDirectory(_reportDir);
                string path = Path.Combine(_reportDir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_report.html");
                File.WriteAllText(path, "<html><body><h1>Report</h1></body></html>");
                outputs["path"] = path;
                return new ExecutionResult { Success = true, Message = "report generated", Outputs = outputs };
            }
            catch (OperationCanceledException)
            { return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
            catch (Exception ex)
            { return new ExecutionResult { Success = false, Message = "ReportGenerator Exception: " + ex.Message, Outputs = outputs }; }
        }
    }

    public class MockSystem : IDevice
    {
        private readonly DeviceConfig _cfg;
        public MockSystem(DeviceConfig cfg) { _cfg = cfg; }
        public ExecutionResult Execute(StepConfig step, StepContext context)
        {
            var token = context.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(50, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                outputs["status"] = "done";
                return new ExecutionResult { Success = true, Message = "system step ok", Outputs = outputs };
            }
            catch (OperationCanceledException)
            { return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
            catch (Exception ex)
            { return new ExecutionResult { Success = false, Message = "System Exception: " + ex.Message, Outputs = outputs }; }
        }
    }

    public class ResistorBoxDevice : SerialDeviceBase
    {
        public ResistorBoxDevice(DeviceConfig cfg) : base(cfg) { }
        protected override ExecutionResult HandleCommand(
            StepConfig step,
            StepContext ctx,
            Dictionary<string, object> outputs,
            CancellationToken token)
        {
            if (step.Command == "set_resistance")
            {
                if (!step.Parameters.ContainsKey("value"))
                    throw new Exception("参数缺失: value");
                var value = Convert.ToDouble(step.Parameters["value"]);
                string cmd = $"SET R={value}";
                _transport.Send(cmd);
                string resp = _transport.WaitForResponse(msg => msg.Contains("OK"), 10000, token);
                outputs["resistance"] = value;
                outputs["status"] = resp.Contains("OK") ? "ok" : "fail";
                return new ExecutionResult { Success = outputs["status"].ToString() == "ok", Message = $"resistance set to {value} Ohm, resp={resp}", Outputs = outputs };
            }
            throw new Exception("Unsupported command: " + step.Command);
        }
    }

    public class VoltmeterDevice : IDevice
    {
        private readonly DeviceConfig _cfg;
        private readonly Random _rnd = new Random();
        public VoltmeterDevice(DeviceConfig config) { _cfg = config; }
        public ExecutionResult Execute(StepConfig step, StepContext ctx)
        {
            var token = ctx.Cancellation;
            var outputs = new Dictionary<string, object>();
            try
            {
                Task.Delay(20, token).Wait(token);
                if (token.IsCancellationRequested)
                    return new ExecutionResult { Success = false, Message = "Cancelled", Outputs = outputs };
                var result = new ExecutionResult { Outputs = outputs };
                if (step.Command == "measure")
                {
                    double value = 11.5 + _rnd.NextDouble();
                    result.Success = true;
                    result.Message = $"measured voltage {value:F3}V";
                    result.Outputs["current"] = value;
                    result.Outputs["status"] = "ok";
                    return result;
                }
                else throw new Exception("Unsupported command: " + step.Command);
            }
            catch (OperationCanceledException)
            { return new ExecutionResult { Success = false, Message = "Step cancelled by timeout", Outputs = outputs }; }
            catch (Exception ex)
            { return new ExecutionResult { Success = false, Message = "Voltmeter Exception: " + ex.Message, Outputs = outputs }; }
        }
    }
}

