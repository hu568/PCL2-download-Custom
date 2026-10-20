using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>Forge / NeoForge 安装参数。</summary>
    public sealed class ForgelikeInstallOptions
    {
        /// <summary>加载器显示名：Forge / NeoForge。</summary>
        public string LoaderName = "Forge";
        /// <summary>对应的 Minecraft 版本。</summary>
        public string McVersion;
        /// <summary>加载器版本（用于展示 / 日志）。</summary>
        public string LoaderVersion;
        /// <summary>安装器 maven 版本名（Forge：47.2.0；NeoForge：21.1.216 / 1.20.1-47.1.99）。</summary>
        public string ApiVersion;
        /// <summary>安装器下载地址（按优先级）。</summary>
        public string[] InstallerUrls = new string[0];
        /// <summary>安装完成后的版本目录名 / id，例如 1.20.1-Forge_47.2.0。</summary>
        public string TargetId;
        /// <summary>是否把安装器内下载地址改写为 BMCLAPI 镜像（NeoForge 必须开启）。</summary>
        public bool PatchToMirror;
        /// <summary>安装器失败后是否允许改写镜像重试一次。</summary>
        public bool AllowMirrorRetry;
        /// <summary>是否预下载 install_profile.json 中的依赖库（安装器自带下载器对镜像重定向较脆弱）。</summary>
        public bool PrefetchLibraries = true;
        /// <summary>是否优先使用 PCL 程序内嵌的 BMCLAPI 补丁版安装器（失败自动回退官方流程）。</summary>
        public bool UsePclInstaller = true;
    }

    /// <summary>
    /// Forge / NeoForge 安装器的公共流程：
    /// 下载安装器 → 运行 java -jar installer --installClient → 整理版本目录 → 修正 JSON id。
    /// </summary>
    public static class ForgelikeInstaller
    {
        public static async Task<string> RunAsync(InstallContext ctx, ForgelikeInstallOptions opt)
        {
            if (opt == null) throw new ArgumentNullException("opt");
            string name = string.IsNullOrWhiteSpace(opt.LoaderName) ? "Forge" : opt.LoaderName;
            try
            {
                return await RunOnceAsync(ctx, opt).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (!opt.AllowMirrorRetry) throw;
                ctx.Warn(name + " 安装失败：" + ex.Message + "；将改写为 BMCLAPI 镜像后重试一次");
                opt.AllowMirrorRetry = false;
                opt.PatchToMirror = true;
                return await RunOnceAsync(ctx, opt).ConfigureAwait(false);
            }
        }

        private static async Task<string> RunOnceAsync(InstallContext ctx, ForgelikeInstallOptions opt)
        {
            string name = string.IsNullOrWhiteSpace(opt.LoaderName) ? "Forge" : opt.LoaderName;
            if (string.IsNullOrWhiteSpace(opt.McVersion)) throw new Exception("缺少 Minecraft 版本");
            if (string.IsNullOrWhiteSpace(opt.TargetId)) throw new Exception("缺少目标版本 ID");

            // 1. 选择 Java
            ctx.Progress("准备 " + name + " 安装环境", 0.05);
            string javaExe = LoaderCommon.FindJavaForMc(ctx, opt.McVersion);
            if (string.IsNullOrWhiteSpace(javaExe) || !File.Exists(javaExe))
                throw new Exception("未找到可用于安装 " + name + " 的 Java");
            int javaMajor = LoaderCommon.DetectJavaMajor(javaExe);
            int required = LoaderCommon.RequiredJavaMajor(opt.McVersion);
            ctx.Info("使用 Java：" + javaExe + "（Java " + javaMajor + "，最低需要 " + required + "）");

            // 2. 下载安装器
            ctx.Progress("下载 " + name + " 安装器", 0.1);
            string installer = ctx.Scratch(name.ToLowerInvariant() + "-installer.jar");
            await LoaderCommon.DownloadInstallerAsync(ctx, opt.InstallerUrls, installer).ConfigureAwait(false);

            // 2.5 预下载安装器依赖库（并发 + 校验 + 镜像回退）
            if (opt.PrefetchLibraries)
            {
                ctx.Progress("预下载 " + name + " 安装器依赖库", 0.12);
                await LoaderCommon.PrefetchInstallerLibrariesAsync(ctx, installer, ctx.LibrariesDir,
                    "预下载 " + name + " 安装器依赖库", 0.12, 0.45).ConfigureAwait(false);
            }

            // 3. 尝试提取 PCL 内嵌的 BMCLAPI 补丁版安装器（首选方案，见 PclForgeInstaller）
            string pclPatchJar = null;
            if (opt.UsePclInstaller)
            {
                try { pclPatchJar = PclForgeInstaller.ExtractToScratch(ctx); }
                catch (Exception ex) { ctx.Warn("提取 PCL 内嵌补丁安装器失败：" + ex.Message); }
                if (pclPatchJar == null)
                    ctx.Info("未找到 PCL 内嵌补丁安装器，将使用官方安装器流程");
            }

            // 4. 安装目录（MC 路径含空格时改用暂存目录，装完再合并）
            bool staged = ctx.McDir.IndexOf(' ') >= 0 || ctx.McDir.IndexOf('\t') >= 0;
            string installDir = ctx.McDir;
            if (staged)
            {
                installDir = ctx.Scratch("mc-stage");
                FileUtil.EnsureDir(installDir);
                ctx.Info("MC 目录包含空格，使用暂存目录安装：" + installDir);
            }
            LoaderCommon.EnsureLauncherProfiles(ctx, installDir);

            // 5. 运行安装器
            ctx.Progress("运行 " + name + " 安装器（下载依赖库与处理客户端，可能需要几分钟）", 0.58);
            var tail = new Queue<string>();
            int fileCount = 0;
            double lastRatio = 0.58;
            Action<string> onLine = line =>
            {
                ctx.ThrowIfCancelled();
                if (line == null) return;
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                {
                    tail.Enqueue(line);
                    while (tail.Count > 40) tail.Dequeue();
                }
                if (line.Contains("Downloading ") || line.Contains("Extracting") || line.Contains("Patching ") || line.Contains("Building"))
                {
                    fileCount++;
                    double ratio = 0.58 + Math.Min(0.28, fileCount * 0.002);
                    if (ratio > lastRatio + 0.01)
                    {
                        lastRatio = ratio;
                        ctx.Progress("运行 " + name + " 安装器（已处理 " + fileCount + " 个文件）", ratio);
                    }
                }
                else if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                {
                    ctx.Info(line);
                }
            };

            var before = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string versionsRoot = Path.Combine(installDir, "versions");
                if (Directory.Exists(versionsRoot))
                    foreach (var d in Directory.GetDirectories(versionsRoot)) before.Add(Path.GetFileName(d));
            }
            catch { }

            DateTime startTime = DateTime.Now;
            bool installed = false;

            // 5.1 首选：PCL 补丁安装器（镜像注入；镜像失败时 installer 会自行回退官方地址）
            if (!string.IsNullOrWhiteSpace(pclPatchJar))
            {
                try
                {
                    installed = await PclForgeInstaller.RunAsync(ctx, pclPatchJar, installer, installDir,
                        opt.McVersion, onLine, ctx.Ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    ctx.Warn("PCL 补丁安装器运行异常：" + ex.Message);
                    installed = false;
                }
                if (!installed) ctx.Warn("PCL 补丁安装器未成功，回退到官方安装器流程");
            }

            // 5.2 回退：官方 installer --installClient（可按需改写 jar 内地址为镜像）
            if (!installed)
            {
                if (opt.PatchToMirror)
                {
                    try { LoaderCommon.PatchInstallerJar(ctx, installer, LoaderCommon.MirrorReplacements()); }
                    catch (Exception ex) { ctx.Warn("改写安装器内下载地址失败（继续尝试原地址）：" + ex.Message); }
                }
                ctx.Info("开始运行 " + name + " 官方安装器：" + javaExe + " -jar " + Path.GetFileName(installer) + " --installClient " + installDir);
                int exitCode = await LoaderCommon.RunProcessAsync(
                    javaExe,
                    "-jar \"" + installer + "\" --installClient \"" + installDir + "\"",
                    onLine, ctx.Ct, ctx.ScratchDir, 30 * 60 * 1000).ConfigureAwait(false);
                ctx.ThrowIfCancelled();
                if (exitCode != 0)
                    throw new Exception(name + " 安装器退出码 " + exitCode + "，日志结尾：\n" + string.Join("\n", tail.ToArray()));
            }

            // 6. 找到安装器生成的版本目录
            ctx.Progress("整理 " + name + " 安装结果", 0.85);
            string sourceDir = FindGeneratedVersionDir(ctx, installDir, opt, before);
            if (sourceDir == null)
                throw new Exception(name + " 安装器已结束，但未找到生成的版本目录。日志结尾：\n" + string.Join("\n", tail.ToArray()));
            ctx.Info("安装器生成版本目录：" + sourceDir);

            // 7. 移动到目标目录并修正 JSON
            FileUtil.EnsureDir(ctx.VersionsDir);
            string targetDir = Path.Combine(ctx.VersionsDir, opt.TargetId);
            if (staged)
            {
                // 暂存目录中的依赖库合并回真实 MC 目录
                string stageLibs = Path.Combine(installDir, "libraries");
                if (Directory.Exists(stageLibs))
                {
                    ctx.Info("复制依赖库回 MC 目录：" + stageLibs + " → " + ctx.LibrariesDir);
                    FileUtil.CopyDir(stageLibs, ctx.LibrariesDir, true);
                }
            }
            if (Directory.Exists(targetDir))
            {
                ctx.Info("覆盖已存在的版本目录：" + opt.TargetId);
                FileUtil.SafeDeleteDir(targetDir);
            }
            string fullSource = Path.GetFullPath(sourceDir).TrimEnd('\\');
            string fullTarget = Path.GetFullPath(targetDir).TrimEnd('\\');
            if (string.Equals(fullSource, fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                // 安装器直接生成到了目标目录
            }
            else
            {
                try
                {
                    Directory.Move(sourceDir, targetDir);
                }
                catch
                {
                    // 跨盘符时 Directory.Move 会失败，退化为复制后删除
                    FileUtil.CopyDir(sourceDir, targetDir, true);
                    FileUtil.SafeDeleteDir(sourceDir);
                }
            }

            // 8. 修正版本 JSON（id / inheritsFrom）
            string jsonFile = PickVersionJson(targetDir);
            if (jsonFile == null)
                throw new Exception("版本目录中未找到 JSON 文件：" + targetDir);
            JObject versionJson;
            try
            {
                versionJson = LoaderCommon.ReadJson(jsonFile);
            }
            catch (Exception ex)
            {
                throw new Exception("读取安装器生成的版本 JSON 失败：" + ex.Message, ex);
            }
            versionJson["id"] = opt.TargetId;
            if (versionJson["inheritsFrom"] == null || string.IsNullOrWhiteSpace(versionJson["inheritsFrom"].ToString()))
                versionJson["inheritsFrom"] = opt.McVersion;
            string targetJson = Path.Combine(targetDir, opt.TargetId + ".json");
            LoaderCommon.WriteJson(targetJson, versionJson);
            try
            {
                foreach (var other in Directory.GetFiles(targetDir, "*.json"))
                {
                    if (!string.Equals(other, targetJson, StringComparison.OrdinalIgnoreCase))
                        FileUtil.SafeDelete(other);
                }
            }
            catch { }
            ctx.Info("已写入版本配置：" + targetJson + "（id=" + opt.TargetId + "，inheritsFrom=" + versionJson["inheritsFrom"] + "）");

            // 9. 清理安装器
            try { FileUtil.SafeDelete(installer); } catch { }
            if (staged)
            {
                try { FileUtil.SafeDeleteDir(installDir); } catch { }
            }

            // 10. 校验依赖库（安装器通常已全部下载，这里复用 vanilla 的 LibraryInstaller 兜底检查）
            ctx.Progress("校验 " + name + " 依赖库", 0.92);
            try
            {
                await LoaderCommon.DownloadLibrariesAsync(ctx, versionJson, null, "校验 " + name + " 依赖库", 0.92, 0.06).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ctx.Warn("依赖库兜底校验未通过（安装器已下载的库文件通常仍然可用）：" + ex.Message);
            }

            ctx.Progress(name + " 安装完成", 1.0);
            ctx.Info(name + " 安装完成：" + opt.TargetId);
            return opt.TargetId;
        }

        /// <summary>在安装器运行后寻找生成的版本目录（优先新出现的、含 forge 字样的目录）。</summary>
        private static string FindGeneratedVersionDir(InstallContext ctx, string installDir, ForgelikeInstallOptions opt, HashSet<string> before)
        {
            string versionsRoot = Path.Combine(installDir, "versions");
            if (!Directory.Exists(versionsRoot)) return null;
            var scored = new List<KeyValuePair<string, int>>();
            foreach (var dir in Directory.GetDirectories(versionsRoot))
            {
                string dirName = Path.GetFileName(dir);
                string[] jsons;
                try { jsons = Directory.GetFiles(dir, "*.json"); } catch { continue; }
                if (jsons.Length == 0) continue;
                int score = 0;
                if (!before.Contains(dirName)) score += 5;
                if (dirName.IndexOf("forge", StringComparison.OrdinalIgnoreCase) >= 0) score += 3;
                string lower = dirName.ToLowerInvariant();
                string api = (opt.ApiVersion ?? "").ToLowerInvariant();
                if (api.Length > 0)
                {
                    if (lower.Equals((opt.McVersion + "-forge-" + opt.ApiVersion).ToLowerInvariant())) score += 4;
                    if (lower.Equals("forge-" + opt.McVersion + "-" + opt.ApiVersion) || lower.Equals("forge-" + opt.ApiVersion)) score += 4;
                    if (lower.Equals("neoforge-" + opt.ApiVersion)) score += 4;
                    if (lower.Equals((opt.McVersion + "-neoforge-" + opt.ApiVersion).ToLowerInvariant())) score += 4;
                    if (lower.Contains(api)) score += 1;
                }
                if (string.Equals(dirName, opt.TargetId, StringComparison.OrdinalIgnoreCase)) score += 6;
                if (score > 0) scored.Add(new KeyValuePair<string, int>(dir, score));
            }
            if (scored.Count == 0) return null;
            scored.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (scored.Count > 1)
                ctx.Info("检测到多个候选版本目录，选择得分最高的：" + Path.GetFileName(scored[0].Key));
            return scored[0].Key;
        }

        private static string PickVersionJson(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir, "*.json");
                if (files.Length == 0) return null;
                // 优先 id 与目录同名的 json，其次取最新的
                string dirName = Path.GetFileName(dir);
                foreach (var f in files)
                    if (string.Equals(Path.GetFileNameWithoutExtension(f), dirName, StringComparison.OrdinalIgnoreCase))
                        return f;
                return files.OrderByDescending(f => { try { return File.GetLastWriteTime(f); } catch { return DateTime.MinValue; } }).First();
            }
            catch { return null; }
        }
    }
}
