using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PCLDownloadCenter
{
    /// <summary>插件涉及的各个目录。</summary>
    public static class Paths
    {
        /// <summary>插件目录：&lt;PCL根&gt;\PCL\DownloadCenter</summary>
        public static string PluginDir = "";
        /// <summary>PCL 的 PCL 文件夹：&lt;PCL根&gt;\PCL（Custom.xaml 所在处，也是命令行工作目录）。</summary>
        public static string PclDir = "";
        /// <summary>PCL 根目录（exe 所在处）。</summary>
        public static string BaseDir = "";
        /// <summary>插件数据目录：PluginDir\data。</summary>
        public static string DataDir = "";
        public static string LogDir = "";
        public static string ScratchDir = "";
        public static string ConfigFile = "";
        public static string HomepageFile = "";
        public static string TasksFile = "";
        public static string CacheDir = "";
        /// <summary>Minecraft 目录（.minecraft）。</summary>
        public static string McDir = "";
        public static AppConfig Config;

        public static string VersionsDir { get { return Path.Combine(McDir, "versions"); } }
        public static string LibrariesDir { get { return Path.Combine(McDir, "libraries"); } }
        public static string AssetsDir { get { return Path.Combine(McDir, "assets"); } }

        public static void Init()
        {
            PluginDir = ResolvePluginDir();
            PclDir = Path.GetDirectoryName(PluginDir.TrimEnd('\\', '/'));
            BaseDir = Path.GetDirectoryName(PclDir.TrimEnd('\\', '/'));
            DataDir = Path.Combine(PluginDir, "data");
            LogDir = Path.Combine(DataDir, "logs");
            ScratchDir = Path.Combine(DataDir, "temp");
            CacheDir = Path.Combine(DataDir, "cache");
            TasksFile = Path.Combine(DataDir, "tasks.json");
            HomepageFile = Path.Combine(PclDir, "Custom.xaml");
            ConfigFile = Path.Combine(PluginDir, "config.json");
            FileUtil.EnsureDir(DataDir);
            FileUtil.EnsureDir(LogDir);
            FileUtil.EnsureDir(ScratchDir);
            FileUtil.EnsureDir(CacheDir);
            Config = AppConfig.Load();
            McDir = ResolveMcDir();
            FileUtil.EnsureDir(McDir);
        }

        private static string ResolvePluginDir()
        {
            string loc = "";
            try { loc = System.Reflection.Assembly.GetExecutingAssembly().Location; } catch { }
            if (string.IsNullOrWhiteSpace(loc))
                loc = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PCLDownloadCenter.exe");
            var dir = Path.GetDirectoryName(loc);
            if (string.IsNullOrWhiteSpace(dir)) dir = AppDomain.CurrentDomain.BaseDirectory;
            // 兼容：直接放在 PCL\ 下运行时
            return dir.TrimEnd('\\', '/');
        }

        /// <summary>从 PCL 的 config.v1.yml 推断 .minecraft 位置。</summary>
        private static string ResolveMcDir()
        {
            if (Config != null && !string.IsNullOrWhiteSpace(Config.McDirOverride))
                return NormalizePath(Config.McDirOverride);

            string folder = null;
            var cfgFile = Path.Combine(PclDir, "config.v1.yml");
            if (File.Exists(cfgFile))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(cfgFile, Encoding.UTF8))
                    {
                        var t = line.Trim();
                        if (t.StartsWith("LaunchFolderSelect:", StringComparison.OrdinalIgnoreCase))
                        {
                            folder = t.Substring("LaunchFolderSelect:".Length).Trim().Trim('\'', '"');
                            break;
                        }
                    }
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(folder))
            {
                var candidate = Path.Combine(BaseDir, ".minecraft");
                if (Directory.Exists(candidate)) return candidate;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
            }
            return NormalizePath(folder);
        }

        /// <summary>
        /// PCL 里 $.minecraft\ 表示相对于 PCL 根目录；普通相对路径也相对 PCL 根目录。
        /// </summary>
        public static string NormalizePath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Path.Combine(BaseDir, ".minecraft");
            var p = raw.Trim().Trim('\'', '"').Replace('/', '\\');
            if (p.StartsWith("$"))
            {
                p = p.Substring(1).TrimStart('\\');
                p = Path.Combine(BaseDir, p);
            }
            else if (!Path.IsPathRooted(p))
            {
                p = Path.Combine(BaseDir, p);
            }
            try { p = Path.GetFullPath(p); } catch { }
            return p.TrimEnd('\\');
        }

        /// <summary>读取已安装的版本目录名列表。</summary>
        public static List<string> ListInstalledVersions()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(VersionsDir)) return list;
                foreach (var dir in Directory.GetDirectories(VersionsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (File.Exists(Path.Combine(dir, name + ".json"))) list.Add(name);
                }
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }
}
