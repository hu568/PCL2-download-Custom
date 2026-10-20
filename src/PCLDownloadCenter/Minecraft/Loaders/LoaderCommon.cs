using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 加载器安装模块的公共辅助方法：原版基础版本检查、版本 JSON 读写、
    /// Java 探测、安装器进程执行、安装器下载与 jar 内地址改写。
    /// </summary>
    public static class LoaderCommon
    {
        // ---------------- 版本目录 ----------------

        public static string VersionDir(InstallContext ctx, string id)
        {
            return Path.Combine(ctx.VersionsDir, id);
        }

        public static string VersionJsonPath(InstallContext ctx, string id)
        {
            return Path.Combine(VersionDir(ctx, id), id + ".json");
        }

        public static bool IsVersionInstalled(InstallContext ctx, string id)
        {
            return !string.IsNullOrWhiteSpace(id) && File.Exists(VersionJsonPath(ctx, id));
        }

        /// <summary>删除某个版本目录（仅用于安装器重新生成时清理旧目录）。</summary>
        public static void DeleteVersionDir(InstallContext ctx, string id)
        {
            var dir = VersionDir(ctx, id);
            if (!Directory.Exists(dir)) return;
            ctx.Info("清理已存在的版本目录：" + id);
            FileUtil.SafeDeleteDir(dir);
        }

        /// <summary>确保原版基础版本已安装（loaders 的版本 JSON 依赖 inheritsFrom）。</summary>
        public static async Task EnsureVanillaAsync(InstallContext ctx, string mcVersion)
        {
            if (IsVersionInstalled(ctx, mcVersion))
            {
                ctx.Info("原版基础版本已安装：" + mcVersion);
                return;
            }
            ctx.Info("未找到原版基础版本 " + mcVersion + "，先安装原版……");
            ctx.Progress("安装原版基础版本 " + mcVersion, 0.03);
            string id;
            try
            {
                id = await VersionManifest.InstallAsync(ctx, mcVersion).ConfigureAwait(false);
            }
            catch (NotImplementedException)
            {
                throw new Exception("原版安装模块尚未实现（vanilla 模块仍在开发中），无法安装加载器");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new Exception("安装原版基础版本 " + mcVersion + " 失败：" + ex.Message, ex);
            }
            ctx.ThrowIfCancelled();
            if (!IsVersionInstalled(ctx, mcVersion))
                throw new Exception("原版基础版本安装后仍未找到 versions\\" + mcVersion + "\\" + mcVersion + ".json");
            ctx.Info("原版基础版本安装完成：" + (string.IsNullOrWhiteSpace(id) ? mcVersion : id));
        }

        // ---------------- JSON ----------------

        public static JObject ReadJson(string path)
        {
            return JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void WriteJson(string path, JObject json)
        {
            FileUtil.EnsureDir(Path.GetDirectoryName(path));
            FileUtil.WriteAllTextAtomic(path, json.ToString(Formatting.Indented));
        }

        // ---------------- 依赖库 ----------------

        /// <summary>调用 vanilla 模块的 LibraryInstaller 下载依赖库（带进度与明确错误）。</summary>
        public static async Task DownloadLibrariesAsync(InstallContext ctx, JObject versionJson, string nativesDir,
            string phase, double progressBase, double progressSpan)
        {
            if (versionJson == null) throw new ArgumentNullException("versionJson");
            phase = string.IsNullOrWhiteSpace(phase) ? "下载依赖库" : phase;
            ctx.Progress(phase, progressBase);
            ctx.Info("开始" + phase + "……");
            try
            {
                await LibraryInstaller.DownloadAsync(ctx, versionJson, nativesDir, (done, total) =>
                {
                    double ratio = total <= 0 ? 1.0 : (double)done / total;
                    if (ratio > 1) ratio = 1;
                    ctx.Progress(phase + "（" + done + "/" + total + "）", progressBase + progressSpan * ratio);
                }).ConfigureAwait(false);
            }
            catch (NotImplementedException)
            {
                throw new Exception("依赖库下载模块尚未实现（vanilla 模块仍在开发中）");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new Exception(phase + "失败：" + ex.Message, ex);
            }
            ctx.ThrowIfCancelled();
            ctx.Info(phase + "完成");
        }

        // ---------------- 安装器下载 ----------------

        /// <summary>依次尝试多个地址下载安装器（失败会整体再来一轮）；成功后返回实际使用的地址。</summary>
        public static async Task<string> DownloadInstallerAsync(InstallContext ctx, string[] urls, string targetPath, long minSize = 64 * 1024)
        {
            Exception last = null;
            var candidates = new List<string>();
            foreach (var raw in urls)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string url = raw.Trim();
                if (!candidates.Any(x => string.Equals(x, url, StringComparison.OrdinalIgnoreCase))) candidates.Add(url);
            }
            // BMCLAPI 镜像后端偶发连接重置，整体重试两轮
            for (int round = 1; round <= 2; round++)
            {
                foreach (var url in candidates)
                {
                    ctx.ThrowIfCancelled();
                    try
                    {
                        ctx.Info("下载安装器（第 " + round + " 轮）：" + url);
                        await ctx.Dl.DownloadFileAsync(url, targetPath, null, -1, null, ctx.Ct, round == 1 ? 2 : 3, true).ConfigureAwait(false);
                        long size = FileUtil.SizeOf(targetPath);
                        if (size < minSize)
                            throw new Exception("文件过小（" + size + " 字节）");
                        ctx.Info("安装器已下载：" + ((size / 1024.0).ToString("0.0")) + " KB");
                        return url;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        last = ex;
                        ctx.Warn("安装器下载失败：" + url + " :: " + ex.Message);
                        FileUtil.SafeDelete(targetPath);
                    }
                }
                if (round == 1) ctx.Warn("所有安装器地址均失败，稍后整体重试一次……");
            }
            throw new Exception("下载加载器安装器失败：" + (last == null ? "没有可用地址" : last.Message), last);
        }

        // ---------------- 安装器依赖库预下载 ----------------

        /// <summary>把 maven 坐标（group:artifact:version[:classifier][@ext]）转换为相对路径。</summary>
        public static string MavenNameToPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string ext = "jar";
            int at = name.IndexOf('@');
            if (at >= 0)
            {
                ext = name.Substring(at + 1).Trim();
                name = name.Substring(0, at);
            }
            var parts = name.Split(':');
            if (parts.Length < 3) return null;
            string group = parts[0].Trim();
            string artifact = parts[1].Trim();
            string version = parts[2].Trim();
            string classifier = parts.Length > 3 ? parts[3].Trim() : null;
            if (group.Length == 0 || artifact.Length == 0 || version.Length == 0) return null;
            string file = artifact + "-" + version + (string.IsNullOrWhiteSpace(classifier) ? "" : "-" + classifier) + "." + ext;
            return group.Replace('.', '/') + "/" + artifact + "/" + version + "/" + file;
        }

        /// <summary>把 NeoForge 官方 maven 地址换成 BMCLAPI 镜像（本机直连 maven.neoforged.net 会超时）。</summary>
        public static string RewriteNeoForgeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (url.IndexOf("maven.neoforged.net", StringComparison.OrdinalIgnoreCase) < 0) return url;
            return url
                .Replace("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven")
                .Replace("https://maven.neoforged.net", "https://bmclapi2.bangbang93.com/maven");
        }

        /// <summary>
        /// 预下载安装器 install_profile.json 中声明的依赖库。
        /// 安装器自带的单线程下载器对镜像重定向较脆弱，先用下载器并发拉取（含校验/重试），
        /// 安装器随后会发现文件已存在并跳过下载。
        /// </summary>
        public static async Task PrefetchInstallerLibrariesAsync(InstallContext ctx, string installerJarPath,
            string librariesDir, string phase, double progressBase, double progressSpan)
        {
            List<DownloadItem> items;
            try
            {
                items = ReadInstallerLibraryItems(installerJarPath, librariesDir);
            }
            catch (Exception ex)
            {
                ctx.Warn("解析安装器依赖库列表失败（跳过预下载）：" + ex.Message);
                return;
            }
            if (items.Count == 0)
            {
                ctx.Info("安装器没有需要预下载的依赖库");
                return;
            }
            ctx.Info("预下载 " + items.Count + " 个安装器依赖库……");
            int total = items.Count;
            int done = 0;
            int threads = ctx.Cfg == null ? 8 : Math.Max(1, Math.Min(16, ctx.Cfg.Threads));
            var failures = new List<string>();
            var sem = new SemaphoreSlim(threads);
            var tasks = new List<Task>();
            try
            {
                foreach (var item in items)
                {
                    await sem.WaitAsync(ctx.Ct).ConfigureAwait(false);
                    ctx.Ct.ThrowIfCancellationRequested();
                    var current = item;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var urls = BuildPrefetchUrls(current.Url);
                            Exception last = null;
                            foreach (var u in urls)
                            {
                                try
                                {
                                    await ctx.Dl.DownloadFileAsync(u, current.Path, current.Sha1, current.Size, null, ctx.Ct, 2, false).ConfigureAwait(false);
                                    last = null;
                                    break;
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex) { last = ex; }
                            }
                            if (last != null)
                            {
                                lock (failures) failures.Add(current.Name + "：" + last.Message);
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            lock (failures) failures.Add(current.Name + "：" + ex.Message);
                        }
                        finally
                        {
                            try { sem.Release(); } catch { }
                            int d = Interlocked.Increment(ref done);
                            if (total > 0)
                                ctx.Progress(phase + "（" + d + "/" + total + "）", progressBase + progressSpan * ((double)d / total));
                        }
                    }, ctx.Ct));
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            finally
            {
                try { sem.Dispose(); } catch { }
            }
            ctx.ThrowIfCancelled();
            if (failures.Count > 0)
            {
                ctx.Warn("有 " + failures.Count + " 个安装器依赖库预下载失败（安装器会自行重试）：" + string.Join("；", failures.Take(5).ToArray()));
            }
            else
            {
                ctx.Info("安装器依赖库预下载完成（" + total + " 个）");
            }
        }

        /// <summary>
        /// 生成预下载候选地址：BMCLAPI 主站 → BMCLAPI 的直连 CDN 镜像（cernet/ustc）→ 官方地址兜底。
        /// BMCLAPI 主站会随机 302 到多个后端，后端偶发连接重置，因此多准备几个直连镜像。
        /// </summary>
        private static List<string> BuildPrefetchUrls(string url)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(url)) return list;
            string mirror = RewriteToMirror(url);
            if (!string.IsNullOrWhiteSpace(mirror) && !string.Equals(mirror, url, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(mirror);
                // BMCLAPI 的 maven 镜像路径：https://bmclapi2.bangbang93.com/maven/<artifactPath>
                const string mavenPrefix = "https://bmclapi2.bangbang93.com/maven/";
                if (mirror.StartsWith(mavenPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string artifactPath = mirror.Substring(mavenPrefix.Length);
                    list.Add("https://mirrors.cernet.edu.cn/bmclapi/" + artifactPath);
                    list.Add("https://cmcc.mirrors.ustc.edu.cn/bmclapi/" + artifactPath);
                }
            }
            if (!list.Any(x => string.Equals(x, url, StringComparison.OrdinalIgnoreCase))) list.Add(url);
            return list;
        }

        /// <summary>把官方 maven 地址改写为 BMCLAPI 镜像地址（maven.neoforged.net / maven.minecraftforge.net / libraries.minecraft.net）。</summary>
        public static string RewriteToMirror(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            const string mirror = "https://bmclapi2.bangbang93.com/maven/";
            if (url.StartsWith("https://maven.minecraftforge.net/", StringComparison.OrdinalIgnoreCase))
                return mirror + url.Substring("https://maven.minecraftforge.net/".Length);
            if (url.StartsWith("https://files.minecraftforge.net/maven/", StringComparison.OrdinalIgnoreCase))
                return mirror + url.Substring("https://files.minecraftforge.net/maven/".Length);
            if (url.StartsWith("https://libraries.minecraft.net/", StringComparison.OrdinalIgnoreCase))
                return mirror + url.Substring("https://libraries.minecraft.net/".Length);
            if (url.StartsWith("https://maven.neoforged.net/releases/", StringComparison.OrdinalIgnoreCase))
                return mirror + url.Substring("https://maven.neoforged.net/releases/".Length);
            if (url.StartsWith("https://maven.neoforged.net/", StringComparison.OrdinalIgnoreCase))
                return mirror + url.Substring("https://maven.neoforged.net/".Length);
            return url;
        }

        private static List<DownloadItem> ReadInstallerLibraryItems(string installerJarPath, string librariesDir)
        {
            var items = new List<DownloadItem>();
            using (var zip = ZipFile.OpenRead(installerJarPath))
            {
                var entry = zip.GetEntry("install_profile.json");
                if (entry == null) return items;
                string text;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    text = reader.ReadToEnd();
                var root = JObject.Parse(text);
                var libs = root["libraries"] as JArray;
                if (libs == null) return items;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var token in libs)
                {
                    var lib = token as JObject;
                    if (lib == null) continue;
                    var artifact = lib.SelectToken("downloads.artifact") as JObject;
                    if (artifact == null) continue;
                    string path = JsonUtil.Str(artifact, "path");
                    string url = JsonUtil.Str(artifact, "url");
                    if (string.IsNullOrWhiteSpace(path))
                        path = MavenNameToPath(JsonUtil.Str(lib, "name"));
                    if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(url)) continue;
                    string local = Path.Combine(librariesDir, path.Replace('/', Path.DirectorySeparatorChar));
                    if (!seen.Add(local)) continue;
                    items.Add(new DownloadItem(url, local, JsonUtil.Str(artifact, "sha1"), JsonUtil.Long(artifact, "size", -1), Path.GetFileName(local)));
                }
            }
            return items;
        }

        /// <summary>
        /// 改写安装器 jar 内 install_profile.json / version.json 中的下载地址（NeoForge 官方 maven 本机不可达，必须改写为镜像）。
        /// 采用 ZipArchive 就地更新，保留其余条目与清单。
        /// </summary>
        public static bool PatchInstallerJar(InstallContext ctx, string jarPath, IDictionary<string, string> replacements)
        {
            if (replacements == null || replacements.Count == 0) return false;
            bool changed = false;
            using (var zip = ZipFile.Open(jarPath, ZipArchiveMode.Update))
            {
                foreach (var name in new[] { "install_profile.json", "version.json" })
                {
                    var entry = zip.GetEntry(name);
                    if (entry == null) continue;
                    string text;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                        text = reader.ReadToEnd();
                    string patched = text;
                    foreach (var kv in replacements)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        patched = patched.Replace(kv.Key, kv.Value ?? "");
                    }
                    if (string.Equals(patched, text, StringComparison.Ordinal)) continue;
                    entry.Delete();
                    var newEntry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(false)))
                        writer.Write(patched);
                    changed = true;
                    ctx.Info("已改写安装器内下载地址：" + name);
                }
            }
            return changed;
        }

        /// <summary>把官方 maven 地址改写为 BMCLAPI 镜像的替换表。</summary>
        public static Dictionary<string, string> MirrorReplacements(params string[] nope)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven" },
                { "https://maven.neoforged.net", "https://bmclapi2.bangbang93.com/maven" },
                { "https://mirrors.neoforged.net", "https://bmclapi2.bangbang93.com/maven" },
                { "https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven" },
                { "https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven" },
                { "https://files.minecraftforge.net/maven", "https://bmclapi2.bangbang93.com/maven" }
            };
        }

        /// <summary>在 MC 目录创建 launcher_profiles.json（Forge/NeoForge 安装器需要）。</summary>
        public static void EnsureLauncherProfiles(InstallContext ctx, string mcDir)
        {
            try
            {
                var path = Path.Combine(mcDir, "launcher_profiles.json");
                if (File.Exists(path)) return;
                string json = "{\r\n" +
                    "  \"profiles\": {\r\n" +
                    "    \"PCL\": {\r\n" +
                    "      \"icon\": \"Grass\",\r\n" +
                    "      \"name\": \"PCL\",\r\n" +
                    "      \"lastVersionId\": \"latest-release\",\r\n" +
                    "      \"type\": \"latest-release\",\r\n" +
                    "      \"lastUsed\": \"" + DateTime.Now.ToString("yyyy-MM-dd") + "T" + DateTime.Now.ToString("HH:mm:ss") + ".0000Z\"\r\n" +
                    "    }\r\n" +
                    "  },\r\n" +
                    "  \"selectedProfile\": \"PCL\",\r\n" +
                    "  \"clientToken\": \"23323323323323323323323323323333\"\r\n" +
                    "}";
                FileUtil.EnsureDir(mcDir);
                File.WriteAllText(path, json, new UTF8Encoding(false));
                ctx.Info("已创建 launcher_profiles.json：" + path);
            }
            catch (Exception ex)
            {
                ctx.Warn("创建 launcher_profiles.json 失败：" + ex.Message);
            }
        }

        // ---------------- Java ----------------

        private static readonly ConcurrentDictionary<string, int> JavaVersionCache =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly object DiscoverLock = new object();
        private static List<string> _discoveredJava;

        private static readonly string[] JavaVendorHints =
        {
            "java", "jdk", "jre", "adoptium", "temurin", "corretto", "zulu", "graal", "semeru", "liberica", "dragonwell", "openjdk"
        };

        /// <summary>根据 MC 版本推断运行安装器所需的 Java 主版本。</summary>
        public static int RequiredJavaMajor(string mcVersion)
        {
            var nums = ParseVersionNumbers(mcVersion);
            if (nums == null || nums.Count < 2) return 21;
            if (nums[0] == 1)
            {
                int minor = nums[1];
                int patch = nums.Count > 2 ? nums[2] : 0;
                if (minor > 20 || (minor == 20 && patch >= 5)) return 21;
                if (minor >= 18) return 17;
                if (minor == 17) return 16;
                return 8;
            }
            return 21; // 26.x 等新版本号命名
        }

        private static List<int> ParseVersionNumbers(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;
            var list = new List<int>();
            foreach (var part in version.Trim().Split('.'))
            {
                int v;
                if (!int.TryParse(part.Trim(), out v)) break;
                list.Add(v);
                if (list.Count >= 4) break;
            }
            return list.Count == 0 ? null : list;
        }

        /// <summary>探测所有可用的 java.exe（配置、JavaLocator、PATH、JAVA_HOME、runtime、Program Files）。</summary>
        public static List<string> DiscoverJavaExes(InstallContext ctx)
        {
            lock (DiscoverLock)
            {
                if (_discoveredJava != null) return new List<string>(_discoveredJava);
                var list = new List<string>();
                Action<string> add = p =>
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(p)) return;
                        p = p.Trim().Trim('"');
                        if (!File.Exists(p)) return;
                        if (!list.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) list.Add(p);
                    }
                    catch { }
                };

                // 1. 用户配置的 Java 路径
                try
                {
                    if (ctx != null && ctx.Cfg != null && !string.IsNullOrWhiteSpace(ctx.Cfg.JavaPath))
                        add(ctx.Cfg.JavaPath);
                }
                catch { }

                // 2. vanilla 的 JavaLocator（若尚未实现则返回空列表，不会抛异常）
                try
                {
                    var all = JavaLocator.FindAll();
                    if (all != null) foreach (var j in all) add(j);
                }
                catch { }
                try
                {
                    add(JavaLocator.FindJava(8));
                    add(JavaLocator.FindJava(17));
                    add(JavaLocator.FindJava(21));
                }
                catch { }

                // 3. PATH
                try
                {
                    var path = Environment.GetEnvironmentVariable("PATH");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        foreach (var dir in path.Split(';'))
                        {
                            if (string.IsNullOrWhiteSpace(dir)) continue;
                            try { add(Path.Combine(dir.Trim().Trim('"'), "java.exe")); } catch { }
                        }
                    }
                }
                catch { }
                try
                {
                    var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                    if (!string.IsNullOrWhiteSpace(javaHome)) add(Path.Combine(javaHome, "bin", "java.exe"));
                }
                catch { }

                // 4. Minecraft runtime 目录（PCL 启动器下载的 Java）
                var runtimeDirs = new List<string>();
                try { if (ctx != null && !string.IsNullOrWhiteSpace(ctx.McDir)) runtimeDirs.Add(Path.Combine(ctx.McDir, "runtime")); } catch { }
                try { runtimeDirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime")); } catch { }
                foreach (var runtimeDir in runtimeDirs)
                {
                    try
                    {
                        if (!Directory.Exists(runtimeDir)) continue;
                        foreach (var dir in Directory.GetDirectories(runtimeDir))
                        {
                            add(Path.Combine(dir, "bin", "java.exe"));
                            try
                            {
                                foreach (var sub in Directory.GetDirectories(dir))
                                    add(Path.Combine(sub, "bin", "java.exe"));
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                // 5. Program Files 下的常见 JDK
                var roots = new List<string>();
                try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch { }
                try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch { }
                foreach (var root in roots)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
                        foreach (var dir in Directory.GetDirectories(root))
                        {
                            string name;
                            try { name = Path.GetFileName(dir).ToLowerInvariant(); } catch { continue; }
                            if (string.IsNullOrEmpty(name)) continue;
                            if (!JavaVendorHints.Any(h => name.Contains(h))) continue;
                            AddJavaUnder(dir, add, 2);
                        }
                    }
                    catch { }
                }

                _discoveredJava = list;
                return new List<string>(list);
            }
        }

        private static void AddJavaUnder(string dir, Action<string> add, int depth)
        {
            try
            {
                add(Path.Combine(dir, "bin", "java.exe"));
                if (depth <= 0) return;
                foreach (var sub in Directory.GetDirectories(dir))
                    AddJavaUnder(sub, add, depth - 1);
            }
            catch { }
        }

        /// <summary>读取 java.exe 的主版本号；失败返回 -1。</summary>
        public static int DetectJavaMajor(string javaExe)
        {
            if (string.IsNullOrWhiteSpace(javaExe)) return -1;
            return JavaVersionCache.GetOrAdd(javaExe, p =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = p,
                        Arguments = "-version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using (var proc = Process.Start(psi))
                    {
                        if (proc == null) return -1;
                        string err = proc.StandardError.ReadToEnd();
                        string output = proc.StandardOutput.ReadToEnd();
                        try { proc.WaitForExit(15000); } catch { }
                        var m = Regex.Match(err + "\n" + output, "version\\s+\"(\\d+)(?:\\.(\\d+))?");
                        if (!m.Success) return -1;
                        int first = int.Parse(m.Groups[1].Value);
                        if (first == 1 && m.Groups[2].Success) return int.Parse(m.Groups[2].Value);
                        return first;
                    }
                }
                catch { return -1; }
            });
        }

        /// <summary>
        /// 为安装器选择 Java：优先用户配置，其次 JavaLocator，最后自行探测；
        /// 选择满足最低版本要求的最低主版本（避免用 Java 25 跑老安装器）。
        /// </summary>
        public static string FindJavaForMc(InstallContext ctx, string mcVersion)
        {
            int required = RequiredJavaMajor(mcVersion);
            var candidates = new List<KeyValuePair<string, int>>();
            foreach (var exe in DiscoverJavaExes(ctx))
            {
                int major = DetectJavaMajor(exe);
                if (major > 0) candidates.Add(new KeyValuePair<string, int>(exe, major));
            }
            if (candidates.Count == 0)
                throw new Exception("未探测到任何可用的 Java，请先安装 Java " + required + " 或更高版本（或在设置中填写 Java 路径）");

            if (ctx != null) ctx.Info("Java 探测结果：" + string.Join("；", candidates.Select(c => c.Key + "（Java " + c.Value + "）")));

            // 用户指定优先
            try
            {
                if (ctx != null && ctx.Cfg != null && !string.IsNullOrWhiteSpace(ctx.Cfg.JavaPath))
                {
                    var hit = candidates.FirstOrDefault(c => string.Equals(c.Key, ctx.Cfg.JavaPath.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase));
                    if (hit.Key != null && hit.Value >= required) return hit.Key;
                }
            }
            catch { }

            // JavaLocator 结果优先（它是 vanilla 模块的推荐逻辑）
            try
            {
                var guess = JavaLocator.FindJavaForVersion(required);
                if (!string.IsNullOrWhiteSpace(guess))
                {
                    var hit = candidates.FirstOrDefault(c => string.Equals(c.Key, guess, StringComparison.OrdinalIgnoreCase));
                    if (hit.Key != null && hit.Value >= required) return hit.Key;
                }
            }
            catch { }

            var ok = candidates.Where(c => c.Value >= required).OrderBy(c => c.Value).ToList();
            if (ok.Count > 0) return ok[0].Key;

            var best = candidates.OrderByDescending(c => c.Value).First();
            if (ctx != null) ctx.Warn("未找到 Java " + required + " 或更高版本，尝试使用 Java " + best.Value + "：" + best.Key);
            return best.Key;
        }

        // ---------------- 进程执行 ----------------

        /// <summary>执行外部进程，逐行回调输出；支持取消与超时。返回退出码。</summary>
        public static Task<int> RunProcessAsync(string exe, string arguments, Action<string> onLine,
            CancellationToken ct, string workingDir = null, int timeoutMs = 0)
        {
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (!string.IsNullOrWhiteSpace(workingDir)) psi.WorkingDirectory = workingDir;

            Process proc = new Process();
            proc.StartInfo = psi;
            proc.EnableRaisingEvents = true;
            Action<string> emit = line =>
            {
                if (onLine == null || line == null) return;
                try { onLine(line); } catch { }
            };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) emit(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) emit(e.Data); };
            proc.Exited += (s, e) =>
            {
                try { tcs.TrySetResult(proc.ExitCode); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };

            CancellationTokenRegistration reg = default(CancellationTokenRegistration);
            Timer timer = null;
            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                if (ct.CanBeCanceled)
                {
                    reg = ct.Register(() =>
                    {
                        try { if (!proc.HasExited) proc.Kill(); } catch { }
                        tcs.TrySetCanceled();
                    });
                }
                if (timeoutMs > 0)
                {
                    timer = new Timer(_ =>
                    {
                        try { if (!proc.HasExited) proc.Kill(); } catch { }
                        tcs.TrySetException(new TimeoutException("安装器执行超时（" + (timeoutMs / 1000) + " 秒）"));
                    }, null, timeoutMs, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                try { proc.Dispose(); } catch { }
                tcs.TrySetException(ex);
            }
            return tcs.Task.ContinueWith(t =>
            {
                try { reg.Dispose(); } catch { }
                try { if (timer != null) timer.Dispose(); } catch { }
                try { proc.Dispose(); } catch { }
                return t.GetAwaiter().GetResult();
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        // ---------------- 版本号排序 ----------------

        /// <summary>比较两个加载器版本号（尽量按数字段比较，兼容 47.2.0 / 21.1.216 / 0.20.0-beta.9）。</summary>
        public static int CompareVersions(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.IsNullOrWhiteSpace(a)) return -1;
            if (string.IsNullOrWhiteSpace(b)) return 1;
            var pa = SplitVersion(a);
            var pb = SplitVersion(b);
            int n = Math.Max(pa.Count, pb.Count);
            for (int i = 0; i < n; i++)
            {
                string x = i < pa.Count ? pa[i] : "0";
                string y = i < pb.Count ? pb[i] : "0";
                int ix, iy;
                bool nx = int.TryParse(x, out ix);
                bool ny = int.TryParse(y, out iy);
                if (nx && ny)
                {
                    if (ix != iy) return ix.CompareTo(iy);
                }
                else
                {
                    int c = string.CompareOrdinal(x, y);
                    if (c != 0) return c;
                }
            }
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> SplitVersion(string version)
        {
            var list = new List<string>();
            foreach (var token in version.Split('.', '-', '+', '_'))
            {
                if (token.Length == 0) continue;
                if (int.TryParse(token, out _)) list.Add(token);
                else break;
            }
            if (list.Count == 0) list.Add("0");
            return list;
        }
    }
}
