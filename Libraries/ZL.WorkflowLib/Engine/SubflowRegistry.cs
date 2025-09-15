using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using ZL.DeviceLib.Models;

namespace ZL.WorkflowLib.Engine
{
    public class SubflowRegistry
    {
        private readonly Dictionary<string, StepConfig> _map = new Dictionary<string, StepConfig>();
        public void LoadFromDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            var files = Directory.GetFiles(dir, "*.json");
            foreach (var f in files)
            {
                var txt = File.ReadAllText(f);
                var sc = JsonConvert.DeserializeObject<StepConfig>(txt);
                if (sc != null && !string.IsNullOrEmpty(sc.Name) && sc.Steps != null && sc.Steps.Count > 0)
                    _map[sc.Name] = sc;
            }
        }
        public bool TryGet(string name, out StepConfig subflow) => _map.TryGetValue(name, out subflow);
    }
}

