using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 主页渲染所需的数据缓存。
    /// 渲染（Render/Publish）只读内存快照，绝不阻塞等待网络；
    /// 版本清单与 Modrinth 推荐数据由后台线程刷新，刷新到新数据后通过 App.MarkHomepageDirty 触发重新发布。
    /// </summary>
    public static class HomepageData
    {
        // ---------------- 数据结构 ----------------

        /// <summary>版本清单中的一项。</summary>
        public sealed class VersionEntry
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public DateTime ReleaseTime { get; set; }

            public bool IsRelease { get { return Type == "release"; } }
            public bool IsSnapshot { get { return Type == "snapshot"; } }

            public string TypeText
            {
                get
                {
                    switch (Type)
                    {
                        case "release": return "正式版";
                        case "snapshot": return "快照";
                        case "old_beta": return "远古 Beta";
                        case "old_alpha": return "远古 Alpha";
                        default: return Type ?? "未知";
                    }
                }
            }

            public string DateText
            {
                get { return ReleaseTime == DateTime.MinValue ? "" : ReleaseTime.ToString("yyyy-MM-dd"); }
            }
        }

        /// <summary>版本清单快照。</summary>
        public sealed class ManifestSnapshot
        {
            public string LatestRelease { get; set; }
            public string LatestSnapshot { get; set; }
            public List<VersionEntry> Versions { get; set; }
            public DateTime FetchedAt { get; set; }
            public string Origin { get; set; }

            public ManifestSnapshot() { Versions = new List<VersionEntry>(); }

            public VersionEntry Find(string id)
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var v in Versions)
                    if (string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)) return v;
                return null;
            }

            /// <summary>最近 n 个正式版（按清单顺序，清单本身是新版本在前）。</summary>
            public List<VersionEntry> TakeReleases(int n)
            {
                var list = new List<VersionEntry>();
                foreach (var v in Versions)
                {
                    if (!v.IsRelease) continue;
                    list.Add(v);
                    if (list.Count >= n) break;
                }
                return list;
            }

            /// <summary>最近 n 个版本（含快照，跳过远古版本）。</summary>
            public List<VersionEntry> TakeAll(int n)
            {
                var list = new List<VersionEntry>();
                foreach (var v in Versions)
                {
                    if (v.Type == "old_beta" || v.Type == "old_alpha") continue;
                    list.Add(v);
                    if (list.Count >= n) break;
                }
                return list;
            }
        }

        /// <summary>推荐整合包。</summary>
        public sealed class PackInfo
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Info { get; set; }
            public string Logo { get; set; }
        }

        // ---------------- 字段 ----------------

        private static readonly object Lock = new object();
        private static ManifestSnapshot _manifest;
        private static List<PackInfo> _packs;
        private static DateTime _packsAt = DateTime.MinValue;
        private static DateTime _lastManifestTry = DateTime.MinValue;
        private static DateTime _lastPackTry = DateTime.MinValue;
        private static bool _loaded;
        private static int _busy;
        private static Timer _timer;

        private const int ManifestTtlMinutes = 360;
        private const int PacksTtlMinutes = 720;
        private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
        private const string ModrinthSearchUrl = "https://api.modrinth.com/v2/search?limit=8&index=downloads&facets=%5B%5B%22project_type%3Amodpack%22%5D%5D";

        private static string ManifestCacheFile { get { return Path.Combine(Paths.CacheDir, "homepage_manifest.json"); } }
        private static string PacksCacheFile { get { return Path.Combine(Paths.CacheDir, "homepage_packs.json"); } }

        // ---------------- 对外读取 ----------------

        public static ManifestSnapshot Manifest
        {
            get { lock (Lock) return _manifest; }
        }

        public static bool ManifestReady
        {
            get { lock (Lock) return _manifest != null; }
        }

        public static string ManifestAgeText
        {
            get
            {
                DateTime at;
                lock (Lock) at = _manifest == null ? DateTime.MinValue : _manifest.FetchedAt;
                return AgeText(at);
            }
        }

        public static List<PackInfo> Packs
        {
            get
            {
                lock (Lock)
                {
                    if (_packs != null && _packs.Count > 0) return new List<PackInfo>(_packs);
                }
                return new List<PackInfo>(StaticPacks);
            }
        }

        public static string PacksAgeText
        {
            get
            {
                DateTime at;
                lock (Lock) at = _packsAt;
                return AgeText(at);
            }
        }

        private static string AgeText(DateTime at)
        {
            if (at == DateTime.MinValue) return "未知";
            var span = DateTime.Now - at;
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " 分钟前";
            if (span.TotalHours < 24) return (int)span.TotalHours + " 小时前";
            return (int)span.TotalDays + " 天前";
        }

        // ---------------- 磁盘缓存 ----------------

        /// <summary>读取磁盘缓存（同步、快速），只在首次调用时执行。</summary>
        public static void EnsureLoaded()
        {
            lock (Lock)
            {
                if (_loaded) return;
                _loaded = true;
            }
            try { LoadManifestFromDisk(); }
            catch (Exception ex) { SafeLog("读取版本清单缓存失败", ex); }
            try { LoadPacksFromDisk(); }
            catch (Exception ex) { SafeLog("读取整合包缓存失败", ex); }
            EnsureTimer();
        }

        private static void LoadManifestFromDisk()
        {
            // 1) 本模块自己的缓存
            var mine = TryParseManifestFile(ManifestCacheFile);
            if (mine != null) { lock (Lock) { if (_manifest == null) _manifest = mine; } return; }
            // 2) 兼容 vanilla 模块可能写入的清单缓存
            var candidates = new[]
            {
                Path.Combine(Paths.CacheDir, "version_manifest.json"),
                Path.Combine(Paths.CacheDir, "manifest.json"),
                Path.Combine(Paths.CacheDir, "version_manifest_v2.json"),
                Path.Combine(Paths.DataDir, "version_manifest.json")
            };
            foreach (var file in candidates)
            {
                var snap = TryParseManifestFile(file);
                if (snap != null) { lock (Lock) _manifest = snap; return; }
            }
        }

        /// <summary>解析 Mojang 格式的版本清单文件。</summary>
        private static ManifestSnapshot TryParseManifestFile(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                var text = File.ReadAllText(file, Encoding.UTF8);
                var snap = ParseManifest(text);
                if (snap == null) return null;
                snap.FetchedAt = File.GetLastWriteTime(file);
                snap.Origin = Path.GetFileName(file);
                return snap;
            }
            catch { return null; }
        }

        /// <summary>解析 Mojang 版本清单 JSON。</summary>
        public static ManifestSnapshot ParseManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            JObject root;
            try { root = JObject.Parse(json); }
            catch { return null; }
            var versions = root["versions"] as JArray;
            if (versions == null || versions.Count == 0) return null;
            var snap = new ManifestSnapshot { LatestRelease = Str(root["latest"], "release"), LatestSnapshot = Str(root["latest"], "snapshot") };
            foreach (var token in versions)
            {
                var id = Str(token, "id");
                if (string.IsNullOrEmpty(id)) continue;
                snap.Versions.Add(new VersionEntry
                {
                    Id = id,
                    Type = Str(token, "type") ?? "release",
                    ReleaseTime = ParseTime(Str(token, "releaseTime"))
                });
            }
            if (snap.Versions.Count == 0) return null;
            if (string.IsNullOrEmpty(snap.LatestRelease))
            {
                var first = snap.TakeReleases(1);
                if (first.Count > 0) snap.LatestRelease = first[0].Id;
            }
            return snap;
        }

        private static string Str(JToken token, string name)
        {
            if (token == null) return null;
            var v = token[name];
            return v == null || v.Type == JTokenType.Null ? null : v.ToString();
        }

        private static DateTime ParseTime(string text)
        {
            DateTime dt;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt)) return dt;
            return DateTime.MinValue;
        }

        private static void LoadPacksFromDisk()
        {
            try
            {
                if (!File.Exists(PacksCacheFile)) return;
                var list = JsonUtil.ReadFile<List<PackInfo>>(PacksCacheFile, null);
                if (list == null || list.Count == 0) return;
                lock (Lock)
                {
                    _packs = list;
                    _packsAt = File.GetLastWriteTime(PacksCacheFile);
                }
            }
            catch { }
        }

        // ---------------- 后台刷新 ----------------

        /// <summary>首次加载后启动定时器，定期检查缓存是否过期。</summary>
        private static void EnsureTimer()
        {
            if (_timer != null) return;
            _timer = new Timer(_ =>
            {
                try { WarmupAsync(false); }
                catch { }
            }, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15));
        }

        /// <summary>异步刷新（不阻塞调用方）。同一时间只允许一个刷新任务。</summary>
        public static void WarmupAsync(bool force)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            Task.Run(async () =>
            {
                try { await WarmupCoreAsync(force).ConfigureAwait(false); }
                catch (Exception ex) { SafeLog("主页数据后台刷新失败", ex); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        private static bool ManifestExpired(bool force)
        {
            lock (Lock)
            {
                if (force) return true;
                if (_manifest == null) return true;
                return (DateTime.Now - _manifest.FetchedAt).TotalMinutes > ManifestTtlMinutes;
            }
        }

        private static bool PacksExpired(bool force)
        {
            lock (Lock)
            {
                if (force) return true;
                if (_packs == null || _packs.Count == 0) return true;
                return (DateTime.Now - _packsAt).TotalMinutes > PacksTtlMinutes;
            }
        }

        private static async Task WarmupCoreAsync(bool force)
        {
            if (!App.Initialized || App.Dl == null) return;
            bool changed = false;

            // ---- 版本清单 ----
            if (ManifestExpired(force))
            {
                bool tooSoon;
                lock (Lock) tooSoon = (DateTime.Now - _lastManifestTry).TotalSeconds < 60;
                lock (Lock) _lastManifestTry = DateTime.Now;
                if (!tooSoon || force)
                {
                    var before = ManifestSignature();
                    // 1) 先试 vanilla 模块的缓存接口
                    try
                    {
                        var cached = VersionManifest.GetCached();
                        if (cached != null && cached.Versions != null && cached.Versions.Count > 0)
                        {
                            var snap = new ManifestSnapshot
                            {
                                LatestRelease = cached.LatestRelease,
                                LatestSnapshot = cached.LatestSnapshot,
                                FetchedAt = cached.FetchedAt == DateTime.MinValue ? DateTime.Now : cached.FetchedAt,
                                Origin = "版本清单模块"
                            };
                            foreach (var e in cached.Versions)
                                snap.Versions.Add(new VersionEntry { Id = e.Id, Type = e.Type, ReleaseTime = e.ReleaseTime });
                            if (string.IsNullOrEmpty(snap.LatestRelease)) snap.LatestRelease = cached.LatestRelease;
                            SetManifest(snap);
                        }
                    }
                    catch { }
                    // 2) 网络获取
                    if (ManifestExpired(force))
                    {
                        try
                        {
                            var text = await App.Dl.GetStringAsync(ManifestUrl, CancellationToken.None, 20000, 2, true).ConfigureAwait(false);
                            var snap = ParseManifest(text);
                            if (snap != null)
                            {
                                snap.FetchedAt = DateTime.Now;
                                snap.Origin = "Mojang 官方";
                                FileUtil.WriteAllTextAtomic(ManifestCacheFile, text);
                                SetManifest(snap);
                                SafeLog("主页版本清单已更新：" + snap.Versions.Count + " 个版本，最新正式版 " + snap.LatestRelease, null);
                            }
                        }
                        catch (Exception ex) { SafeLog("获取版本清单失败（主页版本列表将暂时留空）", ex); }
                    }
                    if (before != ManifestSignature()) changed = true;
                }
            }

            // ---- 推荐整合包 ----
            if (PacksExpired(force))
            {
                bool tooSoon;
                lock (Lock) tooSoon = (DateTime.Now - _lastPackTry).TotalSeconds < 60;
                lock (Lock) _lastPackTry = DateTime.Now;
                if (!tooSoon || force)
                {
                    var before = PacksSignature();
                    try
                    {
                        var json = await App.Dl.GetJsonAsync(ModrinthSearchUrl, CancellationToken.None, 6000, 1, false).ConfigureAwait(false);
                        var list = ParseModrinthPacks(json);
                        if (list.Count > 0)
                        {
                            lock (Lock) { _packs = list; _packsAt = DateTime.Now; }
                            try { JsonUtil.WriteFile(PacksCacheFile, list, true); } catch { }
                            SafeLog("主页推荐整合包已更新：" + list.Count + " 个", null);
                        }
                    }
                    catch (Exception ex) { SafeLog("获取 Modrinth 推荐整合包失败（使用静态推荐）", ex); }
                    if (before != PacksSignature()) changed = true;
                }
            }

            if (changed && App.Initialized)
            {
                try { App.MarkHomepageDirty(); } catch { }
            }
        }

        private static void SetManifest(ManifestSnapshot snap)
        {
            lock (Lock) _manifest = snap;
        }

        private static string ManifestSignature()
        {
            lock (Lock)
            {
                if (_manifest == null) return "";
                var sb = new StringBuilder();
                sb.Append(_manifest.LatestRelease).Append('|').Append(_manifest.LatestSnapshot).Append('|').Append(_manifest.Versions.Count);
                for (int i = 0; i < Math.Min(6, _manifest.Versions.Count); i++) sb.Append('|').Append(_manifest.Versions[i].Id);
                return sb.ToString();
            }
        }

        private static string PacksSignature()
        {
            lock (Lock)
            {
                if (_packs == null) return "";
                var sb = new StringBuilder();
                foreach (var p in _packs) sb.Append(p.Id).Append('|');
                return sb.ToString();
            }
        }

        /// <summary>解析 Modrinth 搜索结果为推荐整合包列表。</summary>
        private static List<PackInfo> ParseModrinthPacks(JObject json)
        {
            var list = new List<PackInfo>();
            if (json == null) return list;
            var hits = json["hits"] as JArray;
            if (hits == null) return list;
            foreach (var hit in hits)
            {
                var id = Str(hit, "project_id");
                if (string.IsNullOrEmpty(id)) continue;
                var title = Str(hit, "title") ?? id;
                var description = Str(hit, "description") ?? "";
                var downloads = hit["downloads"] == null ? 0L : (long)hit["downloads"].ToObject<double>();
                var logo = Str(hit, "icon_url");
                if (string.IsNullOrEmpty(logo)) logo = "https://cdn.modrinth.com/data/" + id + "/icon.png";
                list.Add(new PackInfo
                {
                    Id = id,
                    Title = title,
                    Info = "下载 " + FormatDownloads(downloads) + " · " + Shorten(description, 52),
                    Logo = logo
                });
            }
            return list;
        }

        public static string FormatDownloads(long downloads)
        {
            if (downloads >= 100000000L) return (downloads / 100000000.0).ToString("0.0") + " 亿";
            if (downloads >= 10000L) return (downloads / 10000).ToString("0") + " 万";
            return downloads.ToString();
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("**", "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= max) return text;
            return text.Substring(0, max) + "…";
        }

        private static void SafeLog(string message, Exception ex)
        {
            try
            {
                if (App.Log == null) return;
                if (ex == null) App.Log.Info(message);
                else App.Log.Warn(message + "：" + ex.Message);
            }
            catch { }
        }

        // ---------------- 静态推荐（网络不可用时的兜底） ----------------

        /// <summary>静态推荐整合包：均为 Modrinth 热门作品，直接使用 project id。</summary>
        public static readonly PackInfo[] StaticPacks =
        {
            new PackInfo { Id = "1KVo5zza", Title = "Fabulously Optimized", Info = "下载 1899 万 · 客户端优化，轻量流畅", Logo = Icon("1KVo5zza") },
            new PackInfo { Id = "l9m9tuPN", Title = "Zombie Invade 100 Days", Info = "下载 1522 万 · 僵尸入侵 100 天", Logo = Icon("l9m9tuPN") },
            new PackInfo { Id = "5FFgwNNP", Title = "Cobblemon Official Modpack", Info = "下载 1141 万 · 宝可梦主题 Fabric 整合包", Logo = Icon("5FFgwNNP") },
            new PackInfo { Id = "Jkb29YJU", Title = "COBBLEVERSE", Info = "下载 722 万 · 宝可梦冒险，1025 只宝可梦", Logo = Icon("Jkb29YJU") },
            new PackInfo { Id = "rQiXwLhB", Title = "BattleArmory TACZ", Info = "下载 373 万 · 现代枪械战术整合包", Logo = Icon("rQiXwLhB") },
            new PackInfo { Id = "shFhR8Vx", Title = "Better MC [FABRIC]", Info = "下载 369 万 · 经典整合包，原版增强", Logo = Icon("shFhR8Vx") },
            new PackInfo { Id = "ch7UHY2J", Title = "Sodium Plus", Info = "下载 288 万 · 极致优化整合包", Logo = Icon("ch7UHY2J") },
            new PackInfo { Id = "1ocGzRHv", Title = "Vanilla Perfected", Info = "下载 268 万 · 保持原版体验的优化组合", Logo = Icon("1ocGzRHv") }
        };

        private static string Icon(string projectId)
        {
            return "https://cdn.modrinth.com/data/" + projectId + "/icon.png";
        }
    }
}
