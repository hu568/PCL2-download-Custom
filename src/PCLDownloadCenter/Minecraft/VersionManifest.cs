using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>版本清单中的一项。</summary>
    public sealed class ManifestEntry
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Url { get; set; }
        public string Sha1 { get; set; }
        public DateTime ReleaseTime { get; set; }

        public bool IsSnapshot { get { return Type == "snapshot"; } }
        public bool IsOld { get { return Type == "old_beta" || Type == "old_alpha"; } }
        public bool IsRelease { get { return Type == "release"; } }
        public override string ToString() { return Id; }
    }

    /// <summary>版本清单。</summary>
    public sealed class ManifestData
    {
        public string LatestRelease { get; set; }
        public string LatestSnapshot { get; set; }
        public List<ManifestEntry> Versions { get; set; }
        public DateTime FetchedAt { get; set; }

        public ManifestData() { Versions = new List<ManifestEntry>(); }

        public ManifestEntry Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var v in Versions)
                if (string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)) return v;
            return null;
        }

        /// <summary>按类型筛选：release / snapshot / old / all（null 或空 = all）。</summary>
        public List<ManifestEntry> Filter(string type)
        {
            var list = new List<ManifestEntry>();
            string t = (type ?? "").Trim().ToLowerInvariant();
            foreach (var v in Versions)
            {
                if (t.Length == 0 || t == "all")
                {
                    list.Add(v);
                    continue;
                }
                if (t == "release" && v.IsRelease) list.Add(v);
                else if (t == "snapshot" && v.IsSnapshot) list.Add(v);
                else if ((t == "old" || t == "old_beta" || t == "old_alpha") && v.IsOld) list.Add(v);
            }
            return list;
        }
    }

    /// <summary>
    /// 版本清单：下载 Mojang 版本清单、本地缓存（30 分钟内复用）、解析并对外提供版本信息。
    /// </summary>
    public static class VersionManifest
    {
        /// <summary>官方版本清单地址（SourceRewriter 会自动镜像到 bmclapi）。</summary>
        public const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";

        /// <summary>缓存有效期（分钟）。</summary>
        public const int CacheMinutes = 30;

        private static readonly object _lock = new object();
        private static ManifestData _memory;

        /// <summary>本地缓存文件：data/cache/version_manifest.json。</summary>
        public static string CacheFile { get { return Path.Combine(Paths.CacheDir, "version_manifest.json"); } }

        /// <summary>本次进程内最近一次拿到的清单（可能来自内存 / 文件缓存 / 网络）。</summary>
        public static ManifestData GetCached()
        {
            lock (_lock) { if (_memory != null) return _memory; }
            ManifestData file = TryLoadCacheFile(true);
            if (file != null) { lock (_lock) _memory = file; }
            return file;
        }

        /// <summary>
        /// 获取版本清单：内存缓存 → 本地文件缓存（30 分钟内）→ 网络。
        /// forceRefresh = true 时跳过缓存强制刷新（失败时若有旧缓存仍会回退使用）。
        /// </summary>
        public static async Task<ManifestData> GetAsync(InstallContext ctx, bool forceRefresh = false)
        {
            ctx = EnsureContext(ctx);

            if (!forceRefresh)
            {
                lock (_lock)
                {
                    if (_memory != null && (DateTime.Now - _memory.FetchedAt).TotalMinutes < CacheMinutes)
                    {
                        ctx.Info("使用内存中的版本清单（共 " + _memory.Versions.Count + " 个版本，30 分钟内复用）");
                        return _memory;
                    }
                }
                ManifestData fromFile = TryLoadCacheFile(false);
                if (fromFile != null)
                {
                    lock (_lock) _memory = fromFile;
                    ctx.Info("使用本地缓存的版本清单（" + fromFile.FetchedAt.ToString("yyyy-MM-dd HH:mm") +
                             "，共 " + fromFile.Versions.Count + " 个版本，30 分钟内复用）");
                    return fromFile;
                }
            }

            ctx.Progress("获取版本清单", 0.01);
            ctx.Info("正在获取 Minecraft 版本清单：" + ManifestUrl);
            try
            {
                string text = await ctx.Dl.GetStringAsync(ManifestUrl, ctx.Ct, 60000, 3, true).ConfigureAwait(false);
                ManifestData data = Parse(text);
                if (data.Versions.Count == 0) throw new Exception("下载到的版本清单为空");

                data.FetchedAt = DateTime.Now;
                SaveCacheFile(ctx, data);
                lock (_lock) _memory = data;

                ctx.Info("版本清单已更新：共 " + data.Versions.Count + " 个版本，最新正式版 " + data.LatestRelease +
                         "，最新快照 " + data.LatestSnapshot);
                return data;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ManifestData stale = TryLoadCacheFile(true);
                if (stale != null)
                {
                    ctx.Warn("获取版本清单失败，改用本地缓存（" + stale.FetchedAt.ToString("yyyy-MM-dd HH:mm") +
                             "）：" + ex.Message);
                    lock (_lock) _memory = stale;
                    return stale;
                }
                throw new Exception("获取版本清单失败：" + ex.Message +
                                    "。请检查网络连接，或切换下载源（--set-source bmclapi）后重试。", ex);
            }
        }

        /// <summary>从网络下载某个版本的版本 JSON（不做磁盘缓存，由调用方负责保存）。</summary>
        public static async Task<VersionInfo> GetVersionInfoAsync(InstallContext ctx, ManifestEntry entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            ctx = EnsureContext(ctx);
            return await VersionInfo.FromUrlAsync(ctx, entry.Url).ConfigureAwait(false);
        }

        /// <summary>安装原版游戏（转发到 VanillaInstaller）。</summary>
        public static Task<string> InstallAsync(InstallContext ctx, string versionId)
        {
            return VanillaInstaller.InstallAsync(ctx, versionId);
        }

        /// <summary>强制刷新版本清单。</summary>
        public static Task<ManifestData> RefreshAsync(InstallContext ctx)
        {
            return GetAsync(EnsureContext(ctx), true);
        }

        /// <summary>在清单里查找版本；找不到时抛出可操作的异常（附最新正式版提示）。</summary>
        public static ManifestEntry FindOrThrow(ManifestData manifest, string versionId)
        {
            ManifestEntry entry = manifest == null ? null : manifest.Find(versionId);
            if (entry != null) return entry;
            string hint = manifest == null ? "" : "（最新正式版：" + manifest.LatestRelease + "，最新快照：" + manifest.LatestSnapshot + "）";
            throw new Exception("找不到版本“" + versionId + "”" + hint +
                                "。请检查版本号是否完整（例如 1.21.4），也可以从主页版本列表中选择。");
        }

        /// <summary>解析版本清单 JSON 文本。</summary>
        public static ManifestData Parse(string text)
        {
            JObject json = JsonUtil.Parse(text);
            ManifestData data = new ManifestData();
            JObject latest = json["latest"] as JObject;
            if (latest != null)
            {
                data.LatestRelease = JsonUtil.Str(latest, "release");
                data.LatestSnapshot = JsonUtil.Str(latest, "snapshot");
            }
            JArray versions = json["versions"] as JArray;
            if (versions != null)
            {
                foreach (JToken t in versions)
                {
                    JObject v = t as JObject;
                    if (v == null) continue;
                    ManifestEntry e = new ManifestEntry
                    {
                        Id = JsonUtil.Str(v, "id"),
                        Type = JsonUtil.Str(v, "type", "release"),
                        Url = JsonUtil.Str(v, "url"),
                        Sha1 = JsonUtil.Str(v, "sha1"),
                        ReleaseTime = ParseDate(JsonUtil.Str(v, "releaseTime"))
                    };
                    if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Url)) continue;
                    if (string.IsNullOrWhiteSpace(e.Type)) e.Type = "release";
                    data.Versions.Add(e);
                }
            }
            if (data.Versions.Count > 0 && string.IsNullOrWhiteSpace(data.LatestRelease))
            {
                // 老清单（v1）没有 latest 节点时兜底：取第一个正式版
                foreach (ManifestEntry e in data.Versions)
                {
                    if (e.IsRelease) { data.LatestRelease = e.Id; break; }
                }
            }
            return data;
        }

        /// <summary>把版本清单写入本地缓存。</summary>
        public static void SaveCacheFile(InstallContext ctx, ManifestData data)
        {
            if (data == null) return;
            try
            {
                FileUtil.EnsureDir(Paths.CacheDir);
                JsonUtil.WriteFile(CacheFile, data, true);
            }
            catch (Exception ex)
            {
                if (ctx != null) ctx.Warn("写入版本清单缓存失败：" + ex.Message);
            }
        }

        private static ManifestData TryLoadCacheFile(bool allowStale)
        {
            try
            {
                if (!File.Exists(CacheFile)) return null;
                ManifestData data = JsonUtil.ReadFile<ManifestData>(CacheFile, null);
                if (data == null || data.Versions == null || data.Versions.Count == 0) return null;
                double age = (DateTime.Now - data.FetchedAt).TotalMinutes;
                if (!allowStale && (data.FetchedAt == DateTime.MinValue || age < 0 || age >= CacheMinutes)) return null;
                return data;
            }
            catch { return null; }
        }

        private static DateTime ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return DateTime.MinValue;
            DateTime dt;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out dt))
                return dt;
            return DateTime.MinValue;
        }

        // ==================== 上下文兜底 ====================

        /// <summary>
        /// 允许调用方传入 null 上下文（例如 Web API 没有任务）：自动改用 App 的全局对象。
        /// 其它 Minecraft 模块也会用到这个兜底方法。
        /// </summary>
        public static InstallContext EnsureContext(InstallContext ctx)
        {
            if (ctx != null) return ctx;
            if (App.Dl == null)
                throw new InvalidOperationException("程序尚未初始化（App.Initialize 未执行），无法执行安装操作。");
            return App.CreateContext(null);
        }
    }
}
