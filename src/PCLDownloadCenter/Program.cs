using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PCLDownloadCenter
{
    public static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);
        private const int AttachParentProcess = -1;

        private static readonly ManualResetEventSlim ExitEvent = new ManualResetEventSlim(false);

        [STAThread]
        public static int Main(string[] args)
        {
            bool console = args.Any(a => string.Equals(a, "--console", StringComparison.OrdinalIgnoreCase));
            if (console)
            {
                try
                {
                    AttachConsole(AttachParentProcess);
                    // 让中文在控制台正确显示
                    try { Console.OutputEncoding = new System.Text.UTF8Encoding(false); } catch { }
                }
                catch { }
            }
            try
            {
                App.Initialize();
                if (args.Length == 0) return RunServer();
                return Cli.Run(args);
            }
            catch (Exception ex)
            {
                try { if (App.Log != null) App.Log.Error("主程序异常", ex); } catch { }
                if (console)
                {
                    try { Console.Error.WriteLine("致命错误：" + ex); } catch { }
                }
                else
                {
                    try
                    {
                        MessageBox.Show("PCL 下载中心启动失败：\n\n" + ex.Message + "\n\n日志：" + (App.Log == null ? "" : App.Log.LogFile),
                            "PCL 下载中心", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch { }
                }
                return 1;
            }
        }

        /// <summary>前台运行 HTTP 服务（阻塞）。</summary>
        public static int RunServer()
        {
            App.StartServer();
            App.Tasks.Changed += App.MarkHomepageDirty;
            // 启动时重写一次主页（若内容有变化）
            try { HomepageRenderer.Publish(); } catch (Exception ex) { App.Log.Error("启动时重写主页失败", ex); }
            App.MarkHomepageDirty();
            try
            {
                Console.CancelKeyPress += (s, e) => { e.Cancel = true; ExitEvent.Set(); };
                AppDomain.CurrentDomain.ProcessExit += (s, e) => App.Shutdown();
            }
            catch { }
            App.Log.Info("服务已就绪，等待请求。");
            ExitEvent.Wait();
            try { App.Tasks.Save(); } catch { }
            try { if (App.Server != null) App.Server.Stop(); } catch { }
            return 0;
        }

        public static void RequestExit()
        {
            ExitEvent.Set();
        }
    }
}