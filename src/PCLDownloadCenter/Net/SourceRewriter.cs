using System;
using System.Collections.Generic;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 下载源 / 镜像地址改写。参考 PCL 的 DlSourceGet 与镜像替换逻辑。
    /// </summary>
    public static class SourceRewriter
    {
        private static readonly List<KeyValuePair<string, string>> MirrorRules = new List<KeyValuePair<string, string>>
        {
            // Mojang 元数据
            new KeyValuePair<string, string>("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com"),
            new KeyValuePair<string, string>("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com"),
            new KeyValuePair<string, string>("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com"),
            new KeyValuePair<string, string>("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com"),
            // 依赖库与资源
            new KeyValuePair<string, string>("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven"),
            new KeyValuePair<string, string>("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets"),
            // 加载器
            new KeyValuePair<string, string>("https://meta.fabricmc.net", "https://bmclapi2.bangbang93.com/fabric-meta"),
            new KeyValuePair<string, string>("https://meta.quiltmc.org", "https://bmclapi2.bangbang93.com/quilt-meta"),
            new KeyValuePair<string, string>("https://maven.fabricmc.net", "https://bmclapi2.bangbang93.com/maven"),
            new KeyValuePair<string, string>("https://maven.quiltmc.org", "https://bmclapi.bangbang93.com/maven"),
            new KeyValuePair<string, string>("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven"),
            new KeyValuePair<string, string>("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven"),
            new KeyValuePair<string, string>("https://maven.neoforged.net", "https://bmclapi2.bangbang93.com/maven"),
            new KeyValuePair<string, string>("https://files.minecraftforge.net/maven", "https://bmclapi2.bangbang93.com/maven"),
        };

        /// <summary>返回镜像地址；若有多种可能则返回多条候选（第一条最优先）。</summary>
        public static List<string> GetMirrorCandidates(string url)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(url)) return result;
            foreach (var rule in MirrorRules)
            {
                if (url.StartsWith(rule.Key, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(rule.Value + url.Substring(rule.Key.Length));
                    break;
                }
            }
            // Forge 安装器专用镜像
            var forge = TryForgeInstaller(url);
            if (forge != null) result.Add(forge);
            return result;
        }

        /// <summary>
        /// files.minecraftforge.net/net/minecraftforge/forge/&lt;mc&gt;-&lt;ver&gt;/forge-&lt;mc&gt;-&lt;ver&gt;-installer.jar
        /// → bmclapi forge 下载接口
        /// </summary>
        public static string TryForgeInstaller(string url)
        {
            try
            {
                if (url == null) return null;
                if (!url.Contains("forge") || !url.EndsWith("-installer.jar", StringComparison.OrdinalIgnoreCase)) return null;
                var name = url.Substring(url.LastIndexOf('/') + 1);
                if (!name.StartsWith("forge-", StringComparison.OrdinalIgnoreCase)) return null;
                var core = name.Substring("forge-".Length, name.Length - "forge-".Length - "-installer.jar".Length);
                var dash = core.IndexOf('-');
                if (dash <= 0) return null;
                var mc = core.Substring(0, dash);
                var ver = core.Substring(dash + 1);
                return "https://bmclapi2.bangbang93.com/forge/download?mcversion=" + Uri.EscapeDataString(mc) +
                       "&version=" + Uri.EscapeDataString(ver) + "&category=installer&format=jar";
            }
            catch { return null; }
        }

        /// <summary>把官方地址改写为指定下载源地址。</summary>
        public static string Apply(string url, string source)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (source == "official") return url;
            var cands = GetMirrorCandidates(url);
            return cands.Count > 0 ? cands[0] : url;
        }

        public static bool IsMirror(string url)
        {
            return url != null && url.Contains("bangbang93.com");
        }
    }
}
