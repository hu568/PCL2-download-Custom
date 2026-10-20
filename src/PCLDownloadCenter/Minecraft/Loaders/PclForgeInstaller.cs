using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PCLDownloadCenter
{
    /// <summary>
    /// PCL 程序本体中内嵌了一个「BMCLAPI 补丁版」Forge/NeoForge 安装器
    /// （资源名 ForgeInstaller，类 com.bangbang93.ForgeInstaller）。
    /// 它会给官方 installer 注入 Mirror = http://bmclapi.bangbang93.com/maven/，
    /// 从而让 libraries 优先走镜像（镜像失败时 installer 会自行回退官方地址）。
    /// 运行方式（与 PCL 一致）：
    ///   java [--add-exports ...] -cp "&lt;补丁jar&gt;;&lt;官方installer.jar&gt;" com.bangbang93.ForgeInstaller "&lt;McDir&gt;"
    /// 成功判据：输出最后 5 行里有一行是 "true"。
    /// </summary>
    public static class PclForgeInstaller
    {
        private sealed class CacheEntry
        {
            public string Key;
            public byte[] Bytes;
        }

        private static readonly object Sync = new object();
        private static CacheEntry _cache;

        /// <summary>固定文件名（按 PCL / PCL CE 常见命名精确匹配，避免加载无关 exe）。</summary>
        private static readonly string[] CandidateFileNames =
        {
            "Plain Craft Launcher 2.exe",
            "PCL2_CE.exe",
            "Plain Craft Launcher 2 CE.exe",
            "PCL2.exe"
        };

        /// <summary>尝试从 PCL 程序本体提取补丁安装器；成功返回 true 并输出字节。</summary>
        public static bool TryExtract(InstallContext ctx, out byte[] bytes)
        {
            bytes = null;
            foreach (var exe in CandidateExePaths(ctx))
            {
                string key = BuildCacheKey(exe);
                lock (Sync)
                {
                    if (_cache != null && string.Equals(_cache.Key, key, StringComparison.Ordinal))
                    {
                        bytes = _cache.Bytes;
                        return true;
                    }
                }
                try
                {
                    var data = ExtractFromExe(exe, ctx);
                    if (data == null) continue;
                    lock (Sync) _cache = new CacheEntry { Key = key, Bytes = data };
                    if (ctx != null)
                    {
                        string version = "";
                        try
                        {
                            var vi = FileVersionInfo.GetVersionInfo(exe);
                            if (vi != null && !string.IsNullOrWhiteSpace(vi.FileVersion)) version = "，版本 " + vi.FileVersion;
                        }
                        catch { }
                        long size = -1;
                        try { size = new FileInfo(exe).Length; } catch { }
                        ctx.Info("已从 PCL 程序提取补丁版加载器安装器：" + exe + "（" + data.Length + " 字节" + version +
                                 (size > 0 ? "，文件 " + (size / 1024) + " KB" : "") + "）");
                    }
                    bytes = data;
                    return true;
                }
                catch (Exception ex)
                {
                    if (ctx != null) ctx.Warn("提取 PCL 内嵌补丁安装器失败（" + exe + "）：" + ex.Message);
                }
            }
            if (ctx != null)
            {
                ctx.Info("未找到可用的 PCL 内嵌补丁安装器，将回退到官方 installer --installClient 流程" +
                         "（若官方安装器也不可用，可手动运行 Forge/NeoForge 官方安装器）。");
            }
            return false;
        }

        /// <summary>把补丁安装器写入任务临时目录；失败返回 null。</summary>
        public static string ExtractToScratch(InstallContext ctx)
        {
            byte[] bytes;
            if (!TryExtract(ctx, out bytes)) return null;
            try
            {
                string path = ctx.Scratch("pcl-forge-installer.jar");
                FileUtil.WriteBytesAtomic(path, bytes);
                return path;
            }
            catch (Exception ex)
            {
                if (ctx != null) ctx.Warn("写入补丁安装器失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>用补丁安装器安装。返回 true 表示成功。</summary>
        public static async Task<bool> RunAsync(InstallContext ctx, string patchJar, string officialInstallerJar,
            string mcDir, string mcVersion, Action<string> onLine, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(patchJar) || !File.Exists(patchJar)) return false;
            if (string.IsNullOrWhiteSpace(officialInstallerJar) || !File.Exists(officialInstallerJar)) return false;

            string javaExe = LoaderCommon.FindJavaForMc(ctx, mcVersion);
            if (string.IsNullOrWhiteSpace(javaExe) || !File.Exists(javaExe)) return false;
            int javaMajor = LoaderCommon.DetectJavaMajor(javaExe);

            string classPath = "\"" + patchJar + "\";\"" + officialInstallerJar + "\"";
            string arguments = (javaMajor >= 9 ? "--add-exports cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED " : "")
                               + "-cp " + classPath + " com.bangbang93.ForgeInstaller \"" + mcDir + "\"";
            ctx.Info("使用 PCL 补丁安装器：java " + (javaMajor >= 9 ? "（Java " + javaMajor + "，含 add-exports）" : "（Java " + javaMajor + "）"));

            var tail = new Queue<string>();
            Action<string> lineHandler = line =>
            {
                ctx.ThrowIfCancelled();
                if (line == null) return;
                tail.Enqueue(line);
                while (tail.Count > 60) tail.Dequeue();
                if (onLine != null) { try { onLine(line); } catch { } }
            };

            int exitCode;
            try
            {
                exitCode = await LoaderCommon.RunProcessAsync(javaExe, arguments, lineHandler, ct, ctx.ScratchDir, 30 * 60 * 1000).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (TimeoutException ex)
            {
                ctx.Warn("PCL 补丁安装器执行超时：" + ex.Message);
                return false;
            }
            ctx.ThrowIfCancelled();

            bool printedTrue = tail.Reverse().Take(5).Any(l => string.Equals((l ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase));
            if (exitCode == 0 && printedTrue)
            {
                ctx.Info("PCL 补丁安装器报告成功（输出包含 true）");
                return true;
            }
            ctx.Warn("PCL 补丁安装器未报告成功（退出码 " + exitCode + "，最后输出：" +
                     string.Join(" | ", tail.Reverse().Take(5).ToArray()) + "）");
            return false;
        }

        // ---------------- 提取实现 ----------------

        private static IEnumerable<string> CandidateExePaths(InstallContext ctx)
        {
            var dirs = new List<string>();
            Action<string> addDir = d =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(d)) return;
                    d = d.TrimEnd('\\', '/');
                    if (d.Length == 0 || !Directory.Exists(d)) return;
                    if (!dirs.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase))) dirs.Add(d);
                }
                catch { }
            };

            // 插件部署在 PCL\DownloadCenter 时，Paths.BaseDir 就是 PCL 根目录
            try { addDir(Paths.BaseDir); } catch { }
            // 测试/非标准部署时，从 MC 目录反推 PCL 根目录（<PCL根>\.minecraft）
            try
            {
                if (ctx != null && !string.IsNullOrWhiteSpace(ctx.McDir))
                {
                    var parent = Path.GetDirectoryName(ctx.McDir.TrimEnd('\\', '/'));
                    addDir(parent);
                    try { addDir(Path.GetDirectoryName(parent)); } catch { }
                }
            }
            catch { }
            // PCL 的 PCL 文件夹的上一级（与 BaseDir 通常相同）
            try { addDir(Path.GetDirectoryName(Paths.PclDir.TrimEnd('\\', '/'))); } catch { }

            var result = new List<string>();
            foreach (var dir in dirs)
            {
                foreach (var fileName in CandidateFileNames)
                {
                    string exe = Path.Combine(dir, fileName);
                    if (File.Exists(exe) && !result.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase)))
                        result.Add(exe);
                }
            }
            return result;
        }

        /// <summary>按 (路径 + 修改时间 + 大小) 生成缓存键，PCL 更新后自动失效。</summary>
        private static string BuildCacheKey(string exePath)
        {
            try
            {
                var fi = new FileInfo(exePath);
                return exePath.ToLowerInvariant() + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { return (exePath ?? "").ToLowerInvariant(); }
        }

        /// <summary>
        /// 读取 PCL exe 字节后再 Assembly.Load(byte[])：不会锁定 PCL 主程序文件（PCL 自更新需要替换 exe）。
        /// </summary>
        private static byte[] ExtractFromExe(string exePath, InstallContext ctx)
        {
            byte[] fileBytes;
            try { fileBytes = File.ReadAllBytes(exePath); }
            catch (Exception ex)
            {
                if (ctx != null) ctx.Warn("读取 PCL 主程序失败（" + exePath + "）：" + ex.Message);
                return null;
            }

            Assembly asm;
            try { asm = Assembly.Load(fileBytes); }
            catch (Exception ex)
            {
                if (ctx != null) ctx.Warn("加载 PCL 程序集失败（" + exePath + "）：" + ex.GetType().Name + " " + ex.Message);
                return null;
            }

            var baseNames = new List<string> { "PCL.Resources" };
            try
            {
                string asmName = asm.GetName().Name;
                if (!string.IsNullOrWhiteSpace(asmName)) baseNames.Add(asmName + ".Resources");
            }
            catch { }

            foreach (var baseName in baseNames)
            {
                try
                {
                    var rm = new System.Resources.ResourceManager(baseName, asm);
                    object obj = rm.GetObject("ForgeInstaller");
                    var bytes = obj as byte[];
                    if (bytes != null && bytes.Length > 1024 && IsPatchJar(bytes)) return bytes;
                }
                catch { }
            }
            if (ctx != null) ctx.Warn("PCL 主程序中未找到 ForgeInstaller 资源（可能是 CE/精简版）：" + exePath);
            return null;
        }

        private static bool IsPatchJar(byte[] bytes)
        {
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    return zip.GetEntry("com/bangbang93/ForgeInstaller.class") != null;
                }
            }
            catch { return false; }
        }
    }
}
