using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using ZL.DeviceLib.Storage;

namespace ZL.WorkflowLib.Engine
{
    public class ParamKey
    {
        public string Line;
        public string Station;
        public string Model;
        public string Step;
        public ParamKey(string line, string station, string model, string step)
        {
            Line = line ?? "*";
            Station = station ?? "*";
            Model = model ?? "*";
            Step = step ?? "";
        }
        public string ToKey() => (Line ?? "*") + "|" + (Station ?? "*") + "|" + (Model ?? "*") + "|" + (Step ?? "");
    }

    public class ParamInjector
    {
        private readonly IDatabaseService _db; private readonly TimeSpan _ttl; private DateTime _expireAtUtc;
        private readonly ConcurrentDictionary<string, JObject> _cache = new ConcurrentDictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        public string DefaultLine { get; set; }
        public string DefaultStation { get; set; }
        public ParamInjector(IDatabaseService db, int ttlSeconds, string defaultLine, string defaultStation)
        { _db = db; _ttl = TimeSpan.FromSeconds(ttlSeconds > 0 ? ttlSeconds : 300); DefaultLine = string.IsNullOrEmpty(defaultLine) ? "*" : defaultLine; DefaultStation = string.IsNullOrEmpty(defaultStation) ? "*" : defaultStation; _expireAtUtc = DateTime.MinValue; }

        public void PreloadAll()
        {
            var all = _db.GetAllActiveParams(); _cache.Clear();
            foreach (var row in all)
            {
                var key = new ParamKey(row.Line, row.StationNo, row.Model, row.StepName).ToKey();
                try { var jobj = JObject.Parse(row.ParamJson ?? "{}"); _cache[key] = jobj; } catch { }
            }
            _expireAtUtc = DateTime.Now.Add(_ttl);
        }

        public void Clear() { _cache.Clear(); _expireAtUtc = DateTime.MinValue; }

        public Dictionary<string, object> GetParams(string line, string station, string model, string stepName)
        {
            if (DateTime.Now >= _expireAtUtc) { PreloadAll(); }
            string[] tryKeys = new[]
            {
                new ParamKey(line, station, model, stepName).ToKey(),
                new ParamKey(line, station, "*",    stepName).ToKey(),
                new ParamKey(line, "*",     model,  stepName).ToKey(),
                new ParamKey("*", "*",      model,  stepName).ToKey(),
                new ParamKey("*", "*",      "*",    stepName).ToKey()
            };
            JObject jobj = null; foreach (var tk in tryKeys) { if (_cache.TryGetValue(tk, out jobj)) break; }
            if (jobj == null) throw new Exception("未找到参数: " + line + "|" + station + "|" + model + "|" + stepName);
            return jobj.ToObject<Dictionary<string, object>>();
        }
    }
}
