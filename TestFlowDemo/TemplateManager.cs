using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZL.DeviceLib;
using ZL.DeviceLib.Models;

namespace TestFlowDemo
{
    /// <summary>
    /// 模板管理器：负责加载流程模板、根据输入勾选生成完整的测试流程。
    /// </summary>
    public class TemplateManager
    {
        private readonly string _templateFilePath;            // 模板文件的完整路径
        private TemplateLibrary _library;                     // 反序列化后的模板数据
        private readonly object _lock = new object();         // 确保多线程环境下的懒加载安全

        /// <summary>
        /// 构造函数：允许通过 baseDirectory 指定模板所在的基准目录。
        /// </summary>
        /// <param name="baseDirectory">模板文件所在目录，默认取程序基目录。</param>
        public TemplateManager(string baseDirectory = null)
        {
            var baseDir = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            _templateFilePath = Path.Combine(baseDir, "Flows", "flow_templates.json");
        }

        /// <summary>
        /// 对外暴露的流程生成方法，根据型号与勾选的测试项生成完整流程配置。
        /// </summary>
        /// <param name="model">产品型号</param>
        /// <param name="selectedTests">被勾选的测试模板标识集合</param>
        /// <returns>生成的流程配置对象</returns>
        public FlowConfig GenerateFlow(string model, IEnumerable<string> selectedTests)
        {
            EnsureLibraryLoaded();

            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("型号不能为空", nameof(model));
            if (selectedTests == null)
                throw new ArgumentException("必须提供测试模板列表", nameof(selectedTests));

            var trimmedModel = model.Trim();
            var tests = selectedTests
                .Select(x => (x ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (tests.Count == 0)
                throw new ArgumentException("至少需要勾选一个测试模板", nameof(selectedTests));

            // 生成步骤序列：前置步骤 + 勾选的模板步骤 + 后置步骤
            var steps = new List<StepConfig>();
            var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int autoIndex = 1; // 用于生成唯一名称的自增计数器

            void AppendStep(StepConfig source)
            {
                if (source == null) return;
                var clone = CloneStep(source);

                if (string.IsNullOrWhiteSpace(clone.Name))
                {
                    clone.Name = $"dynamic_step_{autoIndex++}";
                }
                var baseName = clone.Name;
                while (!nameSet.Add(clone.Name))
                {
                    clone.Name = $"{baseName}_{autoIndex++}";
                }
                steps.Add(clone);
            }

            foreach (var pre in _library.Prefix)
                AppendStep(pre);

            foreach (var testId in tests)
            {
                if (!_library.Templates.TryGetValue(testId, out var templateStep))
                    throw new KeyNotFoundException($"未找到名称为 {testId} 的测试模板");
                AppendStep(templateStep);
            }

            foreach (var post in _library.Suffix)
                AppendStep(post);

            if (steps.Count == 0)
                throw new InvalidOperationException("模板未定义任何步骤，无法生成流程");

            LinkStepsSequentially(steps, _library.FailureStepName);

            return new FlowConfig
            {
                Model = trimmedModel,
                TestSteps = steps,
                // 若当前已经加载过流程配置，则沿用设备映射；否则给出空字典以避免空引用
                Devices = DeviceServices.Config?.Devices ?? new Dictionary<string, DeviceConfig>()
            };
        }

        /// <summary>
        /// 将生成的流程配置保存为 JSON 文件，并返回保存路径。
        /// </summary>
        /// <param name="flow">待保存的流程配置对象</param>
        public string SaveGeneratedFlow(FlowConfig flow)
        {
            EnsureLibraryLoaded();
            if (flow == null)
                throw new ArgumentNullException(nameof(flow));

            var targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage", "GeneratedPlans");
            Directory.CreateDirectory(targetDir);

            // 处理文件名中的非法字符，避免因型号包含特殊符号导致保存失败
            var safeModel = string.Concat(flow.Model.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeModel))
                safeModel = "MODEL";

            var filePath = Path.Combine(targetDir, $"{safeModel}_{DateTime.Now:yyyyMMddHHmmss}.json");
            var json = JsonConvert.SerializeObject(flow, Formatting.Indented);
            File.WriteAllText(filePath, json);
            return filePath;
        }

        /// <summary>
        /// 将流程配置转换为格式化后的 JSON 字符串，便于展示或日志输出。
        /// </summary>
        public string ToJson(FlowConfig flow)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));
            return JsonConvert.SerializeObject(flow, Formatting.Indented);
        }

        /// <summary>
        /// 加载模板文件并缓存，确保后续访问无需重复读取磁盘。
        /// </summary>
        private void EnsureLibraryLoaded()
        {
            if (_library != null) return;
            lock (_lock)
            {
                if (_library != null) return;
                if (!File.Exists(_templateFilePath))
                    throw new FileNotFoundException($"未找到流程模板文件: {_templateFilePath}");

                var raw = File.ReadAllText(_templateFilePath);
                var library = JsonConvert.DeserializeObject<TemplateLibrary>(raw);
                if (library == null)
                    throw new InvalidOperationException("流程模板文件内容为空或格式错误");

                library.Prefix = library.Prefix ?? new List<StepConfig>();
                library.Suffix = library.Suffix ?? new List<StepConfig>();
                library.Templates = library.Templates != null
                    ? new Dictionary<string, StepConfig>(library.Templates, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, StepConfig>(StringComparer.OrdinalIgnoreCase);
                library.FailureStepName = string.IsNullOrWhiteSpace(library.FailureStepName)
                    ? "power_off"
                    : library.FailureStepName;

                _library = library;
            }
        }

        /// <summary>
        /// 使用序列化 + 反序列化的方式克隆步骤，避免引用共享导致原始模板被修改。
        /// </summary>
        private static StepConfig CloneStep(StepConfig source)
        {
            var json = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<StepConfig>(json);
        }

        /// <summary>
        /// 根据步骤顺序补齐 DependsOn / OnSuccess / OnFailure 信息，构造串行流程。
        /// </summary>
        /// <param name="steps">需要串联的步骤列表</param>
        /// <param name="fallbackFailureStep">失败时默认跳转的步骤名称</param>
        private static void LinkStepsSequentially(List<StepConfig> steps, string fallbackFailureStep)
        {
            if (steps == null || steps.Count == 0)
                throw new ArgumentException("步骤集合为空", nameof(steps));

            string failureStepName = null;
            if (!string.IsNullOrWhiteSpace(fallbackFailureStep))
            {
                var fallbackStep = steps.FirstOrDefault(s => string.Equals(s.Name, fallbackFailureStep, StringComparison.OrdinalIgnoreCase));
                if (fallbackStep != null)
                    failureStepName = fallbackStep.Name; // 使用模板中定义的实际大小写
            }

            for (int i = 0; i < steps.Count; i++)
            {
                var current = steps[i];
                if (i == 0)
                {
                    current.DependsOn = current.DependsOn ?? new List<string>();
                }
                else
                {
                    current.DependsOn = new List<string> { steps[i - 1].Name };
                }

                var nextName = i < steps.Count - 1 ? steps[i + 1].Name : string.Empty;
                current.OnSuccess = nextName;

                if (string.IsNullOrWhiteSpace(current.OnFailure))
                {
                    if (!string.IsNullOrWhiteSpace(failureStepName) && !string.Equals(current.Name, failureStepName, StringComparison.OrdinalIgnoreCase))
                    {
                        current.OnFailure = failureStepName;
                    }
                    else
                    {
                        current.OnFailure = nextName;
                    }
                }
            }
        }

        /// <summary>
        /// 内部使用的模板结构，用于承载 JSON 中的定义。
        /// </summary>
        private class TemplateLibrary
        {
            [JsonProperty("prefix")]
            public List<StepConfig> Prefix { get; set; }

            [JsonProperty("suffix")]
            public List<StepConfig> Suffix { get; set; }

            [JsonProperty("templates")]
            public Dictionary<string, StepConfig> Templates { get; set; }

            [JsonProperty("failureStepName")]
            public string FailureStepName { get; set; }
        }
    }
}
