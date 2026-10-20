using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// Web 下载中心的追加接口：版本清单 / 加载器版本 / Modrinth 搜索 / SSE 事件流 /
    /// 已安装版本 / 配置保存 / 关于信息。核心接口（安装、任务、取消）在 ApiCore.cs。
    /// </summary>
    public static class WebApi
    {
        public static void Register(Dictionary<string, Func<HttpRequest, HttpResponse>> routes)
        {
            // 单页前端
            routes["/"] = req => HttpResponse.Html(UiAssets.IndexHtml());
            routes["/index.html"] = req => HttpResponse.Html(UiAssets.IndexHtml());

            // 数据接口
            routes["/api/manifest"] = Manifest;
            routes["/api/loader/versions"] = LoaderVersions;
            routes["/api/loader/supported"] = LoaderSupported;
            routes["/api/modrinth/search"] = ModrinthSearch;
            routes["/api/modrinth/project"] = ModrinthProject;
            routes["/api/installed"] = Installed;
            routes["/api/events"] = Events;
            routes["/api/config/save"] = ConfigSave;
            routes["/api/about"] = About;
            routes["/api/logs"] = Logs;
        }

        // ===================== 公共小工具 =====================

        private static HttpResponse JsonError(string message, bool ready = true)
        {
            return HttpResponse.Json(new { ok = false, ready = ready, error = message });
        }

        private static bool TryInt(JObject body, string name, out int value)
        {
            value = 0;
            var token = body == null ? null : body[name];
            if (token == null || token.Type == JTokenType.Null) return false;
            return int.TryParse(token.ToString().Trim(), out value);
        }

        private static bool TryBool(JObject body, string name, out bool value)
        {
            value = false;
            var token = body == null ? null : body[name];
            if (token == null || token.Type == JTokenType.Null) return false;
            var text = token.ToString().Trim().ToLowerInvariant();
            if (text == "true" || text == "1" || text == "on" || text == "yes") { value = true; return true; }
            if (text == "false" || text == "0" || text == "off" || text == "no") { value = false; return true; }
            return bool.TryParse(text, out value);
        }

        private static object ConfigSnapshot()
        {
            return new
            {
                downloadSource = App.Config.DownloadSource,
                threads = App.Config.Threads,
                serverPort = App.Config.ServerPort,
                curseForgeApiKey = App.Config.CurseForgeApiKey,
                javaPath = App.Config.JavaPath,
                mcDir = Paths.McDir,
                mcDirOverride = App.Config.McDirOverride,
                pclDir = Paths.PclDir,
                baseDir = Paths.BaseDir,
                pluginDir = Paths.PluginDir,
                configFile = Paths.ConfigFile,
                homepageMaxVersions = App.Config.HomepageMaxVersions,
                homepageShowSnapshots = App.Config.HomepageShowSnapshots,
                homepagePublish = App.Config.HomepagePublish,
                maxConcurrentTasks = App.Config.MaxConcurrentTasks,
                useMirrorFallback = App.Config.UseMirrorFallback
            };
        }

        // ===================== 版本清单 =====================

        private static readonly object ManifestLock = new object();
        private static ManifestData _manifestCache;
        private static DateTime _manifestCacheAt = DateTime.MinValue;
        private static string _manifestSource = "";
        private static string _manifestWarning = "";

        /// <summary>读取版本清单：优先用 vanilla 模块（带缓存），未实现或失败时用内置回退。</summary>
        private static ManifestData LoadManifest(bool force, out string source, out string warning)
        {
            lock (ManifestLock)
            {
                if (!force && _manifestCache != null && (DateTime.Now - _manifestCacheAt).TotalMinutes < 10)
                {
                    source = _manifestSource;
                    warning = _manifestWarning;
                    return _manifestCache;
                }
                ManifestData data = null;
                string warn = "";
                string src = "module";
                Exception moduleError = null;
                try
                {
                    data = VersionManifest.GetAsync(App.CreateContext(), force).GetAwaiter().GetResult();
                    if (data == null || data.Versions == null || data.Versions.Count == 0)
                        throw new Exception("版本清单为空");
                }
                catch (Exception ex)
                {
                    moduleError = ex;
                    data = null;
                }
                if (data == null)
                {
                    src = "fallback";
                    warn = moduleError == null ? "" : moduleError.Message;
                    try
                    {
                        data = FetchManifestFallback();
                    }
                    catch (Exception ex2)
                    {
                        throw new Exception("获取版本清单失败：" + (moduleError == null ? "" : moduleError.Message + "；") + "回退失败：" + ex2.Message);
                    }
                }
                _manifestCache = data;
                _manifestCacheAt = DateTime.Now;
                _manifestSource = src;
                _manifestWarning = warn;
                source = src;
                warning = warn;
                return data;
            }
        }

        /// <summary>内置回退：直接请求 Mojang 版本清单（其它模块未完成时保证 Web UI 可用）。</summary>
        private static ManifestData FetchManifestFallback()
        {
            var url = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
            var text = App.Dl.GetStringAsync(url, CancellationToken.None, 30000, 3, true).GetAwaiter().GetResult();
            var json = JObject.Parse(text);
            var data = new ManifestData();
            var latest = json["latest"];
            data.LatestRelease = JsonUtil.Str(latest, "release");
            data.LatestSnapshot = JsonUtil.Str(latest, "snapshot");
            var arr = json["versions"] as JArray;
            if (arr != null)
            {
                foreach (var v in arr)
                {
                    var entry = new ManifestEntry
                    {
                        Id = JsonUtil.Str(v, "id"),
                        Type = JsonUtil.Str(v, "type"),
                        Url = JsonUtil.Str(v, "url"),
                        Sha1 = JsonUtil.Str(v, "sha1")
                    };
                    DateTime time;
                    if (DateTime.TryParse(JsonUtil.Str(v, "releaseTime"), out time)) entry.ReleaseTime = time;
                    if (!string.IsNullOrWhiteSpace(entry.Id)) data.Versions.Add(entry);
                }
            }
            data.FetchedAt = DateTime.Now;
            if (string.IsNullOrWhiteSpace(data.LatestRelease) && data.Versions.Count > 0)
                data.LatestRelease = data.Versions.Where(v => v.IsRelease).Select(v => v.Id).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(data.LatestSnapshot) && data.Versions.Count > 0)
                data.LatestSnapshot = data.Versions.Where(v => v.IsSnapshot).Select(v => v.Id).FirstOrDefault();
            return data;
        }

        private static HttpResponse Manifest(HttpRequest req)
        {
            var filter = (req.Get("filter") ?? "all").Trim().ToLowerInvariant();
            var query = (req.Get("query") ?? "").Trim();
            int page = req.GetInt("page", 1);
            int pageSize = req.GetInt("pageSize", 50);
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 1000) pageSize = 1000;
            bool refresh = req.Get("refresh") == "1" || req.Get("refresh") == "true";

            ManifestData data;
            string source, warning;
            try { data = LoadManifest(refresh, out source, out warning); }
            catch (Exception ex) { return HttpResponse.Json(new { ok = false, error = ex.Message }); }

            IEnumerable<ManifestEntry> list = data.Versions ?? new List<ManifestEntry>();
            switch (filter)
            {
                case "release": list = list.Where(v => v.IsRelease); break;
                case "snapshot": list = list.Where(v => v.IsSnapshot); break;
                case "old":
                case "ancient": list = list.Where(v => v.IsOld); break;
                case "all": break;
                default: filter = "all"; break;
            }
            if (!string.IsNullOrWhiteSpace(query))
                list = list.Where(v => v.Id != null && v.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            var filtered = list.ToList();
            int pages = filtered.Count == 0 ? 1 : (int)Math.Ceiling(filtered.Count / (double)pageSize);
            if (page > pages) page = pages;
            var slice = filtered.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(v => new
                {
                    id = v.Id,
                    type = v.Type,
                    releaseTime = v.ReleaseTime == DateTime.MinValue ? "" : v.ReleaseTime.ToString("yyyy-MM-dd"),
                    releaseTimeFull = v.ReleaseTime == DateTime.MinValue ? "" : v.ReleaseTime.ToString("yyyy-MM-dd HH:mm")
                })
                .ToList();
            return HttpResponse.Json(new
            {
                ok = true,
                total = filtered.Count,
                page = page,
                pageSize = pageSize,
                pages = pages,
                filter = filter,
                query = query,
                source = source,
                warning = string.IsNullOrEmpty(warning) ? null : ("内置回退：" + warning),
                latestRelease = data.LatestRelease,
                latestSnapshot = data.LatestSnapshot,
                fetchedAt = data.FetchedAt == DateTime.MinValue ? "" : data.FetchedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                versions = slice
            });
        }

        // ===================== 加载器 =====================

        private static HttpResponse LoaderSupported(HttpRequest req)
        {
            var list = LoaderRegistry.Supported ?? new string[0];
            var items = list.Select(name => new
            {
                id = name,
                name = LoaderDisplayName(name),
                supported = name == "fabric" || name == "quilt" || name == "forge" || name == "neoforge"
            }).ToList();
            return HttpResponse.Json(new { ok = true, loaders = items });
        }

        private static string LoaderDisplayName(string id)
        {
            switch ((id ?? "").ToLowerInvariant())
            {
                case "fabric": return "Fabric";
                case "quilt": return "Quilt";
                case "forge": return "Forge";
                case "neoforge": return "NeoForge";
                case "optifine": return "OptiFine（暂不支持）";
                case "liteloader": return "LiteLoader（暂不支持）";
                default: return id ?? "";
            }
        }

        private static HttpResponse LoaderVersions(HttpRequest req)
        {
            var loader = (req.Get("loader") ?? "").Trim().ToLowerInvariant();
            var mc = (req.Get("mc") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(loader)) return JsonError("缺少参数 loader");
            if (string.IsNullOrWhiteSpace(mc)) return JsonError("缺少参数 mc（Minecraft 版本）");

            InstallContext ctx = null;
            try
            {
                ctx = App.CreateContext();
                var versions = LoaderRegistry.GetVersionsAsync(ctx, loader, mc).GetAwaiter().GetResult();
                var items = (versions ?? new List<LoaderVersion>()).Select(v => new
                {
                    version = v.Version,
                    mcVersion = string.IsNullOrWhiteSpace(v.McVersion) ? mc : v.McVersion,
                    display = string.IsNullOrWhiteSpace(v.Display) ? v.Version : v.Display,
                    isRecommended = v.IsRecommended,
                    isLatest = v.IsLatest,
                    kind = string.IsNullOrWhiteSpace(v.Kind) ? loader : v.Kind
                }).ToList();
                return HttpResponse.Json(new
                {
                    ok = true,
                    loader = loader,
                    mc = mc,
                    count = items.Count,
                    versions = items
                });
            }
            catch (NotImplementedException)
            {
                return JsonError("加载器模块尚未完成，暂时无法获取 " + LoaderDisplayName(loader) + " 的版本列表。", false);
            }
            catch (Exception ex)
            {
                return JsonError("获取 " + LoaderDisplayName(loader) + " 版本列表失败：" + ex.Message, false);
            }
        }

        // ===================== Modrinth =====================

        private const string ModrinthApi = "https://api.modrinth.com/v2";

        private static HttpResponse ModrinthSearch(HttpRequest req)
        {
            var query = req.Get("query") ?? "";
            var projectType = (req.Get("projectType") ?? "modpack").Trim().ToLowerInvariant();
            var gameVersion = (req.Get("gameVersion") ?? "").Trim();
            var loader = (req.Get("loader") ?? "").Trim().ToLowerInvariant();
            int offset = req.GetInt("offset", 0);
            int limit = req.GetInt("limit", 20);
            if (offset < 0) offset = 0;
            if (limit < 1) limit = 20;
            if (limit > 40) limit = 40;

            var facets = new JArray();
            if (!string.IsNullOrWhiteSpace(projectType) && projectType != "all")
                facets.Add(new JArray("project_type:" + projectType));
            if (!string.IsNullOrWhiteSpace(loader))
                facets.Add(new JArray("categories:" + loader));
            if (!string.IsNullOrWhiteSpace(gameVersion))
                facets.Add(new JArray("versions:" + gameVersion));

            var url = ModrinthApi + "/search?query=" + Uri.EscapeDataString(query ?? "") +
                      "&limit=" + limit + "&offset=" + offset + "&index=relevance";
            if (facets.Count > 0)
                url += "&facets=" + Uri.EscapeDataString(facets.ToString(Formatting.None));

            try
            {
                var json = App.Dl.GetJsonAsync(url, CancellationToken.None, 20000, 2, false).GetAwaiter().GetResult();
                var hits = json["hits"] as JArray ?? new JArray();
                var items = hits.Select(h => new
                {
                    projectId = JsonUtil.Str(h, "project_id"),
                    slug = JsonUtil.Str(h, "slug"),
                    title = JsonUtil.Str(h, "title"),
                    description = JsonUtil.Str(h, "description"),
                    iconUrl = JsonUtil.Str(h, "icon_url"),
                    author = JsonUtil.Str(h, "author"),
                    projectType = JsonUtil.Str(h, "project_type"),
                    downloads = JsonUtil.Long(h, "downloads", 0),
                    follows = JsonUtil.Long(h, "follows", 0),
                    categories = (h["categories"] as JArray ?? new JArray()).Select(x => x.ToString()).ToList(),
                    versions = (h["versions"] as JArray ?? new JArray()).Select(x => x.ToString()).ToList(),
                    dateModified = JsonUtil.Str(h, "date_modified")
                }).ToList();
                return HttpResponse.Json(new
                {
                    ok = true,
                    totalHits = JsonUtil.Long(json, "total_hits", items.Count),
                    offset = offset,
                    limit = limit,
                    hits = items
                });
            }
            catch (Exception ex)
            {
                return JsonError("Modrinth 搜索失败：" + ex.Message, false);
            }
        }

        private static HttpResponse ModrinthProject(HttpRequest req)
        {
            var id = (req.Get("id") ?? req.Get("projectId") ?? req.Get("slug") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id)) return JsonError("缺少参数 id");
            id = ExtractModrinthId(id);
            if (string.IsNullOrWhiteSpace(id)) return JsonError("无法识别 Modrinth 项目 ID 或链接");
            try
            {
                var json = App.Dl.GetJsonAsync(ModrinthApi + "/project/" + Uri.EscapeDataString(id), CancellationToken.None, 20000, 2, false).GetAwaiter().GetResult();
                return HttpResponse.Json(new
                {
                    ok = true,
                    project = new
                    {
                        id = JsonUtil.Str(json, "id"),
                        slug = JsonUtil.Str(json, "slug"),
                        title = JsonUtil.Str(json, "title"),
                        description = JsonUtil.Str(json, "description"),
                        iconUrl = JsonUtil.Str(json, "icon_url"),
                        projectType = JsonUtil.Str(json, "project_type"),
                        downloads = JsonUtil.Long(json, "downloads", 0),
                        gameVersions = (json["game_versions"] as JArray ?? new JArray()).Select(x => x.ToString()).ToList(),
                        loaders = (json["loaders"] as JArray ?? new JArray()).Select(x => x.ToString()).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                return JsonError("获取 Modrinth 项目信息失败：" + ex.Message, false);
            }
        }

        /// <summary>从 Modrinth 链接中提取项目 slug / ID。</summary>
        private static string ExtractModrinthId(string raw)
        {
            var text = (raw ?? "").Trim();
            if (text.Length == 0) return "";
            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(text);
                    var segs = uri.AbsolutePath.Trim('/').Split('/');
                    foreach (var key in new[] { "modpack", "mod", "project", "datapack", "resourcepack", "shader" })
                    {
                        for (int i = 0; i < segs.Length - 1; i++)
                            if (string.Equals(segs[i], key, StringComparison.OrdinalIgnoreCase)) return segs[i + 1];
                    }
                    if (segs.Length > 0) return segs[segs.Length - 1];
                }
                catch { }
            }
            return text;
        }

        // ===================== 已安装版本 =====================

        private sealed class InstalledEntry
        {
            public string Name { get; set; }
            public string Id { get; set; }
            public string Type { get; set; }
            public string InheritsFrom { get; set; }
            public bool HasJar { get; set; }
            public bool HasJson { get; set; }
            public string Modified { get; set; }
        }

        private static HttpResponse Installed(HttpRequest req)
        {
            var list = new List<InstalledEntry>();
            try
            {
                if (Directory.Exists(Paths.VersionsDir))
                {
                    foreach (var dir in Directory.GetDirectories(Paths.VersionsDir))
                    {
                        var name = Path.GetFileName(dir);
                        try
                        {
                            var jsonFile = Path.Combine(dir, name + ".json");
                            var entry = new InstalledEntry
                            {
                                Name = name,
                                Id = name,
                                Type = "",
                                HasJson = File.Exists(jsonFile),
                                HasJar = File.Exists(Path.Combine(dir, name + ".jar"))
                            };
                            bool hasJson = entry.HasJson;
                            if (hasJson)
                            {
                                try
                                {
                                    var json = JObject.Parse(File.ReadAllText(jsonFile, Encoding.UTF8));
                                    entry.Id = JsonUtil.Str(json, "id", name);
                                    entry.Type = JsonUtil.Str(json, "type", "");
                                    entry.InheritsFrom = JsonUtil.Str(json, "inheritsFrom", null);
                                }
                                catch { }
                            }
                            DateTime modified = DateTime.MinValue;
                            try { modified = Directory.GetLastWriteTime(dir); } catch { }
                            entry.Modified = modified == DateTime.MinValue ? "" : modified.ToString("yyyy-MM-dd HH:mm");
                            list.Add(entry);
                        }
                        catch { }
                    }
                }
            }
            catch { }
            var sorted = list
                .OrderByDescending(x => x.Modified, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new
                {
                    name = x.Name,
                    id = x.Id,
                    type = x.Type,
                    inheritsFrom = x.InheritsFrom,
                    hasJar = x.HasJar,
                    hasJson = x.HasJson,
                    modified = x.Modified
                })
                .ToList();
            return HttpResponse.Json(new
            {
                ok = true,
                mcDir = Paths.McDir,
                count = sorted.Count,
                versions = sorted
            });
        }

        // ===================== 配置保存 =====================

        private static HttpResponse ConfigSave(HttpRequest req)
        {
            if (req.Method == "GET") return HttpResponse.Json(new { ok = true, config = ConfigSnapshot() });
            var body = req.JsonBody();
            var notes = new List<string>();
            try
            {
                var ds = body["downloadSource"];
                if (ds != null && ds.Type != JTokenType.Null)
                {
                    var source = ds.ToString().Trim().ToLowerInvariant();
                    if (source != "official" && source != "bmclapi")
                        return JsonError("下载源只能是 official 或 bmclapi");
                    App.Config.DownloadSource = source;
                }
                int intValue;
                if (TryInt(body, "threads", out intValue)) App.Config.Threads = Math.Max(1, Math.Min(128, intValue));
                if (TryInt(body, "serverPort", out intValue))
                {
                    if (intValue < 1024 || intValue > 65535) return JsonError("端口必须在 1024 ~ 65535 之间");
                    if (intValue != App.Config.ServerPort) notes.Add("端口已保存，重启服务后生效。");
                    App.Config.ServerPort = intValue;
                }
                if (TryInt(body, "homepageMaxVersions", out intValue)) App.Config.HomepageMaxVersions = Math.Max(5, Math.Min(200, intValue));
                if (TryInt(body, "maxConcurrentTasks", out intValue)) App.Config.MaxConcurrentTasks = Math.Max(1, Math.Min(4, intValue));
                bool boolValue;
                if (TryBool(body, "homepageShowSnapshots", out boolValue)) App.Config.HomepageShowSnapshots = boolValue;
                if (TryBool(body, "homepagePublish", out boolValue)) App.Config.HomepagePublish = boolValue;
                if (TryBool(body, "useMirrorFallback", out boolValue)) App.Config.UseMirrorFallback = boolValue;
                if (body["curseForgeApiKey"] != null) App.Config.CurseForgeApiKey = body["curseForgeApiKey"].ToString().Trim();
                if (body["javaPath"] != null) App.Config.JavaPath = body["javaPath"].ToString().Trim();

                // MC 目录（运行期切换，无需重启）
                var mcDirToken = body["mcDir"] ?? body["mcDirOverride"];
                if (mcDirToken != null && mcDirToken.Type != JTokenType.Null)
                {
                    var raw = mcDirToken.ToString().Trim();
                    if (raw.Length > 0 && !string.Equals(raw, Paths.McDir, StringComparison.OrdinalIgnoreCase))
                    {
                        string normalized;
                        try
                        {
                            normalized = Paths.NormalizePath(raw);
                            Directory.CreateDirectory(normalized);
                        }
                        catch (Exception ex)
                        {
                            return JsonError("Minecraft 目录无效：" + ex.Message);
                        }
                        App.Config.McDirOverride = normalized;
                        Paths.McDir = normalized;
                        notes.Add("Minecraft 目录已切换：" + normalized);
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonError("保存配置失败：" + ex.Message);
            }

            App.Config.Normalize();
            App.Config.Save();
            try { App.PublishHomepageNow(); } catch { }
            return HttpResponse.Json(new { ok = true, note = string.Join(" ", notes), config = ConfigSnapshot() });
        }

        // ===================== 关于 / 日志 =====================

        private static HttpResponse About(HttpRequest req)
        {
            var installed = Paths.ListInstalledVersions();
            DateTime startedAt = DateTime.MinValue;
            int uptime = 0;
            try
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                startedAt = proc.StartTime;
                uptime = (int)Math.Max(0, (DateTime.Now - startedAt).TotalSeconds);
            }
            catch { }
            return HttpResponse.Json(new
            {
                ok = true,
                version = App.Version,
                url = App.ServerUrl,
                running = App.Server != null && App.Server.IsRunning,
                startedAt = startedAt == DateTime.MinValue ? "" : startedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                uptimeSeconds = uptime,
                installedCount = installed.Count,
                tasks = new
                {
                    running = App.Tasks == null ? 0 : App.Tasks.RunningCount,
                    queued = App.Tasks == null ? 0 : App.Tasks.QueuedCount,
                    total = App.Tasks == null ? 0 : App.Tasks.Snapshot().Count
                },
                config = new
                {
                    downloadSource = App.Config.DownloadSource,
                    threads = App.Config.Threads,
                    serverPort = App.Config.ServerPort,
                    maxConcurrentTasks = App.Config.MaxConcurrentTasks,
                    useMirrorFallback = App.Config.UseMirrorFallback
                },
                paths = new
                {
                    pclDir = Paths.PclDir,
                    mcDir = Paths.McDir,
                    pluginDir = Paths.PluginDir,
                    dataDir = Paths.DataDir,
                    logDir = Paths.LogDir,
                    logFile = App.Log == null ? "" : App.Log.LogFile,
                    configFile = Paths.ConfigFile,
                    homepageFile = Paths.HomepageFile,
                    tasksFile = Paths.TasksFile,
                    versionsDir = Paths.VersionsDir,
                    librariesDir = Paths.LibrariesDir,
                    assetsDir = Paths.AssetsDir
                }
            });
        }

        private static HttpResponse Logs(HttpRequest req)
        {
            int count = req.GetInt("lines", 120);
            if (count < 1) count = 120;
            if (count > 1000) count = 1000;
            var text = App.Log == null ? "" : App.Log.Tail(count);
            var lines = string.IsNullOrEmpty(text) ? new List<string>() : text.Replace("\r", "").Split('\n').ToList();
            return HttpResponse.Json(new
            {
                ok = true,
                file = App.Log == null ? "" : App.Log.LogFile,
                count = lines.Count,
                lines = lines
            });
        }

        // ===================== SSE 事件流 =====================

        private sealed class SseClient
        {
            public readonly object Sync = new object();
            public readonly Queue<string> Queue = new Queue<string>();
            public DateTime StartedAt = DateTime.Now;
        }

        private static readonly object SseRoot = new object();
        private static readonly List<SseClient> SseClients = new List<SseClient>();
        private static bool _sseHooked;

        private static HttpResponse Events(HttpRequest req)
        {
            EnsureSseHooked();
            var client = new SseClient();
            return HttpResponse.Sse(stream => RunSseClient(client, stream));
        }

        private static void EnsureSseHooked()
        {
            lock (SseRoot)
            {
                if (_sseHooked) return;
                _sseHooked = true;
                if (App.Tasks != null) App.Tasks.Changed += BroadcastTasks;
                if (App.Log != null) App.Log.OnLine += BroadcastLog;
            }
        }

        private static int SseClientCount()
        {
            lock (SseRoot) return SseClients.Count;
        }

        private static void SseBroadcast(string frame)
        {
            List<SseClient> clients;
            lock (SseRoot) clients = SseClients.ToList();
            foreach (var client in clients)
            {
                lock (client.Sync)
                {
                    if (client.Queue.Count > 300) client.Queue.Dequeue();
                    client.Queue.Enqueue(frame);
                    Monitor.Pulse(client.Sync);
                }
            }
        }

        private static void SseEnqueue(SseClient client, string frame)
        {
            lock (client.Sync)
            {
                if (client.Queue.Count > 300) client.Queue.Dequeue();
                client.Queue.Enqueue(frame);
                Monitor.Pulse(client.Sync);
            }
        }

        private static void BroadcastTasks()
        {
            try
            {
                if (SseClientCount() == 0) return;
                SseBroadcast(BuildTasksFrame());
            }
            catch { }
        }

        private static void BroadcastLog(string line)
        {
            try
            {
                if (SseClientCount() == 0 || line == null) return;
                var text = line.Replace("\r", "");
                foreach (var one in text.Split('\n'))
                {
                    if (one.Length == 0) continue;
                    SseBroadcast("event: log\ndata: " + JsonUtil.Serialize(new { line = one, time = DateTime.Now.ToString("HH:mm:ss") }) + "\n\n");
                }
            }
            catch { }
        }

        private static object TaskSnapshot(DcTask task, bool withLogs, int maxLogs)
        {
            List<string> logs = null;
            if (withLogs && task.Logs != null)
            {
                lock (task.Logs) logs = task.Logs.Skip(Math.Max(0, task.Logs.Count - maxLogs)).ToList();
            }
            return new
            {
                id = task.Id,
                kind = task.Kind,
                title = task.Title,
                phase = task.Phase,
                progress = Math.Round(Math.Max(0, Math.Min(1, task.Progress)), 4),
                status = task.Status.ToString(),
                statusText = task.StatusText,
                progressText = task.ProgressText,
                error = task.Error,
                versionId = task.VersionId,
                message = task.Message,
                createdAt = task.CreatedAt,
                updatedAt = task.UpdatedAt,
                logCount = task.LogCount,
                logs = logs
            };
        }

        private static string BuildTasksFrame()
        {
            var all = App.Tasks == null ? new List<DcTask>() : App.Tasks.Snapshot();
            var items = new List<object>();
            int index = 0;
            foreach (var task in all)
            {
                bool withLogs = task.Status == DcTaskStatus.Running || task.Status == DcTaskStatus.Pending || index < 5;
                items.Add(TaskSnapshot(task, withLogs, 40));
                index++;
            }
            var payload = new
            {
                ok = true,
                running = App.Tasks == null ? 0 : App.Tasks.RunningCount,
                queued = App.Tasks == null ? 0 : App.Tasks.QueuedCount,
                serverTime = DateTime.Now.ToString("HH:mm:ss"),
                tasks = items
            };
            return "event: tasks\ndata: " + JsonUtil.Serialize(payload) + "\n\n";
        }

        private static void WriteSse(Stream stream, string frame)
        {
            var bytes = Encoding.UTF8.GetBytes(frame ?? "");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static void RunSseClient(SseClient client, Stream stream)
        {
            lock (SseRoot) SseClients.Add(client);
            try
            {
                // 断线重连间隔（浏览器 EventSource 自动使用）
                WriteSse(stream, "retry: 2000\n\n");
                WriteSse(stream, "event: hello\ndata: " + JsonUtil.Serialize(new
                {
                    ok = true,
                    version = App.Version,
                    port = App.Config.ServerPort,
                    url = App.ServerUrl,
                    serverTime = DateTime.Now.ToString("HH:mm:ss")
                }) + "\n\n");
                WriteSse(stream, BuildTasksFrame());
                WriteSse(stream, "event: ready\ndata: {}\n\n");

                var lastPing = DateTime.Now;
                while (true)
                {
                    string frame = null;
                    lock (client.Sync)
                    {
                        if (client.Queue.Count > 0) frame = client.Queue.Dequeue();
                        else Monitor.Wait(client.Sync, 1000);
                    }
                    if (frame != null)
                    {
                        WriteSse(stream, frame);
                        continue;
                    }
                    if ((DateTime.Now - lastPing).TotalSeconds >= 10)
                    {
                        WriteSse(stream, "event: ping\ndata: " + DateTime.Now.ToString("HH:mm:ss") + "\n\n");
                        lastPing = DateTime.Now;
                    }
                    // 单连接最长 2 小时后主动断开，浏览器会自动重连，避免长期占用线程
                    if ((DateTime.Now - client.StartedAt).TotalMinutes > 120) break;
                }
            }
            catch (Exception) { }
            finally
            {
                lock (SseRoot) SseClients.Remove(client);
            }
        }
    }
}
