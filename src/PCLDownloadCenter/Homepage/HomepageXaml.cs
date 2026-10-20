using System;
using System.Text;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 主页 XAML 拼接工具：XML 转义、控件片段生成、自定义事件块生成。
    /// 生成的整页会先经过 XML 合法性校验，再原子写入 Custom.xaml。
    /// </summary>
    internal static class HX
    {
        /// <summary>插件可执行文件的相对路径（相对 PCL 目录，即 PCL\）。</summary>
        public const string CliPath = "DownloadCenter\\PCLDownloadCenter.exe";

        /// <summary>XML 属性值转义。</summary>
        public static string Esc(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length + 16);
            foreach (var c in text)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\r': break;
                    case '\n': sb.Append("&#xA;"); break;
                    default:
                        if (c < ' ') sb.Append(' ');
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>拼一个 XML 属性；value 为 null 时返回空串（用于可选属性）。</summary>
        public static string A(string name, string value)
        {
            if (value == null) return "";
            return " " + name + "=\"" + Esc(value) + "\"";
        }

        public static void Open(StringBuilder sb, string tag, params string[] attrs)
        {
            sb.Append('<').Append(tag);
            AppendAttrs(sb, attrs);
            sb.AppendLine(">");
        }

        public static void Close(StringBuilder sb, string tag)
        {
            sb.Append("</").Append(tag).AppendLine(">");
        }

        public static void Self(StringBuilder sb, string tag, params string[] attrs)
        {
            sb.Append('<').Append(tag);
            AppendAttrs(sb, attrs);
            sb.AppendLine(" />");
        }

        private static void AppendAttrs(StringBuilder sb, string[] attrs)
        {
            if (attrs == null) return;
            foreach (var a in attrs)
                if (!string.IsNullOrEmpty(a)) sb.Append(a);
        }

        /// <summary>一段折行文本。</summary>
        public static void Text(StringBuilder sb, string text, params string[] attrs)
        {
            var list = new System.Collections.Generic.List<string>(attrs ?? new string[0]);
            list.Insert(0, A("TextWrapping", "Wrap"));
            list.Insert(0, A("Text", text));
            Self(sb, "TextBlock", list.ToArray());
        }

        /// <summary>提示条。</summary>
        public static void Hint(StringBuilder sb, string text, string theme = null, params string[] attrs)
        {
            var list = new System.Collections.Generic.List<string>(attrs ?? new string[0]);
            list.Add(A("Text", text));
            if (theme != null) list.Add(A("Theme", theme));
            Self(sb, "local:MyHint", list.ToArray());
        }

        /// <summary>普通按钮（单事件用 EventType/EventData，复杂交互请自行拼 Events）。</summary>
        public static void Button(StringBuilder sb, string text, string eventType = null, string eventData = null,
            string colorType = null, int width = 0, int height = 32, string margin = null, string toolTip = null)
        {
            var list = new System.Collections.Generic.List<string>();
            list.Add(A("Height", height.ToString()));
            if (width > 0) list.Add(A("Width", width.ToString()));
            list.Add(A("HorizontalAlignment", "Left"));
            list.Add(A("Padding", "14,0,14,0"));
            list.Add(A("Margin", margin));
            list.Add(A("Text", text));
            if (colorType != null) list.Add(A("ColorType", colorType));
            if (toolTip != null) list.Add(A("ToolTip", toolTip));
            if (eventType != null) list.Add(A("EventType", eventType));
            if (eventData != null) list.Add(A("EventData", eventData));
            Self(sb, "local:MyButton", list.ToArray());
        }

        /// <summary>多事件按钮：带 Events 事件集合。</summary>
        public static void ButtonEvents(StringBuilder sb, string text, string[] typeDataPairs,
            string colorType = null, int width = 0, int height = 32, string margin = null, string toolTip = null, string visibility = null)
        {
            var list = new System.Collections.Generic.List<string>();
            list.Add(A("Height", height.ToString()));
            if (width > 0) list.Add(A("Width", width.ToString()));
            list.Add(A("HorizontalAlignment", "Left"));
            list.Add(A("Padding", "14,0,14,0"));
            list.Add(A("Margin", margin));
            list.Add(A("Text", text));
            if (colorType != null) list.Add(A("ColorType", colorType));
            if (toolTip != null) list.Add(A("ToolTip", toolTip));
            if (visibility != null) list.Add(A("Visibility", visibility));
            Open(sb, "local:MyButton", list.ToArray());
            EventBlock(sb, typeDataPairs);
            Close(sb, "local:MyButton");
        }

        /// <summary>我的世界列表项（Type=Clickable 时可点击触发事件）。</summary>
        public static void ListItem(StringBuilder sb, string title, string info, string eventType = null, string eventData = null,
            string logo = null, bool clickable = false, string margin = "-5,0,-5,2", string toolTip = null)
        {
            var list = new System.Collections.Generic.List<string>();
            list.Add(A("Margin", margin));
            list.Add(A("Title", title));
            if (info != null) list.Add(A("Info", info));
            if (logo != null) list.Add(A("Logo", logo));
            if (clickable) list.Add(A("Type", "Clickable"));
            if (toolTip != null) list.Add(A("ToolTip", toolTip));
            if (eventType != null) list.Add(A("EventType", eventType));
            if (eventData != null) list.Add(A("EventData", eventData));
            Self(sb, "local:MyListItem", list.ToArray());
        }

        /// <summary>事件集合开始。</summary>
        public static void OpenEvents(StringBuilder sb)
        {
            sb.AppendLine("<local:CustomEventService.Events>");
            sb.AppendLine("<local:CustomEventCollection>");
        }

        public static void Event(StringBuilder sb, string type, string data)
        {
            sb.Append("<local:CustomEvent").Append(A("Type", type)).Append(A("Data", data)).AppendLine(" />");
        }

        public static void CloseEvents(StringBuilder sb)
        {
            sb.AppendLine("</local:CustomEventCollection>");
            sb.AppendLine("</local:CustomEventService.Events>");
        }

        /// <summary>生成多事件块；pairs 为 "事件类型", "事件数据", "事件类型", "事件数据" ... 交替出现。</summary>
        public static void EventBlock(StringBuilder sb, params string[] pairs)
        {
            if (pairs == null || pairs.Length < 2) return;
            OpenEvents(sb);
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                Event(sb, pairs[i], pairs[i + 1]);
            CloseEvents(sb);
        }

        /// <summary>执行后端命令的事件数据。</summary>
        public static string Cmd(string arguments)
        {
            return CliPath + "|" + arguments;
        }

        /// <summary>调用 CLIS 的包装：命令行参数。</summary>
        public static string InstallVanilla(string versionId)
        {
            return Cmd("--install vanilla " + versionId);
        }

        public static string InstallLoader(string loader, string mcVersion, string loaderVersion = null)
        {
            var args = "--install loader " + loader + " " + mcVersion;
            if (!string.IsNullOrEmpty(loaderVersion)) args += " " + loaderVersion;
            return Cmd(args);
        }

        public static string InstallModpack(string projectId)
        {
            return Cmd("--install modpack modrinth " + projectId);
        }
    }

    /// <summary>主页视图与变量名约定。</summary>
    internal static class HV
    {
        public const string Home = "home";
        public const string Vanilla = "vanilla";
        public const string Loader = "loader";
        public const string Pack = "pack";
        public const string Task = "task";
        public const string Settings = "settings";

        /// <summary>全部视图（顺序即顶栏按钮顺序）。</summary>
        public static readonly string[] Views = { Home, Vanilla, Loader, Pack, Task, Settings };

        /// <summary>当前视图变量（供 CLIS 与调试使用）。</summary>
        public const string ViewVar = "DCView";
        /// <summary>版本列表筛选容器变量：正式版。</summary>
        public const string FilterReleaseVar = "DCVFRel";
        /// <summary>版本列表筛选容器变量：全部（含快照）。</summary>
        public const string FilterAllVar = "DCVFAll";
        /// <summary>版本列表页码变量（第 1~3 页容器）。</summary>
        public static readonly string[] PageVars = { "DCVP1", "DCVP2", "DCVP3" };
        /// <summary>版本列表页码状态（1/2/3，供外部读取）。</summary>
        public const string PageStateVar = "DCVerPage";
        /// <summary>版本列表筛选状态（release/all，供外部读取）。</summary>
        public const string FilterStateVar = "DCVerFilter";
        /// <summary>版本列表页标题文本变量。</summary>
        public const string PageLabelVar = "DCVerPageLabel";

        /// <summary>视图显隐变量名。</summary>
        public static string VisVar(string view)
        {
            switch (view)
            {
                case Home: return "DCVHome";
                case Vanilla: return "DCVVanilla";
                case Loader: return "DCVLoader";
                case Pack: return "DCVPack";
                case Task: return "DCVTask";
                case Settings: return "DCVSettings";
                default: return "DCVHome";
            }
        }

        /// <summary>视图的中文名。</summary>
        public static string ViewTitle(string view)
        {
            switch (view)
            {
                case Home: return "首页";
                case Vanilla: return "原版游戏";
                case Loader: return "加载器";
                case Pack: return "整合包";
                case Task: return "任务";
                case Settings: return "设置";
                default: return view;
            }
        }
    }
}
