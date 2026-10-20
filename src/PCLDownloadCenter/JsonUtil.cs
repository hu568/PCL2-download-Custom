using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    public static class JsonUtil
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            DateFormatHandling = DateFormatHandling.IsoDateFormat
        };

        public static string Serialize(object obj, bool indented = false)
        {
            return JsonConvert.SerializeObject(obj, indented ? Formatting.Indented : Formatting.None, Settings);
        }
        public static T ToObject<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default(T);
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
        public static T ToObject<T>(JToken token)
        {
            if (token == null) return default(T);
            return token.ToObject<T>();
        }
        public static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            return JObject.Parse(json);
        }
        public static JToken TryParse(string json)
        {
            try { return JToken.Parse(json); } catch { return null; }
        }
        public static string Str(JToken token, string name, string fallback = null)
        {
            if (token == null) return fallback;
            var v = token[name];
            if (v == null || v.Type == JTokenType.Null || v.Type == JTokenType.Undefined) return fallback;
            return v.ToString();
        }
        public static long Long(JToken token, string name, long fallback = -1)
        {
            if (token == null) return fallback;
            var v = token[name];
            if (v == null || v.Type == JTokenType.Null || v.Type == JTokenType.Undefined) return fallback;
            long r;
            return long.TryParse(v.ToString(), out r) ? r : fallback;
        }
        public static bool Bool(JToken token, string name, bool fallback = false)
        {
            if (token == null) return fallback;
            var v = token[name];
            if (v == null) return fallback;
            bool r;
            return bool.TryParse(v.ToString(), out r) ? r : fallback;
        }
        public static void WriteFile(string path, object obj, bool indented = true)
        {
            FileUtil.WriteAllTextAtomic(path, Serialize(obj, indented));
        }
        public static T ReadFile<T>(string path, T fallback = default(T))
        {
            try
            {
                if (!File.Exists(path)) return fallback;
                var text = File.ReadAllText(path, Encoding.UTF8);
                var v = ToObject<T>(text);
                return v == null ? fallback : v;
            }
            catch { return fallback; }
        }
    }
}
