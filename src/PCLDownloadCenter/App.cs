using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PCLDownloadCenter
{
    /// <summary>全局单例，集中持有配置、日志、下载器、任务管理器与 HTTP 服务。</summary>
    public static class App
    {
        public static AppConfig Config;
        public static Logger Log;
        public static Downloader Dl;
        public static TaskManager Tasks;
        public static HttpServer Server;
        public static bool Initialized;

        public static string ServerUrl { get { return "http://127.0.0.1:" + (Config == null ? 27321 : Config.ServerPort); } }

        private static readonly object InitLock = new object();

        public static void Initialize()
        {
            lock (InitLock)
            {
                if (Initialized) return;
                Paths.Init();
                Config = Paths.Config;
                Log = new Logger(Paths.LogDir);
                Log.Info("========================================");
                Log.Info(Name + " 启动：v" + Version + "，PID " + System.Diagnostics.Process.GetCurrentProcess().Id);
                Log.Info("插件目录：" + Paths.PluginDir);
                Log.Info("PCL 目录：" + Paths.PclDir);
                Log.Info("MC 目录：" + Paths.McDir);
                Log.Info("下载源：" + Config.DownloadSource + "，线程数：" + Config.Threads);
                Dl = new Downloader(Log, Config);
                Tasks = new TaskManager(Config, Log, Dl);
                Tasks.Load();
                Initialized = true;
            }
        }

        public const string Version = "1.0.0";

        /// <summary>
        /// 软件名。按上游《PCL 存储库合理使用指南》对「重度使用」的要求，
        /// 名称以 PCL 开头并带足以表明第三方修改的后缀，所有对外标题/横幅统一使用它。
        /// </summary>
        public const string Name = "PCL 下载中心（第三方修改版）";

        public static InstallContext CreateContext(DcTask task = null)
        {
            if (Tasks != null) return Tasks.CreateContext(task);
            return new InstallContext
            {
                Cfg = Config, Log = Log, Dl = Dl, McDir = Paths.McDir,
                ScratchDir = Path.Combine(Paths.ScratchDir, Guid.NewGuid().ToString("N")),
                Ct = CancellationToken.None
            };
        }

        /// <summary>启动 HTTP 服务并注册全部路由。</summary>
        public static void StartServer()
        {
            if (Server != null && Server.IsRunning) return;
            Server = new HttpServer(Config.ServerPort, Log);
            RegisterCoreRoutes(Server.Routes);
            try { ApiCore.Register(Server.Routes); }
            catch (Exception ex) { Log.Error("注册 ApiCore 路由失败", ex); }
            try { WebApi.Register(Server.Routes); }
            catch (Exception ex) { Log.Error("注册 WebApi 路由失败", ex); }
            Server.Start();
        }

        private static void RegisterCoreRoutes(Dictionary<string, Func<HttpRequest, HttpResponse>> routes)
        {
            routes["/api/ping"] = req => HttpResponse.Json(new { ok = true, name = "PCLDownloadCenter", version = Version });
            routes["/api/shutdown"] = req =>
            {
                Log.Info("收到关闭请求");
                Task.Run(() => { try { Thread.Sleep(250); } catch { } Shutdown(); });
                return HttpResponse.Json(new { ok = true });
            };
            routes["/api/open"] = req =>
            {
                var target = req.Get("url") ?? "/";
                OpenUrl(target.StartsWith("http") ? target : ServerUrl + (target.StartsWith("/") ? target : "/" + target));
                return HttpResponse.Json(new { ok = true });
            };
        }

        public static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Warn("打开浏览器失败：" + ex.Message); }
        }

        public static void Shutdown()
        {
            try { if (Tasks != null) Tasks.Save(); } catch { }
            try { if (Server != null) Server.Stop(); } catch { }
            try { Environment.Exit(0); } catch { }
        }

        // ---------- 主页发布 ----------
        private static Timer _homepageTimer;
        private static bool _homepageDirty;

        public static void MarkHomepageDirty()
        {
            if (Config == null || !Config.HomepagePublish) return;
            _homepageDirty = true;
            if (_homepageTimer != null) return;
            _homepageTimer = new Timer(_ =>
            {
                if (!_homepageDirty) return;
                _homepageDirty = false;
                try { HomepageRenderer.Publish(); }
                catch (Exception ex) { try { Log.Error("重写主页失败", ex); } catch { } }
            }, null, 1500, 1500);
        }

        public static void PublishHomepageNow()
        {
            try { HomepageRenderer.Publish(true); }
            catch (Exception ex) { Log.Error("重写主页失败", ex); }
        }
    }
}
