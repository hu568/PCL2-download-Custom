using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>核心 API（安装 / 任务 / 配置），由 Lead 维护；Web UI 的额外接口在 Web/Api.cs。</summary>
    public static class ApiCore
    {
        public static void Register(Dictionary<string, Func<HttpRequest, HttpResponse>> routes)
        {
            routes["/api/install"] = Install;
            routes["/api/tasks"] = Tasks;
            routes["/api/task"] = Task;
            routes["/api/cancel"] = Cancel;
            routes["/api/config"] = Config;
            routes["/api/status"] = Status;
            routes["/api/homepage"] = Homepage;
            routes["/api/versions/installed"] = Installed;
            routes["/api/mcversion"] = McVersion;
        }

        private static object TaskJson(DcTask t, bool withLogs)
        {
            List<string> logs = null;
            if (withLogs)
            {
                lock (t.Logs) logs = t.Logs.Skip(Math.Max(0, t.Logs.Count - 40)).ToList();
            }
            return new
            {
                id = t.Id,
                kind = t.Kind,
                title = t.Title,
                phase = t.Phase,
                progress = t.Progress,
                status = t.Status.ToString(),
                statusText = t.StatusText,
                progressText = t.ProgressText,
                error = t.Error,
                versionId = t.VersionId,
                message = t.Message,
                createdAt = t.CreatedAt,
                updatedAt = t.UpdatedAt,
                logCount = t.LogCount,
                logs = logs
            };
        }

        private static HttpResponse Tasks(HttpRequest req)
        {
            var list = App.Tasks.Snapshot().Select(t => TaskJson(t, false)).ToList();
            return HttpResponse.Json(new { ok = true, running = App.Tasks.RunningCount, queued = App.Tasks.QueuedCount, tasks = list });
        }

        private static HttpResponse Task(HttpRequest req)
        {
            var id = req.Get("id");
            if (string.IsNullOrWhiteSpace(id))
            {
                var latest = App.Tasks.Snapshot().FirstOrDefault();
                if (latest == null) return HttpResponse.Json(new { ok = false, error = "没有任务" });
                return HttpResponse.Json(new { ok = true, task = TaskJson(latest, true) });
            }
            var t = App.Tasks.Find(id);
            if (t == null) return HttpResponse.NotFound("任务不存在");
            return HttpResponse.Json(new { ok = true, task = TaskJson(t, true) });
        }

        private static HttpResponse Install(HttpRequest req)
        {
            var body = req.JsonBody();
            var kind = (body["kind"] == null ? "vanilla" : body["kind"].ToString()).Trim().ToLowerInvariant();
            string[] args;
            var arr = body["args"] as JArray;
            if (arr != null && arr.Count > 0) args = arr.Select(x => x.ToString()).ToArray();
            else
            {
                var target = body["target"] == null ? "" : body["target"].ToString();
                args = string.IsNullOrWhiteSpace(target) ? new string[0] : new[] { target };
            }
            if (kind == "packfile" || kind == "modpack-file")
            {
                if (args.Length == 0) return HttpResponse.Error("缺少整合包文件路径");
            }
            else if (args.Length == 0) return HttpResponse.Error("缺少安装参数");

            var title = Installers.Title(kind, args);
            var task = App.Tasks.Create(kind, title);
            App.Tasks.Enqueue(task, ctx => Installers.RunAsync(ctx, kind, args));
            App.MarkHomepageDirty();
            App.Log.Info("已创建任务：" + title + "（" + task.Id + "）");
            return HttpResponse.Json(new { ok = true, taskId = task.Id, title = title });
        }

        private static HttpResponse Cancel(HttpRequest req)
        {
            var id = req.Get("id");
            if (string.IsNullOrWhiteSpace(id)) return HttpResponse.Error("缺少 id");
            var ok = App.Tasks.Cancel(id);
            App.MarkHomepageDirty();
            return HttpResponse.Json(new { ok = ok });
        }

        private static HttpResponse Config(HttpRequest req)
        {
            if (req.Method == "GET")
            {
                return HttpResponse.Json(new
                {
                    ok = true,
                    config = new
                    {
                        downloadSource = App.Config.DownloadSource,
                        threads = App.Config.Threads,
                        serverPort = App.Config.ServerPort,
                        curseForgeApiKey = App.Config.CurseForgeApiKey,
                        javaPath = App.Config.JavaPath,
                        mcDir = Paths.McDir,
                        pclDir = Paths.PclDir,
                        baseDir = Paths.BaseDir,
                        homepageMaxVersions = App.Config.HomepageMaxVersions,
                        homepageShowSnapshots = App.Config.HomepageShowSnapshots,
                        homepagePublish = App.Config.HomepagePublish,
                        maxConcurrentTasks = App.Config.MaxConcurrentTasks,
                        useMirrorFallback = App.Config.UseMirrorFallback
                    }
                });
            }
            var body = req.JsonBody();
            if (body["downloadSource"] != null) App.Config.DownloadSource = body["downloadSource"].ToString();
            if (body["threads"] != null) App.Config.Threads = int.Parse(body["threads"].ToString());
            if (body["serverPort"] != null) App.Config.ServerPort = int.Parse(body["serverPort"].ToString());
            if (body["curseForgeApiKey"] != null) App.Config.CurseForgeApiKey = body["curseForgeApiKey"].ToString();
            if (body["javaPath"] != null) App.Config.JavaPath = body["javaPath"].ToString();
            if (body["homepageMaxVersions"] != null) App.Config.HomepageMaxVersions = int.Parse(body["homepageMaxVersions"].ToString());
            if (body["homepageShowSnapshots"] != null) App.Config.HomepageShowSnapshots = bool.Parse(body["homepageShowSnapshots"].ToString());
            if (body["homepagePublish"] != null) App.Config.HomepagePublish = bool.Parse(body["homepagePublish"].ToString());
            if (body["maxConcurrentTasks"] != null) App.Config.MaxConcurrentTasks = int.Parse(body["maxConcurrentTasks"].ToString());
            if (body["useMirrorFallback"] != null) App.Config.UseMirrorFallback = bool.Parse(body["useMirrorFallback"].ToString());
            App.Config.Normalize();
            App.Config.Save();
            App.PublishHomepageNow();
            return HttpResponse.Json(new { ok = true, config = App.Config });
        }

        private static HttpResponse Status(HttpRequest req)
        {
            return HttpResponse.Json(new
            {
                ok = true,
                version = App.Version,
                pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                running = App.Server != null && App.Server.IsRunning,
                port = App.Config.ServerPort,
                url = App.ServerUrl,
                mcDir = Paths.McDir,
                pclDir = Paths.PclDir,
                downloadSource = App.Config.DownloadSource,
                runningTasks = App.Tasks.RunningCount,
                queuedTasks = App.Tasks.QueuedCount,
                installedVersions = Paths.ListInstalledVersions()
            });
        }

        private static HttpResponse Homepage(HttpRequest req)
        {
            var action = req.Get("action") ?? "publish";
            if (action == "publish")
            {
                App.PublishHomepageNow();
                return HttpResponse.Json(new { ok = true, file = Paths.HomepageFile });
            }
            if (action == "dirty") { App.MarkHomepageDirty(); return HttpResponse.Json(new { ok = true }); }
            return HttpResponse.Error("未知操作：" + action);
        }

        private static HttpResponse Installed(HttpRequest req)
        {
            var list = Paths.ListInstalledVersions();
            return HttpResponse.Json(new { ok = true, versions = list });
        }

        private static HttpResponse McVersion(HttpRequest req)
        {
            return HttpResponse.Json(new { ok = true, mcDir = Paths.McDir });
        }
    }
}
