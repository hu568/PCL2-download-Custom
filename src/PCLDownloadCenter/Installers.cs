using System;
using System.IO;
using System.Threading.Tasks;

namespace PCLDownloadCenter
{
    /// <summary>安装任务总入口：把 kind + 参数分派到具体的安装模块。</summary>
    public static class Installers
    {
        public static string Title(string kind, string[] args)
        {
            kind = (kind ?? "").ToLowerInvariant();
            args = args ?? new string[0];
            switch (kind)
            {
                case "vanilla":
                    return IsLatest(Join(args, 0)) ? "安装最新原版游戏" : "安装原版游戏 " + Join(args, 0);
                case "loader":
                    return "安装加载器 " + Join(args, 0) + " " + Join(args, 1) + " " + Join(args, 2);
                case "modpack":
                    return "安装整合包 " + Join(args, 0) + " " + Join(args, 1);
                case "packfile":
                case "modpack-file":
                    return "导入整合包 " + (args.Length > 0 ? Path.GetFileName(args[0]) : "");
                default:
                    return "任务 " + kind;
            }
        }

        /// <summary>latest / release / snapshot 这些别名由安装器自己解析。</summary>
        public static bool IsLatest(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            var v = version.Trim().ToLowerInvariant();
            return v == "latest" || v == "release" || v == "snapshot";
        }

        private static string Join(string[] args, int i)
        {
            return args != null && args.Length > i ? args[i] : "";
        }

        /// <summary>执行安装。</summary>
        public static async Task<InstallResult> RunAsync(InstallContext ctx, string kind, string[] args)
        {
            kind = (kind ?? "").ToLowerInvariant();
            args = args ?? new string[0];
            switch (kind)
            {
                case "vanilla":
                {
                    if (args.Length < 1) return InstallResult.Fail("缺少版本号，例如：--install vanilla 1.21.4");
                    var wanted = args[0];
                    if (IsLatest(wanted))
                    {
                        ctx.Info("正在获取最新版本号……");
                        var manifest = await VersionManifest.GetAsync(ctx, forceRefresh: true).ConfigureAwait(false);
                        if (manifest == null) return InstallResult.Fail("无法获取版本清单");
                        wanted = string.Equals(wanted, "snapshot", StringComparison.OrdinalIgnoreCase)
                            ? manifest.LatestSnapshot : manifest.LatestRelease;
                        if (string.IsNullOrEmpty(wanted)) return InstallResult.Fail("版本清单中没有最新版本信息");
                        ctx.Info("最新版本：" + wanted);
                    }
                    var id = await VanillaInstaller.InstallAsync(ctx, wanted).ConfigureAwait(false);
                    return InstallResult.Success(id, "原版游戏安装完成：" + id);
                }
                case "loader":
                {
                    if (args.Length < 2) return InstallResult.Fail("参数不足：--install loader <fabric|quilt|forge|neoforge> <mc版本> [加载器版本]");
                    var loader = args[0].ToLowerInvariant();
                    var mc = args[1];
                    var ver = args.Length > 2 ? args[2] : null;
                    var id = await LoaderRegistry.InstallAsync(ctx, loader, mc, ver).ConfigureAwait(false);
                    return InstallResult.Success(id, "加载器安装完成：" + id);
                }
                case "modpack":
                {
                    if (args.Length < 2) return InstallResult.Fail("参数不足：--install modpack <modrinth|curseforge|url> <id或网址> [版本]");
                    var source = args[0].ToLowerInvariant();
                    var id = args[1];
                    var ver = args.Length > 2 ? args[2] : null;
                    var name = await ModpackInstaller.InstallAsync(ctx, source, id, ver).ConfigureAwait(false);
                    return InstallResult.Success(name, "整合包安装完成：" + name);
                }
                case "packfile":
                case "modpack-file":
                {
                    if (args.Length < 1) return InstallResult.Fail("缺少整合包文件路径");
                    var name = await ModpackInstaller.InstallFromFileAsync(ctx, args[0]).ConfigureAwait(false);
                    return InstallResult.Success(name, "整合包导入完成：" + name);
                }
                default:
                    return InstallResult.Fail("未知的安装类型：" + kind);
            }
        }
    }
}