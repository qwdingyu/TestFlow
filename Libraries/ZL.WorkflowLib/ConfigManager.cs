using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using ZL.DeviceLib;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Workflow;

namespace ZL.WorkflowLib
{
    public class ConfigManager
    {
        private static readonly Lazy<ConfigManager> _instance = new Lazy<ConfigManager>(() => new ConfigManager());
        public static ConfigManager Instance => _instance.Value;

        private Dictionary<string, DeviceConfig> _allDevices;
        private Dictionary<string, DeviceConfig> _allInfrastructure;
        private Dictionary<string, FlowConfig> _flowCache;
        private string _baseDir;

        private ConfigManager()
        {
            _flowCache = new Dictionary<string, FlowConfig>();
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            LoadDevices();
            LoadInfrastructure();

            // 合并 设备 + 基础设施（保持对 StepConfig.Device 的兼容）
            var merged = new Dictionary<string, DeviceConfig>(_allDevices, StringComparer.OrdinalIgnoreCase);
            if (_allInfrastructure != null)
            {
                foreach (var kv in _allInfrastructure)
                    merged[kv.Key] = kv.Value;
            }
            DeviceServices.Devices = merged;
        }

        private void LoadDevices()
        {
            // 优先小写新文件 devices.json，兼容老文件 Devices.json
            string devicesPath = Path.Combine(_baseDir, "devices.json");
            if (!File.Exists(devicesPath))
                devicesPath = Path.Combine(_baseDir, "Devices.json");
            if (!File.Exists(devicesPath))
            {
                LogHelper.Info("设备配置文件不存在: devices.json / Devices.json");
            }
            // Schema 校验
            Engine.JsonSchemaValidator.ValidateDevicesFile(devicesPath);
            var devicesRoot = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, DeviceConfig>>>(File.ReadAllText(devicesPath));
            _allDevices = devicesRoot.ContainsKey("Devices") ? (devicesRoot["Devices"] ?? new Dictionary<string, DeviceConfig>()) : new Dictionary<string, DeviceConfig>();
            LogHelper.Info($"设备配置已加载，共 {_allDevices.Count} 个物理设备。");
        }

        private void LoadInfrastructure()
        {
            string infraPath = Path.Combine(_baseDir, "infrastructure.json");
            if (!File.Exists(infraPath))
            {
                _allInfrastructure = new Dictionary<string, DeviceConfig>();
                LogHelper.Info("未发现 infrastructure.json，跳过基础设施加载。");
                return;
            }
            // Schema 校验
            Engine.JsonSchemaValidator.ValidateInfrastructureFile(infraPath);
            var infraRoot = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, DeviceConfig>>>(File.ReadAllText(infraPath));
            _allInfrastructure = infraRoot.ContainsKey("Infrastructure") ? (infraRoot["Infrastructure"] ?? new Dictionary<string, DeviceConfig>()) : new Dictionary<string, DeviceConfig>();
            LogHelper.Info($"基础设施配置已加载，共 {_allInfrastructure.Count} 个服务。");
        }

        public FlowConfig GetFlowConfig(string model)
        {
            if (_flowCache.ContainsKey(model)) return _flowCache[model];
            string flowPath = Path.Combine(_baseDir, "Flows", model + ".json");
            if (!File.Exists(flowPath)) throw new FileNotFoundException("未找到该型号的流程配置: " + flowPath);
            // 先读取并进行健壮性规范化（缺少或不一致的 Model）
            var txt = File.ReadAllText(flowPath);
            var token = Newtonsoft.Json.Linq.JToken.Parse(txt);
            if (token.Type != Newtonsoft.Json.Linq.JTokenType.Object) throw new Exception("Flow 文件根应为对象");
            var jobj = (Newtonsoft.Json.Linq.JObject)token;
            var fileModel = Path.GetFileNameWithoutExtension(flowPath);
            var declaredModel = jobj.Value<string>("Model");
            if (string.IsNullOrWhiteSpace(declaredModel))
            {
                LogHelper.Info($"[WARN] {fileModel}.json 缺少 Model 字段，已自动填充为文件名: {fileModel}");
                jobj["Model"] = fileModel;
            }
            else if (!string.Equals(declaredModel, fileModel, StringComparison.Ordinal))
            {
                LogHelper.Info($"[WARN] {fileModel}.json 的 Model=\"{declaredModel}\" 与文件名不一致，建议保持一致");
            }
            // Schema 校验（基础结构）基于规范化后的 token
            Engine.JsonSchemaValidator.ValidateFlowToken(jobj);
            FlowConfig config = JsonConvert.DeserializeObject<FlowConfig>(jobj.ToString());

            // 配置预校验（确保流程完整、设备与子流程有效）
            Engine.FlowValidator.Validate(config, _baseDir);
            _flowCache[model] = config;
            LogHelper.Info($"流程配置已加载并缓存: {model}，步骤数: {config.TestSteps.Count}");
            return config;
        }
    }
}
