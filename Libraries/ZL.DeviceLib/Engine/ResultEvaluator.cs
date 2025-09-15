using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZL.DeviceLib.Engine
{
    public static class ResultEvaluator
    {
        public static bool Evaluate(object expectedObj,
                            IDictionary<string, object> outputs,
                            IDictionary<string, object> parameters,
                            out string reason)
        {
            reason = null;
            if (expectedObj == null) return true;
            if (outputs == null) outputs = new Dictionary<string, object>();
            var expectedToken = expectedObj as JToken ?? JToken.FromObject(expectedObj);
            if (expectedToken.Type == JTokenType.Object && !expectedToken.HasValues) return true;

            if (IsFlatEqualsExpectation(expectedToken))
            {
                var expectedObjFlat = (JObject)expectedToken;
                if (expectedObjFlat.Count == 0) return true;
                if (outputs.Count == 0) { reason = "device outputs empty"; return false; }
                return EvaluateFlatEquals(expectedObjFlat, outputs, out reason);
            }

            JArray rules = expectedToken.Type == JTokenType.Array ? (JArray)expectedToken : new JArray(expectedToken);
            var effectiveRules = new List<JObject>();
            foreach (var r in rules)
            {
                if (r == null || r.Type != JTokenType.Object) continue;
                var obj = (JObject)r; if (!obj.HasValues) continue; effectiveRules.Add(obj);
            }
            if (effectiveRules.Count == 0) return true;
            if (outputs.Count == 0) { reason = "device outputs empty"; return false; }

            foreach (var rule in effectiveRules)
            {
                var mode = (rule.Value<string>("mode") ?? "equals").Trim().ToLowerInvariant();
                if (mode == "exists")
                {
                    var existsKey = rule.Value<string>("key") ?? rule.Value<string>("exists") ?? rule.Value<string>("name");
                    if (string.IsNullOrEmpty(existsKey)) { reason = "exists rule missing key"; return false; }
                    if (!outputs.ContainsKey(existsKey)) { reason = "missing key: " + existsKey; return false; }
                    continue;
                }

                var key = rule.Value<string>("key");
                if (string.IsNullOrEmpty(key)) { reason = "expected rule missing key"; return false; }
                object actual; if (!outputs.TryGetValue(key, out actual)) { reason = "missing key: " + key; return false; }

                switch (mode)
                {
                    case "equals":
                        {
                            var expectedValToken = rule["value"]; if (!LooseEquals(actual, expectedValToken))
                            { reason = key + "=" + ToDebugString(actual) + " != expected " + ToDebugString(expectedValToken); return false; }
                            break;
                        }
                    case "not_equals":
                        {
                            var notValToken = rule["value"]; if (LooseEquals(actual, notValToken))
                            { reason = key + "=" + ToDebugString(actual) + " should not equal " + ToDebugString(notValToken); return false; }
                            break;
                        }
                    case "range":
                        {
                            double actualVal; if (!TryToDouble(actual, out actualVal)) { reason = key + "=" + ToDebugString(actual) + " not numeric"; return false; }
                            double? min = rule["min"] != null ? (double?)rule.Value<double>("min") : null;
                            double? max = rule["max"] != null ? (double?)rule.Value<double>("max") : null;
                            if (min.HasValue && actualVal < min.Value) { reason = key + "=" + actualVal.ToString(CultureInfo.InvariantCulture) + " < min " + min.Value.ToString(CultureInfo.InvariantCulture); return false; }
                            if (max.HasValue && actualVal > max.Value) { reason = key + "=" + actualVal.ToString(CultureInfo.InvariantCulture) + " > max " + max.Value.ToString(CultureInfo.InvariantCulture); return false; }
                            break;
                        }
                    case "tolerance":
                        {
                            double actualVal; if (!TryToDouble(actual, out actualVal)) { reason = key + "=" + ToDebugString(actual) + " not numeric"; return false; }
                            if (rule["target"] == null || rule["tolerance"] == null) { reason = "tolerance rule missing target or tolerance"; return false; }
                            var target = rule.Value<double>("target"); var tol = rule.Value<double>("tolerance");
                            if (Math.Abs(actualVal - target) > tol) { reason = key + "=" + actualVal.ToString(CultureInfo.InvariantCulture) + " not within " + target.ToString(CultureInfo.InvariantCulture) + "±" + tol.ToString(CultureInfo.InvariantCulture); return false; }
                            break;
                        }
                    case "contains":
                        {
                            var expectStr = rule["value"] != null ? rule["value"].ToString() : null; var actualStr = actual == null ? null : actual.ToString();
                            if (string.IsNullOrEmpty(expectStr) || string.IsNullOrEmpty(actualStr) || actualStr.IndexOf(expectStr, StringComparison.Ordinal) < 0)
                            { reason = key + " does not contain " + expectStr; return false; }
                            break;
                        }
                    case "regex":
                        {
                            var pattern = rule["pattern"] != null ? rule["pattern"].ToString() : null; var actualStr = actual == null ? null : actual.ToString();
                            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(actualStr) || !Regex.IsMatch(actualStr, pattern))
                            { reason = key + "=" + ToDebugString(actual) + " does not match regex " + pattern; return false; }
                            break;
                        }
                    case "in_set":
                        {
                            var values = rule["values"] != null ? rule["values"].ToObject<List<string>>() : null;
                            if (values == null || !values.Contains((actual ?? "").ToString())) { reason = key + "=" + ToDebugString(actual) + " not in allowed set"; return false; }
                            break;
                        }
                    case "gt":
                    case "ge":
                    case "lt":
                    case "le":
                        {
                            double actualVal; if (!TryToDouble(actual, out actualVal)) { reason = key + "=" + ToDebugString(actual) + " not numeric"; return false; }
                            if (rule["value"] == null) { reason = mode + " rule missing value"; return false; }
                            double bound; if (!TryToDouble(rule["value"], out bound)) { reason = "rule value not numeric"; return false; }
                            var ok = (mode == "gt" && actualVal > bound) || (mode == "ge" && actualVal >= bound) || (mode == "lt" && actualVal < bound) || (mode == "le" && actualVal <= bound);
                            if (!ok) { reason = key + "=" + actualVal.ToString(CultureInfo.InvariantCulture) + " not satisfy " + mode + " " + bound.ToString(CultureInfo.InvariantCulture); return false; }
                            break;
                        }
                    default:
                        reason = "unsupported mode: " + mode; return false;
                }
            }
            return true;
        }

        private static bool EvaluateFlatEquals(JObject expectedObj, IDictionary<string, object> outputs, out string reason)
        {
            reason = null;
            foreach (var prop in expectedObj.Properties())
            {
                object actual; if (!outputs.TryGetValue(prop.Name, out actual)) { reason = "missing key: " + prop.Name; return false; }
                if (!LooseEquals(actual, prop.Value)) { reason = prop.Name + "=" + ToDebugString(actual) + " != expected " + ToDebugString(prop.Value); return false; }
            }
            return true;
        }

        private static bool IsFlatEqualsExpectation(JToken token)
        {
            if (token.Type != JTokenType.Object) return false;
            var obj = (JObject)token;
            return obj["mode"] == null && obj["key"] == null && obj["values"] == null && obj["pattern"] == null;
        }

        private static bool LooseEquals(object actual, JToken expectedToken)
        {
            if (expectedToken == null) return actual == null;
            double aNum, eNum; if (TryToDouble(actual, out aNum) && TryToDouble(expectedToken, out eNum)) return Math.Abs(aNum - eNum) < 1e-9;
            bool aBool, eBool; if (TryToBool(actual, out aBool) && TryToBool(expectedToken, out eBool)) return aBool == eBool;
            if (actual is System.Collections.IEnumerable && !(actual is string) && (expectedToken.Type == JTokenType.Array))
            { var aStr = ToDebugString(actual); var eStr = ToDebugString(expectedToken); return string.Equals(aStr, eStr, StringComparison.Ordinal); }
            var a = actual == null ? null : actual.ToString(); var e = expectedToken.ToString(); return string.Equals(a, e, StringComparison.Ordinal);
        }

        private static bool TryToDouble(object value, out double result)
        {
            result = 0; if (value == null) return false;
            var token = value as JToken; if (token != null)
            { if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) { result = token.Value<double>(); return true; }
              return double.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result); }
            if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is float || value is double || value is decimal)
            { try { result = Convert.ToDouble(value, CultureInfo.InvariantCulture); return true; } catch { return false; } }
            var str = value as string; if (str != null) return double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            return false;
        }
        private static bool TryToDouble(JToken token, out double result)
        {
            result = 0; if (token == null) return false; if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float) { result = token.Value<double>(); return true; }
            return double.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result);
        }
        private static bool TryToBool(object value, out bool result)
        {
            result = false; if (value == null) return false; var token = value as JToken; if (token != null)
            { if (token.Type == JTokenType.Boolean) { result = token.Value<bool>(); return true; }
              bool b; if (bool.TryParse(token.ToString(), out b)) { result = b; return true; } return false; }
            if (value is bool) { result = (bool)value; return true; }
            var str = value as string; if (str != null) return bool.TryParse(str, out result);
            return false;
        }
        private static string ToDebugString(object obj)
        {
            if (obj == null) return "null"; var token = obj as JToken; if (token != null) return token.ToString(Newtonsoft.Json.Formatting.None);
            var enumerable = obj as System.Collections.IEnumerable; if (enumerable != null && !(obj is string))
            { var items = new List<string>(); foreach (var it in enumerable) items.Add(ToDebugString(it)); return "[" + string.Join(",", items) + "]"; }
            return Convert.ToString(obj, CultureInfo.InvariantCulture);
        }
    }
}

