using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 主页 Custom.xaml 渲染器（完整实现）。
    /// 七个视图全部预渲染在同一个文件里，用 {variable:DCVxxx:Visible/Collapsed} 控制显隐；
    /// 视图切换由 PCL 侧的「修改变量 + 刷新主页」纯本地完成，避免执行命令的异步竞态。
    /// </summary>
    public static class HomepageRenderer
    {
        /// <summary>
        /// 软件名：按上游《PCL 存储库合理使用指南》对「重度使用」的要求，
        /// 名称以 PCL 开头并带足以表明第三方修改的后缀。
        /// </summary>
        public const string AppTitle = "PCL 下载中心（第三方修改版）";

        /// <summary>生成的页面节点数软上限（PCL 用 XamlReader 解析，节点过多会明显变慢）。</summary>
        private const int NodeWarnLimit = 400;
        private const int NodeHardLimit = 520;
        private const int ContentHardLimit = 200 * 1024;

        /// <summary>校验 XML 时使用与 PCL 相同的外层命名空间声明。</summary>
        private const string ValidateWrapperStart =
            "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
            " xmlns:sys=\"clr-namespace:System;assembly=mscorlib\"" +
            " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" +
            " xmlns:local=\"clr-namespace:PCL;assembly=Plain Craft Launcher 2\"" +
            " xmlns:core=\"clr-namespace:MeloongCore;assembly=MeloongCore\"" +
            " xmlns:corewpf=\"clr-namespace:MeloongCore.Wpf;assembly=MeloongCore.Wpf\">";

        // ---------------- 渲染入口 ----------------

        /// <summary>按当前状态渲染完整主页内容（不含外层 StackPanel，PCL 会自行包裹）。</summary>
        public static string Render(HomepageState state)
        {
            if (state == null) state = BuildState();
            HomepageData.EnsureLoaded();
            var sb = new StringBuilder(96 * 1024);
            sb.AppendLine("<!-- PCL 下载中心 · 主页插件，由 PCLDownloadCenter 自动生成；手动修改会在下次发布时被覆盖。 -->");
            RenderTopBar(sb, state);
            HomepageViews.RenderHome(sb, state);
            HomepageViews.RenderVanilla(sb, state);
            HomepageViews.RenderLoader(sb, state);
            HomepageViews.RenderPack(sb, state);
            HomepageViews.RenderTask(sb, state);
            HomepageViews.RenderSettings(sb, state);
            HomepageViews.RenderAbout(sb, state);
            return sb.ToString();
        }

        /// <summary>引导页：首次安装、渲染异常或内容不合法时使用的极简页面。</summary>
        public static string RenderBootstrap(AppConfig cfg, string status)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!-- PCL 下载中心 · 引导页 -->");
            HX.Open(sb, "local:MyCard", HX.A("Title", AppTitle), HX.A("Margin", "0,0,0,15"));
            HX.Open(sb, "StackPanel", HX.A("Margin", "25,40,23,15"));
            HX.Text(sb, "状态：" + (status ?? "未知"));
            HX.Text(sb, "插件目录：" + Paths.PluginDir, HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,12,0,0"));
            HX.Button(sb, "打开 Web 下载中心", "执行命令", HX.Cmd("--open-ui"), "Highlight", 160, 32);
            HX.ButtonEvents(sb, "重新生成主页", new[] { "执行命令", HX.Cmd("--homepage publish"), "弹出提示", "已重新生成主页，请点「刷新主页」查看" }, null, 150, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            HX.Button(sb, "刷新主页", "刷新主页", "-", null, 100, 32, "0,8,0,0");
            HX.Hint(sb, "如果本页长期出现，请查看 DownloadCenter/data/logs 目录下的日志排查原因。", "Yellow", HX.A("Margin", "0,12,0,0"));
            HX.Close(sb, "StackPanel");
            HX.Close(sb, "local:MyCard");
            return sb.ToString();
        }

        /// <summary>组装渲染状态快照。</summary>
        public static HomepageState BuildState()
        {
            var state = new HomepageState
            {
                View = RegistryUtil.ReadVariable(HV.ViewVar, HV.Home),
                ServerRunning = App.Server != null && App.Server.IsRunning,
                ServerUrl = App.ServerUrl,
                Tasks = App.Tasks == null ? new List<DcTask>() : App.Tasks.Snapshot(),
                InstalledVersions = Paths.ListInstalledVersions(),
                Versions = HomepageData.Manifest,
                GeneratedAt = DateTime.Now
            };
            state.StatusText = state.ServerRunning ? "服务运行中" : "服务未运行";
            return state;
        }

        // ---------------- 发布 ----------------

        /// <summary>重写 Custom.xaml（内容有变化时才写）。</summary>
        public static void Publish(bool force = false)
        {
            try
            {
                HomepageData.EnsureLoaded();
                HomepageData.WarmupAsync(false);
                string content;
                try
                {
                    content = Render(BuildState());
                }
                catch (Exception ex)
                {
                    if (App.Log != null) App.Log.Error("渲染主页异常，改用引导页", ex);
                    content = RenderBootstrap(AppConfigSafe(), "渲染异常：" + ex.Message);
                }
                // 写盘前校验 XML 合法性，异常时退回引导页，绝不让坏文件上线
                string error;
                if (!ValidateXaml(content, out error))
                {
                    if (App.Log != null) App.Log.Warn("生成的主页 XML 校验失败：" + error);
                    content = RenderBootstrap(AppConfigSafe(), "内容校验失败：" + error);
                    if (!ValidateXaml(content, out error))
                    {
                        if (App.Log != null) App.Log.Error("引导页同样校验失败，已放弃写入：" + error);
                        return;
                    }
                }
                var nodes = CountNodes(content);
                if (nodes > NodeWarnLimit && App.Log != null)
                    App.Log.Warn("主页节点数偏多：" + nodes + "（建议 < " + NodeWarnLimit + "）");
                if (nodes > NodeHardLimit || content.Length > ContentHardLimit)
                {
                    if (App.Log != null) App.Log.Warn("主页内容过大（节点 " + nodes + "，字符 " + content.Length + "），改用轻量引导页");
                    content = RenderBootstrap(AppConfigSafe(), "数据过多，已使用轻量页面");
                }
                WriteIfChanged(content, force);
            }
            catch (Exception ex)
            {
                if (App.Log != null) App.Log.Error("发布主页失败", ex);
            }
        }

        private static AppConfig AppConfigSafe()
        {
            return App.Config ?? new AppConfig();
        }

        /// <summary>内容变化时原子写入主页文件。</summary>
        public static void WriteIfChanged(string content, bool force = false)
        {
            try
            {
                var path = Paths.HomepageFile;
                if (!force && File.Exists(path))
                {
                    var old = File.ReadAllText(path, Encoding.UTF8);
                    if (string.Equals(old, content, StringComparison.Ordinal)) return;
                }
                FileUtil.WriteAllTextAtomic(path, content);
                if (App.Log != null) App.Log.Info("已重写主页：" + path + "（" + content.Length + " 字符，" + CountNodes(content) + " 个节点）");
            }
            catch (Exception ex)
            {
                if (App.Log != null) App.Log.Error("写入主页失败", ex);
            }
        }

        /// <summary>
        /// XML 合法性校验：套用与 PCL 完全一致的外层命名空间后再用 XmlReader 检查，
        /// 这样 local:/core:/corewpf: 等前缀都能正常解析，能真正拦下错标签、错转义。
        /// </summary>
        public static bool ValidateXaml(string content, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(content)) { error = "内容为空"; return false; }
            try
            {
                var wrapped = ValidateWrapperStart + content + "</StackPanel>";
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    CheckCharacters = true,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                };
                using (var reader = XmlReader.Create(new StringReader(wrapped), settings))
                {
                    while (reader.Read()) { }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>粗略统计节点数（以元素起始标签计）。</summary>
        public static int CountNodes(string content)
        {
            if (string.IsNullOrEmpty(content)) return 0;
            return Regex.Matches(content, "<[A-Za-z]").Count;
        }

        // ---------------- 顶栏 ----------------

        private static void RenderTopBar(StringBuilder sb, HomepageState state)
        {
            var tasks = state.Tasks ?? new List<DcTask>();
            int running = tasks.Count(t => t.Status == DcTaskStatus.Running);
            int queued = tasks.Count(t => t.Status == DcTaskStatus.Pending);
            string status = state.ServerRunning
                ? "服务运行中 · " + (state.ServerUrl ?? "") + " · 运行 " + running + " / 排队 " + queued
                : "服务未运行 · 请打开 Web 下载中心或点击「重新生成主页」启动后端";

            HX.Open(sb, "local:MyCard", HX.A("Title", AppTitle), HX.A("Margin", "0,0,0,15"));
            HX.Open(sb, "StackPanel", HX.A("Margin", "25,40,23,15"));
            HX.Text(sb, status, HX.A("FontSize", "11"));
            HX.Text(sb, "在这里安装原版游戏、加载器与整合包；下载任务会在后台执行，可随时切到「任务」页查看进度。", HX.A("Margin", "0,6,0,0"));

            // 视图切换：每行三个按钮（纯本地变量切换，无需等待后端）
            for (int row = 0; row * 3 < HV.Views.Length; row++)
            {
                HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"),
                    HX.A("Margin", row == 0 ? "0,12,0,0" : "0,8,0,0"));
                int end = Math.Min(row * 3 + 3, HV.Views.Length);
                for (int i = row * 3; i < end; i++) NavButton(sb, HV.Views[i]);
                HX.Close(sb, "StackPanel");
            }

            // 常用操作：启动/停止服务两个按钮都常驻。
            // 主页只是“生成时刻的快照”，停止后状态行会过期，所以不能按状态只留一个按钮；
            // --stop 成功时会重写主页（服务未运行），用户点「刷新主页」即可看到最新状态。
            int port = App.Config == null ? 27321 : App.Config.ServerPort;
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,12,0,0"));
            HX.Button(sb, "打开完整下载中心", "执行命令", HX.Cmd("--open-ui"), "Highlight", 150, 32);
            HX.ButtonEvents(sb, "启动服务",
                new[] { "执行命令", HX.Cmd("--ensure"), "弹出提示", "正在启动后台服务，约 2 秒后点「刷新主页」" },
                state.ServerRunning ? null : "Highlight", 96, 32, "8,0,0,0", "启动后台服务（端口 " + port + "）");
            HX.ButtonEvents(sb, "停止服务",
                new[] { "执行命令", HX.Cmd("--stop"), "弹出提示", "已请求停止后台服务，约 2 秒后点「刷新主页」确认" },
                state.ServerRunning ? "Highlight" : null, 96, 32, "8,0,0,0", "停止后台服务（直接关闭 PCL 不会停止它）");
            HX.Button(sb, "刷新主页", "刷新主页", "-", null, 96, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            HX.Close(sb, "StackPanel");
            HX.Close(sb, "local:MyCard");
        }

        private static void NavButton(StringBuilder sb, string view)
        {
            var text = HV.ViewTitle(view);
            HX.Open(sb, "local:MyButton",
                HX.A("Width", "100"), HX.A("Height", "30"), HX.A("Margin", "0,0,8,0"),
                HX.A("Padding", "6,0,6,0"), HX.A("Text", text),
                HX.A("ToolTip", "切换到「" + text + "」"),
                HX.A("EventType", "刷新主页"), HX.A("EventData", "-"));
            HX.OpenEvents(sb);
            foreach (var v in HV.Views)
                HX.Event(sb, "修改变量", HV.VisVar(v) + "|" + (v == view ? "Visible" : "Collapsed") + "|-");
            HX.Event(sb, "修改变量", HV.ViewVar + "|" + view + "|-");
            HX.CloseEvents(sb);
            HX.Close(sb, "local:MyButton");
        }
    }
}
