using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// NeoForge 加载器：版本列表与安装。
    /// 注意：maven.neoforged.net 本机直连超时，版本列表与安装器都走 BMCLAPI 镜像。
    /// 版本号与 MC 映射：21.1.x → 1.21.1，20.4.x → 1.20.4，20.6.x → 1.20.6；1.20.1 使用旧包名 net/neoforged/forge（47.x）。
    /// </summary>
    public static class NeoForgeInstaller
    {
        public const string Bmclapi = "https://bmclapi2.bangbang93.com";
        public const string ListLatest = Bmclapi + "/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge";
        public const string ListLegacy = Bmclapi + "/neoforge/meta/api/maven/details/releases/net/neoforged/forge";
        public const string MetaLatest = Bmclapi + "/maven/net/neoforged/neoforge/maven-metadata.xml";
        public const string MetaLegacy = Bmclapi + "/maven/net/neoforged/forge/maven-metadata.xml";

        /// <summary>由 NeoForge 版本号推断对应的 Minecraft 版本。</summary>
        public static string McVersionOf(string apiName)
        {
            if (string.IsNullOrWhiteSpace(apiName)) return null;
            apiName = apiName.Trim();
            if (apiName.StartsWith("1.20.1-", StringComparison.OrdinalIgnoreCase)) return "1.20.1";
            if (apiName.StartsWith("0.", StringComparison.Ordinal))
            {
                // 0.25w14craftmine.3-beta：第二段作为 MC 版本（与 PCL 一致）
                string beforeDash = apiName.Split('-')[0];
                var segments = beforeDash.Split('.');
                return segments.Length >= 2 ? segments[1] : null;
            }
            string core = apiName;
            string snapshot = null;
            int plus = core.IndexOf('+');
            if (plus >= 0)
            {
                snapshot = core.Substring(plus + 1);
                core = core.Substring(0, plus);
            }
            int dash = core.IndexOf('-');
            string numeric = dash >= 0 ? core.Substring(0, dash) : core;
            var parts = numeric.Split('.');
            if (parts.Length < 2) return null;
            int major, minor, build = 0;
            if (!int.TryParse(parts[0].Trim(), out major)) return null;
            if (!int.TryParse(parts[1].Trim(), out minor)) return null;
            if (parts.Length > 2) int.TryParse(parts[2].Trim(), out build);
            string inherit;
            if (major >= 24)
            {
                inherit = build > 0 ? (major + "." + minor + "." + build) : (major + "." + minor);
            }
            else
            {
                inherit = "1." + major + "." + minor;
            }
            if (inherit.EndsWith(".0", StringComparison.Ordinal))
                inherit = inherit.Substring(0, inherit.Length - 2);
            if (!string.IsNullOrWhiteSpace(snapshot))
                inherit += "-" + snapshot;
            return inherit;
        }

        /// <summary>NeoForge 版本对应的 maven 包名（1.20.1 为旧包 net/neoforged/forge）。</summary>
        public static string PackageNameOf(string apiName, string mcVersion)
        {
            if (string.Equals(mcVersion, "1.20.1", StringComparison.OrdinalIgnoreCase) ||
                (apiName != null && apiName.StartsWith("1.20.1-", StringComparison.OrdinalIgnoreCase)))
                return "forge";
            return "neoforge";
        }

        /// <summary>版本列表中用于显示/生成目录名的版本（旧版 1.20.1 去掉 "1.20.1-" 前缀）。</summary>
        public static string DisplayVersionOf(string apiName)
        {
            if (string.IsNullOrWhiteSpace(apiName)) return apiName;
            if (apiName.StartsWith("1.20.1-", StringComparison.OrdinalIgnoreCase))
                return apiName.Substring("1.20.1-".Length);
            return apiName;
        }

        public static bool IsBetaVersion(string apiName)
        {
            if (string.IsNullOrWhiteSpace(apiName)) return false;
            string v = apiName.ToLowerInvariant();
            return v.Contains("beta") || v.Contains("alpha") || v.Contains("snapshot") || v.Contains("pre");
        }

        /// <summary>获取某个 MC 版本可用的 NeoForge 版本列表。</summary>
        public static async Task<List<LoaderVersion>> GetVersionsAsync(InstallContext ctx, string mcVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new ArgumentException("缺少 Minecraft 版本");
            mcVersion = mcVersion.Trim();

            var apiNames = new List<string>();
            Exception lastError = null;

            // 1. BMCLAPI 的 NeoForged maven 目录接口（官方 API 的镜像）
            foreach (var url in new[] { ListLegacy, ListLatest })
            {
                try
                {
                    string text = await ctx.Dl.GetStringAsync(url, ctx.Ct, 30000, 2, false).ConfigureAwait(false);
                    var root = JObject.Parse(text);
                    var files = root["files"] as JArray;
                    if (files == null) continue;
                    foreach (var token in files)
                    {
                        string name = JsonUtil.Str(token, "name");
                        string type = JsonUtil.Str(token, "type");
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "DIRECTORY", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".md5", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".sha1", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".sha512", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!apiNames.Contains(name, StringComparer.OrdinalIgnoreCase)) apiNames.Add(name);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    lastError = ex;
                    ctx.Warn("获取 NeoForge 版本列表失败（" + url + "）：" + ex.Message);
                }
            }

            // 2. 兜底：maven-metadata.xml
            if (apiNames.Count == 0)
            {
                foreach (var metaUrl in new[] { MetaLatest, MetaLegacy })
                {
                    try
                    {
                        string xml = await ctx.Dl.GetStringAsync(metaUrl, ctx.Ct, 30000, 2, false).ConfigureAwait(false);
                        var doc = XDocument.Parse(xml);
                        foreach (var element in doc.Descendants())
                        {
                            if (!string.Equals(element.Name.LocalName, "version", StringComparison.Ordinal)) continue;
                            string value = (element.Value ?? "").Trim();
                            if (value.Length > 0 && !apiNames.Contains(value, StringComparer.OrdinalIgnoreCase)) apiNames.Add(value);
                        }
                        if (apiNames.Count > 0) break;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        ctx.Warn("获取 NeoForge maven-metadata 失败（" + metaUrl + "）：" + ex.Message);
                    }
                }
            }

            if (apiNames.Count == 0)
                throw new Exception("获取 NeoForge 版本列表失败：" + (lastError == null ? "没有可用数据" : lastError.Message), lastError);

            var list = new List<LoaderVersion>();
            foreach (var apiName in apiNames)
            {
                string inherit = McVersionOf(apiName);
                if (string.IsNullOrWhiteSpace(inherit)) continue;
                if (!string.Equals(inherit, mcVersion, StringComparison.OrdinalIgnoreCase)) continue;
                string display = DisplayVersionOf(apiName);
                list.Add(new LoaderVersion
                {
                    Version = display,
                    McVersion = mcVersion,
                    Display = display,
                    Kind = "neoforge",
                    ApiName = apiName,
                    VersionId = LoaderRegistry.BuildVersionId("neoforge", mcVersion, display),
                    IsBeta = IsBetaVersion(apiName),
                    IsRecommended = false,
                    IsLatest = false
                });
            }

            list.Sort((a, b) => LoaderCommon.CompareVersions(b.ApiName, a.ApiName));
            if (list.Count == 0)
            {
                ctx.Info("未找到 NeoForge 适用于 Minecraft " + mcVersion + " 的版本");
                return list;
            }
            list[0].IsLatest = true;
            var recommended = list.FirstOrDefault(v => !v.IsBeta) ?? list[0];
            recommended.IsRecommended = true;
            ctx.Info("获取到 NeoForge 版本 " + list.Count + " 个（MC " + mcVersion + "，推荐 " + recommended.Version + "）");
            return list;
        }

        /// <summary>安装 NeoForge。</summary>
        public static async Task<string> InstallAsync(InstallContext ctx, string mcVersion, string loaderVersion)
        {
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new Exception("缺少 Minecraft 版本，例如：--install loader neoforge 1.21.1 21.1.216");
            mcVersion = mcVersion.Trim();

            List<LoaderVersion> list = null;
            try { list = await GetVersionsAsync(ctx, mcVersion).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { ctx.Warn("获取 NeoForge 版本列表失败（若已指定版本则继续安装）：" + ex.Message); }

            string apiName = null;
            string displayVersion = null;
            if (string.IsNullOrWhiteSpace(loaderVersion))
            {
                if (list == null || list.Count == 0)
                    throw new Exception("未能获取 NeoForge 版本列表，请显式指定版本，例如：--install loader neoforge " + mcVersion + " 21.1.216");
                var pick = list.FirstOrDefault(v => v.IsRecommended) ?? list[0];
                apiName = pick.ApiName ?? pick.Version;
                displayVersion = pick.Version;
                ctx.Info("自动选择 NeoForge 版本：" + displayVersion);
            }
            else
            {
                loaderVersion = loaderVersion.Trim();
                if (string.Equals(mcVersion, "1.20.1", StringComparison.OrdinalIgnoreCase) &&
                    !loaderVersion.StartsWith("1.20.1-", StringComparison.OrdinalIgnoreCase))
                    apiName = "1.20.1-" + loaderVersion;
                else if (!string.Equals(mcVersion, "1.20.1", StringComparison.OrdinalIgnoreCase) &&
                         loaderVersion.StartsWith(mcVersion + "-", StringComparison.OrdinalIgnoreCase))
                    apiName = loaderVersion.Substring(mcVersion.Length + 1);
                else
                    apiName = loaderVersion;
                displayVersion = DisplayVersionOf(apiName);
            }

            // 校验版本与 MC 版本是否匹配
            string inherit = McVersionOf(apiName);
            if (!string.Equals(inherit, mcVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("NeoForge " + apiName + " 对应的 Minecraft 版本是 " + (inherit ?? "未知") +
                                    "，与请求的 " + mcVersion + " 不一致");
            }
            if (list != null && list.Count > 0 && !list.Any(v => string.Equals(v.ApiName ?? v.Version, apiName, StringComparison.OrdinalIgnoreCase)))
            {
                string preview = string.Join("、", list.Take(8).Select(v => v.Version));
                ctx.Warn("NeoForge " + apiName + " 不在版本列表中（可能是较旧/较新的版本），继续尝试安装。可用版本如：" + preview);
            }

            // 确保原版基础版本
            ctx.Progress("检查原版基础版本", 0.03);
            await LoaderCommon.EnsureVanillaAsync(ctx, mcVersion).ConfigureAwait(false);

            string targetId = LoaderRegistry.BuildVersionId("neoforge", mcVersion, displayVersion);
            string packageName = PackageNameOf(apiName, mcVersion);
            string artifactPath = "net/neoforged/" + packageName + "/" + apiName + "/" + packageName + "-" + apiName + "-installer.jar";
            // NeoForge 官方 maven 本机不可达；BMCLAPI 主站会 302 到多个后端，直连 cernet/ustc 镜像更稳定
            string[] installerUrls =
            {
                Bmclapi + "/maven/" + artifactPath,
                "https://mirrors.cernet.edu.cn/bmclapi/" + artifactPath,
                "https://cmcc.mirrors.ustc.edu.cn/bmclapi/" + artifactPath,
                "https://maven.neoforged.net/releases/" + artifactPath
            };

            var opt = new ForgelikeInstallOptions
            {
                LoaderName = "NeoForge",
                McVersion = mcVersion,
                LoaderVersion = displayVersion,
                ApiVersion = apiName,
                InstallerUrls = installerUrls,
                TargetId = targetId,
                PatchToMirror = true, // 官方 maven 不可达，必须改写为 BMCLAPI
                AllowMirrorRetry = true,
                PrefetchLibraries = true
            };
            ctx.Info("开始安装 NeoForge " + apiName + " → " + targetId);
            return await ForgelikeInstaller.RunAsync(ctx, opt).ConfigureAwait(false);
        }
    }
}
