using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using ZL.DeviceLib.Models;

namespace ZL.WorkflowLib.Engine
{
    public class SubflowRegistry
    {
        private readonly Dictionary<string, StepConfig> _map = new Dictionary<string, StepConfig>(StringComparer.OrdinalIgnoreCase);

        public void LoadFromDirectory(string dir = "")
        {
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows", "Subflows");
            }
            if (!Directory.Exists(dir))
                return;

            var files = Directory.GetFiles(dir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                var txt = File.ReadAllText(files[i]);
                var sc = JsonConvert.DeserializeObject<StepConfig>(txt);
                if (sc != null && !string.IsNullOrEmpty(sc.Name) && sc.Steps != null && sc.Steps.Count > 0)
                    _map[sc.Name] = Clone(sc);
            }
        }

        public void Register(StepConfig definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Name))
                return;

            if (definition.Steps == null || definition.Steps.Count == 0)
                return;

            _map[definition.Name] = Clone(definition);
        }

        public bool TryGet(string name, out StepConfig subflow)
        {
            StepConfig value;
            if (_map.TryGetValue(name, out value))
            {
                subflow = Clone(value);
                return true;
            }

            subflow = null;
            return false;
        }

        public IEnumerable<StepConfig> GetAll()
        {
            foreach (var item in _map.Values)
                yield return Clone(item);
        }

        private static StepConfig Clone(StepConfig source)
        {
            if (source == null)
                return null;

            var copy = new StepConfig
            {
                Name = source.Name,
                Description = source.Description,
                Target = source.Target,
                Command = source.Command,
                TimeoutMs = source.TimeoutMs,
                OnSuccess = source.OnSuccess,
                OnFailure = source.OnFailure,
                Type = source.Type,
                Ref = source.Ref
            };

            if (source.Parameters != null)
            {
                copy.Parameters = new Dictionary<string, object>();
                foreach (var kv in source.Parameters)
                    copy.Parameters[kv.Key] = kv.Value;
            }

            if (source.ExpectedResults != null)
            {
                copy.ExpectedResults = new Dictionary<string, object>();
                foreach (var kv in source.ExpectedResults)
                    copy.ExpectedResults[kv.Key] = kv.Value;
            }

            if (source.DependsOn != null)
                copy.DependsOn = new List<string>(source.DependsOn);

            if (source.Steps != null)
            {
                copy.Steps = new List<StepConfig>();
                for (int i = 0; i < source.Steps.Count; i++)
                    copy.Steps.Add(Clone(source.Steps[i]));
            }

            return copy;
        }
    }
}

