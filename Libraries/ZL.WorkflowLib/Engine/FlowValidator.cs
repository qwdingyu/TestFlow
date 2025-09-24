using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using ZL.DeviceLib;
using ZL.DeviceLib.Models;
using ZL.WorkflowLib.Workflow;

namespace ZL.WorkflowLib.Engine
{
    public static class FlowValidator
    {
        public static void Validate(FlowConfig cfg, string baseDir)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (cfg.TestSteps == null || cfg.TestSteps.Count == 0)
                throw new Exception($"型号 {cfg.Model} 流程为空");
            if (DeviceServices.DevicesCfg == null)
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
                if (s == null)
                    throw new Exception("流程配置中存在空步骤节点，请检查 TestSteps 数组");

                var type = (s.Type ?? "Normal").Trim();
                if (string.Equals(type, "Normal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(type))
                {
                    // 普通步骤必须具备设备、命令以及期望结果定义，否则后续执行阶段将无法正确派发与判定
                    ValidateExecutableStep(cfg, s, null, "普通步骤");
                }
                else if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
                {
                    if (s.Steps == null || s.Steps.Count == 0)
                        throw new Exception($"子流程 {s.Name} 未包含任何子步骤");
                    ValidateSubStepsDevices(cfg, s.Name, s.Steps, subflowDir);
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
                        var ownerName = string.IsNullOrWhiteSpace(s.Name)
                            ? s.Ref
                            : s.Name + ":ref:" + s.Ref;
                        ValidateSubStepsDevices(cfg, ownerName, subCfg.Steps, subflowDir);
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

        private static void ValidateSubStepsDevices(FlowConfig cfg, string owner, IList<StepConfig> subSteps, string subflowDir)
        {
            if (subSteps == null)
                return;

            foreach (var ss in subSteps)
            {
                if (ss == null)
                    continue;

                var type = (ss.Type ?? "Normal").Trim();
                if (string.Equals(type, "Normal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(type))
                {
                    // 子流程中的普通步骤同样需要具备完整的设备/命令/期望配置
                    ValidateExecutableStep(cfg, ss, owner, "子流程");
                    continue;
                }

                if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
                {
                    if (ss.Steps == null || ss.Steps.Count == 0)
                        throw new Exception($"子流程 {owner} 的子步骤 {ss.Name} 未包含任何子步骤");
                    var nestedOwner = CombineOwnerName(owner, ss.Name);
                    ValidateSubStepsDevices(cfg, nestedOwner, ss.Steps, subflowDir);
                    continue;
                }

                if (string.Equals(type, "SubFlowRef", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(ss.Ref))
                        throw new Exception($"子流程 {owner} 的子步骤 {ss.Name} 缺少 Ref 字段");
                    var path = Path.Combine(subflowDir, ss.Ref + ".json");
                    if (!File.Exists(path))
                        throw new Exception($"子流程引用未找到文件: {ss.Ref}.json ({path})");
                    try
                    {
                        var subTxt = File.ReadAllText(path);
                        var subCfg = JsonConvert.DeserializeObject<StepConfig>(subTxt);
                        if (subCfg == null || subCfg.Steps == null || subCfg.Steps.Count == 0)
                            throw new Exception($"子流程引用 {ss.Ref} 文件内容无效（缺少 Steps）");
                        var refOwner = CombineOwnerName(owner, (ss.Name ?? ss.Ref) + ":ref:" + ss.Ref);
                        ValidateSubStepsDevices(cfg, refOwner, subCfg.Steps, subflowDir);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"子流程引用 {ss.Ref} 解析失败: {ex.Message}");
                    }
                    continue;
                }

                throw new Exception($"子流程 {owner} 的子步骤 {ss.Name} 类型不支持: {ss.Type}");
            }
        }

        private static void ValidateExecutableStep(FlowConfig cfg, StepConfig step, string owner, string category)
        {
            if (step == null)
                throw new Exception("检测到空的步骤节点，请确认配置文件格式是否正确");

            var displayName = BuildStepDisplayName(owner, step.Name, category);

            var key = string.IsNullOrWhiteSpace(step.Target) ? step.Target : step.Target;
            if (string.IsNullOrWhiteSpace(key))
                throw new Exception($"{displayName} 缺少 Device/Target 字段");
            if (!DeviceServices.DevicesCfg.ContainsKey(key))
                throw new Exception($"{displayName} 引用未知设备: {key}");
            if (string.IsNullOrWhiteSpace(step.Command))
                throw new Exception($"{displayName} 缺少 Command 字段");
            if (step.ExpectedResults == null)
                throw new Exception($"{displayName} 缺少 ExpectedResults 定义");
        }

        private static string BuildStepDisplayName(string owner, string name, string category)
        {
            var stepName = string.IsNullOrWhiteSpace(name) ? "<未命名步骤>" : name.Trim();
            if (string.IsNullOrWhiteSpace(owner))
                return $"{category} {stepName}";
            return $"{category} {owner} 的子步骤 {stepName}";
        }

        private static string CombineOwnerName(string owner, string child)
        {
            var childName = string.IsNullOrWhiteSpace(child) ? "<未命名子流程>" : child.Trim();
            if (string.IsNullOrWhiteSpace(owner))
                return childName;
            return owner + "/" + childName;
        }
    }
}
