using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// Fabric / Quilt 加载器安装。
    /// 两者都使用 meta 服务器的 profile/json（仅包含加载器自身的依赖库，基础游戏由 inheritsFrom 指向原版）。
    /// </summary>
    public static class FabricInstaller
    {
        public const string FabricMeta = "https://meta.fabricmc.net";
        public const string QuiltMeta = "https://meta.quiltmc.org";

        private static string DisplayName(string loader)
        {
            return string.Equals(loader, "quilt", StringComparison.OrdinalIgnoreCase) ? "Quilt" : "Fabric";
        }

        /// <summary>获取某个 MC 版本可用的加载器版本列表（按新到旧排序）。</summary>
        public static async Task<List<LoaderVersion>> GetVersionsAsync(InstallContext ctx, string loader, string mcVersion)
        {
            string name = DisplayName(loader);
            bool isQuilt = string.Equals(loader, "quilt", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new ArgumentException("缺少 Minecraft 版本");

            string url = isQuilt
                ? QuiltMeta + "/v3/versions/loader/" + Uri.EscapeDataString(mcVersion.Trim())
                : FabricMeta + "/v2/versions/loader/" + Uri.EscapeDataString(mcVersion.Trim());

            string text;
            try
            {
                text = await ctx.Dl.GetStringAsync(url, ctx.Ct, 30000, 3, true).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new Exception("获取 " + name + " 版本列表失败（" + url + "）：" + ex.Message, ex);
            }

            JArray array;
            try
            {
                array = JArray.Parse(text);
            }
            catch (Exception ex)
            {
                throw new Exception(name + " 版本列表格式异常（可能该版本不受支持）：" + ex.Message, ex);
            }

            var list = new List<LoaderVersion>();
            foreach (var token in array)
            {
                var item = token as JObject;
                if (item == null) continue;
                var loaderObj = item["loader"] as JObject;
                if (loaderObj == null) continue;
                string version = JsonUtil.Str(loaderObj, "version");
                if (string.IsNullOrWhiteSpace(version)) continue;
                bool stable = JsonUtil.Bool(loaderObj, "stable", false);
                bool beta = isQuilt ? LooksUnstable(version) : !stable;
                list.Add(new LoaderVersion
                {
                    Version = version,
                    McVersion = mcVersion,
                    Display = version,
                    Kind = isQuilt ? "quilt" : "fabric",
                    VersionId = LoaderRegistry.BuildVersionId(isQuilt ? "quilt" : "fabric", mcVersion, version),
                    IsBeta = beta,
                    IsLatest = false,
                    IsRecommended = false
                });
            }

            if (list.Count == 0)
            {
                ctx.Info("未找到 " + name + " 适用于 Minecraft " + mcVersion + " 的版本");
                return list;
            }

            // Fabric 的接口本身按新到旧返回；Quilt 的顺序不可靠，统一排序
            if (isQuilt)
                list.Sort((a, b) => LoaderCommon.CompareVersions(b.Version, a.Version));
            else
                list.Sort((a, b) => LoaderCommon.CompareVersions(b.Version, a.Version));

            list[0].IsLatest = true;
            var recommended = list.FirstOrDefault(v => !v.IsBeta) ?? list[0];
            recommended.IsRecommended = true;
            ctx.Info("获取到 " + name + " 版本 " + list.Count + " 个（推荐 " + recommended.Version + "）");
            return list;
        }

        /// <summary>安装 Fabric / Quilt。</summary>
        public static async Task<string> InstallAsync(InstallContext ctx, string loader, string mcVersion, string loaderVersion)
        {
            bool isQuilt = string.Equals(loader, "quilt", StringComparison.OrdinalIgnoreCase);
            string name = DisplayName(loader);
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new Exception("缺少 Minecraft 版本，例如：--install loader " + (isQuilt ? "quilt" : "fabric") + " 1.21.4");
            mcVersion = mcVersion.Trim();

            // 1. 选择加载器版本
            if (string.IsNullOrWhiteSpace(loaderVersion))
            {
                ctx.Info("未指定 " + name + " 版本，正在获取版本列表……");
                var versions = await GetVersionsAsync(ctx, loader, mcVersion).ConfigureAwait(false);
                if (versions.Count == 0)
                    throw new Exception("未找到 " + name + " 适用于 Minecraft " + mcVersion + " 的版本");
                var pick = versions.FirstOrDefault(v => v.IsRecommended) ?? versions[0];
                loaderVersion = pick.Version;
                ctx.Info("自动选择 " + name + " 版本：" + loaderVersion);
            }
            loaderVersion = loaderVersion.Trim();

            string targetId = LoaderRegistry.BuildVersionId(isQuilt ? "quilt" : "fabric", mcVersion, loaderVersion);
            ctx.Info("目标版本：" + targetId + "（" + name + " " + loaderVersion + " + Minecraft " + mcVersion + "）");

            // 2. 确保原版基础版本
            ctx.Progress("检查原版基础版本", 0.05);
            await LoaderCommon.EnsureVanillaAsync(ctx, mcVersion).ConfigureAwait(false);

            // 3. 获取补丁 JSON
            ctx.Progress("获取 " + name + " 配置", 0.15);
            string metaBase = isQuilt ? QuiltMeta + "/v3" : FabricMeta + "/v2";
            string profileUrl = metaBase + "/versions/loader/" + Uri.EscapeDataString(mcVersion) + "/" +
                                Uri.EscapeDataString(loaderVersion) + "/profile/json";
            string profileText;
            try
            {
                ctx.Info("获取 " + name + " 配置：" + profileUrl);
                profileText = await ctx.Dl.GetStringAsync(profileUrl, ctx.Ct, 30000, 3, true).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new Exception("获取 " + name + " 配置失败（可能该加载器版本不支持 Minecraft " + mcVersion + "）：" + ex.Message, ex);
            }

            JObject profile;
            try { profile = JObject.Parse(profileText); }
            catch (Exception ex) { throw new Exception(name + " 配置 JSON 解析失败：" + ex.Message, ex); }

            var libraries = profile["libraries"] as JArray;
            if (libraries == null || libraries.Count == 0)
                throw new Exception(name + " 配置中没有 libraries 字段，无法安装");

            // 4. 修正 id / inheritsFrom 并写入版本目录
            profile["id"] = targetId;
            if (string.IsNullOrWhiteSpace(JsonUtil.Str(profile, "inheritsFrom")))
                profile["inheritsFrom"] = mcVersion;
            if (string.IsNullOrWhiteSpace(JsonUtil.Str(profile, "type")))
                profile["type"] = "release";

            string dir = LoaderCommon.VersionDir(ctx, targetId);
            FileUtil.EnsureDir(dir);
            string jsonPath = Path.Combine(dir, targetId + ".json");
            if (File.Exists(jsonPath))
            {
                ctx.Info("覆盖已存在的版本配置：" + jsonPath);
                FileUtil.SafeDelete(jsonPath);
            }
            LoaderCommon.WriteJson(jsonPath, profile);
            ctx.Info("已写入版本配置：" + jsonPath + "（inheritsFrom=" + profile["inheritsFrom"] + "，libraries=" + libraries.Count + " 个）");

            // 5. 下载加载器依赖库
            ctx.Progress("下载 " + name + " 依赖库", 0.25);
            await LoaderCommon.DownloadLibrariesAsync(ctx, profile, null, "下载 " + name + " 依赖库", 0.25, 0.7).ConfigureAwait(false);

            // 6. 校验
            if (!File.Exists(jsonPath))
                throw new Exception("版本配置写入后未找到：" + jsonPath);
            // 版本级 Java 区间：避免 PCL 在装有更新 Java 的机器上自动选到过新的 Java（见 JavaCompat）
            JavaCompat.Apply(ctx, dir);

            ctx.Progress(name + " 安装完成", 1.0);
            ctx.Info(name + " 安装完成：" + targetId);
            return targetId;
        }

        private static bool LooksUnstable(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return true;
            string v = version.ToLowerInvariant();
            return v.Contains("beta") || v.Contains("alpha") || v.Contains("pre") || v.Contains("rc") ||
                   v.Contains("snapshot") || v.Contains("-dev");
        }
    }
}
