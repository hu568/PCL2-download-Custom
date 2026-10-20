using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>Modrinth 项目（整合包）。</summary>
    internal sealed class ModrinthProject
    {
        public string Id;
        public string Slug;
        public string Title;
        public string Description;
        public string IconUrl;
        public string ProjectType;
    }

    /// <summary>Modrinth 版本中的一个文件。</summary>
    internal sealed class ModrinthVersionFile
    {
        public string Url;
        public string Filename;
        public long Size = -1;
        public string Sha1;
        public string Sha512;
        public bool Primary;
    }

    /// <summary>Modrinth 项目版本。</summary>
    internal sealed class ModrinthVersion
    {
        public string Id;
        public string ProjectId;
        public string Name;
        public string VersionNumber;
        public string VersionType;
        public DateTime Published;
        public string GameVersionsText = "";
        public List<string> GameVersions = new List<string>();
        public List<string> Loaders = new List<string>();
        public List<ModrinthVersionFile> Files = new List<ModrinthVersionFile>();
    }

    /// <summary>
    /// Modrinth API 最小封装（整合包安装所需）：项目信息、版本列表、版本详情。
    /// 无需 API Key，但必须带 User-Agent（Downloader 已提供）。
    /// </summary>
    internal static class ModrinthApi
    {
        public const string BaseUrl = "https://api.modrinth.com/v2";

        /// <summary>解析项目输入：支持项目 ID、slug、项目网址，以及带 /version/ 的网址。</summary>
        public static void ParseProjectInput(string input, out string projectId, out string versionId)
        {
            projectId = (input ?? "").Trim();
            versionId = null;
            if (projectId.Length == 0) return;
            if (!projectId.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !projectId.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                var uri = new Uri(projectId);
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                for (int i = 0; i + 1 < parts.Length; i++)
                {
                    if (string.Equals(parts[i], "version", StringComparison.OrdinalIgnoreCase))
                    {
                        versionId = Uri.UnescapeDataString(parts[i + 1]);
                        break;
                    }
                }
                string candidate = null;
                string[] typeSegments = { "modpack", "project", "mod", "datapack", "resourcepack", "shader" };
                for (int i = 0; i + 1 < parts.Length; i++)
                {
                    if (Array.Exists(typeSegments, s => string.Equals(s, parts[i], StringComparison.OrdinalIgnoreCase)))
                    {
                        candidate = parts[i + 1];
                        break;
                    }
                }
                if (candidate == null && parts.Length >= 1) candidate = parts[parts.Length - 1];
                if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "version", StringComparison.OrdinalIgnoreCase))
                    projectId = Uri.UnescapeDataString(candidate);
            }
            catch { }
        }

        /// <summary>获取项目信息。</summary>
        public static async Task<ModrinthProject> GetProjectAsync(InstallContext ctx, string idOrUrl)
        {
            string projectId, ignoredVersion;
            ParseProjectInput(idOrUrl, out projectId, out ignoredVersion);
            if (string.IsNullOrWhiteSpace(projectId)) throw new Exception("缺少 Modrinth 项目 ID");
            var url = BaseUrl + "/project/" + Uri.EscapeDataString(projectId);
            JObject obj;
            try
            {
                obj = await ctx.Dl.GetJsonAsync(url, ctx.Ct, 30000, 3, false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new Exception("查询 Modrinth 项目失败（" + projectId + "）：" + ex.Message +
                                    "。请检查网络连接，或确认项目 ID / 网址是否正确。", ex);
            }
            var project = new ModrinthProject
            {
                Id = JsonUtil.Str(obj, "id", projectId),
                Slug = JsonUtil.Str(obj, "slug", ""),
                Title = JsonUtil.Str(obj, "title", projectId),
                Description = JsonUtil.Str(obj, "description", ""),
                IconUrl = JsonUtil.Str(obj, "icon_url", null),
                ProjectType = JsonUtil.Str(obj, "project_type", "")
            };
            if (!string.IsNullOrEmpty(project.ProjectType) && project.ProjectType != "modpack")
            {
                if (ctx != null) ctx.Warn("该项目类型为 " + project.ProjectType + "，不是整合包，仍尝试按整合包安装");
            }
            return project;
        }

        /// <summary>获取项目全部版本（按发布时间从新到旧）。</summary>
        public static async Task<List<ModrinthVersion>> GetVersionsAsync(InstallContext ctx, string projectId)
        {
            var url = BaseUrl + "/project/" + Uri.EscapeDataString(projectId) + "/version";
            string text;
            try
            {
                text = await ctx.Dl.GetStringAsync(url, ctx.Ct, 30000, 3, false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new Exception("获取 Modrinth 整合包版本列表失败：" + ex.Message, ex);
            }
            var list = new List<ModrinthVersion>();
            var arr = JArray.Parse(text);
            foreach (var token in arr)
            {
                var v = ParseVersion(token as JObject);
                if (v != null) list.Add(v);
            }
            list.Sort((a, b) => b.Published.CompareTo(a.Published));
            return list;
        }

        /// <summary>按版本 ID 获取版本详情；失败返回 null。</summary>
        public static async Task<ModrinthVersion> GetVersionAsync(InstallContext ctx, string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId)) return null;
            var url = BaseUrl + "/version/" + Uri.EscapeDataString(versionId);
            try
            {
                var obj = await ctx.Dl.GetJsonAsync(url, ctx.Ct, 30000, 2, false).ConfigureAwait(false);
                return ParseVersion(obj);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 选择整合包版本：优先版本 ID，其次版本号/名称，最后自动选择最新正式版。
        /// </summary>
        public static async Task<ModrinthVersion> ResolveVersionAsync(InstallContext ctx, string projectId, string versionId,
            string gameVersion = null, string loader = null)
        {
            if (!string.IsNullOrWhiteSpace(versionId))
            {
                var direct = await GetVersionAsync(ctx, versionId).ConfigureAwait(false);
                if (direct != null) return direct;
                var versions = await GetVersionsAsync(ctx, projectId).ConfigureAwait(false);
                var match = versions.FirstOrDefault(v =>
                    string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v.VersionNumber, versionId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v.Name, versionId, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
                var hint = versions.Count > 0
                    ? "可用的最新版本号：" + string.Join("、", versions.Take(5).Select(v => v.VersionNumber))
                    : "该项目没有任何可用版本";
                throw new Exception("在 Modrinth 项目 " + projectId + " 中找不到版本 " + versionId + "。" + hint);
            }

            var all = await GetVersionsAsync(ctx, projectId).ConfigureAwait(false);
            if (all.Count == 0) throw new Exception("该 Modrinth 整合包没有可用版本");
            var candidates = all;
            if (!string.IsNullOrWhiteSpace(gameVersion))
            {
                var filtered = all.Where(v => v.GameVersions.Any(g => string.Equals(g, gameVersion, StringComparison.OrdinalIgnoreCase))).ToList();
                if (filtered.Count > 0) candidates = filtered;
            }
            if (!string.IsNullOrWhiteSpace(loader))
            {
                var filtered = candidates.Where(v => v.Loaders.Any(l => string.Equals(l, loader, StringComparison.OrdinalIgnoreCase))).ToList();
                if (filtered.Count > 0) candidates = filtered;
            }
            return candidates
                .OrderByDescending(v => string.Equals(v.VersionType, "release", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(v => v.Published)
                .First();
        }

        /// <summary>从版本中挑选 .mrpack 文件。</summary>
        public static ModrinthVersionFile PickPackFile(ModrinthVersion version)
        {
            if (version == null || version.Files == null || version.Files.Count == 0) return null;
            foreach (var f in version.Files)
                if (!string.IsNullOrEmpty(f.Filename) && f.Filename.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                    return f;
            foreach (var f in version.Files)
                if (f.Primary) return f;
            return version.Files[0];
        }

        /// <summary>搜索整合包（供 Web UI / 主页使用的最小集）。</summary>
        public static async Task<JObject> SearchAsync(InstallContext ctx, string query, int limit = 20, string projectType = "modpack")
        {
            var facets = "[[\"project_type:" + projectType + "\"]]";
            var url = BaseUrl + "/search?query=" + Uri.EscapeDataString(query ?? "") +
                      "&limit=" + Math.Max(1, limit) + "&facets=" + Uri.EscapeDataString(facets);
            var text = await ctx.Dl.GetStringAsync(url, ctx.Ct, 30000, 2, false).ConfigureAwait(false);
            return JObject.Parse(text);
        }

        private static ModrinthVersion ParseVersion(JObject obj)
        {
            if (obj == null) return null;
            var version = new ModrinthVersion
            {
                Id = JsonUtil.Str(obj, "id", null),
                ProjectId = JsonUtil.Str(obj, "project_id", null),
                Name = JsonUtil.Str(obj, "name", null),
                VersionNumber = JsonUtil.Str(obj, "version_number", null),
                VersionType = JsonUtil.Str(obj, "version_type", "release"),
                Published = ParseDate(JsonUtil.Str(obj, "date_published", null))
            };
            if (string.IsNullOrEmpty(version.Id)) return null;

            var games = obj["game_versions"] as JArray;
            if (games != null)
                foreach (var g in games)
                {
                    var s = g.ToString();
                    version.GameVersions.Add(s);
                }
            version.GameVersionsText = string.Join("、", version.GameVersions);

            var loaders = obj["loaders"] as JArray;
            if (loaders != null)
                foreach (var l in loaders)
                    version.Loaders.Add(l.ToString());

            var files = obj["files"] as JArray;
            if (files != null)
            {
                foreach (var token in files)
                {
                    var f = token as JObject;
                    if (f == null) continue;
                    var file = new ModrinthVersionFile
                    {
                        Url = JsonUtil.Str(f, "url", null),
                        Filename = JsonUtil.Str(f, "filename", null),
                        Size = JsonUtil.Long(f, "size", -1),
                        Primary = JsonUtil.Bool(f, "primary", false)
                    };
                    var hashes = f["hashes"] as JObject;
                    if (hashes != null)
                    {
                        file.Sha1 = JsonUtil.Str(hashes, "sha1", null);
                        file.Sha512 = JsonUtil.Str(hashes, "sha512", null);
                    }
                    if (!string.IsNullOrWhiteSpace(file.Url)) version.Files.Add(file);
                }
            }
            return version;
        }

        private static DateTime ParseDate(string text)
        {
            DateTime value;
            if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out value))
                return value;
            return DateTime.MinValue;
        }
    }
}
