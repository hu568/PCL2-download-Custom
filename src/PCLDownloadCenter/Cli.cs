using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Microsoft.Win32;
using System.Windows.Forms;

namespace PCLDownloadCenter
{
    /// <summary>命令行入口。所有主页按钮都通过它调用。</summary>
    public static class Cli
    {
        public static int Run(string[] args)
        {
            var cmd = Normalize(args[0]);
            var rest = args.Skip(1).ToArray();
            switch (cmd)
            {
                case "server": RunServerBlocking(); return 0;
                case "start":
                case "ensure":
                case "ensure-server": return EnsureServer() ? 0 : 1;
                case "stop":
                case "shutdown": return StopServer();
                case "status": return Status();
                case "install": return Install(rest);
                case "import": return Import(rest);
                case "open":
                case "open-ui": return OpenUi(rest);
                case "view": return SetView(rest);
                case "cancel": return CancelTask(rest);
                case "set-source":
                case "source": return SetSource(rest);
                case "logs": return ShowLogs(rest);
                case "homepage": return Homepage(rest);
                case "help":
                case "-h":
                case "version":
                case "-v": PrintUsage(); return 0;
                default:
                    WriteLine("未知命令：" + args[0]);
                    PrintUsage();
                    return 2;
            }
        }

        private static string Normalize(string arg)
        {
            return (arg ?? "").TrimStart('-', '/').ToLowerInvariant();
        }

        private static void RunServerBlocking()
        {
            Program.RunServer();
        }

        private static void WriteLine(string message)
        {
            try { Console.WriteLine(message); } catch { }
        }

        private static void WriteError(string message)
        {
            try { Console.Error.WriteLine(message); } catch { }
        }

        // ---------------- 服务管理 ----------------

        /// <summary>确保后台服务在运行；返回是否成功。</summary>
        public static bool EnsureServer()
        {
            if (HttpServer.IsPortOpen(App.Config.ServerPort))
            {
                WriteLine("服务已在运行：" + App.ServerUrl);
                return true;
            }
            WriteLine("正在启动后台服务……");
            try
            {
                var exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--server",
                    WorkingDirectory = Paths.PclDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                WriteError("启动服务进程失败：" + ex.Message);
            }
            for (int i = 0; i < 60; i++)
            {
                if (HttpServer.IsPortOpen(App.Config.ServerPort)) { WriteLine("服务已启动：" + App.ServerUrl); return true; }
                Thread.Sleep(250);
            }
            WriteError("服务启动超时。");
            return false;
        }

        /// <summary>
        /// 停止后台服务：先请求 /api/shutdown 并等待端口关闭；
        /// 5 秒内没停下（例如后端卡死）就直接结束同路径的 --server 进程，避免“服务停不下来”。
        /// 成功后把主页重写成「服务未运行」，PCL 点「刷新主页」即可看到启动按钮。
        /// </summary>
        private static int StopServer()
        {
            int port = App.Config.ServerPort;
            if (!HttpServer.IsPortOpen(port))
            {
                WriteLine("服务未在运行。");
                PublishStoppedHomepage();
                return 0;
            }
            try
            {
                var resp = App.Dl.PostJsonAsync(App.ServerUrl + "/api/shutdown", "{}", CancellationToken.None, 5000, 1, false).GetAwaiter().GetResult();
                WriteLine("已请求关闭服务：" + resp);
            }
            catch (Exception ex)
            {
                WriteError("请求关闭失败（将直接结束后台进程）：" + ex.Message);
            }
            if (WaitPortClosed(port, 5000))
            {
                WriteLine("服务已停止。");
                PublishStoppedHomepage();
                return 0;
            }
            WriteLine("服务未在 5 秒内停止，尝试直接结束后台进程……");
            int killed = KillServerProcesses();
            if (WaitPortClosed(port, 5000))
            {
                WriteLine("服务已停止（结束进程 " + killed + " 个）。");
                PublishStoppedHomepage();
                return 0;
            }
            WriteError("服务仍未停止（端口 " + port + "）。请手动结束 PCLDownloadCenter.exe 进程。");
            return 1;
        }

        /// <summary>轮询等待端口关闭。</summary>
        private static bool WaitPortClosed(int port, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!HttpServer.IsPortOpen(port)) return true;
                Thread.Sleep(200);
            }
            return !HttpServer.IsPortOpen(port);
        }

        /// <summary>
        /// 结束后台服务进程：只处理与当前 exe 路径完全一致的进程，避免误杀其它程序。
        /// 提权运行的进程取不到路径时会跳过并提示。
        /// </summary>
        private static int KillServerProcesses()
        {
            int killed = 0;
            string self = null;
            try { self = System.Reflection.Assembly.GetExecutingAssembly().Location; } catch { }
            if (string.IsNullOrWhiteSpace(self)) return 0;
            try { self = Path.GetFullPath(self); } catch { return 0; }
            int selfId = -1;
            try { selfId = Process.GetCurrentProcess().Id; } catch { }
            string name = Path.GetFileNameWithoutExtension(self);
            Process[] list;
            try { list = Process.GetProcessesByName(name); } catch { return 0; }
            foreach (var proc in list)
            {
                try
                {
                    if (proc.Id == selfId) continue;
                    string path = null;
                    try { path = proc.MainModule == null ? null : proc.MainModule.FileName; } catch { }
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    try { path = Path.GetFullPath(path); } catch { continue; }
                    if (!string.Equals(path, self, StringComparison.OrdinalIgnoreCase)) continue;
                    WriteLine("结束后台进程：" + proc.Id + "（" + path + "）");
                    proc.Kill();
                    if (proc.WaitForExit(5000)) killed++;
                    else WriteError("进程 " + proc.Id + " 未能结束。");
                }
                catch (Exception ex)
                {
                    WriteError("结束进程失败：" + ex.Message);
                }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }
            return killed;
        }

        /// <summary>停止成功后重写主页（此时本进程没有 HTTP 服务，渲染出的状态即“服务未运行”）。</summary>
        private static void PublishStoppedHomepage()
        {
            try { HomepageRenderer.Publish(true); }
            catch (Exception ex) { WriteError("重写主页失败：" + ex.Message); }
        }

        private static int Status()
        {
            if (!HttpServer.IsPortOpen(App.Config.ServerPort))
            {
                WriteLine("服务状态：未运行（端口 " + App.Config.ServerPort + "）");
                WriteLine("MC 目录：" + Paths.McDir);
                return 0;
            }
            try
            {
                var json = App.Dl.GetStringAsync(App.ServerUrl + "/api/status", CancellationToken.None, 5000, 1, false).GetAwaiter().GetResult();
                WriteLine(json);
                return 0;
            }
            catch (Exception ex)
            {
                WriteError("获取状态失败：" + ex.Message);
                return 1;
            }
        }

        // ---------------- 安装 ----------------

        private static int Install(string[] args)
        {
            if (args.Length == 0) { WriteError("用法：--install <vanilla|loader|modpack|packfile> <参数…>"); return 2; }
            var kind = (args[0] ?? "").ToLowerInvariant();
            var installArgs = args.Skip(1).ToArray();

            // 参数预校验：不合法直接返回 2，不去启动服务/打开浏览器
            string usageError = ValidateInstallArgs(kind, installArgs);
            if (usageError != null)
            {
                WriteError(usageError);
                WriteLine("");
                PrintUsage();
                return 2;
            }

            var payload = new JObject();
            payload["kind"] = kind;
            payload["args"] = new JArray(installArgs);
            var title = Installers.Title(kind, installArgs);
            WriteLine("准备" + title + " ……");

            if (!EnsureServer())
            {
                WriteError("后台服务不可用，改为在当前进程执行。");
                return RunLocal(kind, installArgs);
            }
            try
            {
                var resp = App.Dl.PostJsonAsync(App.ServerUrl + "/api/install", payload.ToString(Newtonsoft.Json.Formatting.None), CancellationToken.None, 15000, 2, false).GetAwaiter().GetResult();
                var json = JObject.Parse(resp);
                var taskId = json["taskId"] == null ? null : json["taskId"].ToString();
                if (string.IsNullOrEmpty(taskId)) { WriteError("服务未返回任务 ID：" + resp); return 1; }
                WriteLine("已创建任务：" + taskId);
                return WaitTask(taskId);
            }
            catch (Exception ex)
            {
                WriteError("提交任务失败：" + ex.Message);
                App.OpenUrl(App.ServerUrl + "/");
                return 1;
            }
        }

        /// <summary>校验安装参数个数；不合法时返回错误提示。</summary>
        private static string ValidateInstallArgs(string kind, string[] args)
        {
            int n = args == null ? 0 : args.Length;
            switch (kind)
            {
                case "vanilla":
                    return n >= 1 ? null : "缺少版本号，例如：--install vanilla 1.21.4（也可用 latest / snapshot）";
                case "loader":
                    return n >= 2 ? null : "参数不足，例如：--install loader fabric 1.21.4";
                case "modpack":
                    return n >= 2 ? null : "参数不足，例如：--install modpack modrinth <项目ID> [版本ID]";
                case "packfile":
                case "modpack-file":
                    return n >= 1 ? null : "缺少整合包文件路径，例如：--import pack D:\\packs\\xxx.mrpack";
                default:
                    return "未知的安装类型：" + kind + "（可用：vanilla / loader / modpack / packfile）";
            }
        }

        /// <summary>轮询任务直到结束，并把日志打印到控制台。</summary>
        private static int WaitTask(string taskId)
        {
            int printed = 0;
            int lastPercent = -1;
            string finalStatus = "";
            for (int i = 0; i < 3600 * 4; i++)
            {
                try
                {
                    var resp = App.Dl.GetStringAsync(App.ServerUrl + "/api/task?id=" + Uri.EscapeDataString(taskId), CancellationToken.None, 10000, 2, false).GetAwaiter().GetResult();
                    var json = JObject.Parse(resp);
                    var task = json["task"];
                    if (task == null) { Thread.Sleep(500); continue; }
                    var status = task["status"] == null ? "" : task["status"].ToString();
                    finalStatus = status;
                    var phase = task["phase"] == null ? "" : task["phase"].ToString();
                    var percent = task["progress"] == null ? 0 : (int)(task["progress"].ToObject<double>() * 100);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        WriteLine("[" + percent + "%] " + phase);
                    }
                    var logs = task["logs"] as JArray;
                    if (logs != null)
                    {
                        for (; printed < logs.Count; printed++)
                            WriteLine("  " + logs[printed]);
                    }
                    if (status == "Success") { WriteLine("✔ " + (task["message"] == null ? "完成" : task["message"].ToString())); return 0; }
                    if (status == "Failed") { WriteError("✘ 失败：" + (task["error"] == null ? "" : task["error"].ToString())); return 1; }
                    if (status == "Canceled") { WriteError("任务已取消。"); return 1; }
                }
                catch (Exception ex) { WriteError("查询任务失败：" + ex.Message); }
                Thread.Sleep(700);
            }
            WriteError("等待任务超时（状态：" + finalStatus + "）");
            return 1;
        }

        /// <summary>服务不可用时，直接在当前进程执行安装。</summary>
        private static int RunLocal(string kind, string[] args)
        {
            try
            {
                var task = App.Tasks.Create(kind, Installers.Title(kind, args));
                task.Cts = new CancellationTokenSource();
                task.Token = task.Cts.Token;
                task.Status = DcTaskStatus.Running;
                var ctx = App.Tasks.CreateContext(task);
                ctx.InfoCallback = line => WriteLine("  " + line);
                ctx.ProgressCallback = (phase, ratio) => WriteLine("[" + (int)(ratio * 100) + "%] " + phase);
                var result = Installers.RunAsync(ctx, kind, args).GetAwaiter().GetResult();
                if (result.Ok) { WriteLine("✔ " + result.Message); return 0; }
                WriteError("✘ " + result.Message);
                return 1;
            }
            catch (Exception ex)
            {
                WriteError("安装失败：" + ex.Message);
                return 1;
            }
        }

        private static int CancelTask(string[] args)
        {
            var id = args.Length > 0 ? args[0] : "";
            if (!string.IsNullOrEmpty(id))
            {
                if (!EnsureServer()) return 1;
                try
                {
                    var resp = App.Dl.GetStringAsync(App.ServerUrl + "/api/cancel?id=" + Uri.EscapeDataString(id), CancellationToken.None, 5000, 1, false).GetAwaiter().GetResult();
                    WriteLine("取消请求：" + resp);
                    return 0;
                }
                catch (Exception ex) { WriteError("取消失败：" + ex.Message); return 1; }
            }
            // 没有 id：取消最新的运行中任务
            if (!EnsureServer()) return 1;
            try
            {
                var text = App.Dl.GetStringAsync(App.ServerUrl + "/api/tasks", CancellationToken.None, 5000, 1, false).GetAwaiter().GetResult();
                var json = JObject.Parse(text);
                var tasks = json["tasks"] as JArray;
                if (tasks != null)
                {
                    foreach (var t in tasks)
                    {
                        var status = t["status"] == null ? "" : t["status"].ToString();
                        if (status == "Running" || status == "Pending")
                        {
                            var resp = App.Dl.GetStringAsync(App.ServerUrl + "/api/cancel?id=" + Uri.EscapeDataString(t["id"].ToString()), CancellationToken.None, 5000, 1, false).GetAwaiter().GetResult();
                            WriteLine("已取消：" + t["title"] + " -> " + resp);
                            return 0;
                        }
                    }
                }
                WriteLine("没有正在运行的任务。");
                return 0;
            }
            catch (Exception ex) { WriteError("取消失败：" + ex.Message); return 1; }
        }

        private static int SetSource(string[] args)
        {
            var source = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "";
            if (source != "official" && source != "bmclapi") { WriteError("用法：--set-source <official|bmclapi>"); return 2; }
            App.Config.DownloadSource = source;
            App.Config.Save();
            if (HttpServer.IsPortOpen(App.Config.ServerPort))
            {
                try
                {
                    var payload = "{\"downloadSource\":\"" + source + "\"}";
                    App.Dl.PostJsonAsync(App.ServerUrl + "/api/config", payload, CancellationToken.None, 5000, 1, false).GetAwaiter().GetResult();
                }
                catch { }
            }
            WriteLine("下载源已切换为：" + (source == "official" ? "官方源" : "BMCLAPI 镜像"));
            return 0;
        }

        private static int ShowLogs(string[] args)
        {
            var appLog = App.Log == null ? null : App.Log.LogFile;
            if (!string.IsNullOrEmpty(appLog) && File.Exists(appLog))
            {
                WriteLine(File.ReadAllText(appLog, Encoding.UTF8));
                return 0;
            }
            WriteLine("暂无日志。");
            return 0;
        }

        // ---------------- 其它 ----------------

        private static int Import(string[] args)
        {
            var sub = args.Length > 0 ? Normalize(args[0]) : "pack";
            string file = args.Length > 1 ? args[1] : null;
            if (file == null)
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择要导入的整合包";
                    dialog.Filter = "整合包文件|*.zip;*.mrpack|所有文件|*.*";
                    if (dialog.ShowDialog() != DialogResult.OK) { WriteLine("已取消。"); return 1; }
                    file = dialog.FileName;
                }
            }
            return Install(new[] { "packfile", file });
        }

        private static int OpenUi(string[] args)
        {
            var url = App.ServerUrl + "/";
            if (args.Length > 0)
            {
                var sub = args[0].TrimStart('/');
                if (!string.IsNullOrEmpty(sub)) url += "#" + sub;
            }
            if (!EnsureServer()) return 1;
            App.OpenUrl(url);
            WriteLine("已打开：" + url);
            return 0;
        }

        /// <summary>--view &lt;home|vanilla|loader|pack|task|settings&gt;</summary>
        private static int SetView(string[] args)
        {
            var view = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "home";
            RegistryUtil.WriteVariable("DCView", view);
            WriteLine("主页视图已切换为：" + view + "（请在 PCL 主页点击刷新查看）");
            return 0;
        }

        private static int Homepage(string[] args)
        {
            var sub = args.Length > 0 ? Normalize(args[0]) : "publish";
            if (sub == "publish")
            {
                App.PublishHomepageNow();
                WriteLine("已重写主页文件：" + Paths.HomepageFile);
                return 0;
            }
            if (sub == "path") { WriteLine(Paths.HomepageFile); return 0; }
            WriteError("用法：--homepage publish|path");
            return 2;
        }

        private static void PrintUsage()
        {
            WriteLine(App.Name + " v" + App.Version);
            WriteLine("用法：PCLDownloadCenter.exe <命令> [参数…]");
            WriteLine("");
            WriteLine("  --server                     前台运行后台服务");
            WriteLine("  --start / --ensure           确保后台服务在运行");
            WriteLine("  --stop                       关闭后台服务");
            WriteLine("  --status                     查看运行状态（JSON）");
            WriteLine("  --install vanilla <版本>      安装原版游戏，如 --install vanilla 1.21.4");
            WriteLine("  --install loader <类型> <MC版本> [加载器版本]");
            WriteLine("          类型：fabric | quilt | forge | neoforge");
            WriteLine("  --install modpack <来源> <ID> [版本]");
            WriteLine("          来源：modrinth | curseforge | url");
            WriteLine("  --import pack [文件]          导入本地整合包（zip / mrpack）");
            WriteLine("  --open-ui [子页]              打开 Web 下载中心");
            WriteLine("  --view <home|vanilla|loader|pack|task|settings>  切换主页视图");
            WriteLine("  --cancel [任务ID]             取消任务（省略 ID 则取消最新的运行中任务）");
            WriteLine("  --set-source <official|bmclapi>   切换下载源");
            WriteLine("  --logs                        输出日志文件内容");
            WriteLine("  --homepage publish            重写 PCL 主页 Custom.xaml");
        }
    }
}