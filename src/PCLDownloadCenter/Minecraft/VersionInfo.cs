using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 一个版本 JSON 的解析结果。
    /// 支持从文件 / URL / 内存 JSON 读取，并支持沿 inheritsFrom 向上合并父版本
    /// （加载器 / 整合包安装会用到）。
    /// </summary>
    public sealed class VersionInfo
    {
        public string Id { get; set; }
        public string Type { get; set; }
        /// <summary>版本 JSON（FromFile / FromUrl 时为原始 JSON；ResolveFromDisk 后为合并结果）。</summary>
        public JObject Json { get; set; }
        /// <summary>父版本 id；为空表示没有继承。</summary>
        public string InheritsFrom { get; set; }

        public VersionInfo() { }

        /// <summary>是否有父版本（需要合并）。</summary>
        public bool HasParent { get { return !string.IsNullOrWhiteSpace(InheritsFrom); } }

        public string MainClass { get { return JsonUtil.Str(Json, "mainClass"); } }

        /// <summary>版本要求的 Java 主版本号（没有 javaVersion 时返回 0）。</summary>
        public int JavaMajor
        {
            get
            {
                JObject jv = Json == null ? null : Json["javaVersion"] as JObject;
                return (int)JsonUtil.Long(jv, "majorVersion", 0);
            }
        }

        public JObject Downloads { get { return Json == null ? null : Json["downloads"] as JObject; } }
        public JArray Libraries { get { return Json == null ? null : Json["libraries"] as JArray; } }
        public string Assets { get { return JsonUtil.Str(Json, "assets"); } }
        public string Jar { get { return JsonUtil.Str(Json, "jar"); } }

        public string AssetIndexId
        {
            get
            {
                JObject a = Json == null ? null : Json["assetIndex"] as JObject;
                return JsonUtil.Str(a, "id");
            }
        }

        // ==================== 路径工具 ====================

        public static string GetVersionDir(InstallContext ctx, string id)
        {
            ctx = VersionManifest.EnsureContext(ctx);
            return Path.Combine(ctx.VersionsDir, id);
        }

        public static string GetVersionJsonPath(InstallContext ctx, string id)
        {
            ctx = VersionManifest.EnsureContext(ctx);
            return Path.Combine(ctx.VersionsDir, id, id + ".json");
        }

        /// <summary>该版本目录是否已安装（版本 JSON 存在且可解析）。</summary>
        public static bool IsInstalled(InstallContext ctx, string id)
        {
            try
            {
                ctx = VersionManifest.EnsureContext(ctx);
                if (string.IsNullOrWhiteSpace(id)) return false;
                string path = GetVersionJsonPath(ctx, id);
                if (!File.Exists(path)) return false;
                JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 沿 inheritsFrom 找到实际游戏主文件（jar）的路径；找不到返回 null。
        /// 原版返回 versions/&lt;id&gt;/&lt;id&gt;.jar；加载器返回其父原版的 jar。
        /// </summary>
        public static string GetClientJarPath(InstallContext ctx, string versionId, bool followInherits = true)
        {
            ctx = VersionManifest.EnsureContext(ctx);
            string cur = versionId;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 20 && !string.IsNullOrWhiteSpace(cur); i++)
            {
                if (!seen.Add(cur)) break;
                VersionInfo vi = LoadFromDisk(ctx, cur);
                if (vi == null) return null;
                string jar = vi.Jar;
                if (!string.IsNullOrWhiteSpace(jar)) return Path.Combine(ctx.VersionsDir, jar, jar + ".jar");
                if (vi.Downloads != null && vi.Downloads["client"] != null) return Path.Combine(ctx.VersionsDir, cur, cur + ".jar");
                if (!followInherits || !vi.HasParent) return null;
                cur = vi.InheritsFrom;
            }
            return null;
        }

        // ==================== 读取 ====================

        public static VersionInfo FromJson(JObject json)
        {
            if (json == null) throw new ArgumentNullException("json");
            return new VersionInfo
            {
                Id = JsonUtil.Str(json, "id"),
                Type = JsonUtil.Str(json, "type"),
                InheritsFrom = JsonUtil.Str(json, "inheritsFrom"),
                Json = json
            };
        }

        public static VersionInfo FromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("找不到版本 JSON：" + path, path);
            JObject json = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            VersionInfo vi = FromJson(json);
            if (string.IsNullOrWhiteSpace(vi.Id))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                vi.Id = name;
            }
            return vi;
        }

        public static async Task<VersionInfo> FromUrlAsync(InstallContext ctx, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("版本 JSON 地址为空", "url");
            ctx = VersionManifest.EnsureContext(ctx);
            string text = await ctx.Dl.GetStringAsync(url, ctx.Ct, 60000, 3, true).ConfigureAwait(false);
            JObject json = JsonUtil.Parse(text);
            if (json.Count == 0) throw new Exception("版本 JSON 内容为空：" + url);
            return FromJson(json);
        }

        /// <summary>从 Minecraft 目录读取某个已安装版本的原始 JSON（未合并）；不存在返回 null。</summary>
        public static VersionInfo LoadFromDisk(InstallContext ctx, string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId)) return null;
            ctx = VersionManifest.EnsureContext(ctx);
            string path = GetVersionJsonPath(ctx, versionId);
            if (!File.Exists(path)) return null;
            try
            {
                JObject json = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                VersionInfo vi = FromJson(json);
                if (string.IsNullOrWhiteSpace(vi.Id)) vi.Id = versionId;
                return vi;
            }
            catch (Exception ex)
            {
                throw new Exception("读取版本 JSON 失败：" + path + "（文件可能损坏，请重新安装该版本）:: " + ex.Message, ex);
            }
        }

        /// <summary>读取某个已安装版本并向上合并 inheritsFrom（不存在返回 null）。</summary>
        public static VersionInfo LoadResolved(InstallContext ctx, string versionId)
        {
            VersionInfo raw = LoadFromDisk(ctx, versionId);
            if (raw == null) return null;
            return raw.ResolveFromDisk(ctx);
        }

        // ==================== inheritsFrom 合并 ====================

        /// <summary>
        /// 沿 inheritsFrom 向上合并父版本 JSON，返回合并后的 VersionInfo（Json 为完整内容）。
        /// 父版本缺失时抛出可操作的异常。
        /// </summary>
        public VersionInfo ResolveFromDisk(InstallContext ctx)
        {
            ctx = VersionManifest.EnsureContext(ctx);
            return ResolveChain(ctx, this, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
        }

        private static VersionInfo ResolveChain(InstallContext ctx, VersionInfo info, HashSet<string> seen, int depth)
        {
            if (info == null) throw new ArgumentNullException("info");
            if (depth > 20) throw new Exception("版本继承链过深（超过 20 层），版本 JSON 可能有误");
            if (!info.HasParent) return info;

            string parentId = info.InheritsFrom.Trim();
            if (!seen.Add(parentId)) throw new Exception("版本继承出现循环：" + parentId);
            VersionInfo parent = LoadFromDisk(ctx, parentId);
            if (parent == null)
                throw new Exception("缺少父版本“" + parentId + "”：请先安装原版 " + parentId +
                                    "（--install vanilla " + parentId + "）后再重试。");

            parent = ResolveChain(ctx, parent, seen, depth + 1);
            JObject merged = Merge(parent.Json, info.Json);
            return new VersionInfo
            {
                Id = string.IsNullOrWhiteSpace(info.Id) ? parentId : info.Id,
                Type = info.Type,
                Json = merged,
                InheritsFrom = null
            };
        }

        /// <summary>把子版本 JSON 合并到父版本 JSON 上（libraries 合并去重、arguments 拼接、downloads 覆盖）。</summary>
        public static JObject Merge(JObject parent, JObject child)
        {
            if (parent == null) return child == null ? new JObject() : (JObject)child.DeepClone();
            if (child == null) return (JObject)parent.DeepClone();

            JObject result = (JObject)parent.DeepClone();
            foreach (JProperty prop in child.Properties())
            {
                switch (prop.Name)
                {
                    case "libraries":
                        result["libraries"] = MergeLibraries(result["libraries"] as JArray, prop.Value as JArray);
                        break;
                    case "arguments":
                        result["arguments"] = MergeArguments(result["arguments"] as JObject, prop.Value as JObject);
                        break;
                    case "downloads":
                        result["downloads"] = MergeShallow(result["downloads"] as JObject, prop.Value as JObject);
                        break;
                    default:
                        result[prop.Name] = prop.Value.DeepClone();
                        break;
                }
            }
            return result;
        }

        private static JObject MergeShallow(JObject parent, JObject child)
        {
            if (parent == null) return child == null ? new JObject() : (JObject)child.DeepClone();
            if (child == null) return (JObject)parent.DeepClone();
            JObject result = (JObject)parent.DeepClone();
            foreach (JProperty prop in child.Properties()) result[prop.Name] = prop.Value.DeepClone();
            return result;
        }

        private static JObject MergeArguments(JObject parent, JObject child)
        {
            if (parent == null) return child == null ? new JObject() : (JObject)child.DeepClone();
            if (child == null) return (JObject)parent.DeepClone();
            JObject result = (JObject)parent.DeepClone();
            foreach (JProperty prop in child.Properties())
            {
                JArray pa = result[prop.Name] as JArray;
                JArray ca = prop.Value as JArray;
                if (pa != null && ca != null)
                {
                    foreach (JToken x in ca) pa.Add(x.DeepClone());
                }
                else result[prop.Name] = prop.Value.DeepClone();
            }
            return result;
        }

        private static JArray MergeLibraries(JArray parent, JArray child)
        {
            JArray result = new JArray();
            Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (parent != null)
            {
                foreach (JToken t in parent)
                {
                    result.Add(t.DeepClone());
                    string key = LibraryKey(t as JObject);
                    if (key != null && !index.ContainsKey(key)) index[key] = result.Count - 1;
                }
            }
            if (child != null)
            {
                foreach (JToken t in child)
                {
                    string key = LibraryKey(t as JObject);
                    int pos;
                    if (key != null && index.TryGetValue(key, out pos)) result[pos] = t.DeepClone();
                    else
                    {
                        result.Add(t.DeepClone());
                        if (key != null) index[key] = result.Count - 1;
                    }
                }
            }
            return result;
        }

        /// <summary>库去重键：group:artifact[:classifier]（同 group/artifact/classifier 以子版本为准）。</summary>
        private static string LibraryKey(JObject lib)
        {
            string name = JsonUtil.Str(lib, "name");
            if (string.IsNullOrWhiteSpace(name)) return null;
            string raw = name;
            int at = raw.LastIndexOf('@');
            if (at > 0) raw = raw.Substring(0, at);
            string[] parts = raw.Split(':');
            if (parts.Length < 2) return name;
            string key = parts[0] + ":" + parts[1];
            if (parts.Length >= 4) key += ":" + parts[3];
            return key;
        }
    }
}
