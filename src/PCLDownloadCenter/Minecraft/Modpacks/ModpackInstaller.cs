using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 整合包安装统一入口（CLI / Web / 主页插件共用）。
    /// source: modrinth | curseforge | url | file
    /// </summary>
    public static class ModpackInstaller
    {
        /// <summary>
        /// 安装整合包，返回创建的实例名（versions 下的文件夹名）。
        /// </summary>
        public static async Task<string> InstallAsync(InstallContext ctx, string source, string id, string versionId = null)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            var src = (source ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(id)) throw new Exception("缺少整合包 ID 或网址");

            switch (src)
            {
                case "modrinth":
                    return await InstallFromModrinthAsync(ctx, id, versionId).ConfigureAwait(false);
                case "curseforge":
                case "cf":
                    return await InstallFromCurseForgeAsync(ctx, id, versionId).ConfigureAwait(false);
                case "url":
                case "download":
                    return await InstallFromUrlAsync(ctx, id, versionId).ConfigureAwait(false);
                case "file":
                case "local":
                    return await InstallFromFileAsync(ctx, id).ConfigureAwait(false);
                case "auto":
                case "":
                    if (IsUrl(id)) return await InstallFromUrlAsync(ctx, id, versionId).ConfigureAwait(false);
                    if (File.Exists(id)) return await InstallFromFileAsync(ctx, id).ConfigureAwait(false);
                    return await InstallFromModrinthAsync(ctx, id, versionId).ConfigureAwait(false);
                default:
                    throw new Exception("不支持的整合包来源：" + source + "（可用：modrinth / curseforge / url / file）");
            }
        }

        /// <summary>从本地文件导入整合包（自动识别 .mrpack / CurseForge zip / 普通 zip）。</summary>
        public static async Task<string> InstallFromFileAsync(InstallContext ctx, string filePath)
        {
            return await InstallFromFileCoreAsync(ctx, filePath, null, false).ConfigureAwait(false);
        }

        // ---------------- Modrinth ----------------

        private static async Task<string> InstallFromModrinthAsync(InstallContext ctx, string idOrUrl, string versionId)
        {
            ctx.Progress("查询 Modrinth 整合包", 0.02);
            string projectId, urlVersion;
            ModrinthApi.ParseProjectInput(idOrUrl, out projectId, out urlVersion);
            if (string.IsNullOrWhiteSpace(versionId)) versionId = urlVersion;
            if (string.IsNullOrWhiteSpace(projectId)) throw new Exception("缺少 Modrinth 项目 ID");

            ctx.Info("正在查询 Modrinth 项目：" + projectId);
            var project = await ModrinthApi.GetProjectAsync(ctx, projectId).ConfigureAwait(false);
            ctx.Info("整合包：" + project.Title +
                     (string.IsNullOrWhiteSpace(project.Description) ? "" : " —— " + PackCore.Shorten(project.Description, 80)));

            var version = await ModrinthApi.ResolveVersionAsync(ctx, project.Id, versionId, null, null).ConfigureAwait(false);
            var file = ModrinthApi.PickPackFile(version);
            if (file == null) throw new Exception("该 Modrinth 整合包版本中找不到 .mrpack 文件");
            ctx.Info("已选择版本：" + version.VersionNumber + "（" + version.VersionType + "，" +
                     (string.IsNullOrWhiteSpace(version.GameVersionsText) ? "未知游戏版本" : version.GameVersionsText) + "）");
            ctx.Info("整合包文件：" + file.Filename + "（" + PackCore.FormatSize(file.Size) + "）");

            var local = Path.Combine(ctx.Scratch("pack"), PackCore.SafeFileName(file.Filename, "pack.mrpack"));
            var totalSize = file.Size;
            await ctx.Dl.DownloadFileAsync(file.Url, local, file.Sha1, file.Size, done =>
            {
                var fraction = totalSize > 0 ? Math.Min(1.0, (double)done / totalSize) : 0;
                ctx.Progress("下载整合包文件 " + PackCore.FormatSize(done), 0.03 + 0.05 * fraction);
            }, ctx.Ct, 3, false).ConfigureAwait(false);
            ctx.Info("整合包文件下载完成：" + PackCore.FormatSize(FileUtil.SizeOf(local)));

            var info = new PackSourceInfo
            {
                Name = project.Title,
                ProjectId = project.Id,
                VersionLabel = version.VersionNumber,
                Source = "Modrinth",
                IconUrl = project.IconUrl,
                Description = project.Description
            };
            return await MrpackInstaller.InstallAsync(ctx, local, info).ConfigureAwait(false);
        }

        // ---------------- CurseForge ----------------

        private static async Task<string> InstallFromCurseForgeAsync(InstallContext ctx, string idOrUrl, string versionId)
        {
            long projectId = ExtractFirstNumber(idOrUrl);
            if (projectId <= 0)
                throw new Exception("CurseForge 在线安装需要项目 ID（数字）或包含项目 ID 的网址；当前输入：" + idOrUrl +
                                    "。请在 CurseForge 网站上确认项目 ID（例如 https://www.curseforge.com/minecraft/modpacks/xxx 的页面信息），" +
                                    "或改为导入本地整合包 zip。");
            ctx.Progress("查询 CurseForge 整合包", 0.02);
            ctx.Info("正在查询 CurseForge 项目：" + projectId +
                     (CurseForgeApi.HasKey(ctx.Cfg) ? "（使用 API Key）" : "（未配置 API Key，尝试第三方镜像）"));

            var title = "CurseForge 整合包 " + projectId;
            string iconUrl = null;
            try
            {
                var mod = await CurseForgeApi.GetModAsync(ctx, projectId).ConfigureAwait(false);
                if (mod != null)
                {
                    title = JsonUtil.Str(mod, "name", title);
                    var logo = mod["logo"] as JObject;
                    if (logo != null) iconUrl = JsonUtil.Str(logo, "thumbnailUrl", null) ?? JsonUtil.Str(logo, "url", null);
                }
            }
            catch (Exception ex)
            {
                ctx.Warn("获取 CurseForge 项目信息失败（继续尝试获取文件列表）：" + ex.Message);
            }

            var files = await CurseForgeApi.GetModFilesAsync(ctx, projectId).ConfigureAwait(false);
            if (files.Count == 0) throw new Exception("CurseForge 项目 " + projectId + " 没有可用文件");

            CfFile chosen = null;
            if (!string.IsNullOrWhiteSpace(versionId))
            {
                long wanted;
                if (long.TryParse(versionId.Trim(), out wanted))
                    chosen = files.FirstOrDefault(f => f.Id == wanted);
                if (chosen == null)
                    chosen = files.FirstOrDefault(f => string.Equals(f.FileName, versionId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (chosen == null)
                    throw new Exception("在 CurseForge 项目 " + projectId + " 中找不到文件：" + versionId);
            }
            if (chosen == null)
                chosen = files.FirstOrDefault(f => !string.IsNullOrEmpty(f.FileName) && f.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (chosen == null) chosen = files[0];

            ctx.Info("已选择文件：" + chosen.FileName + "（" + PackCore.FormatSize(chosen.Length) + "）");
            var local = Path.Combine(ctx.Scratch("pack"), PackCore.SafeFileName(chosen.FileName, "pack.zip"));
            var candidates = CurseForgeApi.DownloadCandidates(chosen);
            await CurseForgeApi.DownloadFileAsync(ctx, candidates, local, chosen.Sha1, chosen.Length, done =>
            {
                var fraction = chosen.Length > 0 ? Math.Min(1.0, (double)done / chosen.Length) : 0;
                ctx.Progress("下载整合包文件 " + PackCore.FormatSize(done), 0.03 + 0.05 * fraction);
            }).ConfigureAwait(false);

            var info = new PackSourceInfo
            {
                Name = title,
                ProjectId = projectId.ToString(),
                VersionLabel = string.IsNullOrWhiteSpace(chosen.DisplayName) ? chosen.FileName : chosen.DisplayName,
                Source = "CurseForge",
                IconUrl = iconUrl
            };
            return await CurseForgePackInstaller.InstallAsync(ctx, local, info).ConfigureAwait(false);
        }

        // ---------------- URL ----------------

        private static async Task<string> InstallFromUrlAsync(InstallContext ctx, string url, string name)
        {
            if (!IsUrl(url)) throw new Exception("不是有效的下载链接：" + url);
            var local = Path.Combine(ctx.Scratch("pack"), PackCore.SafeFileName(url, "pack.zip"));
            ctx.Info("正在下载整合包：" + url);
            await ctx.Dl.DownloadFileAsync(url, local, null, -1, done =>
            {
                ctx.Progress("下载整合包文件 " + PackCore.FormatSize(done), 0.05);
            }, ctx.Ct, 3, true).ConfigureAwait(false);
            ctx.Info("下载完成：" + PackCore.FormatSize(FileUtil.SizeOf(local)));

            var info = new PackSourceInfo
            {
                Name = name,
                OverrideName = !string.IsNullOrWhiteSpace(name),
                Source = "下载链接"
            };
            return await InstallFromFileCoreAsync(ctx, local, info, true).ConfigureAwait(false);
        }

        // ---------------- 本地文件 ----------------

        private static async Task<string> InstallFromFileCoreAsync(InstallContext ctx, string filePath, PackSourceInfo info, bool deleteAfter)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new Exception("缺少整合包文件路径");
            var full = Path.GetFullPath(filePath);
            if (!File.Exists(full)) throw new Exception("找不到整合包文件：" + full);
            if (!ZipUtil.LooksLikeZip(full))
                throw new Exception("不是有效的整合包压缩包（仅支持 .zip / .mrpack）：" + full);

            info = info ?? new PackSourceInfo
            {
                Name = Path.GetFileNameWithoutExtension(full),
                Source = "本地文件"
            };
            ctx.Info("正在识别整合包：" + Path.GetFileName(full));
            try
            {
                PackArchiveKind kind;
                try { kind = PackCore.DetectArchiveKind(full); }
                catch (Exception ex) { throw new Exception("无法读取整合包压缩包：" + ex.Message, ex); }
                ctx.Info("整合包格式：" + DescribeKind(kind));

                switch (kind)
                {
                    case PackArchiveKind.Modrinth:
                        return await MrpackInstaller.InstallAsync(ctx, full, info).ConfigureAwait(false);
                    case PackArchiveKind.CurseForge:
                        return await CurseForgePackInstaller.InstallAsync(ctx, full, info).ConfigureAwait(false);
                    case PackArchiveKind.Hmcl:
                        return await ZipPackInstaller.InstallHmclAsync(ctx, full, info).ConfigureAwait(false);
                    case PackArchiveKind.Mmc:
                        return await ZipPackInstaller.InstallMmcAsync(ctx, full, info).ConfigureAwait(false);
                    case PackArchiveKind.Mcbbs:
                        throw new Exception("MCBBS 格式整合包暂不支持；请改用 Modrinth (.mrpack) 或 CurseForge 整合包");
                    case PackArchiveKind.LauncherBundle:
                        throw new Exception("该压缩包内包含启动器本体（modpack.zip / modpack.mrpack），不是标准整合包；" +
                                            "请解压后导入其中的 .mrpack 或 .zip 文件");
                    default:
                        return await ZipPackInstaller.InstallPlainAsync(ctx, full, info).ConfigureAwait(false);
                }
            }
            finally
            {
                if (deleteAfter) FileUtil.SafeDelete(full);
            }
        }

        private static string DescribeKind(PackArchiveKind kind)
        {
            switch (kind)
            {
                case PackArchiveKind.Modrinth: return "Modrinth 整合包（modrinth.index.json）";
                case PackArchiveKind.CurseForge: return "CurseForge 整合包（manifest.json）";
                case PackArchiveKind.Hmcl: return "HMCL 整合包（modpack.json）";
                case PackArchiveKind.Mmc: return "MultiMC / Prism 整合包（mmc-pack.json）";
                case PackArchiveKind.Mcbbs: return "MCBBS 整合包（暂不支持）";
                case PackArchiveKind.LauncherBundle: return "带启动器的压缩包（暂不支持）";
                default: return "普通压缩包";
            }
        }

        private static bool IsUrl(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static long ExtractFirstNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return -1;
            var match = Regex.Match(text, @"\d{3,}");
            if (!match.Success) return -1;
            long value;
            return long.TryParse(match.Value, out value) ? value : -1;
        }
    }
}
