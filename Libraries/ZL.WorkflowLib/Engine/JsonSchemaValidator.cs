using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace ZL.WorkflowLib.Engine
{
    public static class JsonSchemaValidator
    {
        public static void ValidateFlowToken(JToken root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (root.Type != JTokenType.Object) throw new Exception("Flow 文件根应为对象");
            var obj = (JObject)root;
            var model = obj.Value<string>("Model");
            if (string.IsNullOrWhiteSpace(model)) throw new Exception("Flow 缺少 Model");
            var steps = obj["TestSteps"] as JArray;
            if (steps == null) throw new Exception("Flow 缺少 TestSteps 数组");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var stepTok in steps)
            {
                ValidateStep(stepTok as JObject, isSub: false);
                var name = ((JObject)stepTok).Value<string>("Name");
                if (!names.Add(name)) throw new Exception($"Flow 步骤名重复: {name}");
            }
        }
        public static void ValidateDevicesFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var root = JToken.Parse(File.ReadAllText(path));
            if (root.Type != JTokenType.Object) throw new Exception("devices.json 根应为对象");
            var obj = (JObject)root;
            if (!obj.TryGetValue("Devices", StringComparison.OrdinalIgnoreCase, out var devsTok) || devsTok.Type != JTokenType.Object)
                throw new Exception("devices.json 缺少 Devices 对象");
            foreach (var prop in ((JObject)devsTok).Properties())
            {
                var dev = prop.Value as JObject;
                if (dev == null) throw new Exception($"Devices.{prop.Name} 需为对象");
                if (!dev.TryGetValue("Type", StringComparison.OrdinalIgnoreCase, out var typeTok) || typeTok.Type != JTokenType.String || string.IsNullOrWhiteSpace(typeTok.ToString()))
                    throw new Exception($"Devices.{prop.Name} 缺少 Type 字段或非法");
                // optional: ConnectionString (string), Settings (object)
                if (dev.TryGetValue("ConnectionString", StringComparison.OrdinalIgnoreCase, out var connTok) && connTok.Type != JTokenType.String)
                    throw new Exception($"Devices.{prop.Name}.ConnectionString 必须是字符串");
                if (dev.TryGetValue("Settings", StringComparison.OrdinalIgnoreCase, out var setTok) && setTok.Type != JTokenType.Object)
                    throw new Exception($"Devices.{prop.Name}.Settings 必须是对象");
            }
        }

        public static void ValidateInfrastructureFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var root = JToken.Parse(File.ReadAllText(path));
            if (root.Type != JTokenType.Object) throw new Exception("infrastructure.json 根应为对象");
            var obj = (JObject)root;
            if (!obj.TryGetValue("Infrastructure", StringComparison.OrdinalIgnoreCase, out var infraTok) || infraTok.Type != JTokenType.Object)
                throw new Exception("infrastructure.json 缺少 Infrastructure 对象");
            foreach (var prop in ((JObject)infraTok).Properties())
            {
                var svc = prop.Value as JObject;
                if (svc == null) throw new Exception($"Infrastructure.{prop.Name} 需为对象");
                if (!svc.TryGetValue("Type", StringComparison.OrdinalIgnoreCase, out var typeTok) || typeTok.Type != JTokenType.String || string.IsNullOrWhiteSpace(typeTok.ToString()))
                    throw new Exception($"Infrastructure.{prop.Name} 缺少 Type 字段或非法");
                if (svc.TryGetValue("ConnectionString", StringComparison.OrdinalIgnoreCase, out var connTok) && connTok.Type != JTokenType.String)
                    throw new Exception($"Infrastructure.{prop.Name}.ConnectionString 必须是字符串");
                if (svc.TryGetValue("Settings", StringComparison.OrdinalIgnoreCase, out var setTok) && setTok.Type != JTokenType.Object)
                    throw new Exception($"Infrastructure.{prop.Name}.Settings 必须是对象");
            }
        }

        public static void ValidateFlowFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var root = JToken.Parse(File.ReadAllText(path));
            ValidateFlowToken(root);
        }

        public static void ValidateSubflowFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var root = JToken.Parse(File.ReadAllText(path));
            if (root.Type != JTokenType.Object) throw new Exception("Subflow 文件根应为对象");
            var obj = (JObject)root;
            var steps = obj["Steps"] as JArray;
            if (steps == null) throw new Exception("Subflow 缺少 Steps 数组");
            foreach (var stepTok in steps)
            {
                ValidateStep(stepTok as JObject, isSub: true);
            }
        }

        private static void ValidateStep(JObject step, bool isSub)
        {
            if (step == null) throw new Exception("无效的步骤（非对象）");
            var name = step.Value<string>("Name");
            if (string.IsNullOrWhiteSpace(name)) throw new Exception("步骤缺少 Name");
            var type = (step.Value<string>("Type") ?? "Normal").Trim();
            if (string.Equals(type, "Normal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(type))
            {
                var dev = step.Value<string>("Device");
                var tgt = step.Value<string>("Target");
                if (string.IsNullOrWhiteSpace(dev) && string.IsNullOrWhiteSpace(tgt))
                    throw new Exception($"步骤 {name} 缺少 Device/Target");
                if (step.TryGetValue("Parameters", out var p) && p.Type != JTokenType.Object) throw new Exception($"步骤 {name}.Parameters 必须是对象");
                if (step.TryGetValue("ExpectedResults", out var e) && e.Type != JTokenType.Object) throw new Exception($"步骤 {name}.ExpectedResults 必须是对象");
                if (step.TryGetValue("TimeoutMs", out var t) && t.Type != JTokenType.Integer) throw new Exception($"步骤 {name}.TimeoutMs 必须是整数");
            }
            else if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
            {
                var steps = step["Steps"] as JArray;
                if (steps == null || steps.Count == 0) throw new Exception($"子流程 {name} 需包含 Steps");
                foreach (var st in steps) ValidateStep(st as JObject, isSub: true);
            }
            else if (string.Equals(type, "SubFlowRef", StringComparison.OrdinalIgnoreCase))
            {
                var rf = step.Value<string>("Ref");
                if (string.IsNullOrWhiteSpace(rf)) throw new Exception($"子流程引用 {name} 需包含 Ref");
            }
            else throw new Exception($"步骤 {name} 不支持的 Type: {type}");
        }
    }
}
