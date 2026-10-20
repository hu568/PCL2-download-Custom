using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PCLDownloadCenter
{
    /// <summary>一个可安装的加载器版本。</summary>
    public sealed class LoaderVersion
    {
        /// <summary>加载器版本号（NeoForge 旧版 1.20.1 会去掉 "1.20.1-" 前缀）。</summary>
        public string Version { get; set; }
        public string McVersion { get; set; }
        public string Display { get; set; }
        public bool IsRecommended { get; set; }
        public bool IsLatest { get; set; }
        /// <summary>fabric / quilt / forge / neoforge / optifine / liteloader。</summary>
        public string Kind { get; set; }
        /// <summary>安装后的版本目录名 / 版本 ID，例如 1.21.4-Fabric_0.19.5。</summary>
        public string VersionId { get; set; }
        /// <summary>NeoForge 的原始 maven 版本名（如 21.1.216 / 1.20.1-47.1.106）。</summary>
        public string ApiName { get; set; }
        /// <summary>是否测试版（beta/alpha/snapshot）。</summary>
        public bool IsBeta { get; set; }
        public override string ToString() { return Display ?? Version; }
    }

    /// <summary>加载器注册表：版本列表与安装入口。</summary>
    public static class LoaderRegistry
    {
        public static readonly string[] Supported = { "fabric", "quilt", "forge", "neoforge", "optifine", "liteloader" };

        private sealed class CacheEntry
        {
            public List<LoaderVersion> List;
            public DateTime Time;
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CacheEntry> Cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

        /// <summary>把加载器名称统一为小写标准名（支持 neo / neo-forge / lite-loader 等别名）。</summary>
        public static string Normalize(string loader)
        {
            string l = (loader ?? "").Trim().ToLowerInvariant();
            switch (l)
            {
                case "fabric": return "fabric";
                case "quilt": return "quilt";
                case "forge": return "forge";
                case "neoforge":
                case "neo":
                case "neo-forge":
                case "neo forge":
                case "neoforged": return "neoforge";
                case "optifine":
                case "opti": return "optifine";
                case "liteloader":
                case "lite-loader":
                case "lite loader": return "liteloader";
                default: return l;
            }
        }

        public static bool IsSupported(string loader)
        {
            string l = Normalize(loader);
            foreach (var s in Supported)
                if (string.Equals(s, l, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>生成安装后的版本 ID：&lt;mc&gt;-&lt;Loader&gt;_&lt;版本&gt;，例如 1.21.4-Fabric_0.19.5。</summary>
        public static string BuildVersionId(string loader, string mcVersion, string loaderVersion)
        {
            string l = Normalize(loader);
            string name;
            switch (l)
            {
                case "fabric": name = "Fabric"; break;
                case "quilt": name = "Quilt"; break;
                case "forge": name = "Forge"; break;
                case "neoforge": name = "NeoForge"; break;
                case "optifine": name = "OptiFine"; break;
                case "liteloader": name = "LiteLoader"; break;
                default: name = "Loader"; break;
            }
            return (mcVersion ?? "").Trim() + "-" + name + "_" + (loaderVersion ?? "").Trim();
        }

        /// <summary>获取某个 MC 版本可安装的加载器版本列表（带推荐/最新标记）。</summary>
        public static async Task<List<LoaderVersion>> GetVersionsAsync(InstallContext ctx, string loader, string mcVersion)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            string l = Normalize(loader);
            string mc = (mcVersion ?? "").Trim();
            if (mc.Length == 0)
                throw new ArgumentException("缺少 Minecraft 版本，例如：1.21.4");
            if (!IsSupported(l))
                throw new NotSupportedException("不支持的加载器：" + (loader ?? "") + "，可用：fabric / quilt / forge / neoforge");
            if (l == "optifine" || l == "liteloader")
            {
                ctx.Info("暂不支持 " + (l == "optifine" ? "OptiFine" : "LiteLoader") + " 的版本列表");
                return new List<LoaderVersion>();
            }

            string key = l + "|" + mc;
            lock (CacheLock)
            {
                CacheEntry entry;
                if (Cache.TryGetValue(key, out entry) && DateTime.Now - entry.Time < CacheTtl)
                    return new List<LoaderVersion>(entry.List);
            }

            List<LoaderVersion> list;
            switch (l)
            {
                case "fabric":
                case "quilt":
                    list = await FabricInstaller.GetVersionsAsync(ctx, l, mc).ConfigureAwait(false);
                    break;
                case "forge":
                    list = await ForgeInstaller.GetVersionsAsync(ctx, mc).ConfigureAwait(false);
                    break;
                case "neoforge":
                    list = await NeoForgeInstaller.GetVersionsAsync(ctx, mc).ConfigureAwait(false);
                    break;
                default:
                    throw new NotSupportedException("不支持的加载器：" + l);
            }

            if (list != null)
            {
                lock (CacheLock)
                {
                    Cache[key] = new CacheEntry { List = new List<LoaderVersion>(list), Time = DateTime.Now };
                }
            }
            return list ?? new List<LoaderVersion>();
        }

        /// <summary>安装加载器，返回安装后的版本 ID（&lt;mc&gt;-&lt;Loader&gt;_&lt;版本&gt;）。</summary>
        public static async Task<string> InstallAsync(InstallContext ctx, string loader, string mcVersion, string loaderVersion)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            string l = Normalize(loader);
            string mc = (mcVersion ?? "").Trim();
            if (mc.Length == 0)
                throw new ArgumentException("缺少 Minecraft 版本，例如：--install loader fabric 1.21.4");
            if (!IsSupported(l))
                throw new NotSupportedException("不支持的加载器：" + (loader ?? "") + "，可用：fabric / quilt / forge / neoforge");

            ctx.Info("加载器安装：" + l + " / MC " + mc + (string.IsNullOrWhiteSpace(loaderVersion) ? "（自动选择版本）" : " / " + loaderVersion.Trim()));
            switch (l)
            {
                case "fabric":
                case "quilt":
                    return await FabricInstaller.InstallAsync(ctx, l, mc, loaderVersion).ConfigureAwait(false);
                case "forge":
                    return await ForgeInstaller.InstallAsync(ctx, mc, loaderVersion).ConfigureAwait(false);
                case "neoforge":
                    return await NeoForgeInstaller.InstallAsync(ctx, mc, loaderVersion).ConfigureAwait(false);
                case "optifine":
                    throw new NotSupportedException("暂不支持 OptiFine 安装：OptiFine 官方安装器需要图形界面，请使用官方安装器或 PCL 内置社区资源页。");
                case "liteloader":
                    throw new NotSupportedException("暂不支持 LiteLoader 安装：该加载器已停止更新，请使用官方安装器。");
                default:
                    throw new NotSupportedException("不支持的加载器：" + l);
            }
        }

        /// <summary>清空版本列表缓存（安装完成后可调用以便 Web UI 刷新）。</summary>
        public static void ClearCache()
        {
            lock (CacheLock) Cache.Clear();
        }
    }
}
