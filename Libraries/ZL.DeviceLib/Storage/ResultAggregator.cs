using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ZL.DeviceLib.Storage
{
    public sealed class ResultAggregator
    {
        private readonly Dictionary<string, object> _values = new();

        public void AddStepResult(string stepName, Dictionary<string, object> outputs)
        {
            if (outputs == null) return;
            foreach (var kv in outputs)
            {
                string key = $"{stepName}.{kv.Key}";
                _values[key] = kv.Value;
            }
        }

        public SeatResults ToSeatResults(string model, string sn, float testing_time, Dictionary<string, string> mapping)
        {
            var now = DateTime.Now;
            var result = new SeatResults
            {
                model = model,
                sn = sn,
                test_date = now.Date,
                test_time = now,
                created_at = now,
                updated_at = now,
                testing_time = testing_time,
                test_result = EvaluateFinalResult()
            };

            foreach (var kv in mapping)
            {
                if (_values.TryGetValue(kv.Key, out var v) && v != null)
                {
                    SetProperty(result, kv.Value, v);
                }
            }

            // 如果 test_result 没有通过映射赋值，就用默认规则
            if (string.IsNullOrEmpty(result.test_result))
            {
                result.test_result = EvaluateFinalResult();
            }

            return result;
        }

        private void SetProperty(SeatResults target, string propName, object value)
        {
            var prop = typeof(SeatResults).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite) return;

            try
            {
                object converted = Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
                prop.SetValue(target, converted);
            }
            catch(Exception ex) 
            {
                // 忽略转换失败
            }
        }

        private string EvaluateFinalResult()
        {
            var fails = _values.Where(kv => kv.Key.EndsWith(".pass") && kv.Value is bool b && !b);
            return fails.Any() ? "False" : "True";
        }
    }
}
