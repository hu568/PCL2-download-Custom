using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// Modrinth 整合包（.mrpack）安装：
    /// 解析 modrinth.index.json → 安装原版 + 加载器 → 写实例 JSON → 解压 overrides → 下载 files[]（SHA1 校验）。
    /// </summary>
    internal static class MrpackInstaller
    {
        public static async Task<string> InstallAsync(InstallContext ctx, string archivePath, PackSourceInfo info)
        {
            if (!File.Exists(archivePath)) throw new Exception("找不到整合包文件：" + archivePath);
            info = info ?? new PackSourceInfo { Source = "Modrinth" };

            var baseFolder = PackCore.FindEntryBaseFolder(archivePath, "modrinth.index.json");
            if (baseFolder == null)
                throw new Exception("该文件不是有效的 Modrinth 整合包（压缩包内缺少 modrinth.index.json）");

            ctx.Progress("读取 Modrinth 整合包信息", 0.10);
            var indexText = PackCore.ReadEntryText(archivePath, baseFolder + "modrinth.index.json");
            if (string.IsNullOrWhiteSpace(indexText)) throw new Exception("无法读取 modrinth.index.json（文件为空）");
            JObject index;
            try { index = JObject.Parse(indexText); }
            catch (Exception ex) { throw new Exception("modrinth.index.json 格式错误：" + ex.Message, ex); }

            // ---------- 名称与版本 ----------
            var indexName = JsonUtil.Str(index, "name", null);
            var rawName = indexName;
            if (info.OverrideName && !string.IsNullOrWhiteSpace(info.Name)) rawName = info.Name;
            if (string.IsNullOrWhiteSpace(rawName)) rawName = info.Name;
            if (string.IsNullOrWhiteSpace(rawName)) rawName = Path.GetFileNameWithoutExtension(archivePath);
            var versionLabel = JsonUtil.Str(index, "versionId", info.VersionLabel);

            // ---------- 依赖（原版 + 加载器） ----------
            var deps = index["dependencies"] as JObject;
            if (deps == null || deps["minecraft"] == null)
                throw new Exception("Modrinth 整合包未提供 Minecraft 版本信息（dependencies.minecraft）");
            var mcVersion = deps["minecraft"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new Exception("Modrinth 整合包中的 Minecraft 版本为空");

            string loader = null;
            string loaderVersion = null;
            string loaderKey = null;
            string[] loaderKeys = { "fabric-loader", "quilt-loader", "forge", "neoforge", "neo-forge" };
            foreach (var key in loaderKeys)
            {
                var token = deps[key];
                if (token == null) continue;
                loaderKey = key;
                switch (key)
                {
                    case "fabric-loader": loader = "fabric"; break;
                    case "quilt-loader": loader = "quilt"; break;
                    case "forge": loader = "forge"; break;
                    default: loader = "neoforge"; break;
                }
                loaderVersion = token.ToString().Trim();
                break;
            }
            foreach (var prop in deps.Properties())
            {
                if (prop.Name == "minecraft") continue;
                if (loaderKey != null && string.Equals(prop.Name, loaderKey, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(prop.Name, "neo-forge", StringComparison.OrdinalIgnoreCase)) continue;
                ctx.Warn("整合包声明了未识别的依赖：" + prop.Name + " = " + prop.Value);
            }

            ctx.Info("整合包：" + rawName);
            ctx.Info("游戏版本：" + mcVersion + (loader == null ? "（无加载器）" : "，加载器：" + PackCore.LoaderDisplay(loader) + " " + loaderVersion));
            if (!string.IsNullOrWhiteSpace(versionLabel)) ctx.Info("整合包版本号：" + versionLabel);

            // ---------- 安装原版 + 加载器 ----------
            var baseInstall = await PackCore.EnsureBaseAndLoaderAsync(ctx, mcVersion, loader, loaderVersion, 0.12, 0.70)
                .ConfigureAwait(false);

            // ---------- 创建实例 ----------
            var instanceName = PackCore.AllocateInstanceName(ctx, rawName);
            var instanceDir = Path.Combine(ctx.VersionsDir, instanceName);
            var created = false;
            try
            {
                FileUtil.EnsureDir(instanceDir);
                created = true;
                PackCore.MarkInstalling(instanceDir);
                PackCore.WriteInstanceJson(ctx, instanceDir, instanceName, baseInstall.InheritsFrom, mcVersion, versionLabel, baseInstall.VanillaId);

                // ---------- overrides / client-overrides ----------
                ctx.Progress("解压整合包覆写文件", 0.70);
                var overridesCount = 0;
                overridesCount += ZipUtil.ExtractPrefix(archivePath, baseFolder + "overrides/", instanceDir);
                overridesCount += ZipUtil.ExtractPrefix(archivePath, baseFolder + "client-overrides/", instanceDir);
                if (overridesCount > 0) ctx.Info("已解压 " + overridesCount + " 个覆写文件（overrides / client-overrides）");
                else ctx.Info("整合包没有 overrides 覆写文件");

                // 覆写文件可能自带 PCL\Setup.ini，因此在解压之后再合并写入版本隔离等必要键
                var customInfo = (string.IsNullOrWhiteSpace(info.Source) ? "整合包" : info.Source) +
                                 (string.IsNullOrWhiteSpace(versionLabel) ? "" : " · " + versionLabel) +
                                 "（MC " + mcVersion +
                                 (loader == null ? "" : " / " + PackCore.LoaderDisplay(loader)) + "）";
                PackCore.WriteInstanceSetup(instanceDir, mcVersion, customInfo, info, versionLabel, loader, loaderVersion);

                // ---------- files[] ----------
                var items = new List<DownloadItem>();
                var files = index["files"] as JArray;
                var skipped = 0;
                var optional = 0;
                if (files != null)
                {
                    foreach (var token in files)
                    {
                        var file = token as JObject;
                        if (file == null) continue;
                        var relative = JsonUtil.Str(file, "path", null);
                        if (string.IsNullOrWhiteSpace(relative))
                        {
                            ctx.Warn("整合包内存在缺少 path 的文件项，已跳过");
                            skipped++;
                            continue;
                        }
                        var env = file["env"] as JObject;
                        var client = env != null ? JsonUtil.Str(env, "client", "") : "";
                        if (string.Equals(client, "unsupported", StringComparison.OrdinalIgnoreCase))
                        {
                            skipped++;
                            continue;
                        }
                        if (string.Equals(client, "optional", StringComparison.OrdinalIgnoreCase)) optional++;

                        var url = PickDownloadUrl(file);
                        if (string.IsNullOrWhiteSpace(url))
                        {
                            ctx.Warn("文件没有可用的下载地址，已跳过：" + relative);
                            skipped++;
                            continue;
                        }
                        var hashes = file["hashes"] as JObject;
                        var sha1 = hashes != null ? JsonUtil.Str(hashes, "sha1", null) : null;
                        var size = JsonUtil.Long(file, "fileSize", -1);
                        var local = PackCore.ResolveInside(instanceDir, relative);
                        items.Add(new DownloadItem(url, local, sha1, size, relative));
                    }
                }

                ctx.Info("整合包文件：" + items.Count + " 个需要下载" +
                         (skipped > 0 ? "，跳过 " + skipped + " 个（服务端专用/无地址）" : "") +
                         (optional > 0 ? "，其中 " + optional + " 个为可选文件" : ""));

                if (items.Count > 0)
                {
                    await ctx.Dl.DownloadManyAsync(items, (done, total) =>
                    {
                        ctx.Progress("下载整合包文件 " + done + " / " + total, 0.74 + 0.22 * done / Math.Max(1, total));
                    }, ctx.Ct).ConfigureAwait(false);
                }

                await PackCore.TrySetIconAsync(ctx, instanceDir, info.IconUrl).ConfigureAwait(false);
                PackCore.FinishInstalling(instanceDir);

                var jarCount = CountJars(Path.Combine(instanceDir, "mods"));
                ctx.Info("安装完成：versions/" + instanceName + "（mods 目录 " + jarCount + " 个 jar 文件）");
                ctx.Progress("整合包安装完成", 1.0);
                return instanceName;
            }
            catch
            {
                if (created) PackCore.Rollback(ctx, instanceDir, instanceName);
                throw;
            }
        }

        private static string PickDownloadUrl(JObject file)
        {
            var downloads = file["downloads"] as JArray;
            if (downloads == null) return null;
            string first = null;
            string modrinth = null;
            foreach (var token in downloads)
            {
                var url = token == null ? null : token.ToString();
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (first == null) first = url;
                if (url.IndexOf("cdn.modrinth.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    modrinth = url;
                    break;
                }
            }
            return modrinth ?? first;
        }

        private static int CountJars(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.jar", SearchOption.TopDirectoryOnly).Length : 0; }
            catch { return 0; }
        }
    }
}
