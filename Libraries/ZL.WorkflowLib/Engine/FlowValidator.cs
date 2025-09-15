using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using ZL.DeviceLib.Models;

namespace ZL.WorkflowLib.Engine
{
    public static class FlowValidator
    {
        public static void Validate(FlowConfig cfg, string baseDir)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (cfg.TestSteps == null || cfg.TestSteps.Count == 0)
                throw new Exception($"型号 {cfg.Model} 流程为空");
            if (cfg.Devices == null)
                throw new Exception("设备清单未注入（Devices 为空）");

            // 1) 步骤名唯一、建立索引
            var stepsByName = new Dictionary<string, StepConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in cfg.TestSteps)
            {
                if (string.IsNullOrWhiteSpace(s.Name))
                    throw new Exception("存在未命名的步骤");
                if (stepsByName.ContainsKey(s.Name))
                    throw new Exception($"步骤名重复: {s.Name}");
                stepsByName[s.Name] = s;
            }

            // 2) 基于 DependsOn 的起点检查
            int roots = 0;
            foreach (var s in cfg.TestSteps)
            {
                if (s.DependsOn == null || s.DependsOn.Count == 0) roots++;
                else
                {
                    foreach (var dep in s.DependsOn)
                    {
                        if (!stepsByName.ContainsKey(dep))
                            throw new Exception($"步骤 {s.Name} 的 DependsOn 引用不存在: {dep}");
                    }
                }
            }
            if (roots == 0)
                throw new Exception("未找到起始步骤（缺少无 DependsOn 的步骤）");

            // 3) OnSuccess/OnFailure 目标检查（允许为空）
            foreach (var s in cfg.TestSteps)
            {
                if (!string.IsNullOrEmpty(s.OnSuccess) && !stepsByName.ContainsKey(s.OnSuccess))
                    throw new Exception($"步骤 {s.Name} 的 OnSuccess 指向不存在: {s.OnSuccess}");
                if (!string.IsNullOrEmpty(s.OnFailure) && !stepsByName.ContainsKey(s.OnFailure))
                    throw new Exception($"步骤 {s.Name} 的 OnFailure 指向不存在: {s.OnFailure}");
            }

            // 4) 设备/子流程有效性
            string subflowDir = Path.Combine(baseDir ?? AppDomain.CurrentDomain.BaseDirectory, "Flows", "Subflows");
            foreach (var s in cfg.TestSteps)
            {
                var type = (s.Type ?? "Normal").Trim();
                if (string.Equals(type, "Normal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(type))
                {
                    var key = string.IsNullOrWhiteSpace(s.Device) ? s.Target : s.Device;
                    if (string.IsNullOrWhiteSpace(key))
                        throw new Exception($"普通步骤 {s.Name} 缺少 Device/Target 字段");
                    if (!cfg.Devices.ContainsKey(key))
                        throw new Exception($"普通步骤 {s.Name} 引用未知设备: {key}");
                }
                else if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
                {
                    if (s.Steps == null || s.Steps.Count == 0)
                        throw new Exception($"子流程 {s.Name} 未包含任何子步骤");
                    ValidateSubStepsDevices(cfg, s.Name, s.Steps);
                }
                else if (string.Equals(type, "SubFlowRef", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(s.Ref))
                        throw new Exception($"子流程引用 {s.Name} 缺少 Ref 字段");
                    var path = Path.Combine(subflowDir, s.Ref + ".json");
                    if (!File.Exists(path))
                        throw new Exception($"子流程引用未找到文件: {s.Ref}.json ({path})");
                    try
                    {
                        var subTxt = File.ReadAllText(path);
                        var subCfg = JsonConvert.DeserializeObject<StepConfig>(subTxt);
                        if (subCfg == null || subCfg.Steps == null || subCfg.Steps.Count == 0)
                            throw new Exception($"子流程引用 {s.Ref} 文件内容无效（缺少 Steps）");
                        ValidateSubStepsDevices(cfg, s.Name + ":ref:" + s.Ref, subCfg.Steps);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"子流程引用 {s.Ref} 解析失败: {ex.Message}");
                    }
                }
                else
                {
                    throw new Exception($"步骤 {s.Name} 类型不支持: {s.Type}");
                }
            }
        }

        private static void ValidateSubStepsDevices(FlowConfig cfg, string owner, IList<StepConfig> subSteps)
        {
            foreach (var ss in subSteps)
            {
                var key = string.IsNullOrWhiteSpace(ss.Device) ? ss.Target : ss.Device;
                if (string.IsNullOrWhiteSpace(key))
                    throw new Exception($"子流程 {owner} 的子步骤 {ss.Name} 缺少 Device/Target");
                if (!cfg.Devices.ContainsKey(key))
                    throw new Exception($"子流程 {owner} 的子步骤 {ss.Name} 引用未知设备: {key}");
            }
        }
    }
}
