using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace ZL.DeviceLib.Devices
{
    /*
     * // HandshakeSpec 与 InitStepSpec 是你之前定义的 POCO
        var hs = SettingsBinder.Bind<HandshakeSpec>(cfg.Settings, "handshake");
        var initSteps = SettingsBinder.BindList<InitStepSpec>(cfg.Settings, "initSequence");

        // 也可以拿简单值（大小写不敏感、点路径）
        var delimiter = SettingsBinder.Get<string>(cfg.Settings, "transport.delimiter", "\n");
        var timeout = SettingsBinder.Get<int>(cfg.Settings, "handshake.timeoutMs", 300);
     */
    public static class SettingsBinder
    {
        // 全局 JsonSerializer，尽量“容错”
        private static readonly JsonSerializer _serializer = new JsonSerializer
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Culture = CultureInfo.InvariantCulture
        };

        /// <summary>
        /// 绑定一个对象（如 HandshakeSpec），未找到或失败返回 default(T)。
        /// path 支持点路径（大小写不敏感）：handshake、transport.delimiter
        /// </summary>
        public static T Bind<T>(IDictionary<string, object> settings, string path)
        {
            var tok = SelectToken(settings, path);
            if (tok == null || tok.Type == JTokenType.Null) return default(T);
            try { return tok.ToObject<T>(_serializer); }
            catch { return default(T); }
        }

        /// <summary>
        /// 绑定一个对象（如 HandshakeSpec），支持提供默认值。
        /// </summary>
        public static T BindOrDefault<T>(IDictionary<string, object> settings, string path, T defaultValue)
        {
            var tok = SelectToken(settings, path);
            if (tok == null || tok.Type == JTokenType.Null) return defaultValue;
            try { return tok.ToObject<T>(_serializer); }
            catch { return defaultValue; }
        }

        /// <summary>
        /// 绑定列表（如 List&lt;InitStepSpec&gt;）。未找到返回空列表（可选）。
        /// </summary>
        public static List<T> BindList<T>(IDictionary<string, object> settings, string path, bool returnEmptyIfMissing = true)
        {
            var tok = SelectToken(settings, path);
            if (tok == null || tok.Type == JTokenType.Null)
                return returnEmptyIfMissing ? new List<T>() : null;

            try
            {
                if (tok.Type == JTokenType.Array)
                    return tok.ToObject<List<T>>(_serializer) ?? (returnEmptyIfMissing ? new List<T>() : null);

                // 单对象自动包一层成为列表
                var one = tok.ToObject<T>(_serializer);
                if (one == null) return returnEmptyIfMissing ? new List<T>() : null;
                return new List<T> { one };
            }
            catch
            {
                return returnEmptyIfMissing ? new List<T>() : null;
            }
        }

        /// <summary>
        /// 获取简单标量（string/int/double/bool等）；失败给默认值。
        /// </summary>
        public static T Get<T>(IDictionary<string, object> settings, string path, T defaultValue = default(T))
        {
            var tok = SelectToken(settings, path);
            if (tok == null || tok.Type == JTokenType.Null) return defaultValue;

            try { return tok.ToObject<T>(_serializer); }
            catch
            {
                // 尝试简单转换（string → 数值/布尔）
                try
                {
                    var s = tok.Type == JTokenType.String ? tok.Value<string>() : tok.ToString();
                    return (T)ConvertSimple(typeof(T), s, defaultValue);
                }
                catch { return defaultValue; }
            }
        }

        /// <summary>
        /// TryGet 标量，失败不抛异常。
        /// </summary>
        public static bool TryGet<T>(IDictionary<string, object> settings, string path, out T value)
        {
            var tok = SelectToken(settings, path);
            if (tok == null || tok.Type == JTokenType.Null) { value = default(T); return false; }

            try { value = tok.ToObject<T>(_serializer); return true; }
            catch
            {
                try
                {
                    var s = tok.Type == JTokenType.String ? tok.Value<string>() : tok.ToString();
                    value = (T)ConvertSimple(typeof(T), s, default(T));
                    return true;
                }
                catch { value = default(T); return false; }
            }
        }

        // ==================== 内部工具：从 Settings + 路径选出 JToken ====================

        private static JToken SelectToken(IDictionary<string, object> settings, string path)
        {
            if (settings == null || settings.Count == 0 || string.IsNullOrWhiteSpace(path)) return null;

            var root = ToJToken(settings);
            return SelectTokenCaseInsensitive(root, path);
        }

        private static JToken SelectTokenCaseInsensitive(JToken root, string path)
        {
            if (root == null) return null;
            var segments = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            JToken current = root;

            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i];

                if (current is JObject obj)
                {
                    // 大小写不敏感
                    JProperty found = null;
                    foreach (var prop in obj.Properties())
                    {
                        if (string.Equals(prop.Name, seg, StringComparison.OrdinalIgnoreCase))
                        {
                            found = prop; break;
                        }
                    }
                    if (found == null) return null;
                    current = found.Value;
                }
                else if (current is JArray arr)
                {
                    // 支持数组索引（可选）
                    if (int.TryParse(seg, out var idx))
                    {
                        if (idx < 0 || idx >= arr.Count) return null;
                        current = arr[idx];
                    }
                    else
                    {
                        // 不支持用名字取数组元素
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }

            return current;
        }

        private static JToken ToJToken(object value)
        {
            if (value == null) return JValue.CreateNull();

            // 已经是 JToken
            var asToken = value as JToken;
            if (asToken != null) return asToken;

            // 字符串：如果是 JSON，则解析；否则当作原值
            var s = value as string;
            if (s != null)
            {
                var trimmed = s.Trim();
                if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
                    (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
                {
                    try { return JToken.Parse(trimmed); } catch { return new JValue(s); }
                }
                return new JValue(s);
            }

            // IDictionary / IEnumerable 自动转 JToken
            var dict = value as IDictionary;
            if (dict != null) return JObject.FromObject(value, _serializer);

            var enumerable = value as IEnumerable;
            if (enumerable != null && !(value is string)) return JArray.FromObject(value, _serializer);

            // 其他对象直接 FromObject
            return JToken.FromObject(value, _serializer);
        }

        // 简单标量转换（给 Get/TryGet 的兜底用）
        private static object ConvertSimple(Type targetType, string s, object defaultValue)
        {
            if (targetType == typeof(string)) return s;
            if (targetType == typeof(bool) || targetType == typeof(bool?))
            {
                var up = (s ?? "").Trim().ToUpperInvariant();
                if (up == "1" || up == "TRUE" || up == "ON") return true;
                if (up == "0" || up == "FALSE" || up == "OFF") return false;

                bool b; if (bool.TryParse(s, out b)) return b;
                return defaultValue;
            }

            if (targetType == typeof(int) || targetType == typeof(int?))
            {
                int v; if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
                double dv; if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) return (int)dv;
                return defaultValue;
            }

            if (targetType == typeof(long) || targetType == typeof(long?))
            {
                long v; if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
                double dv; if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out dv)) return (long)dv;
                return defaultValue;
            }

            if (targetType == typeof(double) || targetType == typeof(double?))
            {
                double v; if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v)) return v;
                return defaultValue;
            }

            if (targetType == typeof(float) || targetType == typeof(float?))
            {
                float v; if (float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v)) return v;
                double dv; if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out dv)) return (float)dv;
                return defaultValue;
            }

            // 其他类型：放弃，交回默认值
            return defaultValue;
        }
    }
}
