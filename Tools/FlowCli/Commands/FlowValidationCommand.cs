using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cli;
using Newtonsoft.Json;
using ZL.DeviceLib;
using ZL.DeviceLib.Models;

namespace Cli.Commands
{
    /// <summary>
    /// 负责实现 validate 与 validate-all 命令，用于校验流程文件与设备配置的关联关系。
    /// </summary>
    internal static class FlowValidationCommand
    {
        /// <summary>
        /// 校验全部流程文件，统计通过与失败数量。
        /// </summary>
        /// <returns>返回 0 表示全部通过，返回 1 表示存在校验失败。</returns>
        public static int ValidateAll()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string flowsDir = Path.Combine(baseDir, "Flows");
            if (!Directory.Exists(flowsDir))
            {
                LogHelper.Info("Flows 目录不存在");
                return 1;
            }

            List<string> files = Directory.GetFiles(flowsDir, "*.json").OrderBy(x => x).ToList();
            if (files.Count == 0)
            {
                LogHelper.Info("未发现流程文件");
                return 1;
            }

            int ok = 0;
            int fail = 0;
            foreach (string file in files)
            {
                string model = Path.GetFileNameWithoutExtension(file);
                int rc = ValidateOne(model, true);
                if (rc == 0)
                {
                    ok++;
                    LogHelper.Info("[OK] " + model);
                }
                else
                {
                    fail++;
                    LogHelper.Info("[ERR] " + model);
                }
            }

            LogHelper.Info("Summary: OK=" + ok + ", ERR=" + fail);
            return fail > 0 ? 1 : 0;
        }

        /// <summary>
        /// 校验单一型号流程，并在控制台输出结果。
        /// </summary>
        /// <param name="model">需要校验的流程型号名称。</param>
        /// <returns>返回 0 表示通过，其余值表示失败。</returns>
        public static int ValidateOne(string model)
        {
            return ValidateOne(model, false);
        }

        /// <summary>
        /// 校验单一型号流程，可选择是否静默输出详细信息。
        /// </summary>
        /// <param name="model">需要校验的流程型号名称。</param>
        /// <param name="quiet">为 true 时仅返回状态，不打印成功日志。</param>
        /// <returns>返回 0 表示通过，其余值表示失败。</returns>
        public static int ValidateOne(string model, bool quiet)
        {
            try
            {
                FlowConfig cfg = LoadFlow(model);
                ValidateFlow(cfg);
                if (!quiet)
                {
                    LogHelper.Info("[OK] " + model + " steps=" + cfg.TestSteps.Count);
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    Console.Error.WriteLine("[ERR] " + model + ": " + ex.Message);
                }

                return 3;
            }
        }

        private static FlowConfig LoadFlow(string model)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string devicesPath = Path.Combine(baseDir, "devices.json");
            if (!File.Exists(devicesPath))
            {
                devicesPath = Path.Combine(baseDir, "Devices.json");
            }

            if (!File.Exists(devicesPath))
            {
                throw new FileNotFoundException("设备配置文件不存在: devices.json / Devices.json");
            }

            Dictionary<string, Dictionary<string, DeviceConfig>> devicesRoot = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, DeviceConfig>>>(File.ReadAllText(devicesPath));
            Dictionary<string, DeviceConfig> devs = devicesRoot.ContainsKey("Devices") && devicesRoot["Devices"] != null
                ? new Dictionary<string, DeviceConfig>(devicesRoot["Devices"], StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DeviceConfig>(StringComparer.OrdinalIgnoreCase);

            string infraPath = Path.Combine(baseDir, "infrastructure.json");
            Dictionary<string, DeviceConfig> infra = new Dictionary<string, DeviceConfig>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(infraPath))
            {
                Dictionary<string, Dictionary<string, DeviceConfig>> infraRoot = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, DeviceConfig>>>(File.ReadAllText(infraPath));
                if (infraRoot.ContainsKey("Infrastructure") && infraRoot["Infrastructure"] != null)
                {
                    foreach (KeyValuePair<string, DeviceConfig> kv in infraRoot["Infrastructure"])
                    {
                        infra[kv.Key] = kv.Value;
                    }
                }
            }

            string flowPath = Path.Combine(baseDir, "Flows", model + ".json");
            if (!File.Exists(flowPath))
            {
                throw new FileNotFoundException("未找到该型号的流程配置: " + flowPath);
            }

            FlowConfig cfg = JsonConvert.DeserializeObject<FlowConfig>(File.ReadAllText(flowPath));
            if (cfg == null)
            {
                cfg = new FlowConfig();
                cfg.Model = model;
            }

            Dictionary<string, DeviceConfig> merged = new Dictionary<string, DeviceConfig>(devs, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, DeviceConfig> kv in infra)
            {
                merged[kv.Key] = kv.Value;
            }

            cfg.Devices = merged;
            return cfg;
        }

        private static void ValidateFlow(FlowConfig cfg)
        {
            if (cfg.TestSteps == null || cfg.TestSteps.Count == 0)
            {
                throw new Exception("型号 " + cfg.Model + " 流程为空");
            }

            if (cfg.Devices == null)
            {
                throw new Exception("设备清单未注入（Devices 为空）");
            }

            Dictionary<string, StepConfig> stepsByName = new Dictionary<string, StepConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (StepConfig step in cfg.TestSteps)
            {
                if (string.IsNullOrWhiteSpace(step.Name))
                {
                    throw new Exception("存在未命名的步骤");
                }

                if (stepsByName.ContainsKey(step.Name))
                {
                    throw new Exception("步骤名重复: " + step.Name);
                }

                stepsByName[step.Name] = step;
            }

            int roots = cfg.TestSteps.Count(s => s.DependsOn == null || s.DependsOn.Count == 0);
            if (roots == 0)
            {
                throw new Exception("未找到起始步骤（缺少无 DependsOn 的步骤）");
            }

            foreach (StepConfig step in cfg.TestSteps)
            {
                if (step.DependsOn != null)
                {
                    foreach (string dep in step.DependsOn)
                    {
                        if (!stepsByName.ContainsKey(dep))
                        {
                            throw new Exception("步骤 " + step.Name + " 的 DependsOn 引用不存在: " + dep);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(step.OnSuccess) && !stepsByName.ContainsKey(step.OnSuccess))
                {
                    throw new Exception("步骤 " + step.Name + " 的 OnSuccess 指向不存在: " + step.OnSuccess);
                }

                if (!string.IsNullOrEmpty(step.OnFailure) && !stepsByName.ContainsKey(step.OnFailure))
                {
                    throw new Exception("步骤 " + step.Name + " 的 OnFailure 指向不存在: " + step.OnFailure);
                }

                string type = (step.Type ?? "Normal").Trim();
                if (string.Equals(type, "Normal", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(type))
                {
                    string key = string.IsNullOrWhiteSpace(step.Target) ? step.Target : step.Target;
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        throw new Exception("普通步骤 " + step.Name + " 缺少 Device/Target 字段");
                    }

                    if (!cfg.Devices.ContainsKey(key))
                    {
                        throw new Exception("普通步骤 " + step.Name + " 引用未知设备: " + key);
                    }
                }
                else if (string.Equals(type, "SubFlow", StringComparison.OrdinalIgnoreCase))
                {
                    if (step.Steps == null || step.Steps.Count == 0)
                    {
                        throw new Exception("子流程 " + step.Name + " 未包含任何子步骤");
                    }

                    ValidateSubStepsDevices(cfg, step.Name, step.Steps);
                }
                else if (string.Equals(type, "SubFlowRef", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(step.Ref))
                    {
                        throw new Exception("子流程引用 " + step.Name + " 缺少 Ref 字段");
                    }

                    string subflowDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flows", "Subflows");
                    string path = Path.Combine(subflowDir, step.Ref + ".json");
                    if (!File.Exists(path))
                    {
                        throw new Exception("子流程引用未找到文件: " + step.Ref + ".json");
                    }

                    StepConfig subCfg = JsonConvert.DeserializeObject<StepConfig>(File.ReadAllText(path));
                    if (subCfg == null || subCfg.Steps == null || subCfg.Steps.Count == 0)
                    {
                        throw new Exception("子流程引用 " + step.Ref + " 文件内容无效（缺少 Steps）");
                    }

                    ValidateSubStepsDevices(cfg, step.Name + ":ref:" + step.Ref, subCfg.Steps);
                }
                else
                {
                    throw new Exception("步骤 " + step.Name + " 类型不支持: " + step.Type);
                }
            }
        }

        private static void ValidateSubStepsDevices(FlowConfig cfg, string owner, IList<StepConfig> subSteps)
        {
            foreach (StepConfig ss in subSteps)
            {
                if (string.IsNullOrEmpty(ss.Target))
                {
                    throw new Exception("子流程 " + owner + " 的子步骤 " + ss.Name + " 缺少 Device");
                }

                if (!cfg.Devices.ContainsKey(ss.Target))
                {
                    throw new Exception("子流程 " + owner + " 的子步骤 " + ss.Name + " 引用未知设备: " + ss.Target);
                }
            }
        }
    }
}
