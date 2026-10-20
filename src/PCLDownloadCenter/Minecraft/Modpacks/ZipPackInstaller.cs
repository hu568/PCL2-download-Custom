using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// HMCL / MultiMC / 普通压缩包的整合包导入。
    /// 普通压缩包没有版本信息时，会尝试继承已安装的原版版本，并给出明确提示。
    /// </summary>
    internal static class ZipPackInstaller
    {
        // ---------------- HMCL ----------------

        public static async Task<string> InstallHmclAsync(InstallContext ctx, string archivePath, PackSourceInfo info)
        {
            if (!File.Exists(archivePath)) throw new Exception("找不到整合包文件：" + archivePath);
            info = info ?? new PackSourceInfo { Source = "HMCL 整合包" };

            var baseFolder = PackCore.FindEntryBaseFolder(archivePath, "modpack.json");
            if (baseFolder == null) throw new Exception("压缩包内找不到 HMCL 的 modpack.json");
            ctx.Progress("读取 HMCL 整合包信息", 0.10);
            JObject json;
            try { json = JObject.Parse(PackCore.ReadEntryText(archivePath, baseFolder + "modpack.json")); }
            catch (Exception ex) { throw new Exception("HMCL 整合包的 modpack.json 格式错误：" + ex.Message, ex); }

            var rawName = JsonUtil.Str(json, "name", null);
            if (info.OverrideName && !string.IsNullOrWhiteSpace(info.Name)) rawName = info.Name;
            if (string.IsNullOrWhiteSpace(rawName)) rawName = info.Name;
            if (string.IsNullOrWhiteSpace(rawName)) rawName = Path.GetFileNameWithoutExtension(archivePath);
            var versionLabel = JsonUtil.Str(json, "version", info.VersionLabel);
            var gameVersion = JsonUtil.Str(json, "gameVersion", null);
            if (string.IsNullOrWhiteSpace(gameVersion))
                throw new Exception("HMCL 整合包缺少 gameVersion（Minecraft 版本）信息");

            ctx.Info("整合包：" + rawName + "（HMCL 格式，MC " + gameVersion + "）");
            var baseInstall = await PackCore.InstallBaseStagedAsync(ctx, gameVersion, null, null, 0.12, 0.70)
                .ConfigureAwait(false);

            var instanceName = PackCore.AllocateInstanceName(ctx, rawName);
            var instanceDir = Path.Combine(ctx.VersionsDir, instanceName);
            var created = false;
            try
            {
                FileUtil.EnsureDir(instanceDir);
                created = true;
                PackCore.MarkInstalling(instanceDir);
                PackCore.WriteMergedInstance(ctx, baseInstall, instanceDir, instanceName, versionLabel);
                ctx.Progress("解压整合包文件", 0.72);
                var count = ZipUtil.ExtractPrefix(archivePath, baseFolder + "minecraft/", instanceDir);
                if (count == 0) count = ZipUtil.ExtractPrefix(archivePath, baseFolder, instanceDir);
                ctx.Info("已解压 " + count + " 个文件");
                PackCore.WriteInstanceSetup(instanceDir, gameVersion,
                    "HMCL 整合包" + (string.IsNullOrWhiteSpace(versionLabel) ? "" : " · " + versionLabel) + "（MC " + gameVersion + "）",
                    info, versionLabel);

                await PackCore.TrySetIconAsync(ctx, instanceDir, info.IconUrl).ConfigureAwait(false);
                PackCore.FinishInstalling(instanceDir);
                ctx.Info("安装完成：versions/" + instanceName + "（已合并原版，versions 下只有这一个版本）");
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

        // ---------------- MultiMC / Prism ----------------

        public static async Task<string> InstallMmcAsync(InstallContext ctx, string archivePath, PackSourceInfo info)
        {
            if (!File.Exists(archivePath)) throw new Exception("找不到整合包文件：" + archivePath);
            info = info ?? new PackSourceInfo { Source = "MultiMC 整合包" };

            var baseFolder = PackCore.FindEntryBaseFolder(archivePath, "mmc-pack.json");
            if (baseFolder == null) throw new Exception("压缩包内找不到 MultiMC 的 mmc-pack.json");
            ctx.Progress("读取 MultiMC 整合包信息", 0.10);
            JObject pack;
            try { pack = JObject.Parse(PackCore.ReadEntryText(archivePath, baseFolder + "mmc-pack.json")); }
            catch (Exception ex) { throw new Exception("MultiMC 整合包的 mmc-pack.json 格式错误：" + ex.Message, ex); }

            var components = pack["components"] as JArray;
            if (components == null || components.Count == 0)
                throw new Exception("MultiMC 整合包缺少 components（组件）信息");

            string mcVersion = null;
            string loader = null;
            string loaderVersion = null;
            foreach (var token in components)
            {
                var component = token as JObject;
                if (component == null) continue;
                var uid = JsonUtil.Str(component, "uid", "");
                var version = JsonUtil.Str(component, "version", null);
                if (string.IsNullOrWhiteSpace(version)) continue;
                switch (uid)
                {
                    case "net.minecraft": mcVersion = version; break;
                    case "net.minecraftforge": loader = "forge"; loaderVersion = NormalizeForgeLike(version); break;
                    case "net.neoforged": loader = "neoforge"; loaderVersion = NormalizeForgeLike(version); break;
                    case "net.fabricmc.fabric-loader": loader = "fabric"; loaderVersion = version; break;
                    case "org.quiltmc.quilt-loader": loader = "quilt"; loaderVersion = version; break;
                    case "org.lwjgl": break;
                    default: ctx.Warn("MultiMC 组件 " + uid + " 暂不支持，已忽略"); break;
                }
            }
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new Exception("MultiMC 整合包未声明 Minecraft 版本（net.minecraft 组件）");

            // 实例名：instance.cfg 的 name，其次压缩包名
            var rawName = ReadMmcInstanceName(archivePath, baseFolder);
            if (info.OverrideName && !string.IsNullOrWhiteSpace(info.Name)) rawName = info.Name;
            if (string.IsNullOrWhiteSpace(rawName)) rawName = info.Name;
            if (string.IsNullOrWhiteSpace(rawName)) rawName = Path.GetFileNameWithoutExtension(archivePath);

            if (PackCore.HasEntry(archivePath, "patches", false))
                ctx.Warn("该 MultiMC 整合包包含 JSON Patches，本工具暂不应用（个别自定义库可能缺失）");

            ctx.Info("整合包：" + rawName + "（MultiMC 格式，MC " + mcVersion +
                     (loader == null ? "" : "，" + PackCore.LoaderDisplay(loader) + " " + loaderVersion) + "）");
            var baseInstall = await PackCore.InstallBaseStagedAsync(ctx, mcVersion, loader, loaderVersion, 0.12, 0.70)
                .ConfigureAwait(false);

            var instanceName = PackCore.AllocateInstanceName(ctx, rawName);
            var instanceDir = Path.Combine(ctx.VersionsDir, instanceName);
            var created = false;
            try
            {
                FileUtil.EnsureDir(instanceDir);
                created = true;
                PackCore.MarkInstalling(instanceDir);
                PackCore.WriteMergedInstance(ctx, baseInstall, instanceDir, instanceName, null);
                ctx.Progress("解压整合包文件", 0.72);
                var count = ZipUtil.ExtractPrefix(archivePath, baseFolder + "minecraft/", instanceDir);
                if (count == 0) count = ZipUtil.ExtractPrefix(archivePath, baseFolder, instanceDir);
                ctx.Info("已解压 " + count + " 个文件");
                PackCore.WriteInstanceSetup(instanceDir, mcVersion,
                    "MultiMC 整合包（MC " + mcVersion + (loader == null ? "" : " / " + PackCore.LoaderDisplay(loader)) + "）",
                    info, null, loader, loaderVersion);

                await PackCore.TrySetIconAsync(ctx, instanceDir, info.IconUrl).ConfigureAwait(false);
                PackCore.FinishInstalling(instanceDir);
                ctx.Info("安装完成：versions/" + instanceName + "（已合并原版与加载器，versions 下只有这一个版本）");
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

        private static string NormalizeForgeLike(string version)
        {
            // forge 组件版本可能是 47.2.0，也可能是 1.20.1-47.2.0
            if (string.IsNullOrWhiteSpace(version)) return version;
            if (!version.Contains("-")) return version;
            var last = version.LastIndexOf('-');
            return last > 0 ? version.Substring(last + 1) : version;
        }

        private static string ReadMmcInstanceName(string archivePath, string baseFolder)
        {
            try
            {
                var cfg = PackCore.ReadEntryText(archivePath, baseFolder + "instance.cfg");
                if (string.IsNullOrWhiteSpace(cfg)) return null;
                foreach (var line in cfg.Split('\n'))
                {
                    var text = line.Trim();
                    if (text.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                        return text.Substring("name=".Length).Trim();
                }
            }
            catch { }
            return null;
        }

        // ---------------- 普通压缩包 ----------------

        public static async Task<string> InstallPlainAsync(InstallContext ctx, string archivePath, PackSourceInfo info)
        {
            if (!File.Exists(archivePath)) throw new Exception("找不到整合包文件：" + archivePath);
            info = info ?? new PackSourceInfo { Source = "普通压缩包" };

            // 情况一：压缩包里带着完整的 .minecraft/versions/<X>/<X>.json
            string embeddedRoot;
            string embeddedVersion;
            if (TryFindEmbeddedVersion(archivePath, out embeddedRoot, out embeddedVersion))
                return await ImportEmbeddedVersionAsync(ctx, archivePath, embeddedRoot, embeddedVersion, info).ConfigureAwait(false);

            // 情况二：把压缩包直接解压成实例目录（尽量继承一个已安装的原版）
            var fallback = PackCore.FindInstalledVanilla(ctx);
            var singleTop = PackCore.GetSingleTopFolder(archivePath);
            var suggestedName = info.OverrideName && !string.IsNullOrWhiteSpace(info.Name) ? info.Name : info.Name;
            if (string.IsNullOrWhiteSpace(suggestedName)) suggestedName = singleTop;
            if (string.IsNullOrWhiteSpace(suggestedName)) suggestedName = Path.GetFileNameWithoutExtension(archivePath);

            if (string.IsNullOrWhiteSpace(fallback))
            {
                throw new Exception("该压缩包不是 Modrinth / CurseForge 整合包，也不包含 Minecraft 版本信息，且未找到已安装的原版游戏。" +
                                    "请先安装原版（例如 --install vanilla 1.21.4），或改用 .mrpack / CurseForge 整合包。");
            }

            var instanceName = PackCore.AllocateInstanceName(ctx, suggestedName);
            var instanceDir = Path.Combine(ctx.VersionsDir, instanceName);
            var created = false;
            try
            {
                FileUtil.EnsureDir(instanceDir);
                created = true;
                PackCore.MarkInstalling(instanceDir);
                PackCore.WriteStandaloneFromInstalled(ctx, fallback, instanceDir, instanceName);
                ctx.Progress("解压压缩包到实例目录", 0.60);
                var prefix = singleTop == null ? "" : singleTop + "/";
                var count = ZipUtil.ExtractPrefix(archivePath, prefix, instanceDir);
                ctx.Info("已解压 " + count + " 个文件");
                PackCore.WriteInstanceSetup(instanceDir, fallback,
                    "普通压缩包（未能识别整合包格式，已继承原版 " + fallback + "）", info, null);

                PackCore.FinishInstalling(instanceDir);
                ctx.Info("安装完成：versions/" + instanceName);
                ctx.Warn("该压缩包未包含整合包清单，无法识别游戏版本；已按已安装的原版 " + fallback +
                         " 创建实例，请在 PCL 中核对版本是否正确。");
                ctx.Progress("整合包安装完成", 1.0);
                return instanceName;
            }
            catch
            {
                if (created) PackCore.Rollback(ctx, instanceDir, instanceName);
                throw;
            }
        }

        /// <summary>在压缩包中查找形如 .../versions/&lt;X&gt;/&lt;X&gt;.json 的条目。</summary>
        private static bool TryFindEmbeddedVersion(string archivePath, out string rootPrefix, out string versionName)
        {
            rootPrefix = "";
            versionName = null;
            try
            {
                using (var zip = ZipFile.OpenRead(archivePath))
                {
                    var regex = new Regex(@"^(?<root>.*?/)?versions/(?<ver>[^/]+)/\k<ver>\.json$", RegexOptions.IgnoreCase);
                    foreach (var entry in zip.Entries)
                    {
                        var name = entry.FullName.Replace('\\', '/');
                        var match = regex.Match(name);
                        if (!match.Success) continue;
                        rootPrefix = match.Groups["root"].Value;
                        versionName = match.Groups["ver"].Value;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static Task<string> ImportEmbeddedVersionAsync(InstallContext ctx, string archivePath,
            string rootPrefix, string versionName, PackSourceInfo info)
        {
            var suggested = info.OverrideName && !string.IsNullOrWhiteSpace(info.Name) ? info.Name : versionName;
            var instanceName = PackCore.AllocateInstanceName(ctx, suggested);
            var instanceDir = Path.Combine(ctx.VersionsDir, instanceName);
            ctx.Info("检测到压缩包内的 .minecraft 实例：" + versionName);
            var created = false;
            try
            {
                FileUtil.EnsureDir(instanceDir);
                created = true;
                PackCore.MarkInstalling(instanceDir);
                ctx.Progress("解压内嵌实例", 0.40);
                var prefix = rootPrefix + "versions/" + versionName + "/";
                var count = ZipUtil.ExtractPrefix(archivePath, prefix, instanceDir);
                ctx.Info("已解压 " + count + " 个文件");

                // 修正实例 JSON：统一文件名与 id，并补上 PCL 合并所需的 libraries 字段
                var sourceJson = Path.Combine(instanceDir, versionName + ".json");
                if (File.Exists(sourceJson))
                {
                    try
                    {
                        var obj = JObject.Parse(File.ReadAllText(sourceJson, System.Text.Encoding.UTF8));
                        obj["id"] = instanceName;
                        if (obj["libraries"] == null) obj["libraries"] = new JArray();
                        var targetJson = Path.Combine(instanceDir, instanceName + ".json");
                        FileUtil.WriteAllTextAtomic(targetJson, obj.ToString(Newtonsoft.Json.Formatting.Indented));
                        if (!string.Equals(sourceJson, targetJson, StringComparison.OrdinalIgnoreCase))
                            FileUtil.SafeDelete(sourceJson);
                    }
                    catch (Exception ex)
                    {
                        ctx.Warn("修正内嵌实例 JSON 失败：" + ex.Message);
                    }
                }
                else
                {
                    var fallback = PackCore.FindInstalledVanilla(ctx);
                    if (string.IsNullOrWhiteSpace(fallback))
                        throw new Exception("压缩包内的实例缺少版本 JSON，且未找到已安装的原版游戏");
                    PackCore.WriteStandaloneFromInstalled(ctx, fallback, instanceDir, instanceName);
                    PackCore.WriteInstanceSetup(instanceDir, fallback, "普通压缩包（内嵌实例缺少 JSON）", info, null);
                }

                // 内嵌实例同样要写版本隔离等 PCL 配置（PCL 自己导入实例时也会写；SetIniKey 保留压缩包自带的键）
                string vanillaName = null;
                var instanceJsonPath = Path.Combine(instanceDir, instanceName + ".json");
                try
                {
                    if (File.Exists(instanceJsonPath))
                    {
                        var instanceJson = JObject.Parse(File.ReadAllText(instanceJsonPath, System.Text.Encoding.UTF8));
                        vanillaName = JsonUtil.Str(instanceJson, "clientVersion", null);
                        if (string.IsNullOrWhiteSpace(vanillaName)) vanillaName = JsonUtil.Str(instanceJson, "jar", null);
                        if (string.IsNullOrWhiteSpace(vanillaName))
                        {
                            var inherit = JsonUtil.Str(instanceJson, "inheritsFrom", null);
                            if (!string.IsNullOrWhiteSpace(inherit) && Regex.IsMatch(inherit, @"^\d+\.\d+(\.\d+)?$"))
                                vanillaName = inherit;
                        }
                    }
                }
                catch { }
                PackCore.WriteInstanceSetup(instanceDir, vanillaName, "普通压缩包（内嵌实例：" + versionName + "）", info, null);
                PackCore.FinishInstalling(instanceDir);
                ctx.Info("安装完成：versions/" + instanceName);
                ctx.Warn("该压缩包内嵌的实例可能依赖全局 libraries / assets，如 PCL 提示补全请让其自动补全。");
                ctx.Progress("整合包安装完成", 1.0);
                return Task.FromResult(instanceName);
            }
            catch
            {
                if (created) PackCore.Rollback(ctx, instanceDir, instanceName);
                throw;
            }
        }
    }
}
