using System;
using System.IO;
using Newtonsoft.Json.Linq;
using ZL.DeviceLib;
using ZL.WorkflowLib.Engine;

namespace Cli.Commands
{
    /// <summary>
    /// 负责实现 schema-validate 命令，对设备、基础设施及流程 JSON 进行结构校验。
    /// </summary>
    internal static class SchemaValidateCommand
    {
        /// <summary>
        /// 执行结构校验并输出校验结果汇总信息。
        /// </summary>
        /// <returns>返回 0 表示全部通过，返回 1 表示存在校验错误。</returns>
        public static int Execute()
        {
            int ok = 0;
            int err = 0;
            string baseDir = CommandHelper.FindRepoRoot();

            try
            {
                string devPath = File.Exists(Path.Combine(baseDir, "devices.json")) ? Path.Combine(baseDir, "devices.json") : Path.Combine(baseDir, "Devices.json");
                if (File.Exists(devPath))
                {
                    JsonSchemaValidator.ValidateDevicesFile(devPath);
                    LogHelper.Info("[OK] " + Path.GetFileName(devPath));
                    ok++;
                }
                else
                {
                    LogHelper.Info("[SKIP] devices.json 未找到");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERR] devices.json: " + ex.Message);
                err++;
            }

            try
            {
                string infraPath = Path.Combine(baseDir, "infrastructure.json");
                if (File.Exists(infraPath))
                {
                    JsonSchemaValidator.ValidateInfrastructureFile(infraPath);
                    JObject obj = JObject.Parse(File.ReadAllText(infraPath));
                    JObject infra = obj["Infrastructure"] as JObject;
                    if (infra != null)
                    {
                        JToken dbTok;
                        if (infra.TryGetValue("database", StringComparison.OrdinalIgnoreCase, out dbTok) && dbTok is JObject)
                        {
                            string type = ((JObject)dbTok).Value<string>("Type");
                            if (string.IsNullOrWhiteSpace(type))
                            {
                                LogHelper.Info("[WARN] infrastructure.database.Type 缺失，默认按 sqlite 处理");
                            }
                            else if (string.Equals(type, "database", StringComparison.OrdinalIgnoreCase))
                            {
                                LogHelper.Info("[WARN] infrastructure.database.Type=\"database\" 含糊，建议改为 sqlite 或插件提供者名");
                            }
                        }
                    }

                    LogHelper.Info("[OK] infrastructure.json");
                    ok++;
                }
                else
                {
                    LogHelper.Info("[SKIP] infrastructure.json 未找到");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ERR] infrastructure.json: " + ex.Message);
                err++;
            }

            string flowsDir = Path.Combine(baseDir, "Flows");
            if (Directory.Exists(flowsDir))
            {
                foreach (string f in Directory.EnumerateFiles(flowsDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        string fileName = Path.GetFileName(f);
                        string modelFromFile = Path.GetFileNameWithoutExtension(f);
                        JToken tok = JToken.Parse(File.ReadAllText(f));
                        if (tok.Type != JTokenType.Object)
                        {
                            throw new Exception("根应为对象");
                        }

                        JObject obj = (JObject)tok;
                        string declared = obj.Value<string>("Model");
                        if (string.IsNullOrWhiteSpace(declared))
                        {
                            LogHelper.Info("[WARN] " + fileName + " 缺少 Model，校验时按文件名填充: " + modelFromFile);
                            JObject clone = (JObject)obj.DeepClone();
                            clone["Model"] = modelFromFile;
                            JsonSchemaValidator.ValidateFlowToken(clone);
                        }
                        else
                        {
                            if (!string.Equals(declared, modelFromFile, StringComparison.Ordinal))
                            {
                                LogHelper.Info("[WARN] " + fileName + " 中 Model=\"" + declared + "\" 与文件名不一致");
                            }

                            JsonSchemaValidator.ValidateFlowToken(obj);
                        }

                        LogHelper.Info("[OK] " + fileName);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("[ERR] " + Path.GetFileName(f) + ": " + ex.Message);
                        err++;
                    }
                }

                string subDir = Path.Combine(flowsDir, "Subflows");
                if (Directory.Exists(subDir))
                {
                    foreach (string f in Directory.EnumerateFiles(subDir, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            JsonSchemaValidator.ValidateSubflowFile(f);
                            LogHelper.Info("[OK] Subflow " + Path.GetFileName(f));
                            ok++;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("[ERR] Subflow " + Path.GetFileName(f) + ": " + ex.Message);
                            err++;
                        }
                    }
                }
            }
            else
            {
                LogHelper.Info("[SKIP] Flows 目录未找到");
            }

            LogHelper.Info("Summary: OK=" + ok + ", ERR=" + err);
            return err > 0 ? 1 : 0;
        }
    }
}
