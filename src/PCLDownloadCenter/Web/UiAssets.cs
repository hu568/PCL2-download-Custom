using System.Text;

namespace PCLDownloadCenter
{
    /// <summary>
    /// Web 下载中心的单页资源。HTML / CSS / JS 全部内嵌在程序集里，
    /// 不依赖任何外部 CDN、字体或网络资源，离线也能完整使用。
    /// </summary>
    public static class UiAssets
    {
        private static string _index;
        private static readonly object Lock = new object();

        /// <summary>整个下载中心页面（单页应用）。</summary>
        public static string IndexHtml()
        {
            if (_index != null) return _index;
            lock (Lock)
            {
                if (_index != null) return _index;
                var sb = new StringBuilder(UiCss.Text.Length + UiHtml.Body.Length + UiJs.Text.Length + 1024);
                sb.Append("<!DOCTYPE html>\n");
                sb.Append("<html lang=\"zh-CN\">\n<head>\n");
                sb.Append("<meta charset=\"utf-8\">\n");
                sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
                sb.Append("<meta name=\"color-scheme\" content=\"light\">\n");
                sb.Append("<meta name=\"description\" content=\"PCL 下载中心：原版游戏 / 加载器 / 整合包安装\">\n");
                sb.Append("<title>PCL 下载中心</title>\n");
                sb.Append("<style>\n").Append(UiCss.Text).Append("\n</style>\n");
                sb.Append("</head>\n<body>\n");
                sb.Append(UiHtml.Body).Append("\n");
                sb.Append("<script>\n").Append(UiJs.Text).Append("\n</script>\n");
                sb.Append("</body>\n</html>");
                _index = sb.ToString();
                return _index;
            }
        }

        /// <summary>页面体积（字节级字符数，用于日志/自检）。</summary>
        public static int IndexSize { get { return IndexHtml().Length; } }
    }
}
