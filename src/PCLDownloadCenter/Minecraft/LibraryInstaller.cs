using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>一个需要下载的依赖库文件（Collect 的结果）。</summary>
    public sealed class LibraryFile
    {
        /// <summary>Maven 坐标，例如 org.lwjgl:lwjgl:3.3.3:natives-windows。</summary>
        public string Name { get; set; }
        public string Url { get; set; }
        /// <summary>本地绝对路径。</summary>
        public string Path { get; set; }
        public string Sha1 { get; set; }
        public long Size { get; set; } = -1;
        /// <summary>是否为 natives 库（需要解压 dll）。</summary>
        public bool IsNative { get; set; }
        /// <summary>原始 library JSON 节点（解压时需要 extract.exclude）。</summary>
        public JObject Library { get; set; }
        public override string ToString() { return Name ?? Path; }
    }

    /// <summary>
    /// 依赖库（libraries）下载与 natives 解压。
    /// 供原版安装（VanillaInstaller）与加载器安装（Loaders）共同使用。
    /// </summary>
    public static class LibraryInstaller
    {
        /// <summary>官方 Maven 库根地址（SourceRewriter 会镜像到 bmclapi）。</summary>
        public const string DefaultMavenRoot = "https://libraries.minecraft.net/";

        // ==================== 对外主入口 ====================

        /// <summary>
        /// 下载 versionJson 里 libraries 声明的全部依赖库，并把 natives 解压到 nativesDir（可为 null）。
        /// versionJson 可以是完整版本 JSON，也可以是带 inheritsFrom 的补丁 JSON（会自动向上合并父版本）。
        /// </summary>
        public static async Task DownloadAsync(InstallContext ctx, JObject versionJson, string nativesDir = null, Action<int, int> onProgress = null)
        {
            if (versionJson == null) throw new ArgumentNullException("versionJson");
            ctx = VersionManifest.EnsureContext(ctx);
            ctx.ThrowIfCancelled();

            List<LibraryFile> files = Collect(ctx, versionJson);
            if (files.Count == 0)
            {
                ctx.Info("依赖库：该版本没有需要下载的依赖库");
                if (onProgress != null) { try { onProgress(0, 0); } catch { } }
                return;
            }

            // 已存在且大小正确的直接跳过（Downloader 还会按 sha1 复核）
            List<DownloadItem> need = new List<DownloadItem>();
            long needBytes = 0;
            foreach (LibraryFile f in files)
            {
                long have = FileUtil.SizeOf(f.Path);
                if (have >= 0 && (f.Size < 0 ? have > 0 : have == f.Size)) continue;
                if (string.IsNullOrWhiteSpace(f.Url))
                {
                    ctx.Warn("依赖库没有下载地址，跳过：" + f.Name);
                    continue;
                }
                need.Add(new DownloadItem(f.Url, f.Path, f.Sha1, f.Size, f.Name));
                if (f.Size > 0) needBytes += f.Size;
            }

            int existCount = files.Count - need.Count;
            ctx.Info("依赖库：共 " + files.Count + " 个文件，已存在 " + existCount + " 个，需要下载 " + need.Count + " 个" +
                     (needBytes > 0 ? "（约 " + FormatSize(needBytes) + "）" : ""));
            if (onProgress != null) { try { onProgress(existCount, files.Count); } catch { } }

            if (need.Count > 0)
            {
                try
                {
                    await ctx.Dl.DownloadManyAsync(need, (done, total) =>
                    {
                        if (onProgress != null) { try { onProgress(existCount + done, files.Count); } catch { } }
                    }, ctx.Ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new Exception("依赖库下载失败：" + ex.Message + "\n建议：切换下载源（--set-source bmclapi）后重试。", ex);
                }
            }
            if (onProgress != null) { try { onProgress(files.Count, files.Count); } catch { } }
            ctx.ThrowIfCancelled();

            // 解压 natives
            if (!string.IsNullOrWhiteSpace(nativesDir))
            {
                int extracted = 0;
                int nativeLibs = 0;
                foreach (LibraryFile f in files)
                {
                    if (!f.IsNative) continue;
                    nativeLibs++;
                    if (!File.Exists(f.Path)) continue;
                    try { extracted += ExtractNatives(f, nativesDir); }
                    catch (Exception ex) { ctx.Warn("解压 Natives 失败（" + f.Name + "）：" + ex.Message); }
                }
                if (nativeLibs > 0)
                    ctx.Info("Natives：共 " + nativeLibs + " 个压缩包，解压 " + extracted + " 个文件到 " + nativesDir);
            }
        }

        /// <summary>收集所有需要下载的依赖库（已做 rules 过滤、inheritsFrom 合并、去重）。</summary>
        public static List<LibraryFile> Collect(InstallContext ctx, JObject versionJson)
        {
            if (versionJson == null) throw new ArgumentNullException("versionJson");
            ctx = VersionManifest.EnsureContext(ctx);
            List<LibraryFile> result = new List<LibraryFile>();

            VersionInfo info = VersionInfo.FromJson(versionJson);
            if (info.HasParent) info = info.ResolveFromDisk(ctx);
            JArray libs = info.Libraries;
            if (libs == null) return result;

            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (JToken token in libs)
            {
                JObject lib = token as JObject;
                if (lib == null) continue;
                if (!IsAllowed(lib)) continue;

                string name = JsonUtil.Str(lib, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;

                JObject downloads = lib["downloads"] as JObject;
                JObject natives = lib["natives"] as JObject;
                string rootUrl = JsonUtil.Str(lib, "url");

                if (natives != null)
                {
                    // 老版本（<=1.18）：natives 对象 + classifiers
                    string classifier = NativeClassifier(natives);
                    if (classifier == null) continue;   // 当前平台没有 natives

                    JObject dl = null;
                    JObject classifiers = downloads == null ? null : downloads["classifiers"] as JObject;
                    if (classifiers != null) dl = classifiers[classifier] as JObject;

                    string relPath = dl == null ? null : JsonUtil.Str(dl, "path");
                    string url = dl == null ? null : JsonUtil.Str(dl, "url");
                    string sha1 = dl == null ? null : JsonUtil.Str(dl, "sha1");
                    long size = dl == null ? -1 : JsonUtil.Long(dl, "size", -1);

                    if (string.IsNullOrWhiteSpace(relPath)) relPath = AddClassifierToPath(NameToPath(name), classifier);
                    if (string.IsNullOrWhiteSpace(url)) url = BuildUrl(rootUrl, relPath);

                    AddLibrary(result, seen, ctx, name, url, relPath, sha1, size, true, lib);
                    continue;
                }

                JObject artifact = downloads == null ? null : downloads["artifact"] as JObject;
                // 现代版本（1.19+）的 natives 是带 classifier 的普通库：直接进 classpath，由 LWJGL 自行解压，
                // 与 PCL 行为一致，这里不再标记为需要解压到 natives 目录。
                bool isNative = false;

                if (artifact == null)
                {
                    // 只有 name / 根 url 的库（老 Forge 常见）
                    string rel = NameToPath(name);
                    string url = BuildUrl(rootUrl, rel);
                    AddLibrary(result, seen, ctx, name, url, rel, null, -1, isNative, lib);
                    continue;
                }

                string aPath = JsonUtil.Str(artifact, "path");
                if (string.IsNullOrWhiteSpace(aPath)) aPath = NameToPath(name);
                string aUrl = JsonUtil.Str(artifact, "url");
                string aSha1 = JsonUtil.Str(artifact, "sha1");
                long aSize = JsonUtil.Long(artifact, "size", -1);
                if (string.IsNullOrWhiteSpace(aUrl)) aUrl = BuildUrl(rootUrl, aPath);

                AddLibrary(result, seen, ctx, name, aUrl, aPath, aSha1, aSize, isNative, lib);
            }
            return result;
        }

        /// <summary>校验依赖库是否齐全，返回缺失数量（total 为总数）。</summary>
        public static int Verify(InstallContext ctx, JObject versionJson, out int total)
        {
            List<LibraryFile> files = Collect(ctx, versionJson);
            total = files.Count;
            int missing = 0;
            foreach (LibraryFile f in files)
            {
                long have = FileUtil.SizeOf(f.Path);
                if (have < 0 || (f.Size > 0 && have != f.Size)) missing++;
            }
            return missing;
        }

        // ==================== Maven 坐标与规则 ====================

        /// <summary>把 maven 坐标（group:artifact:version[:classifier][@ext]）转成相对路径。</summary>
        public static string NameToPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Maven 坐标为空", "name");
            string raw = name.Trim();
            string ext = "jar";
            int at = raw.LastIndexOf('@');
            if (at > 0)
            {
                ext = raw.Substring(at + 1).Trim();
                raw = raw.Substring(0, at);
                if (ext.Length == 0) ext = "jar";
            }
            string[] parts = raw.Split(':');
            if (parts.Length < 3) throw new Exception("无法识别的 Maven 坐标：" + name);
            string group = parts[0].Trim();
            string artifact = parts[1].Trim();
            string version = parts[2].Trim();
            string classifier = parts.Length > 3 ? parts[3].Trim() : null;
            if (group.Length == 0 || artifact.Length == 0 || version.Length == 0)
                throw new Exception("无法识别的 Maven 坐标：" + name);
            string path = group.Replace('.', '/') + "/" + artifact + "/" + version + "/" + artifact + "-" + version;
            if (!string.IsNullOrEmpty(classifier)) path += "-" + classifier;
            return path + "." + ext;
        }

        /// <summary>根据 rules（os / features）判断当前平台是否需要该库。</summary>
        public static bool IsAllowed(JObject library)
        {
            if (library == null) return false;
            JArray rules = library["rules"] as JArray;
            if (rules == null || rules.Count == 0) return true;

            bool allowed = false;
            foreach (JToken t in rules)
            {
                JObject rule = t as JObject;
                if (rule == null) continue;
                if (!RuleMatches(rule)) continue;
                string action = JsonUtil.Str(rule, "action", "allow");
                allowed = string.Equals(action, "allow", StringComparison.OrdinalIgnoreCase);
            }
            return allowed;
        }

        private static bool RuleMatches(JObject rule)
        {
            JObject os = rule["os"] as JObject;
            if (os != null)
            {
                string osName = JsonUtil.Str(os, "name");
                if (!string.IsNullOrEmpty(osName) && !string.Equals(osName, PlatformName, StringComparison.OrdinalIgnoreCase)) return false;

                string osArch = JsonUtil.Str(os, "arch");
                if (!string.IsNullOrEmpty(osArch) && !ArchMatches(osArch)) return false;

                string osVersion = JsonUtil.Str(os, "version");
                if (!string.IsNullOrEmpty(osVersion))
                {
                    try { if (!Regex.IsMatch(Environment.OSVersion.VersionString, osVersion)) return false; }
                    catch { }
                }
            }

            JObject features = rule["features"] as JObject;
            if (features != null)
            {
                // 安装阶段所有 features（demo / custom_resolution / quick_play 等）都视为 false
                foreach (JProperty p in features.Properties())
                {
                    bool required = p.Value != null && p.Value.Type == JTokenType.Boolean && (bool)p.Value;
                    if (required) return false;
                }
            }
            return true;
        }

        /// <summary>当前 OS 在版本 JSON 里的名字。</summary>
        public static string PlatformName
        {
            get
            {
                try
                {
                    PlatformID p = Environment.OSVersion.Platform;
                    if (p == PlatformID.Win32NT || p == PlatformID.Win32Windows || p == PlatformID.Win32S) return "windows";
                    if (p == PlatformID.MacOSX) return "osx";
                    return "linux";
                }
                catch { return "windows"; }
            }
        }

        private static bool ArchMatches(string osArch)
        {
            string arch = osArch.ToLowerInvariant();
            bool is64 = Environment.Is64BitOperatingSystem;
            string processor = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToLowerInvariant();
            bool isArm64 = processor == "arm64" || processor == "aarch64";
            switch (arch)
            {
                case "x86":
                case "i386":
                case "i686":
                    return !is64;
                case "x86_64":
                case "amd64":
                    return is64 && !isArm64;
                case "arm64":
                case "aarch64":
                    return isArm64;
                default:
                    return true;
            }
        }

        // ==================== 内部辅助 ====================

        private static void AddLibrary(List<LibraryFile> result, Dictionary<string, bool> seen, InstallContext ctx,
            string name, string url, string relPath, string sha1, long size, bool isNative, JObject lib)
        {
            if (string.IsNullOrWhiteSpace(relPath)) return;
            string localPath = Path.Combine(ctx.LibrariesDir, relPath.Replace('/', Path.DirectorySeparatorChar));
            if (seen.ContainsKey(localPath)) return;
            seen[localPath] = true;
            result.Add(new LibraryFile
            {
                Name = name,
                Url = url,
                Path = localPath,
                Sha1 = sha1,
                Size = size,
                IsNative = isNative,
                Library = lib
            });
        }

        private static string BuildUrl(string rootUrl, string relPath)
        {
            string rel = (relPath ?? "").Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(rootUrl)) return rootUrl.TrimEnd('/') + "/" + rel;
            return DefaultMavenRoot + rel;
        }

        private static string AddClassifierToPath(string relPath, string classifier)
        {
            string p = (relPath ?? "").Replace('\\', '/');
            int slash = p.LastIndexOf('/');
            int dot = p.LastIndexOf('.');
            if (dot > slash) return p.Substring(0, dot) + "-" + classifier + p.Substring(dot);
            return p + "-" + classifier;
        }

        private static string NativeClassifier(JObject natives)
        {
            if (natives == null) return null;
            string win = JsonUtil.Str(natives, "windows");
            if (string.IsNullOrEmpty(win))
            {
                // 非 Windows 平台读取对应键（本程序主要跑 Windows，保留兼容）
                if (PlatformName == "osx") win = JsonUtil.Str(natives, "osx");
                else if (PlatformName == "linux") win = JsonUtil.Str(natives, "linux");
            }
            if (string.IsNullOrEmpty(win)) return null;
            return win.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
        }

        /// <summary>解压 natives 压缩包里的动态库到目标目录（含 zip slip 防护与 extract.exclude 支持）。</summary>
        private static int ExtractNatives(LibraryFile file, string nativesDir)
        {
            string rootFull = Path.GetFullPath(nativesDir);
            FileUtil.EnsureDir(rootFull);

            List<string> excludes = new List<string>();
            JObject extract = file.Library == null ? null : file.Library["extract"] as JObject;
            JArray excludeArr = extract == null ? null : extract["exclude"] as JArray;
            if (excludeArr != null)
            {
                foreach (JToken t in excludeArr)
                {
                    string s = (t == null ? "" : t.ToString()).Replace('\\', '/').Trim();
                    if (s.Length > 0) excludes.Add(s);
                }
            }

            int count = 0;
            using (ZipArchive zip = ZipUtil.Open(file.Path))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string name = entry.FullName.Replace('\\', '/');
                    if (name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsExcluded(name, excludes)) continue;
                    if (!IsNativeExtension(Path.GetExtension(name))) continue;

                    string dest = Path.GetFullPath(Path.Combine(rootFull, name.Replace('/', Path.DirectorySeparatorChar)));
                    string rootPrefix = rootFull.EndsWith(Path.DirectorySeparatorChar.ToString()) ? rootFull : rootFull + Path.DirectorySeparatorChar;
                    if (!dest.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Natives 压缩包内路径越界：" + entry.FullName);

                    FileUtil.EnsureDir(Path.GetDirectoryName(dest));
                    if (File.Exists(dest) && FileUtil.SizeOf(dest) == entry.Length) continue;
                    using (Stream src = entry.Open())
                    using (FileStream dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read))
                        src.CopyTo(dst);
                    count++;
                }
            }
            return count;
        }

        private static bool IsExcluded(string name, List<string> excludes)
        {
            if (excludes == null || excludes.Count == 0) return false;
            foreach (string ex in excludes)
            {
                if (string.Equals(name, ex, StringComparison.OrdinalIgnoreCase)) return true;
                string prefix = ex.EndsWith("/") ? ex : ex + "/";
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsNativeExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            switch (ext.ToLowerInvariant())
            {
                case ".dll":
                case ".so":
                case ".dylib":
                case ".jnilib":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>人类可读的大小（用于日志）。</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "未知";
            if (bytes < 1024) return bytes + " B";
            double v = bytes;
            string[] units = { "KB", "MB", "GB", "TB" };
            int i = -1;
            do { v /= 1024.0; i++; } while (v >= 1024 && i < units.Length - 1);
            return v.ToString("0.##") + " " + units[i];
        }
    }
}
