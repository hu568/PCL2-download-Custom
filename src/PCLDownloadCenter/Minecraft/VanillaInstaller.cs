using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 原版游戏安装：
    /// 版本清单 → 版本 JSON → 客户端 jar → 日志配置 → 依赖库 → 资源索引 → 资源对象 → 校验。
    /// 幂等：已存在且校验通过的文件自动跳过；支持 ctx.Ct 取消。
    /// </summary>
    public static class VanillaInstaller
    {
        /// <summary>安装指定原版版本，返回版本号（与目录名一致）。</summary>
        public static async Task<string> InstallAsync(InstallContext ctx, string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId)) throw new ArgumentException("版本号不能为空（例如 1.21.4）");
            versionId = versionId.Trim();
            ctx = VersionManifest.EnsureContext(ctx);
            ctx.ThrowIfCancelled();

            // ---------- 1. 版本清单 ----------
            ctx.Progress("获取版本清单", 0.0);
            ManifestData manifest = await VersionManifest.GetAsync(ctx).ConfigureAwait(false);
            ManifestEntry entry = VersionManifest.FindOrThrow(manifest, versionId);
            ctx.Progress("获取版本清单", 0.02);
            ctx.Info("开始安装原版游戏 " + versionId + "（" + TypeText(entry.Type) + "，发布于 " +
                     entry.ReleaseTime.ToString("yyyy-MM-dd") + "）");

            string versionDir = VersionInfo.GetVersionDir(ctx, versionId);
            string jsonPath = VersionInfo.GetVersionJsonPath(ctx, versionId);
            FileUtil.EnsureDir(versionDir);
            ctx.Info("安装目录：" + versionDir);
            ctx.Info("下载源：" + (string.Equals(ctx.Cfg == null ? null : ctx.Cfg.DownloadSource, "bmclapi", StringComparison.OrdinalIgnoreCase) ? "BMCLAPI 镜像" : "Mojang 官方") +
                     "，并发线程：" + (ctx.Cfg == null ? 32 : ctx.Cfg.Threads));

            // ---------- 2. 版本 JSON ----------
            ctx.Progress("获取版本信息", 0.03);
            VersionInfo info = await EnsureVersionJsonAsync(ctx, entry, jsonPath).ConfigureAwait(false);
            ctx.Progress("获取版本信息", 0.04);

            // ---------- 3. 客户端 jar ----------
            JToken client = info.Downloads == null ? null : info.Downloads["client"];
            if (client == null)
                throw new Exception("版本“" + versionId + "”的 JSON 中没有客户端下载信息（downloads.client），无法自动安装。" +
                                    "如果是远古版本（Alpha/Beta），请改用 PCL 手动导入。");
            string clientUrl = JsonUtil.Str(client, "url");
            string clientSha1 = JsonUtil.Str(client, "sha1");
            long clientSize = JsonUtil.Long(client, "size", -1);
            if (string.IsNullOrWhiteSpace(clientUrl))
                throw new Exception("版本“" + versionId + "”的客户端下载地址为空，无法自动安装。");

            string jarPath = Path.Combine(versionDir, versionId + ".jar");
            ctx.Progress("下载游戏主文件", 0.04);
            ctx.Info("下载游戏主文件：" + (clientSize > 0 ? LibraryInstaller.FormatSize(clientSize) : "大小未知") +
                     " → " + Path.GetFileName(jarPath));
            try
            {
                string usedUrl = await ctx.Dl.DownloadFileAsync(clientUrl, jarPath, clientSha1, clientSize,
                    MapFileProgress(ctx, "下载游戏主文件", clientSize, 0.04, 0.10), ctx.Ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(usedUrl) && !string.Equals(usedUrl, clientUrl, StringComparison.OrdinalIgnoreCase))
                    ctx.Info("客户端文件实际下载地址：" + usedUrl);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new Exception("游戏主文件下载失败：" + ex.Message +
                                    "\n建议：切换下载源（--set-source bmclapi）后重试。", ex);
            }
            ctx.Progress("下载游戏主文件", 0.10);

            // ---------- 4. 日志配置（可选） ----------
            await DownloadLoggingAsync(ctx, info).ConfigureAwait(false);

            // ---------- 5. 依赖库 + natives ----------
            ctx.Progress("下载依赖库", 0.10);
            string nativesDir = Path.Combine(versionDir, versionId + "-natives");
            try
            {
                await LibraryInstaller.DownloadAsync(ctx, info.Json, nativesDir, (done, total) =>
                {
                    double ratio = total <= 0 ? 1.0 : (double)done / total;
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;
                    ctx.Progress("下载依赖库 " + done + "/" + total, 0.10 + 0.30 * ratio);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new Exception("依赖库安装失败：" + ex.Message +
                                    "\n建议：切换下载源（--set-source bmclapi）后重试。", ex);
            }
            ctx.Progress("下载依赖库", 0.40);

            // ---------- 6. 资源文件 ----------
            await InstallAssetsAsync(ctx, info, versionId).ConfigureAwait(false);

            // ---------- 7. Java 检查（仅提示，不影响安装成功） ----------
            CheckJava(ctx, info);

            // ---------- 8. 校验并完成 ----------
            ctx.Progress("校验安装结果", 0.96);
            VerifyInstall(ctx, info, versionDir, jsonPath, jarPath);

            ctx.Progress("完成", 1.0);
            // 版本级 Java 区间：避免 PCL 在装有更新 Java 的机器上自动选到过新的 Java（见 JavaCompat）
            JavaCompat.Apply(ctx, versionDir);

            ctx.Info("原版游戏 " + versionId + " 安装完成，可在 PCL 版本列表中启动。");
            return versionId;
        }

        /// <summary>
        /// 确保某个原版版本已安装（加载器 / 整合包安装前置依赖）。
        /// 已存在版本 JSON 与客户端 jar 时直接返回，否则执行完整安装。
        /// </summary>
        public static async Task<string> EnsureInstalledAsync(InstallContext ctx, string versionId)
        {
            if (string.IsNullOrWhiteSpace(versionId)) throw new ArgumentException("版本号不能为空");
            ctx = VersionManifest.EnsureContext(ctx);
            versionId = versionId.Trim();

            string jsonPath = VersionInfo.GetVersionJsonPath(ctx, versionId);
            string jarPath = VersionInfo.GetClientJarPath(ctx, versionId);
            if (File.Exists(jsonPath) && !string.IsNullOrWhiteSpace(jarPath) && File.Exists(jarPath))
            {
                ctx.Info("原版版本 " + versionId + " 已安装，跳过基础版本安装");
                return versionId;
            }
            return await InstallAsync(ctx, versionId).ConfigureAwait(false);
        }

        // ==================== 各阶段实现 ====================

        /// <summary>确保版本 JSON 存在于 versions/&lt;id&gt;/&lt;id&gt;.json，返回解析结果。</summary>
        private static async Task<VersionInfo> EnsureVersionJsonAsync(InstallContext ctx, ManifestEntry entry, string jsonPath)
        {
            if (File.Exists(jsonPath))
            {
                try
                {
                    // 清单项若带 sha1 则校验（v1 清单通常没有 sha1，此时只看能否解析）
                    if (string.IsNullOrWhiteSpace(entry.Sha1) ||
                        string.Equals(FileUtil.Sha1OfFile(jsonPath), entry.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        VersionInfo cached = VersionInfo.FromFile(jsonPath);
                        ctx.Info("版本 JSON 已存在，跳过下载：" + jsonPath);
                        return cached;
                    }
                }
                catch (Exception ex)
                {
                    ctx.Warn("本地版本 JSON 无法解析，将重新下载：" + ex.Message);
                }
            }

            string url = entry.Url;
            if (string.IsNullOrWhiteSpace(url))
                throw new Exception("版本“" + entry.Id + "”在版本清单中没有 JSON 下载地址。");
            ctx.Info("下载版本 JSON：" + url);
            byte[] bytes;
            try
            {
                bytes = await ctx.Dl.GetBytesAsync(url, ctx.Ct, 60000, 3, true).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // 网络失败时，若本地已有可解析的 JSON 则直接使用
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        VersionInfo fallback = VersionInfo.FromFile(jsonPath);
                        ctx.Warn("下载版本 JSON 失败，改用本地已有文件：" + ex.Message);
                        return fallback;
                    }
                    catch { }
                }
                throw new Exception("下载版本 JSON 失败：" + ex.Message +
                                    "\n建议：切换下载源（--set-source bmclapi）后重试。", ex);
            }

            if (!string.IsNullOrWhiteSpace(entry.Sha1))
            {
                string actual = FileUtil.Sha1OfBytes(bytes);
                if (!string.Equals(actual, entry.Sha1, StringComparison.OrdinalIgnoreCase))
                    ctx.Warn("版本 JSON 的 SHA1 与清单不一致（期望 " + entry.Sha1 + "，实际 " + actual + "），继续使用。");
            }

            FileUtil.WriteBytesAtomic(jsonPath, bytes);
            VersionInfo info = VersionInfo.FromJson(JsonUtil.Parse(Encoding.UTF8.GetString(bytes)));
            if (string.IsNullOrWhiteSpace(info.Id))
            {
                info.Id = entry.Id;
                info.Json["id"] = entry.Id;
            }
            if (info.Downloads == null || info.Downloads["client"] == null)
                throw new Exception("版本“" + entry.Id + "”的版本 JSON 中没有客户端下载信息（downloads.client）。");
            ctx.Info("版本 JSON 已保存：" + jsonPath + "（" + info.Id + "，" +
                     (info.MainClass ?? "?") + "，" + LibraryInstaller.FormatSize(new FileInfo(jsonPath).Length) + "）");
            return info;
        }

        /// <summary>下载 logging.client.file（assets/logging/&lt;id&gt;.xml，可选）。</summary>
        private static async Task DownloadLoggingAsync(InstallContext ctx, VersionInfo info)
        {
            try
            {
                JObject logging = info.Json["logging"] as JObject;
                JObject clientLogging = logging == null ? null : logging["client"] as JObject;
                JObject file = clientLogging == null ? null : clientLogging["file"] as JObject;
                if (file == null) return;

                string id = JsonUtil.Str(file, "id");
                string url = JsonUtil.Str(file, "url");
                if (string.IsNullOrWhiteSpace(url)) return;
                if (string.IsNullOrWhiteSpace(id)) id = Path.GetFileName(new Uri(url).AbsolutePath);
                if (string.IsNullOrWhiteSpace(id)) return;

                string target = Path.Combine(ctx.AssetsDir, "logging", id);
                string sha1 = JsonUtil.Str(file, "sha1");
                long size = JsonUtil.Long(file, "size", -1);
                await ctx.Dl.DownloadFileAsync(url, target, sha1, size, null, ctx.Ct).ConfigureAwait(false);
                ctx.Info("已下载日志配置：" + id);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                ctx.Warn("下载日志配置失败（不影响游戏启动）：" + ex.Message);
            }
        }

        /// <summary>下载资源索引与全部对象（assets/objects/前2位/hash）。</summary>
        private static async Task InstallAssetsAsync(InstallContext ctx, VersionInfo info, string versionId)
        {
            JObject indexMeta = info.Json["assetIndex"] as JObject;
            if (indexMeta == null)
            {
                ctx.Warn("版本 JSON 中没有 assetIndex，跳过资源文件（可能是极老版本）。");
                return;
            }
            string indexId = JsonUtil.Str(indexMeta, "id");
            if (string.IsNullOrWhiteSpace(indexId))
            {
                ctx.Warn("版本 JSON 的 assetIndex 没有 id，跳过资源文件。");
                return;
            }
            string indexUrl = JsonUtil.Str(indexMeta, "url");
            string indexSha1 = JsonUtil.Str(indexMeta, "sha1");
            long indexSize = JsonUtil.Long(indexMeta, "size", -1);

            string indexDir = Path.Combine(ctx.AssetsDir, "indexes");
            string indexPath = Path.Combine(indexDir, indexId + ".json");

            if (string.IsNullOrWhiteSpace(indexUrl))
            {
                if (!File.Exists(indexPath))
                {
                    ctx.Warn("资源索引 " + indexId + " 没有下载地址且本地不存在，跳过资源文件。");
                    return;
                }
            }
            else
            {
                ctx.Progress("下载资源索引", 0.40);
                try
                {
                    await ctx.Dl.DownloadFileAsync(indexUrl, indexPath, indexSha1, indexSize, null, ctx.Ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    if (!File.Exists(indexPath))
                        throw new Exception("资源索引下载失败（索引 " + indexId + "）：" + ex.Message +
                                            "\n建议：切换下载源（--set-source bmclapi）后重试。", ex);
                    ctx.Warn("资源索引下载失败，改用本地已有文件：" + ex.Message);
                }
                ctx.Info("资源索引已就绪：" + indexPath);
            }
            ctx.Progress("下载资源索引", 0.42);

            JObject indexJson;
            try
            {
                indexJson = JObject.Parse(File.ReadAllText(indexPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                throw new Exception("资源索引解析失败：" + indexPath + "（文件可能损坏，删除后重试）:: " + ex.Message, ex);
            }

            JObject objects = indexJson["objects"] as JObject;
            if (objects == null || objects.Count == 0)
            {
                ctx.Info("资源索引 " + indexId + " 没有 objects，跳过资源文件下载。");
                ctx.Progress("下载资源文件", 0.95);
                return;
            }

            List<DownloadItem> need = new List<DownloadItem>();
            List<KeyValuePair<string, string>> nameToRel = new List<KeyValuePair<string, string>>(objects.Count);
            long needBytes = 0;
            int existCount = 0;
            foreach (JProperty prop in objects.Properties())
            {
                JObject obj = prop.Value as JObject;
                if (obj == null) continue;
                string hash = JsonUtil.Str(obj, "hash");
                if (string.IsNullOrWhiteSpace(hash) || hash.Length < 2) continue;
                long size = JsonUtil.Long(obj, "size", -1);
                string rel = hash.Substring(0, 2) + "/" + hash;
                string path = Path.Combine(ctx.AssetsDir, "objects", hash.Substring(0, 2), hash);
                nameToRel.Add(new KeyValuePair<string, string>(prop.Name, rel));

                long have = FileUtil.SizeOf(path);
                if (have >= 0 && (size < 0 || have == size)) { existCount++; continue; }

                // 对象全局共享，地址固定：https://resources.download.minecraft.net/<前2位>/<hash>
                need.Add(new DownloadItem("https://resources.download.minecraft.net/" + rel, path, hash, size, prop.Name));
                if (size > 0) needBytes += size;
            }

            ctx.Info("资源文件（索引 " + indexId + "）：共 " + objects.Count + " 个对象，已存在 " + existCount +
                     " 个，需要下载 " + need.Count + " 个" +
                     (needBytes > 0 ? "（约 " + LibraryInstaller.FormatSize(needBytes) + "）" : ""));

            if (need.Count > 0)
            {
                try
                {
                    await ctx.Dl.DownloadManyAsync(need, (done, total) =>
                    {
                        double ratio = total <= 0 ? 1.0 : (double)done / total;
                        if (ratio < 0) ratio = 0;
                        if (ratio > 1) ratio = 1;
                        ctx.Progress("下载资源文件 " + done + "/" + total, 0.42 + 0.53 * ratio);
                    }, ctx.Ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    throw new Exception("资源文件下载失败：" + ex.Message +
                                        "\n建议：切换下载源（--set-source bmclapi）后重新执行安装（已下载的文件会跳过）。", ex);
                }
            }
            ctx.Progress("下载资源文件", 0.95);
            ctx.Info("资源对象已全部就绪（索引 " + indexId + "，共 " + objects.Count + " 个）");

            // 极老版本（1.6 及以前）需要虚拟资源目录
            bool virtualFlag = JsonUtil.Bool(indexJson, "virtual", false);
            bool mapToResources = JsonUtil.Bool(indexJson, "map_to_resources", false);
            if (virtualFlag)
            {
                // PCL 的 game_assets 变量固定指向 assets\virtual\legacy（见 ref/PCL ModMinecraft.vb McAssetsListGet
                // 与 ModLaunch.vb 中 game_assets 的替换），pre-1.6 的虚拟资源也合并到 legacy，故这里与 PCL 保持一致。
                string target = Path.Combine(ctx.AssetsDir, "virtual", "legacy");
                int n = CopyVirtualAssets(ctx, nameToRel, target);
                ctx.Info("已建立虚拟资源目录：" + target + "（索引 " + indexId + "，新增 " + n + " 个文件，PCL 的 game_assets 指向此处）");
            }
            if (mapToResources)
            {
                string target = Path.Combine(ctx.McDir, "resources");
                int n = CopyVirtualAssets(ctx, nameToRel, target);
                ctx.Info("已建立老版资源目录：" + target + "（" + n + " 个文件）");
            }
        }

        private static int CopyVirtualAssets(InstallContext ctx, List<KeyValuePair<string, string>> nameToRel, string targetDir)
        {
            int count = 0;
            string objectsRoot = Path.Combine(ctx.AssetsDir, "objects");
            foreach (KeyValuePair<string, string> pair in nameToRel)
            {
                ctx.ThrowIfCancelled();
                string src = Path.Combine(objectsRoot, pair.Value.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(src)) continue;
                string name = pair.Key.Replace('\\', '/').TrimStart('/');
                if (name.Length == 0) continue;
                string dest = Path.Combine(targetDir, name.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    FileUtil.EnsureDir(Path.GetDirectoryName(dest));
                    long srcSize = FileUtil.SizeOf(src);
                    if (File.Exists(dest) && FileUtil.SizeOf(dest) == srcSize) continue;
                    File.Copy(src, dest, true);
                    count++;
                }
                catch (Exception ex)
                {
                    ctx.Warn("复制资源文件失败（" + name + "）：" + ex.Message);
                }
            }
            return count;
        }

        /// <summary>最终校验：版本 JSON、客户端 jar、依赖库。</summary>
        private static void VerifyInstall(InstallContext ctx, VersionInfo info, string versionDir, string jsonPath, string jarPath)
        {
            if (!File.Exists(jsonPath))
                throw new Exception("安装失败：版本 JSON 未生成：" + jsonPath);
            if (!File.Exists(jarPath))
                throw new Exception("安装失败：游戏主文件未生成：" + jarPath);

            JToken client = info.Downloads == null ? null : info.Downloads["client"];
            string clientSha1 = client == null ? null : JsonUtil.Str(client, "sha1");
            if (!string.IsNullOrWhiteSpace(clientSha1))
            {
                string actual = FileUtil.Sha1OfFile(jarPath);
                if (!string.Equals(actual, clientSha1, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("安装失败：游戏主文件 SHA1 校验不通过（期望 " + clientSha1 +
                                        "，实际 " + actual + "），请删除后重新安装。");
            }

            int total;
            int missing = LibraryInstaller.Verify(ctx, info.Json, out total);
            if (missing > 0)
                throw new Exception("安装不完整：有 " + missing + "/" + total + " 个依赖库缺失。请重新执行安装补全（已下载的文件会跳过）。");

            ctx.Info("校验通过：版本 JSON、游戏主文件（SHA1 一致）、依赖库 (" + total + " 个) 均已就绪");
            ctx.Info("版本目录：" + versionDir);
        }

        /// <summary>探测本机 Java 是否满足该版本要求（只记录日志，不阻断安装）。</summary>
        private static void CheckJava(InstallContext ctx, VersionInfo info)
        {
            try
            {
                int major = info.JavaMajor;
                if (major <= 0) major = 8;
                List<JavaInfo> javas = JavaLocator.FindAllInfo();
                string chosen = JavaLocator.FindJavaForVersion(major);
                if (chosen == null)
                {
                    // 文件安装本身是成功的（PCL 也能列出版本），但启动需要 Java，必须给出可执行提示
                    ctx.Warn("游戏文件已安装完成，但" + JavaLocator.MissingHint(major));
                    return;
                }
                ctx.Info("该版本需要 Java " + major + "，将使用：" + chosen +
                         (javas.Count > 1 ? "（本机共探测到 " + javas.Count + " 个 Java）" : ""));
            }
            catch (Exception ex)
            {
                ctx.Warn("Java 探测失败（不影响安装）：" + ex.Message);
            }
        }

        // ==================== 小工具 ====================

        private static Action<long> MapFileProgress(InstallContext ctx, string phase, long totalSize, double from, double to)
        {
            return done =>
            {
                double ratio = totalSize > 0 ? (double)done / totalSize : 0;
                if (ratio < 0) ratio = 0;
                if (ratio > 1) ratio = 1;
                string text = phase + " " + (int)(ratio * 100) + "%";
                if (totalSize > 0) text += "（" + LibraryInstaller.FormatSize(done) + "/" + LibraryInstaller.FormatSize(totalSize) + "）";
                ctx.Progress(text, from + (to - from) * ratio);
            };
        }

        private static string TypeText(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "release": return "正式版";
                case "snapshot": return "快照";
                case "old_beta": return "远古 Beta";
                case "old_alpha": return "远古 Alpha";
                default: return string.IsNullOrWhiteSpace(type) ? "未知类型" : type;
            }
        }
    }
}
