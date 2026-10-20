using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>Forge 加载器：版本列表与安装。</summary>
    public static class ForgeInstaller
    {
        public const string ForgeMaven = "https://maven.minecraftforge.net";
        public const string ForgeMavenMeta = ForgeMaven + "/net/minecraftforge/forge/maven-metadata.xml";
        public const string ForgeMavenMetaMirror = "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/maven-metadata.xml";
        public const string ForgePromotions = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
        public const string ForgeBmclapiList = "https://bmclapi2.bangbang93.com/forge/minecraft/";

        /// <summary>获取某个 MC 版本可用的 Forge 版本列表（按新到旧排序，带推荐/最新标记）。</summary>
        public static async Task<List<LoaderVersion>> GetVersionsAsync(InstallContext ctx, string mcVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new ArgumentException("缺少 Minecraft 版本");
            mcVersion = mcVersion.Trim();

            // 推荐版本 / 最新版本（失败不影响列表）
            string recommendedVersion = null;
            string latestPromiseVersion = null;
            try
            {
                string promoText = await ctx.Dl.GetStringAsync(ForgePromotions, ctx.Ct, 20000, 2, false).ConfigureAwait(false);
                var promos = JObject.Parse(promoText)["promos"] as JObject;
                if (promos != null)
                {
                    recommendedVersion = JsonUtil.Str(promos, mcVersion + "-recommended");
                    latestPromiseVersion = JsonUtil.Str(promos, mcVersion + "-latest");
                }
            }
            catch (Exception ex)
            {
                ctx.Warn("获取 Forge 推荐版本失败（继续）：" + ex.Message);
            }

            var versions = new List<string>();
            Exception lastError = null;

            // 1. BMCLAPI 的 Forge 版本接口：按 MC 版本返回 JSON，速度快且稳定（PCL 也优先用它）
            try
            {
                string text = await ctx.Dl.GetStringAsync(ForgeBmclapiList + Uri.EscapeDataString(mcVersion), ctx.Ct, 30000, 2, false).ConfigureAwait(false);
                var array = JArray.Parse(text);
                foreach (var token in array)
                {
                    string ver = JsonUtil.Str(token, "version");
                    if (!string.IsNullOrWhiteSpace(ver) && !versions.Contains(ver.Trim(), StringComparer.OrdinalIgnoreCase))
                        versions.Add(ver.Trim());
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
                ctx.Warn("获取 BMCLAPI Forge 版本列表失败：" + ex.Message);
            }

            // 2. 兜底：maven-metadata.xml（官方源 / BMCLAPI 镜像）
            if (versions.Count == 0)
            {
                foreach (var metaUrl in new[] { ForgeMavenMeta, ForgeMavenMetaMirror })
                {
                    try
                    {
                        string xml = await ctx.Dl.GetStringAsync(metaUrl, ctx.Ct, 30000, 2, true).ConfigureAwait(false);
                        var doc = XDocument.Parse(xml);
                        string prefix = mcVersion + "-";
                        foreach (var element in doc.Descendants())
                        {
                            if (!string.Equals(element.Name.LocalName, "version", StringComparison.Ordinal)) continue;
                            string value = (element.Value ?? "").Trim();
                            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                            string ver = value.Substring(prefix.Length).Trim();
                            if (ver.Length == 0) continue;
                            if (!versions.Contains(ver, StringComparer.OrdinalIgnoreCase)) versions.Add(ver);
                        }
                        if (versions.Count > 0) break;
                        lastError = new Exception("maven-metadata.xml 中没有 " + mcVersion + " 的版本");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        ctx.Warn("获取 Forge 版本清单失败（" + metaUrl + "）：" + ex.Message);
                    }
                }
            }

            if (versions.Count == 0)
                throw new Exception("获取 Forge 版本列表失败：" + (lastError == null ? "没有可用数据" : lastError.Message), lastError);

            var list = new List<LoaderVersion>();
            foreach (var ver in versions)
            {
                list.Add(new LoaderVersion
                {
                    Version = ver,
                    McVersion = mcVersion,
                    Display = ver,
                    Kind = "forge",
                    VersionId = LoaderRegistry.BuildVersionId("forge", mcVersion, ver),
                    IsBeta = ver.IndexOf("pre", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             ver.IndexOf("snapshot", StringComparison.OrdinalIgnoreCase) >= 0,
                    IsRecommended = !string.IsNullOrWhiteSpace(recommendedVersion) && string.Equals(ver, recommendedVersion, StringComparison.OrdinalIgnoreCase),
                    IsLatest = false
                });
            }
            list.Sort((a, b) => LoaderCommon.CompareVersions(b.Version, a.Version));
            if (list.Count == 0)
            {
                ctx.Info("未找到 Forge 适用于 Minecraft " + mcVersion + " 的版本");
                return list;
            }
            var latest = !string.IsNullOrWhiteSpace(latestPromiseVersion)
                ? list.FirstOrDefault(v => string.Equals(v.Version, latestPromiseVersion, StringComparison.OrdinalIgnoreCase))
                : null;
            (latest ?? list[0]).IsLatest = true;
            if (!list.Any(v => v.IsRecommended))
            {
                var firstStable = list.FirstOrDefault(v => !v.IsBeta);
                if (firstStable != null) firstStable.IsRecommended = true;
            }
            ctx.Info("获取到 Forge 版本 " + list.Count + " 个（MC " + mcVersion + "）");
            return list;
        }

        /// <summary>安装 Forge。</summary>
        public static async Task<string> InstallAsync(InstallContext ctx, string mcVersion, string loaderVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new Exception("缺少 Minecraft 版本，例如：--install loader forge 1.20.1 47.2.0");
            mcVersion = mcVersion.Trim();

            List<LoaderVersion> list = null;
            try { list = await GetVersionsAsync(ctx, mcVersion).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { ctx.Warn("获取 Forge 版本列表失败（若已指定版本则继续安装）：" + ex.Message); }

            if (string.IsNullOrWhiteSpace(loaderVersion))
            {
                if (list == null || list.Count == 0)
                    throw new Exception("未能获取 Forge 版本列表，请显式指定版本，例如：--install loader forge " + mcVersion + " 47.2.0");
                var pick = list.FirstOrDefault(v => v.IsRecommended) ?? list[0];
                loaderVersion = pick.Version;
                ctx.Info("自动选择 Forge 版本：" + loaderVersion);
            }
            loaderVersion = loaderVersion.Trim();
            if (loaderVersion.StartsWith(mcVersion + "-", StringComparison.OrdinalIgnoreCase))
                loaderVersion = loaderVersion.Substring(mcVersion.Length + 1);

            if (list != null && list.Count > 0 && !list.Any(v => string.Equals(v.Version, loaderVersion, StringComparison.OrdinalIgnoreCase)))
            {
                string preview = string.Join("、", list.Take(8).Select(v => v.Version));
                throw new Exception("未找到 Forge " + mcVersion + "-" + loaderVersion + "（MC " + mcVersion + " 的可用版本如：" + preview + " 等，共 " + list.Count + " 个）");
            }

            // 确保原版基础版本
            ctx.Progress("检查原版基础版本", 0.03);
            await LoaderCommon.EnsureVanillaAsync(ctx, mcVersion).ConfigureAwait(false);

            string targetId = LoaderRegistry.BuildVersionId("forge", mcVersion, loaderVersion);
            string mavenVersion = mcVersion + "-" + loaderVersion;
            // PCL 同样优先用 BMCLAPI 的 Forge 下载接口：官方 maven.minecraftforge.net 在部分网络下会被 Cloudflare 限速/假死
            string forgePath = "net/minecraftforge/forge/" + mavenVersion + "/forge-" + mavenVersion + "-installer.jar";
            string[] urls =
            {
                "https://bmclapi2.bangbang93.com/forge/download?mcversion=" + Uri.EscapeDataString(mcVersion) +
                    "&version=" + Uri.EscapeDataString(loaderVersion) + "&category=installer&format=jar",
                "https://mirrors.cernet.edu.cn/bmclapi/" + forgePath,
                "https://cmcc.mirrors.ustc.edu.cn/bmclapi/" + forgePath,
                ForgeMaven + "/" + forgePath,
                "https://files.minecraftforge.net/maven/net/minecraftforge/forge/" + mavenVersion + "/forge-" + mavenVersion + "-installer.jar",
                "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/" + mavenVersion + "/forge-" + mavenVersion + "-installer.jar"
            };

            var opt = new ForgelikeInstallOptions
            {
                LoaderName = "Forge",
                McVersion = mcVersion,
                LoaderVersion = loaderVersion,
                ApiVersion = loaderVersion,
                InstallerUrls = urls,
                TargetId = targetId,
                PatchToMirror = ctx.Cfg != null && string.Equals(ctx.Cfg.DownloadSource, "bmclapi", StringComparison.OrdinalIgnoreCase),
                AllowMirrorRetry = true,
                PrefetchLibraries = true
            };
            ctx.Info("开始安装 Forge " + mavenVersion + " → " + targetId);
            return await ForgelikeInstaller.RunAsync(ctx, opt).ConfigureAwait(false);
        }
    }
}
