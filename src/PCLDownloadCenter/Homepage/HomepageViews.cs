using System;
using System.Collections.Generic;
using System.Text;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 主页七个视图的渲染实现（含按上游要求放置署名与赞助链接的「关于」页）。
    /// 所有视图都预渲染在同一个 Custom.xaml 里，用 Visibility 变量切换；
    /// 事件顺序统一为：执行命令（异步）→ 修改变量（同步）→ 弹出提示 → 刷新主页。
    /// </summary>
    internal static class HomepageViews
    {
        private const string CardMargin = "0,0,0,15";
        private const string Inner = "25,40,23,15";
        private const string InstallTip = "已提交任务，稍后点顶部「刷新主页」查看进度";

        // ---------------- 通用小工具 ----------------

        private static void OpenView(StringBuilder sb, string view)
        {
            HX.Open(sb, "StackPanel", HX.A("Visibility",
                "{variable:" + HV.VisVar(view) + ":" + (view == HV.Home ? "Visible" : "Collapsed") + "}"));
        }

        private static void CloseView(StringBuilder sb)
        {
            HX.Close(sb, "StackPanel");
        }

        private static void OpenCard(StringBuilder sb, string title)
        {
            HX.Open(sb, "local:MyCard", HX.A("Title", title), HX.A("Margin", CardMargin));
            HX.Open(sb, "StackPanel", HX.A("Margin", Inner));
        }

        private static void CloseCard(StringBuilder sb)
        {
            HX.Close(sb, "StackPanel");
            HX.Close(sb, "local:MyCard");
        }

        /// <summary>带反馈提示的安装按钮。</summary>
        private static void InstallButton(StringBuilder sb, string text, string execData, int width = 0,
            string margin = null, string colorType = null)
        {
            HX.ButtonEvents(sb, text, new[] { "执行命令", execData, "弹出提示", InstallTip }, colorType, width, 32, margin);
        }

        /// <summary>纯本地视图切换按钮（修改变量 + 刷新主页，无需等待后端）。</summary>
        private static void SwitchViewButton(StringBuilder sb, string text, string view, int width = 0,
            string colorType = null, string margin = null)
        {
            var attrs = new List<string>
            {
                HX.A("Height", "32"), HX.A("HorizontalAlignment", "Left"),
                HX.A("Padding", "14,0,14,0"), HX.A("Text", text)
            };
            if (width > 0) attrs.Add(HX.A("Width", width.ToString()));
            if (margin != null) attrs.Add(HX.A("Margin", margin));
            if (colorType != null) attrs.Add(HX.A("ColorType", colorType));
            HX.Open(sb, "local:MyButton", attrs.ToArray());
            HX.OpenEvents(sb);
            foreach (var v in HV.Views)
                HX.Event(sb, "修改变量", HV.VisVar(v) + "|" + (v == view ? "Visible" : "Collapsed") + "|-");
            HX.Event(sb, "修改变量", HV.ViewVar + "|" + view + "|-");
            HX.Event(sb, "刷新主页", "-");
            HX.CloseEvents(sb);
            HX.Close(sb, "local:MyButton");
        }

        /// <summary>版本列表筛选/翻页按钮：写入显隐变量并立即刷新。</summary>
        private static void PageStateButton(StringBuilder sb, string text, string[] events, bool highlight, string margin = null)
        {
            var attrs = new List<string>
            {
                HX.A("Height", "30"), HX.A("HorizontalAlignment", "Left"),
                HX.A("Padding", "10,0,10,0"), HX.A("Text", text)
            };
            if (margin != null) attrs.Add(HX.A("Margin", margin));
            if (highlight) attrs.Add(HX.A("ColorType", "Highlight"));
            HX.Open(sb, "local:MyButton", attrs.ToArray());
            HX.EventBlock(sb, events);
            HX.Close(sb, "local:MyButton");
        }

        /// <summary>任务摘要的一行文本（标题、状态、阶段、进度；失败时附错误前 80 字）。</summary>
        private static string TaskLine(DcTask t)
        {
            var info = t.Title + "  " + t.StatusText;
            if (!string.IsNullOrEmpty(t.Phase)) info += " · " + t.Phase;
            info += "  " + t.ProgressText;
            if (t.Status == DcTaskStatus.Failed && !string.IsNullOrEmpty(t.Error)) info += " · " + Shorten(t.Error, 80);
            return info;
        }

        private static string TaskItemInfo(DcTask t)
        {
            var info = t.ProgressText + " · " + t.StatusText;
            if (!string.IsNullOrEmpty(t.Phase)) info += " · " + t.Phase;
            if (t.Status == DcTaskStatus.Success && !string.IsNullOrEmpty(t.VersionId)) info += " · " + t.VersionId;
            if (t.Status == DcTaskStatus.Failed && !string.IsNullOrEmpty(t.Error)) info += " · " + Shorten(t.Error, 80);
            return info;
        }

        // ---------------- 首页 ----------------

        public static void RenderHome(StringBuilder sb, HomepageState state)
        {
            OpenView(sb, HV.Home);
            OpenCard(sb, "快速开始");
            HX.Text(sb, "欢迎使用 PCL 下载中心！这里可以一键安装原版游戏、加载器与整合包，下载任务在后台执行，本页会自动更新任务摘要。");
            var manifest = state.Versions as HomepageData.ManifestSnapshot;
            var releaseTitle = manifest != null && !string.IsNullOrEmpty(manifest.LatestRelease)
                ? "最新正式版 " + manifest.LatestRelease : "最新正式版";
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,12,0,0"));
            InstallButton(sb, "一键安装" + releaseTitle, HX.Cmd("--install vanilla latest"), 220, null, "Highlight");
            HX.Close(sb, "StackPanel");
            var snapshotTitle = manifest != null && !string.IsNullOrEmpty(manifest.LatestSnapshot)
                ? "最新快照 " + manifest.LatestSnapshot : "最新快照";
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,8,0,0"));
            InstallButton(sb, "一键安装" + snapshotTitle, HX.Cmd("--install vanilla snapshot"), 260);
            HX.Close(sb, "StackPanel");
            if (manifest != null && manifest.Versions.Count > 0)
                HX.Text(sb, "版本清单更新于 " + HomepageData.ManifestAgeText + "，共收录 " + manifest.Versions.Count + " 个版本；可在「原版游戏」页查看列表。",
                    HX.A("FontSize", "11"), HX.A("Margin", "0,8,0,0"));
            else
                HX.Hint(sb, "版本清单正在后台获取（首次安装或刚启动时需要几秒），稍后点顶部「刷新主页」即可看到完整数据。",
                    "Yellow", HX.A("Margin", "0,10,0,0"));
            CloseCard(sb);

            OpenCard(sb, "当前任务");
            var tasks = state.Tasks ?? new List<DcTask>();
            int running = 0, queued = 0, done = 0, failed = 0;
            foreach (var t in tasks)
            {
                if (t.Status == DcTaskStatus.Running) running++;
                else if (t.Status == DcTaskStatus.Pending) queued++;
                else if (t.Status == DcTaskStatus.Success) done++;
                else if (t.Status == DcTaskStatus.Failed) failed++;
            }
            HX.Text(sb, "运行中 " + running + " · 排队 " + queued + " · 已完成 " + done + " · 失败 " + failed + "（共 " + tasks.Count + " 个任务）",
                HX.A("FontSize", "11"));
            int shown = 0;
            foreach (var t in tasks)
            {
                if (shown >= 2) break;
                HX.Text(sb, "· " + Shorten(TaskLine(t), 96), HX.A("FontSize", "11"), HX.A("Margin", "0,4,0,0"));
                shown++;
            }
            if (shown == 0)
                HX.Text(sb, "暂无任务。安装任意版本后，这里会显示最近的任务与进度。", HX.A("FontSize", "11"), HX.A("Margin", "0,4,0,0"));
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            SwitchViewButton(sb, "查看任务", HV.Task, 110, "Highlight");
            HX.Button(sb, "打开 Web 下载中心", "执行命令", HX.Cmd("--open-ui"), null, 150, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            CloseCard(sb);

            OpenCard(sb, "社区资源（PCL 内置页）");
            HX.Text(sb, "PCL 内置社区资源搜索仍然可用，可直接浏览并下载 Mod、资源包、光影与数据包。", HX.A("FontSize", "11"));
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            HX.Button(sb, "浏览 Mod", "切换页面", "Download|DownloadMod", null, 110, 32);
            HX.Button(sb, "资源包", "切换页面", "Download|DownloadResourcePack", null, 100, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,8,0,0"));
            HX.Button(sb, "光影包", "切换页面", "Download|DownloadShader", null, 100, 32);
            HX.Button(sb, "数据包", "切换页面", "Download|DownloadDataPack", null, 100, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            CloseCard(sb);
            CloseView(sb);
        }

        // ---------------- 原版游戏 ----------------

        public static void RenderVanilla(StringBuilder sb, HomepageState state)
        {
            OpenView(sb, HV.Vanilla);
            OpenCard(sb, "原版游戏");
            var manifest = state.Versions as HomepageData.ManifestSnapshot;
            if (manifest == null || manifest.Versions.Count == 0)
            {
                HX.Hint(sb, "版本清单尚未就绪，暂时无法列出可安装的版本。", "Yellow");
                HX.Text(sb, "插件会在后台自动获取 Mojang 版本清单；若长时间没有数据，请检查网络后点「重新生成主页」。也可直接用下方别名按钮安装最新版。",
                    HX.A("FontSize", "11"), HX.A("Margin", "0,8,0,0"));
                HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
                InstallButton(sb, "安装最新正式版", HX.Cmd("--install vanilla latest"), 150, null, "Highlight");
                InstallButton(sb, "安装最新快照", HX.Cmd("--install vanilla snapshot"), 150, "8,0,0,0");
                HX.Close(sb, "StackPanel");
                HX.ButtonEvents(sb, "重新生成主页", new[] { "执行命令", HX.Cmd("--homepage publish"), "弹出提示", "已重新生成，请稍等 1 秒再点「刷新主页」" }, null, 160, 32, "0,10,0,0");
                CloseCard(sb);
                CloseView(sb);
                return;
            }

            HX.Text(sb, "点击任意版本即可开始安装；安装任务在后台执行，进度见「任务」页或 Web 下载中心。", HX.A("FontSize", "11"));

            // 筛选按钮
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            PageStateButton(sb, "仅正式版", new[]
            {
                "修改变量", HV.FilterReleaseVar + "|Visible|-",
                "修改变量", HV.FilterAllVar + "|Collapsed|-",
                "修改变量", HV.PageVars[0] + "|Visible|-",
                "修改变量", HV.PageVars[1] + "|Collapsed|-",
                "修改变量", HV.PageVars[2] + "|Collapsed|-",
                "修改变量", HV.PageStateVar + "|1|-",
                "修改变量", HV.FilterStateVar + "|release|-",
                "修改变量", HV.PageLabelVar + "|第 1 页 · 仅正式版|-",
                "刷新主页", "-"
            }, true);
            PageStateButton(sb, "全部版本（含快照）", new[]
            {
                "修改变量", HV.FilterReleaseVar + "|Collapsed|-",
                "修改变量", HV.FilterAllVar + "|Visible|-",
                "修改变量", HV.PageVars[0] + "|Visible|-",
                "修改变量", HV.PageVars[1] + "|Collapsed|-",
                "修改变量", HV.PageVars[2] + "|Collapsed|-",
                "修改变量", HV.PageStateVar + "|1|-",
                "修改变量", HV.FilterStateVar + "|all|-",
                "修改变量", HV.PageLabelVar + "|第 1 页 · 全部版本|-",
                "刷新主页", "-"
            }, false, "8,0,0,0");
            HX.Close(sb, "StackPanel");

            // 翻页按钮
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,8,0,0"));
            for (int i = 0; i < HV.PageVars.Length; i++)
            {
                var events = new List<string>();
                for (int j = 0; j < HV.PageVars.Length; j++)
                {
                    events.Add("修改变量");
                    events.Add(HV.PageVars[j] + "|" + (j == i ? "Visible" : "Collapsed") + "|-");
                }
                events.Add("修改变量"); events.Add(HV.PageStateVar + "|" + (i + 1) + "|-");
                events.Add("修改变量"); events.Add(HV.PageLabelVar + "|第 " + (i + 1) + " 页|-");
                events.Add("刷新主页"); events.Add("-");
                PageStateButton(sb, "第 " + (i + 1) + " 页", events.ToArray(), false, i == 0 ? null : "8,0,0,0");
            }
            HX.Close(sb, "StackPanel");

            HX.Text(sb, "{variable:" + HV.PageLabelVar + ":第 1 页 · 仅正式版}", HX.A("FontSize", "11"), HX.A("Margin", "0,8,0,0"));

            // 两种筛选各 3 页
            RenderVersionPages(sb, HV.FilterReleaseVar, true, manifest.TakeReleases(30));
            RenderVersionPages(sb, HV.FilterAllVar, false, manifest.TakeAll(30));

            HX.Button(sb, "在 Web 下载中心查看全部版本", "执行命令", HX.Cmd("--open-ui vanilla"), null, 220, 32, "0,10,0,0");
            CloseCard(sb);
            CloseView(sb);
        }

        /// <summary>渲染一个筛选下的 3 页版本列表（每页 10 个）。</summary>
        private static void RenderVersionPages(StringBuilder sb, string filterVar, bool visible, List<HomepageData.VersionEntry> versions)
        {
            HX.Open(sb, "StackPanel",
                HX.A("Visibility", "{variable:" + filterVar + ":" + (visible ? "Visible" : "Collapsed") + "}"),
                HX.A("Margin", "0,4,0,0"));
            for (int page = 0; page < HV.PageVars.Length; page++)
            {
                HX.Open(sb, "StackPanel",
                    HX.A("Visibility", "{variable:" + HV.PageVars[page] + ":" + (page == 0 ? "Visible" : "Collapsed") + "}"));
                for (int i = page * 10; i < Math.Min(versions.Count, page * 10 + 10); i++)
                {
                    var v = versions[i];
                    var info = v.TypeText + (string.IsNullOrEmpty(v.DateText) ? "" : " · " + v.DateText);
                    HX.ListItem(sb, v.Id, info, "执行命令", HX.InstallVanilla(v.Id), null, true,
                        "-5,0,-5,2", "点击安装 " + v.Id);
                }
                if (versions.Count <= page * 10)
                    HX.Text(sb, "（本页没有更多版本）", HX.A("FontSize", "11"), HX.A("Margin", "4,6,0,0"));
                HX.Close(sb, "StackPanel");
            }
            HX.Close(sb, "StackPanel");
        }

        // ---------------- 加载器 ----------------

        public static void RenderLoader(StringBuilder sb, HomepageState state)
        {
            OpenView(sb, HV.Loader);
            var manifest = state.Versions as HomepageData.ManifestSnapshot;
            var latest = manifest == null ? null : manifest.LatestRelease;

            OpenCard(sb, "推荐加载器组合");
            HX.Text(sb, "一键安装常用的加载器组合，安装完成后会自动出现在 PCL 的版本列表中。", HX.A("FontSize", "11"));
            if (!string.IsNullOrEmpty(latest))
            {
                HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
                InstallButton(sb, "安装 Fabric · MC " + latest, HX.InstallLoader("fabric", latest), 200, null, "Highlight");
                HX.Close(sb, "StackPanel");
                HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,8,0,0"));
                InstallButton(sb, "安装 Quilt · MC 1.21.4", HX.InstallLoader("quilt", "1.21.4"), 200);
                HX.Close(sb, "StackPanel");
            }
            else
            {
                HX.Hint(sb, "版本清单尚未就绪，暂时无法推荐与最新 MC 版对应的加载器；可先使用下方固定组合。", "Yellow", HX.A("Margin", "0,8,0,0"));
            }
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,8,0,0"));
            InstallButton(sb, "安装 Forge · MC 1.20.1", HX.InstallLoader("forge", "1.20.1"), 200);
            HX.Close(sb, "StackPanel");
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,8,0,0"));
            InstallButton(sb, "安装 NeoForge · MC 1.21.1", HX.InstallLoader("neoforge", "1.21.1"), 200);
            HX.Close(sb, "StackPanel");
            HX.Text(sb, "推荐组合均已真机验证：Fabric 跟随最新正式版，Quilt 用 1.21.4，Forge 用 1.20.1，NeoForge 用 1.21.1。OptiFine / LiteLoader 暂不支持自动安装，可在社区资源页手动处理。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,10,0,0"));
            HX.Button(sb, "打开 Web 下载中心选择更多加载器版本", "执行命令", HX.Cmd("--open-ui loader"), null, 260, 32, "0,8,0,0");
            CloseCard(sb);

            OpenCard(sb, "已安装版本");
            var installed = state.InstalledVersions ?? new List<string>();
            HX.Text(sb, "共 " + installed.Count + " 个已安装版本，点击即可直接启动。", HX.A("FontSize", "11"));
            int n = 0;
            foreach (var name in installed)
            {
                if (n >= 10) break;
                HX.ListItem(sb, name, "已安装 · 点击启动", "启动游戏", name, null, true, "-5,0,-5,2", "点击启动 " + name);
                n++;
            }
            if (installed.Count == 0)
                HX.Text(sb, "还没有安装任何版本，请先在上面的推荐组合中安装一个。", HX.A("FontSize", "11"), HX.A("Margin", "0,4,0,0"));
            else if (installed.Count > 10)
                HX.Text(sb, "还有 " + (installed.Count - 10) + " 个版本未显示，可点下面的按钮进入 PCL 版本选择页查看。",
                    HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            HX.Button(sb, "打开 PCL 版本选择页", "切换页面", "InstanceSelect|Default", "Highlight", 160, 32, "0,8,0,0");
            CloseCard(sb);
            CloseView(sb);
        }

        // ---------------- 整合包 ----------------

        public static void RenderPack(StringBuilder sb, HomepageState state)
        {
            OpenView(sb, HV.Pack);
            OpenCard(sb, "推荐整合包");
            HX.Text(sb, "来自 Modrinth 的热门整合包（数据更新于 " + HomepageData.PacksAgeText + "）。点击名称即可开始安装。",
                HX.A("FontSize", "11"));
            foreach (var pack in HomepageData.Packs)
            {
                var title = pack.Title ?? pack.Id;
                var info = pack.Info ?? "";
                HX.ListItem(sb, title, info, "执行命令", HX.InstallModpack(pack.Id), pack.Logo, true,
                    "-5,0,-5,2", "点击安装 " + title);
            }
            HX.Text(sb, "安装任务会下载整合包依赖（原版 + 加载器 + 模组），耗时取决于网络，进度见「任务」页。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,8,0,0"));
            HX.Button(sb, "在 Web 下载中心搜索整合包", "执行命令", HX.Cmd("--open-ui pack"), null, 220, 32, "0,8,0,0");
            CloseCard(sb);

            OpenCard(sb, "导入本地整合包");
            HX.Text(sb, "支持 .mrpack（Modrinth）与 .zip（CurseForge 整合包 / 普通压缩包）。", HX.A("FontSize", "11"));
            HX.Button(sb, "选择整合包文件并导入", "执行命令", HX.Cmd("--import pack"), "Highlight", 200, 32, "0,10,0,0");
            HX.Hint(sb, "点击后会弹出文件选择框；导入过程会下载依赖，请留意「任务」页进度。", "Blue", HX.A("Margin", "0,10,0,0"));
            CloseCard(sb);
            CloseView(sb);
        }

        // ---------------- 任务 ----------------

        public static void RenderTask(StringBuilder sb, HomepageState state)
        {
            OpenView(sb, HV.Task);
            OpenCard(sb, "任务");
            var tasks = state.Tasks ?? new List<DcTask>();
            int running = 0, queued = 0, success = 0, failed = 0, canceled = 0;
            foreach (var t in tasks)
            {
                switch (t.Status)
                {
                    case DcTaskStatus.Running: running++; break;
                    case DcTaskStatus.Pending: queued++; break;
                    case DcTaskStatus.Success: success++; break;
                    case DcTaskStatus.Failed: failed++; break;
                    case DcTaskStatus.Canceled: canceled++; break;
                }
            }
            HX.Text(sb, "运行 " + running + " · 排队 " + queued + " · 成功 " + success + " · 失败 " + failed + " · 取消 " + canceled,
                HX.A("FontSize", "11"));
            if (tasks.Count == 0)
            {
                HX.Text(sb, "暂无任务。到「首页」「原版游戏」「加载器」「整合包」任一视图即可创建安装任务。",
                    HX.A("FontSize", "11"), HX.A("Margin", "0,8,0,0"));
            }
            else
            {
                int shown = 0;
                foreach (var t in tasks)
                {
                    if (shown >= 6) break;
                    HX.ListItem(sb, Shorten(t.Title, 30), TaskItemInfo(t), "执行命令", HX.Cmd("--cancel " + t.Id), null, true,
                        "-5,0,-5,2", "点击取消任务：" + t.Id);
                    shown++;
                }
                if (tasks.Count > shown)
                    HX.Text(sb, "还有 " + (tasks.Count - shown) + " 个任务未显示，可在 Web 下载中心查看完整列表与日志。",
                        HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            }
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            HX.Button(sb, "刷新任务状态", "刷新主页", "-", "Highlight", 120, 32);
            HX.Button(sb, "打开 Web 任务页", "执行命令", HX.Cmd("--open-ui task"), null, 130, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            HX.Hint(sb, "点击任务项可请求取消该任务；取消后点「刷新任务状态」查看结果。", "Blue", HX.A("Margin", "0,10,0,0"));
            CloseCard(sb);
            CloseView(sb);
        }

        // ---------------- 设置 ----------------

        public static void RenderSettings(StringBuilder sb, HomepageState state)
        {
            var cfg = App.Config ?? new AppConfig();
            bool official = !string.Equals(cfg.DownloadSource, "bmclapi", StringComparison.OrdinalIgnoreCase);
            string label = "当前下载源：" + (official ? "官方源" : "BMCLAPI 镜像");

            OpenView(sb, HV.Settings);
            OpenCard(sb, "下载设置");
            HX.Text(sb, "{variable:DCSourceLabel:" + label + "}", HX.A("FontSize", "11"));
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            HX.ButtonEvents(sb, "使用官方源", new[]
            {
                "执行命令", HX.Cmd("--set-source official"),
                "修改变量", "DCSourceLabel|当前下载源：官方源|-",
                "弹出提示", "已切换到官方源，正在应用",
                "刷新主页", "-"
            }, official ? "Highlight" : null, 110, 32);
            HX.ButtonEvents(sb, "使用 BMCLAPI 镜像", new[]
            {
                "执行命令", HX.Cmd("--set-source bmclapi"),
                "修改变量", "DCSourceLabel|当前下载源：BMCLAPI 镜像|-",
                "弹出提示", "已切换到 BMCLAPI 镜像，正在应用",
                "刷新主页", "-"
            }, official ? null : "Highlight", 150, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            HX.Text(sb, "下载线程数 " + cfg.Threads + " · 服务端口 " + cfg.ServerPort + " · MC 目录 " + Paths.McDir,
                HX.A("FontSize", "11"), HX.A("Margin", "0,10,0,0"));
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            HX.Button(sb, "打开 Web 设置页", "执行命令", HX.Cmd("--open-ui settings"), null, 130, 32);
            HX.ButtonEvents(sb, "重新生成主页", new[] { "执行命令", HX.Cmd("--homepage publish"), "弹出提示", "已重新生成，请稍等 1 秒再点「刷新主页」" }, null, 130, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            CloseCard(sb);

            OpenCard(sb, "服务管理");
            HX.Text(sb, "后台服务端口 " + cfg.ServerPort + " · 当前" + (state.ServerRunning ? "运行中" : "未运行") +
                        "。直接关闭 PCL 不会停止后台服务；这里可以手动启动或停止，任务记录与已下载文件都会保留。", HX.A("FontSize", "11"));
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            HX.ButtonEvents(sb, "启动服务", new[] { "执行命令", HX.Cmd("--ensure"), "弹出提示", "正在启动后台服务，约 2 秒后点「刷新主页」" }, null, 100, 32);
            HX.ButtonEvents(sb, "停止服务", new[] { "执行命令", HX.Cmd("--stop"), "弹出提示", "已请求停止后台服务，约 2 秒后点「刷新主页」确认" }, null, 100, 32, "8,0,0,0");
            HX.Button(sb, "用浏览器打开下载中心", "执行命令", HX.Cmd("--open-ui"), null, 170, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            CloseCard(sb);

            OpenCard(sb, "帮助与卸载");
            HX.Text(sb, "本主页由 PCL 下载中心插件自动生成。手动修改 Custom.xaml 会在下次发布时被覆盖。", HX.A("FontSize", "11"));
            HX.Text(sb, "如果主页内容显示异常，可点上述「重新生成主页」重建；也可以直接删除 PCL/Custom.xaml 恢复 PCL 默认主页。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            HX.Text(sb, "卸载：关闭 PCL 后删除 PCL/DownloadCenter 目录与 PCL/Custom.xaml，并把 PCL/Setup.ini 里的 UiCustomType 改为 0。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            HX.Text(sb, "日志目录：PCL/DownloadCenter/data/logs · 插件版本 v" + App.Version +
                " · 生成时间 " + (state.GeneratedAt == DateTime.MinValue ? DateTime.Now : state.GeneratedAt).ToString("yyyy-MM-dd HH:mm"),
                HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            CloseCard(sb);
            CloseView(sb);
        }

        // ---------------- 关于 ----------------

        /// <summary>
        /// 关于页。按上游《PCL 存储库合理使用指南》对「重度使用」的第 3 条要求，
        /// 本页首位给出 PCL 原作者龙腾猫跃的署名与赞助链接，并声明这是第三方二次创作。
        /// </summary>
        public static void RenderAbout(StringBuilder sb, HomepageState state)
        {
            OpenView(sb, HV.About);
            OpenCard(sb, "关于 · 致谢");
            HX.Text(sb, "PCL（Plain Craft Launcher 2）原作者：龙腾猫跃（LTCat）。", HX.A("FontSize", "12"));
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            HX.Button(sb, "赞助 PCL 作者", "打开网页", "https://meloong.com/afd/a/LTCat", "Highlight", 150, 32,
                null, "前往爱发电支持 PCL 原作者龙腾猫跃");
            HX.Button(sb, "PCL GitHub 仓库", "打开网页", "https://github.com/Hex-Dragon/PCL2", null, 150, 32, "8,0,0,0");
            HX.Button(sb, "PCL 分发有限许可", "打开网页", "https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-X0bo", null, 160, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            HX.Text(sb, "本插件是第三方基于 PCL 独立进行二次创作的产物，与 PCL 官方无关；名称中的「第三方修改版」即为此标识。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,10,0,0"));
            HX.Text(sb, "本项目遵循引用自 PCL 存储库的《PCL 存储库合理使用指南》（发布包内 LICENCE-PCL.md），并继续把它作为本项目自己的使用指南。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            HX.Text(sb, "本插件不实现 Minecraft 启动逻辑：上面「已安装版本」里的「启动游戏」调用的是 PCL 本体的启动功能（正版购买劝导由 PCL 实现）；本插件也没有任何类似 PCL「赞助解锁」的功能。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            CloseCard(sb);

            OpenCard(sb, "开源许可");
            HX.Text(sb, "本项目开源仓库（GitHub）：https://github.com/hu568/PCL2-download-Custom", HX.A("FontSize", "11"));
            HX.Text(sb, "上游 PCL 仓库（GitHub）：https://github.com/Meloong-Git/PCL · https://github.com/Hex-Dragon/PCL2",
                HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            HX.Text(sb, "本插件以 GNU GPL-3.0 发布，源码即上面的 GitHub 仓库；随包分发的第三方组件为 Newtonsoft.Json 13.0.3（MIT）。完整声明见发布包内 LICENSE 与 THIRD-PARTY-NOTICES.md。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            HX.Text(sb, "本插件不分发任何 PCL 代码或二进制；安装 Forge / NeoForge 时，会从你本机的 PCL 程序里提取它内嵌的补丁版安装器（与 PCL 自身做法一致）。",
                HX.A("FontSize", "11"), HX.A("Margin", "0,6,0,0"));
            HX.Open(sb, "StackPanel", HX.A("Orientation", "Horizontal"), HX.A("Margin", "0,10,0,0"));
            HX.Button(sb, "本项目 GitHub 仓库", "打开网页", "https://github.com/hu568/PCL2-download-Custom", "Highlight", 170, 32);
            HX.Button(sb, "上游 PCL GitHub", "打开网页", "https://github.com/Meloong-Git/PCL", null, 160, 32, "8,0,0,0");
            HX.Close(sb, "StackPanel");
            CloseCard(sb);
            CloseView(sb);
        }

        // ---------------- 杂项 ----------------

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", "").Replace("\n", " ").Trim();
            if (text.Length <= max) return text;
            return text.Substring(0, max) + "…";
        }
    }
}
