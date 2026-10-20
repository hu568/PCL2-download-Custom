using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PCLDownloadCenter
{
    /// <summary>探测到的一个 Java 运行时。</summary>
    public sealed class JavaInfo
    {
        /// <summary>java.exe 绝对路径。</summary>
        public string Path { get; set; }
        /// <summary>Java 主版本号（8 / 17 / 21 …；未知为 0）。</summary>
        public int Major { get; set; }
        /// <summary>完整版本号文本（例如 21.0.7 / 1.8.0_51）。</summary>
        public string VersionText { get; set; }
        /// <summary>来源说明（配置 / JAVA_HOME / PCL 运行时 / PATH / 安装目录）。</summary>
        public string Source { get; set; }
        public override string ToString()
        {
            return (Major > 0 ? "Java " + Major : "Java（版本未知）") + " · " + Path;
        }
    }

    /// <summary>
    /// Java 探测：配置路径 → JAVA_HOME → PCL 自带运行时（.minecraft\runtime）→ PATH → Program Files。
    /// 结果缓存 60 秒，可通过 Refresh() 强制重扫。
    /// </summary>
    public static class JavaLocator
    {
        private static readonly object _lock = new object();
        private static List<JavaInfo> _cache;
        private static DateTime _cacheTime = DateTime.MinValue;
        private const int CacheSeconds = 60;

        /// <summary>按版本要求返回最合适的 java.exe 路径（&gt;= requiredMajor 的最近版本）；找不到返回 null。</summary>
        public static string FindJava(int requiredMajor = 8)
        {
            if (requiredMajor <= 0) requiredMajor = 8;
            List<JavaInfo> all = FindAllInfo();
            JavaInfo best = null;
            foreach (JavaInfo info in all)
            {
                if (info.Major < requiredMajor) continue;
                if (best == null || info.Major < best.Major) best = info;
            }
            return best == null ? null : best.Path;
        }

        /// <summary>列出所有探测到的 java.exe 路径。</summary>
        public static List<string> FindAll()
        {
            List<string> list = new List<string>();
            foreach (JavaInfo info in FindAllInfo()) list.Add(info.Path);
            return list;
        }

        /// <summary>列出所有探测到的 Java（带版本号）。</summary>
        public static List<JavaInfo> FindAllInfo()
        {
            lock (_lock)
            {
                if (_cache != null && (DateTime.Now - _cacheTime).TotalSeconds < CacheSeconds) return _cache;
            }
            List<JavaInfo> list = Scan();
            lock (_lock)
            {
                _cache = list;
                _cacheTime = DateTime.Now;
            }
            return list;
        }

        /// <summary>根据版本 JSON 的 javaVersion.majorVersion 选择 Java。</summary>
        public static string FindJavaForVersion(int majorVersion)
        {
            if (majorVersion <= 0) majorVersion = 8;
            return FindJava(majorVersion);
        }

        /// <summary>清空缓存，下次探测重新扫描。</summary>
        public static void Refresh()
        {
            lock (_lock)
            {
                _cache = null;
                _cacheTime = DateTime.MinValue;
            }
        }

        /// <summary>
        /// 找不到合适 Java 时给用户的可操作提示（vanilla / loaders / pack 共用）。
        /// 明确告诉用户去哪里下载/指定 Java，而不是只说“未找到 Java”。
        /// </summary>
        public static string MissingHint(int requiredMajor)
        {
            if (requiredMajor <= 0) requiredMajor = 8;
            List<JavaInfo> all = FindAllInfo();
            string found;
            if (all.Count == 0)
            {
                found = "本机未探测到任何 Java";
            }
            else
            {
                List<int> majors = new List<int>();
                foreach (JavaInfo info in all)
                {
                    if (info.Major > 0 && !majors.Contains(info.Major)) majors.Add(info.Major);
                }
                majors.Sort();
                if (majors.Count > 0)
                {
                    string list = "";
                    foreach (int m in majors) list += (list.Length == 0 ? "" : "、") + m;
                    found = "本机已探测到 Java " + list + "，但没有 ≥" + requiredMajor + " 的版本";
                }
                else
                {
                    found = "本机有 " + all.Count + " 个 Java，但版本号无法识别";
                }
            }
            return "未找到可用的 Java " + requiredMajor + "（" + found + "）。" +
                   "请打开 PCL → 设置 → 启动 → Java 管理，下载并选择一个 Java " + requiredMajor + "；" +
                   "或在本插件设置（Web 设置页 / DownloadCenter\\config.json 的 JavaPath）里指定 java.exe 路径后重试。";
        }

        /// <summary>把所有探测结果拼成一句日志文本。</summary>
        public static string DescribeAll()
        {
            List<JavaInfo> list = FindAllInfo();
            if (list.Count == 0) return "未探测到 Java";
            string s = "";
            foreach (JavaInfo info in list)
            {
                if (s.Length > 0) s += "；";
                s += info.ToString();
            }
            return s;
        }

        // ==================== 扫描 ====================

        private static List<JavaInfo> Scan()
        {
            List<JavaInfo> result = new List<JavaInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 配置里指定的 Java
            try
            {
                if (App.Config != null && !string.IsNullOrWhiteSpace(App.Config.JavaPath))
                    AddPath(result, seen, NormalizeJavaPath(App.Config.JavaPath), "配置指定");
            }
            catch { }

            // 2. JAVA_HOME
            try
            {
                string home = Environment.GetEnvironmentVariable("JAVA_HOME");
                if (!string.IsNullOrWhiteSpace(home)) AddPath(result, seen, FindJavaInHome(home), "JAVA_HOME");
            }
            catch { }

            // 3. PCL 自带运行时：.minecraft\runtime\*\bin\java.exe（含多级目录）
            foreach (string runtimeRoot in RuntimeRoots())
            {
                try
                {
                    if (!Directory.Exists(runtimeRoot)) continue;
                    foreach (string javaExe in FindJavaExeRecursive(runtimeRoot, 4))
                        AddPath(result, seen, javaExe, "PCL 运行时");
                }
                catch { }
            }

            // 4. PATH
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string javaExe = Path.Combine(dir.Trim(), "java.exe");
                    if (File.Exists(javaExe)) AddPath(result, seen, javaExe, "PATH");
                }
            }
            catch { }

            // 5. 常见安装目录
            foreach (string root in InstallRoots())
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string javaExe in FindJavaExeRecursive(root, 2))
                        AddPath(result, seen, javaExe, "安装目录");
                }
                catch { }
            }

            // 6. Oracle javapath
            foreach (string p in OracleJavaPathCandidates())
            {
                if (File.Exists(p)) AddPath(result, seen, p, "javapath");
            }

            return result;
        }

        private static List<string> RuntimeRoots()
        {
            List<string> list = new List<string>();
            // 当前 MC 目录下的 runtime
            try
            {
                if (!string.IsNullOrWhiteSpace(Paths.McDir))
                    list.Add(Path.Combine(Paths.McDir, "runtime"));
            }
            catch { }
            // %APPDATA%\.minecraft\runtime（PCL 默认把运行时放在这里）
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrWhiteSpace(appData))
                    list.Add(Path.Combine(appData, ".minecraft", "runtime"));
            }
            catch { }
            // PCL 根目录下的 runtime（少数版本）
            try
            {
                if (!string.IsNullOrWhiteSpace(Paths.BaseDir))
                    list.Add(Path.Combine(Paths.BaseDir, "runtime"));
            }
            catch { }
            return list;
        }

        private static IEnumerable<string> InstallRoots()
        {
            List<string> roots = new List<string>();
            foreach (string pf in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" })
            {
                try
                {
                    string root = Environment.GetEnvironmentVariable(pf);
                    if (!string.IsNullOrWhiteSpace(root)) roots.Add(root);
                }
                catch { }
            }
            List<string> result = new List<string>();
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                if (!result.Exists(x => string.Equals(x, root, StringComparison.OrdinalIgnoreCase))) result.Add(root);
            }
            List<string> withSub = new List<string>();
            foreach (string root in result)
            {
                withSub.Add(root);
                withSub.Add(Path.Combine(root, "Java"));
                withSub.Add(Path.Combine(root, "Eclipse Adoptium"));
                withSub.Add(Path.Combine(root, "Eclipse Foundation"));
                withSub.Add(Path.Combine(root, "Microsoft"));
                withSub.Add(Path.Combine(root, "Zulu"));
                withSub.Add(Path.Combine(root, "Amazon Corretto"));
                withSub.Add(Path.Combine(root, "BellSoft"));
                withSub.Add(Path.Combine(root, "Semeru"));
            }
            return withSub;
        }

        private static IEnumerable<string> OracleJavaPathCandidates()
        {
            List<string> list = new List<string>();
            foreach (string pf in new[] { "ProgramFiles", "ProgramFiles(x86)" })
            {
                try
                {
                    string root = Environment.GetEnvironmentVariable(pf);
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    list.Add(Path.Combine(root, "Common Files", "Oracle", "Java", "javapath", "java.exe"));
                }
                catch { }
            }
            return list;
        }

        /// <summary>在目录下（限制深度）查找 bin\java.exe。</summary>
        private static List<string> FindJavaExeRecursive(string root, int maxDepth)
        {
            List<string> result = new List<string>();
            try
            {
                Walk(root, 0, maxDepth, result);
            }
            catch { }
            return result;
        }

        private static void Walk(string dir, int depth, int maxDepth, List<string> result)
        {
            if (depth > maxDepth) return;
            try
            {
                string javaExe = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(javaExe)) result.Add(javaExe);
                if (depth == maxDepth) return;
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (name.StartsWith(".")) continue;
                    Walk(sub, depth + 1, maxDepth, result);
                }
            }
            catch { }
        }

        private static string FindJavaInHome(string home)
        {
            if (string.IsNullOrWhiteSpace(home)) return null;
            string exe = Path.Combine(home, "bin", "java.exe");
            if (File.Exists(exe)) return exe;
            return null;
        }

        private static string NormalizeJavaPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string p = raw.Trim().Trim('"');
            try
            {
                if (File.Exists(p)) return p;
                if (Directory.Exists(p))
                {
                    string exe = Path.Combine(p, "bin", "java.exe");
                    if (File.Exists(exe)) return exe;
                }
                string candidate = Path.Combine(p, "bin", "java.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
            return p;
        }

        private static void AddPath(List<JavaInfo> result, HashSet<string> seen, string javaExe, string source)
        {
            if (string.IsNullOrWhiteSpace(javaExe)) return;
            string full;
            try { full = Path.GetFullPath(javaExe); } catch { full = javaExe; }
            if (!File.Exists(full)) return;
            if (!seen.Add(full)) return;
            int major = 0;
            string versionText = null;
            try { versionText = ReadReleaseVersion(full, out major); } catch { }
            if (major <= 0)
            {
                try { versionText = RunJavaVersion(full, out major); } catch { }
            }
            result.Add(new JavaInfo { Path = full, Major = major, VersionText = versionText, Source = source });
        }

        // ==================== 版本识别 ====================

        /// <summary>读取 Java 主目录下的 release 文件判断版本。</summary>
        private static string ReadReleaseVersion(string javaExe, out int major)
        {
            major = 0;
            try
            {
                string home = Path.GetDirectoryName(Path.GetDirectoryName(javaExe));
                // 有些布局是 jre\bin\java.exe → home=jre，release 在 jre\release
                for (int i = 0; i < 3 && !string.IsNullOrWhiteSpace(home); i++)
                {
                    string release = Path.Combine(home, "release");
                    if (File.Exists(release))
                    {
                        string text = File.ReadAllText(release);
                        Match m = Regex.Match(text, "JAVA_VERSION=\"?([^\"\\r\\n]+)\"?");
                        if (m.Success)
                        {
                            string ver = m.Groups[1].Value.Trim();
                            major = ParseMajor(ver);
                            return ver;
                        }
                    }
                    home = Path.GetDirectoryName(home);
                }
            }
            catch { }
            return null;
        }

        /// <summary>运行 java -version 兜底识别版本。</summary>
        private static string RunJavaVersion(string javaExe, out int major)
        {
            major = 0;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(javaExe, "-version")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return null;
                    string err = p.StandardError.ReadToEnd();
                    string outp = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); } catch { }
                        return null;
                    }
                    Match m = Regex.Match(err + "\n" + outp, "version \"(\\d+)(?:\\.(\\d+))?");
                    if (m.Success)
                    {
                        int first = int.Parse(m.Groups[1].Value);
                        if (first == 1 && m.Groups[2].Success)
                        {
                            major = int.Parse(m.Groups[2].Value);
                            return "1." + m.Groups[2].Value;
                        }
                        major = first;
                        return m.Groups[1].Value + (m.Groups[2].Success ? "." + m.Groups[2].Value : "");
                    }
                }
            }
            catch { }
            return null;
        }

        private static int ParseMajor(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return 0;
            Match m = Regex.Match(version.Trim(), "^(\\d+)(?:\\.(\\d+))?");
            if (!m.Success) return 0;
            int first = int.Parse(m.Groups[1].Value);
            if (first == 1 && m.Groups[2].Success) return int.Parse(m.Groups[2].Value);
            return first;
        }
    }
}
