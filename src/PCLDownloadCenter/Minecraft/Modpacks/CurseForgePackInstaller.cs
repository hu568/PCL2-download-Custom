using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// CurseForge 整合包（zip + manifest.json）安装。
    /// 文件下载依赖 CurseForge API（App.Config.CurseForgeApiKey）或第三方镜像；
    /// 两者都不可用时给出可操作的错误提示，并回滚不完整实例。
    /// </summary>
    internal static class CurseForgePackInstaller
    {
        private sealed class PendingFile
        {
            public List<string> Candidates;
            public string Path;
            public string Sha1;
            public long Size;
            public string Display;
        }

        public static async Task<string> InstallAsync(InstallContext ctx, string archivePath, PackSourceInfo info)
        {
            if (!File.Exists(archivePath)) throw new Exception("找不到整合包文件：" + archivePath);
            info = info ?? new PackSourceInfo { Source = "CurseForge" };

            var baseFolder = PackCore.FindEntryBaseFolder(archivePath, "manifest.json");
            if (baseFolder == null)
                throw new Exception("该文件不是有效的 CurseForge 整合包（压缩包内缺少 manifest.json）");

            ctx.Progress("读取 CurseForge 整合包信息", 0.10);
            var manifestText = PackCore.ReadEntryText(archivePath, baseFolder + "manifest.json");
            if (string.IsNullOrWhiteSpace(manifestText))
                throw new Exception("无法读取 CurseForge 整合包的 manifest.json（文件为空）");
            JObject manifest;
            try { manifest = JObject.Parse(manifestText); }
            catch (Exception ex) { throw new Exception("CurseForge 整合包的 manifest.json 格式错误：" + ex.Message, ex); }

            var minecraft = manifest["minecraft"] as JObject;
            if (minecraft == null || minecraft["version"] == null)
                throw new Exception("CurseForge 整合包未提供 Minecraft 版本信息（minecraft.version）");
            var mcVersion = minecraft["version"].ToString().Trim();

            // ---------- 加载器 ----------
            string loader = null;
            string loaderVersion = null;
            var modLoaders = minecraft["modLoaders"] as JArray;
            if (modLoaders != null)
            {
                JObject chosen = null;
                foreach (var token in modLoaders)
                {
                    var item = token as JObject;
                    if (item == null) continue;
                    if (JsonUtil.Bool(item, "primary", false)) { chosen = item; break; }
                    if (chosen == null) chosen = item;
                }
                if (chosen != null)
                {
                    var loaderId = JsonUtil.Str(chosen, "id", "");
                    ParseLoaderId(loaderId, out loader, out loaderVersion);
                }
            }

            // ---------- 名称与文件清单 ----------
            var rawName = JsonUtil.Str(manifest, "name", null);
            if (info.OverrideName && !string.IsNullOrWhiteSpace(info.Name)) rawName = info.Name;
            if (string.IsNullOrWhiteSpace(rawName)) rawName = info.Name;
            if (string.IsNullOrWhiteSpace(rawName)) rawName = Path.GetFileNameWithoutExtension(archivePath);
            var versionLabel = JsonUtil.Str(manifest, "version", info.VersionLabel);

            var requested = new List<CfRequestedFile>();
            var filesArray = manifest["files"] as JArray;
            if (filesArray != null)
            {
                foreach (var token in filesArray)
                {
                    var item = token as JObject;
                    if (item == null) continue;
                    var projectId = JsonUtil.Long(item, "projectID", -1);
                    var fileId = JsonUtil.Long(item, "fileID", -1);
                    if (projectId < 0 || fileId < 0)
                    {
                        ctx.Warn("整合包存在缺少 projectID/fileID 的文件项，已跳过");
                        continue;
                    }
                    requested.Add(new CfRequestedFile
                    {
                        ProjectId = projectId,
                        FileId = fileId,
                        Required = JsonUtil.Bool(item, "required", true)
                    });
                }
            }

            ctx.Info("整合包：" + rawName);
            ctx.Info("游戏版本：" + mcVersion + (loader == null ? "（无加载器）" : "，加载器：" + PackCore.LoaderDisplay(loader) + " " + loaderVersion));
            if (!string.IsNullOrWhiteSpace(versionLabel)) ctx.Info("整合包版本号：" + versionLabel);
            ctx.Info("整合包声明了 " + requested.Count + " 个待下载文件");

            // ---------- 安装原版 + 加载器（装到临时目录，稍后合并成单版本） ----------
            var baseInstall = await PackCore.InstallBaseStagedAsync(ctx, mcVersion, loader, loaderVersion, 0.12, 0.58)
                .ConfigureAwait(false);

            // ---------- 创建实例（原版 + 加载器合并为一个版本 JSON + 客户端 jar） ----------
            var instanceName = PackCore.AllocateInstanceName(ctx, rawName);
            var instanceDir = Path.Combine(ctx.VersionsDir, instanceName);
            var created = false;
            try
            {
                FileUtil.EnsureDir(instanceDir);
                created = true;
                PackCore.MarkInstalling(instanceDir);
                PackCore.WriteMergedInstance(ctx, baseInstall, instanceDir, instanceName, versionLabel);

                // ---------- overrides ----------
                ctx.Progress("解压整合包覆写文件", 0.60);
                var overrideFolder = JsonUtil.Str(manifest, "overrides", null);
                var overridesCount = 0;
                if (string.IsNullOrWhiteSpace(overrideFolder))
                {
                    ctx.Info("整合包没有声明 overrides 覆写目录");
                }
                else
                {
                    var normalized = overrideFolder.Replace('\\', '/').Trim();
                    if (normalized == "." || normalized == "./")
                        overridesCount = ZipUtil.ExtractPrefix(archivePath, baseFolder, instanceDir);
                    else
                        overridesCount = ZipUtil.ExtractPrefix(archivePath, baseFolder + normalized.TrimEnd('/') + "/", instanceDir);
                    ctx.Info(overridesCount > 0
                        ? "已解压 " + overridesCount + " 个覆写文件（" + overrideFolder + "）"
                        : "overrides 目录为空或不存在");
                }

                // 覆写文件可能自带 PCL\Setup.ini，因此在解压之后再合并写入版本隔离等必要键
                var customInfo = (string.IsNullOrWhiteSpace(info.Source) ? "整合包" : info.Source) +
                                 (string.IsNullOrWhiteSpace(versionLabel) ? "" : " · " + versionLabel) +
                                 "（MC " + mcVersion +
                                 (loader == null ? "" : " / " + PackCore.LoaderDisplay(loader)) + "）";
                PackCore.WriteInstanceSetup(instanceDir, mcVersion, customInfo, info, versionLabel, loader, loaderVersion, ctx);

                // ---------- 下载 manifest files[] ----------
                if (requested.Count > 0)
                {
                    ctx.Progress("获取 CurseForge 文件下载地址", 0.62);
                    var fileInfos = await CurseForgeApi.GetFilesByIdsAsync(ctx, requested.Select(f => f.FileId).ToList())
                        .ConfigureAwait(false);
                    var map = new Dictionary<long, CfFile>();
                    foreach (var file in fileInfos)
                        if (!map.ContainsKey(file.Id)) map[file.Id] = file;

                    var pending = new List<PendingFile>();
                    var missingRequired = new List<long>();
                    var missingOptional = new List<long>();
                    foreach (var request in requested)
                    {
                        CfFile file;
                        if (!map.TryGetValue(request.FileId, out file))
                        {
                            if (request.Required) missingRequired.Add(request.FileId);
                            else missingOptional.Add(request.FileId);
                            continue;
                        }
                        var targetFolder = PickTargetFolder(file);
                        var fileName = PackCore.SafeFileName(file.FileName, "file-" + request.FileId.ToString());
                        var local = PackCore.ResolveInside(instanceDir, targetFolder + "/" + fileName);
                        pending.Add(new PendingFile
                        {
                            Candidates = CurseForgeApi.DownloadCandidates(file),
                            Path = local,
                            Sha1 = file.Sha1,
                            Size = file.Length,
                            Display = targetFolder + "/" + fileName
                        });
                    }
                    if (missingOptional.Count > 0)
                        ctx.Warn("有 " + missingOptional.Count + " 个可选文件在 CurseForge 上已不可用，已跳过");
                    if (missingRequired.Count > 0)
                        throw new Exception("CurseForge 整合包有 " + missingRequired.Count + " 个必需文件在 CurseForge 上已被删除或不可访问（文件 ID：" +
                                            string.Join("、", missingRequired.Take(5)) + (missingRequired.Count > 5 ? "……" : "") +
                                            "），请换用更新版本的整合包。");

                    ctx.Info("开始下载 " + pending.Count + " 个整合包文件……");
                    await DownloadAllAsync(ctx, pending, 0.62, 0.96).ConfigureAwait(false);
                }

                await PackCore.TrySetIconAsync(ctx, instanceDir, info.IconUrl).ConfigureAwait(false);
                PackCore.FinishInstalling(instanceDir);

                var jarCount = CountJars(Path.Combine(instanceDir, "mods"));
                ctx.Info("安装完成：versions/" + instanceName + "（mods 目录 " + jarCount + " 个 jar 文件；已合并原版与加载器，versions 下只有这一个版本）");
                ctx.Progress("整合包安装完成", 1.0);
                return instanceName;
            }
            catch
            {
                if (created) PackCore.Rollback(ctx, instanceDir, instanceName);
                throw;
            }
            finally
            {
                PackCore.CleanupStage(ctx, baseInstall);
            }
        }

        /// <summary>解析 CurseForge 的加载器 ID，如 forge-47.2.0 / neoforge-21.1.80 / fabric-0.16.9。</summary>
        private static void ParseLoaderId(string loaderId, out string loader, out string version)
        {
            loader = null;
            version = null;
            if (string.IsNullOrWhiteSpace(loaderId)) return;
            var text = loaderId.Trim();
            var dash = text.IndexOf('-');
            if (dash <= 0) return;
            var name = text.Substring(0, dash).ToLowerInvariant();
            var value = text.Substring(dash + 1).Trim();
            if (value.Length == 0) return;
            switch (name)
            {
                case "forge": loader = "forge"; break;
                case "neoforge": loader = "neoforge"; break;
                case "fabric": loader = "fabric"; break;
                case "quilt": loader = "quilt"; break;
                case "liteloader": loader = "liteloader"; break;
                default: return;
            }
            // 少数整合包会写成 forge-1.20.1-47.2.0
            if ((loader == "forge" || loader == "neoforge") && value.Contains("-"))
            {
                var lastDash = value.LastIndexOf('-');
                if (lastDash > 0) value = value.Substring(lastDash + 1);
            }
            version = value;
        }

        private static string PickTargetFolder(CfFile file)
        {
            var fileName = file.FileName ?? "";
            if (file.Modules != null && file.Modules.Count > 0)
            {
                var hasMeta = file.Modules.Any(m => string.Equals(m, "META-INF", StringComparison.OrdinalIgnoreCase) ||
                                                    string.Equals(m, "mcmod.info", StringComparison.OrdinalIgnoreCase));
                if (hasMeta || fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) return "mods";
                if (file.Modules.Any(m => string.Equals(m, "pack.mcmeta", StringComparison.OrdinalIgnoreCase))) return "resourcepacks";
                if (file.Modules.Any(m => string.Equals(m, "level.dat", StringComparison.OrdinalIgnoreCase))) return "saves";
                return "shaderpacks";
            }
            return "mods";
        }

        private static async Task DownloadAllAsync(InstallContext ctx, List<PendingFile> pending, double lo, double hi)
        {
            var total = pending.Count;
            var completed = 0;
            var failures = new List<string>();
            var gate = new object();
            var threads = Math.Max(1, Math.Min(32, ctx.Cfg == null ? 8 : ctx.Cfg.Threads));
            using (var semaphore = new SemaphoreSlim(threads))
            {
                var tasks = new List<Task>();
                foreach (var item in pending)
                {
                    await semaphore.WaitAsync(ctx.Ct).ConfigureAwait(false);
                    var current = item;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await CurseForgeApi.DownloadFileAsync(ctx, current.Candidates, current.Path,
                                current.Sha1, current.Size, null).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            lock (gate) failures.Add(current.Display + " :: " + ex.Message);
                        }
                        finally
                        {
                            semaphore.Release();
                            var done = Interlocked.Increment(ref completed);
                            ctx.Progress("下载整合包文件 " + done + " / " + total, lo + (hi - lo) * done / Math.Max(1, total));
                        }
                    }, ctx.Ct));
                }
                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }
            if (failures.Count > 0)
            {
                var preview = string.Join(Environment.NewLine, failures.Take(6).Select(f => "- " + f));
                throw new Exception("有 " + failures.Count + " 个整合包文件下载失败：" + Environment.NewLine + preview +
                                    (failures.Count > 6 ? Environment.NewLine + "……" : ""));
            }
        }

        private static int CountJars(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.jar", SearchOption.TopDirectoryOnly).Length : 0; }
            catch { return 0; }
        }
    }
}
