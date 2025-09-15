using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ZL.DeviceLib.Engine;
using ZL.DeviceLib.Models;

namespace ZL.DeviceLib.Devices.Actions
{
    public class HttpActionDevice : IDevice
    {
        private static readonly HttpClient _http = new HttpClient();
        private readonly DeviceConfig _cfg;
        public HttpActionDevice(DeviceConfig cfg) { _cfg = cfg; }

        public DeviceExecResult Execute(StepConfig step, StepContext ctx)
        {
            var outputs = new Dictionary<string, object>();
            var token = ctx.Cancellation;
            try
            {
                var p = step.Parameters ?? new Dictionary<string, object>();
                string method = GetStr(p, "method", "GET").ToUpperInvariant();
                string url = GetStr(p, "url", null);
                if (string.IsNullOrWhiteSpace(url)) throw new Exception("HTTP 参数缺失: url");
                var headers = GetDict(p, "headers");
                var query = GetDict(p, "query");
                string contentType = GetStr(p, "contentType", "application/json");
                object body = null; p.TryGetValue("body", out body);

                if (query != null && query.Count > 0)
                {
                    var q = string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(Convert.ToString(kv.Value))));
                    url += (url.Contains("?") ? "&" : "?") + q;
                }

                var rq = new HttpRequestMessage(new HttpMethod(method), url);
                if (headers != null)
                    foreach (var kv in headers) rq.Headers.TryAddWithoutValidation(kv.Key, Convert.ToString(kv.Value));

                if (method != "GET" && method != "HEAD")
                {
                    if (body == null) rq.Content = new StringContent(string.Empty);
                    else if (body is string s) rq.Content = new StringContent(s, System.Text.Encoding.UTF8, contentType);
                    else rq.Content = new StringContent(JsonConvert.SerializeObject(body), System.Text.Encoding.UTF8, contentType);
                }

                Task<HttpResponseMessage> sendTask = _http.SendAsync(rq, HttpCompletionOption.ResponseContentRead, token);
                var resp = sendTask.GetAwaiter().GetResult();
                string respText = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                outputs["status"] = (int)resp.StatusCode;
                outputs["headers"] = resp.Headers.ToDictionary(h => h.Key, h => (object)string.Join(",", h.Value));
                outputs["body"] = respText;
                try { outputs["json"] = JsonConvert.DeserializeObject<object>(respText); } catch { }
                return new DeviceExecResult { Success = resp.IsSuccessStatusCode, Message = "http " + (int)resp.StatusCode, Outputs = outputs };
            }
            catch (OperationCanceledException)
            { return new DeviceExecResult { Success = false, Message = "cancelled", Outputs = outputs }; }
            catch (Exception ex)
            { return new DeviceExecResult { Success = false, Message = "HTTP Exception: " + ex.Message, Outputs = outputs }; }
        }

        private static string GetStr(Dictionary<string, object> dict, string key, string defv)
            => (dict != null && dict.TryGetValue(key, out var v) && v != null) ? Convert.ToString(v) : defv;
        private static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
            => (dict != null && dict.TryGetValue(key, out var v) && v is Newtonsoft.Json.Linq.JObject jo) ? jo.ToObject<Dictionary<string, object>>()
                : (dict != null && dict.TryGetValue(key, out v) && v is Dictionary<string, object> d) ? d : null;
    }
}

