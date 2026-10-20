using System;
using System.IO;

namespace PCLDownloadCenter
{
    /// <summary>运行期配置，存放于 DownloadCenter\config.json。</summary>
    public sealed class AppConfig
    {
        /// <summary>下载源：official | bmclapi。</summary>
        public string DownloadSource = "official";
        /// <summary>单任务并发下载线程数。</summary>
        public int Threads = 32;
        /// <summary>HTTP 服务端口。</summary>
        public int ServerPort = 27321;
        /// <summary>CurseForge API Key（可选，为空则只使用 Modrinth）。</summary>
        public string CurseForgeApiKey = "";
        /// <summary>Java 路径（为空则自动探测）。</summary>
        public string JavaPath = "";
        /// <summary>主页最多显示多少个版本。</summary>
        public int HomepageMaxVersions = 30;
        public bool HomepageShowSnapshots = true;
        /// <summary>是否在任务状态变化后自动重写 Custom.xaml。</summary>
        public bool HomepagePublish = true;
        /// <summary>同时运行的安装任务数量（默认 1）。</summary>
        public int MaxConcurrentTasks = 1;
        /// <summary>覆盖 Minecraft 目录（为空则从 PCL 配置推断）。</summary>
        public string McDirOverride = "";
        /// <summary>是否在下载失败时自动切换到镜像源。</summary>
        public bool UseMirrorFallback = true;
        /// <summary>安装整合包时是否删除安装器缓存。</summary>
        public bool KeepScratch = false;

        public static string FilePath { get { return Path.Combine(Paths.PluginDir, "config.json"); } }

        public static AppConfig Load()
        {
            AppConfig cfg = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    var text = File.ReadAllText(FilePath, System.Text.Encoding.UTF8);
                    cfg = JsonUtil.ToObject<AppConfig>(text);
                }
            }
            catch { cfg = null; }
            if (cfg == null) cfg = new AppConfig();
            cfg.Normalize();
            return cfg;
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(DownloadSource)) DownloadSource = "official";
            DownloadSource = DownloadSource.Trim().ToLowerInvariant();
            if (DownloadSource != "bmclapi") DownloadSource = "official";
            if (Threads < 1) Threads = 1;
            if (Threads > 128) Threads = 128;
            if (ServerPort < 1024 || ServerPort > 65535) ServerPort = 27321;
            if (HomepageMaxVersions < 5) HomepageMaxVersions = 5;
            if (HomepageMaxVersions > 200) HomepageMaxVersions = 200;
            if (MaxConcurrentTasks < 1) MaxConcurrentTasks = 1;
            if (MaxConcurrentTasks > 4) MaxConcurrentTasks = 4;
            if (CurseForgeApiKey == null) CurseForgeApiKey = "";
            if (JavaPath == null) JavaPath = "";
            if (McDirOverride == null) McDirOverride = "";
        }

        public void Save()
        {
            try { JsonUtil.WriteFile(FilePath, this, true); }
            catch { }
        }
    }
}
