using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>整合包压缩包类型。</summary>
    internal enum PackArchiveKind
    {
        Unknown = 0,
        /// <summary>Modrinth 整合包（含 modrinth.index.json）。</summary>
        Modrinth = 1,
        /// <summary>CurseForge 整合包（manifest.json 且有 minecraft 字段）。</summary>
        CurseForge = 2,
        /// <summary>HMCL 整合包（modpack.json）。</summary>
        Hmcl = 3,
        /// <summary>MultiMC / Prism 整合包（mmc-pack.json）。</summary>
        Mmc = 4,
        /// <summary>MCBBS 整合包（mcbbs.packmeta 或 manifest.json 含 addons）。</summary>
        Mcbbs = 5,
        /// <summary>带启动器的压缩包（内含 modpack.zip / modpack.mrpack）。</summary>
        LauncherBundle = 6,
        /// <summary>普通压缩包：直接解压到实例目录。</summary>
        PlainZip = 7
    }

    /// <summary>整合包来源信息，用于实例命名、图标与日志。</summary>
    internal sealed class PackSourceInfo
    {
        /// <summary>建议的整合包名称（来自 Modrinth 项目 / 用户输入）。</summary>
        public string Name;
        /// <summary>是否强制使用 Name，而不是压缩包内的名称。</summary>
        public bool OverrideName;
        public string ProjectId;
        public string VersionLabel;
        /// <summary>来源描述：Modrinth / CurseForge / 本地文件 / 下载链接。</summary>
        public string Source = "本地文件";
        public string IconUrl;
        public string Description;
    }

    /// <summary>
    /// 原版 + 加载器的安装结果（合并成单版本实例之前的中间状态）。
    /// 两者都装在 <see cref="StageVersionsDir"/> 这个临时 versions 目录里，合并完成后由 <see cref="PackCore.CleanupStage"/> 删除。
    /// </summary>
    internal sealed class BaseInstall
    {
        public string MinecraftVersion;
        public string VanillaId;
        public string Loader;
        public string LoaderVersion;
        public string LoaderId;
        public bool LoaderInstalled;
        /// <summary>临时根目录（<c>&lt;scratch&gt;\stage</c>），随任务临时目录一起清理。</summary>
        public string StageRoot;
        /// <summary>临时 versions 目录（<c>&lt;scratch&gt;\stage\versions</c>）。</summary>
        public string StageVersionsDir;
        public string VanillaJsonPath;
        public string VanillaJarPath;
        public string LoaderJsonPath;
    }

    /// <summary>
    /// 整合包安装公共工具：实例命名、实例 JSON、版本隔离、路径安全、进度缩放、回滚等。
    /// </summary>
    internal static class PackCore
    {
        private static readonly char[] IllegalNameChars = "\\/:*?\"<>|!;".ToCharArray();

        private static readonly string[] ReservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        // ---------------- 名称与目录 ----------------

        /// <summary>清洗实例名：去掉 Windows 非法字符、过长截断、避开保留名。</summary>
        public static string SanitizeName(string raw, string fallback = "整合包")
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            var sb = new StringBuilder(raw.Length);
            foreach (var ch in raw.Trim())
            {
                if (ch < 32) continue;
                if (Array.IndexOf(IllegalNameChars, ch) >= 0) { sb.Append('_'); continue; }
                sb.Append(ch);
            }
            var name = Regex.Replace(sb.ToString(), "\\s+", " ").Trim().TrimEnd('.', ' ');
            if (name.Length > 60) name = name.Substring(0, 60).TrimEnd('.', ' ');
            if (name == "." || name == "..") name = "";
            if (name.Length == 0) name = fallback;
            if (Array.IndexOf(ReservedNames, name.ToUpperInvariant()) >= 0) name = "_" + name;
            return name;
        }

        /// <summary>清洗一个下载文件名（保留扩展名）。</summary>
        public static string SafeFileName(string raw, string fallback = "pack.zip")
        {
            var name = Path.GetFileName((raw ?? "").Split('?')[0].Split('#')[0]);
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (ch < 32) continue;
                if (Array.IndexOf(IllegalNameChars, ch) >= 0) { sb.Append('_'); continue; }
                sb.Append(ch);
            }
            var result = sb.ToString().Trim();
            if (result.Length > 150) result = result.Substring(result.Length - 150);
            if (result.Length == 0 || result == "." || result == "..") return fallback;
            return result;
        }

        /// <summary>在 versions 目录下分配一个不重名的实例名（重名时追加 _2、_3……）。</summary>
        public static string AllocateInstanceName(InstallContext ctx, string desired)
        {
            var baseName = SanitizeName(desired);
            var candidate = baseName;
            int index = 2;
            while (InstanceExists(ctx, candidate))
            {
                candidate = baseName + "_" + index;
                index++;
                if (index > 500)
                {
                    candidate = baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    break;
                }
            }
            return candidate;
        }

        private static bool InstanceExists(InstallContext ctx, string name)
        {
            var dir = Path.Combine(ctx.VersionsDir, name);
            return Directory.Exists(dir) || File.Exists(Path.Combine(dir, name + ".json"));
        }

        /// <summary>
        /// 把整合包内的相对路径解析到实例目录内，阻止 ../ 路径穿越。
        /// </summary>
        public static string ResolveInside(string rootDir, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) throw new Exception("整合包内存在空的文件路径");
            if (relative.IndexOf(':') >= 0) throw new Exception("整合包内文件路径非法（含盘符）：" + relative);
            var root = Path.GetFullPath(rootDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;
            var normalized = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, normalized));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new Exception("整合包内文件路径越界：" + relative);
            return full;
        }

        // ---------------- 实例文件 ----------------

        /// <summary>
        /// 生成「单版本」整合包实例：把原版 JSON 与加载器 JSON 合并成一个独立版本 JSON，
        /// 并把原版客户端 jar 复制为 versions\&lt;实例名&gt;\&lt;实例名&gt;.jar。
        /// 合并规则与 PCL 自己的整合包安装（ref/PCL ModDownloadLib.vb MergeJson）逐条一致：
        /// 原版 JSON 为底 → 合并加载器 JSON（数组拼接、加载器项覆盖）→ 去掉 inheritsFrom / jar →
        /// id 写实例名；旧版（1.6~1.12）的 minecraftArguments 先拼接再去重。
        /// 这样 versions 下只会留下整合包这一个目录，不会额外产生原版与加载器两个独立版本。
        /// 另外保留 clientVersion：PCL 的 McVersion.GetVanillaName 优先读该字段，版本号识别不靠猜。
        /// </summary>
        public static void WriteMergedInstance(InstallContext ctx, BaseInstall baseInfo, string instanceDir,
            string instanceName, string modpackVersion, string type = null)
        {
            if (baseInfo == null) throw new ArgumentNullException("baseInfo");
            FileUtil.EnsureDir(instanceDir);
            if (ctx != null && instanceDir.Length > 200)
                ctx.Warn("实例路径较长（" + instanceDir.Length + " 字符），overrides 中的深层文件可能触及 Windows 路径长度上限；" +
                         "如安装失败请缩短整合包名称或把 .minecraft 放到更短的路径。");

            var merged = BuildStandaloneJson(ctx, baseInfo.VanillaJsonPath, baseInfo.VanillaId,
                baseInfo.LoaderJsonPath, baseInfo.StageVersionsDir);
            merged["id"] = instanceName;
            if (!string.IsNullOrWhiteSpace(type)) merged["type"] = type;
            else if (merged["type"] == null) merged["type"] = "release";
            if (!string.IsNullOrWhiteSpace(baseInfo.MinecraftVersion)) merged["clientVersion"] = baseInfo.MinecraftVersion;
            if (!string.IsNullOrEmpty(modpackVersion)) merged["modpackVersion"] = modpackVersion;
            if (merged["libraries"] == null) merged["libraries"] = new JArray();

            string jsonPath = Path.Combine(instanceDir, instanceName + ".json");
            FileUtil.WriteAllTextAtomic(jsonPath, merged.ToString(Newtonsoft.Json.Formatting.Indented));
            if (ctx != null)
                ctx.Info("已写入合并后的实例 JSON：" + jsonPath + "（原版 " + baseInfo.MinecraftVersion +
                         (string.IsNullOrWhiteSpace(baseInfo.LoaderId)
                             ? ""
                             : " + " + LoaderDisplay(baseInfo.Loader) + " " + baseInfo.LoaderVersion) +
                         "，无 inheritsFrom）");
            CopyClientJar(ctx, baseInfo.VanillaJarPath, instanceDir, instanceName);
        }

        /// <summary>
        /// 把「已安装的某个版本」转写成独立实例（普通压缩包无法识别版本时的兜底）：
        /// 合并它的 inheritsFrom 链并复制客户端 jar，使实例不再依赖 versions\&lt;原版&gt; 目录。
        /// </summary>
        public static void WriteStandaloneFromInstalled(InstallContext ctx, string versionId, string instanceDir, string instanceName)
        {
            if (string.IsNullOrWhiteSpace(versionId)) throw new ArgumentException("缺少版本号");
            string jsonPath = Path.Combine(ctx.VersionsDir, versionId, versionId + ".json");
            if (!File.Exists(jsonPath))
                throw new Exception("找不到已安装的版本 JSON：" + jsonPath);
            var merged = BuildStandaloneJson(ctx, jsonPath, versionId, null, null);
            merged["id"] = instanceName;
            merged["clientVersion"] = versionId;
            if (merged["type"] == null) merged["type"] = "release";
            if (merged["libraries"] == null) merged["libraries"] = new JArray();
            FileUtil.EnsureDir(instanceDir);
            string target = Path.Combine(instanceDir, instanceName + ".json");
            FileUtil.WriteAllTextAtomic(target, merged.ToString(Newtonsoft.Json.Formatting.Indented));
            if (ctx != null) ctx.Info("已写入独立实例 JSON：" + target);
            var jarRoots = new List<string> { ctx.VersionsDir };
            CopyClientJar(ctx, FindClientJar(ctx, versionId, jarRoots), instanceDir, instanceName);
        }

        /// <summary>
        /// 沿 inheritsFrom 链找出客户端主 jar（jar 通常在链最深的原版版本目录里），
        /// 其次看 JSON 的 jar 字段；找不到时返回最深一层的推测路径。
        /// </summary>
        private static string FindClientJar(InstallContext ctx, string versionId, List<string> searchRoots)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string current = versionId;
            string candidate = null;
            for (int depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(current); depth++)
            {
                if (!seen.Add(current)) break;
                string jar = Path.Combine(ctx.VersionsDir, current, current + ".jar");
                candidate = jar;
                if (File.Exists(jar)) return jar;
                string jsonPath = FindVersionJson(searchRoots, current);
                if (jsonPath == null) break;
                JObject json;
                try { json = LoaderCommon.ReadJson(jsonPath); }
                catch { break; }
                var jarField = JsonUtil.Str(json, "jar", null);
                if (!string.IsNullOrWhiteSpace(jarField))
                {
                    string jarPath = Path.Combine(ctx.VersionsDir, jarField, jarField + ".jar");
                    candidate = jarPath;
                    if (File.Exists(jarPath)) return jarPath;
                }
                current = JsonUtil.Str(json, "inheritsFrom", null);
            }
            return candidate;
        }

        /// <summary>把原版 JSON 与加载器 JSON（含各自的 inheritsFrom 链）合并成一个独立版本 JSON。</summary>
        public static JObject BuildStandaloneJson(InstallContext ctx, string vanillaJsonPath, string vanillaId,
            string loaderJsonPath, string stageVersionsDir)
        {
            if (string.IsNullOrWhiteSpace(vanillaJsonPath) || !File.Exists(vanillaJsonPath))
                throw new Exception("找不到原版版本 JSON：" + vanillaJsonPath);

            var roots = new List<string>();
            if (!string.IsNullOrWhiteSpace(stageVersionsDir)) roots.Add(stageVersionsDir);
            if (ctx != null && !string.IsNullOrWhiteSpace(ctx.VersionsDir)) roots.Add(ctx.VersionsDir);

            var argsParts = new List<string>();
            var merged = LoadWithInherits(ctx, vanillaJsonPath, vanillaId, roots,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), argsParts, 0, false);
            if (!string.IsNullOrWhiteSpace(loaderJsonPath) && File.Exists(loaderJsonPath))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(vanillaId)) seen.Add(vanillaId);
                // 加载器 JSON 的 time/releaseTime 不覆盖原版的（与 PCL MergeJson 一致，避免 PCL 显示 1970 年）
                var patch = LoadWithInherits(ctx, loaderJsonPath, vanillaId, roots, seen, argsParts, 0, true);
                merged.Merge(patch); // 数组按 PCL 的方式拼接：原版在前、加载器在后
            }

            merged.Remove("_comment_");
            merged.Remove("inheritsFrom");
            merged.Remove("jar");
            if (argsParts.Count > 0) merged["minecraftArguments"] = CombineArguments(argsParts);
            return merged;
        }

        /// <summary>读取版本 JSON 并合并它的 inheritsFrom 父版本（父级在下、本级覆盖在上）。</summary>
        private static JObject LoadWithInherits(InstallContext ctx, string jsonPath, string stopId,
            List<string> searchRoots, HashSet<string> seen, List<string> argsParts, int depth, bool stripTime)
        {
            var json = LoaderCommon.ReadJson(jsonPath);
            var args = JsonUtil.Str(json, "minecraftArguments", null);
            if (!string.IsNullOrWhiteSpace(args)) argsParts.Add(args);
            var inherit = JsonUtil.Str(json, "inheritsFrom", null);
            if (stripTime)
            {
                json.Remove("time");
                json.Remove("releaseTime");
            }
            json.Remove("inheritsFrom");
            if (string.IsNullOrWhiteSpace(inherit)) return json;
            if (!string.IsNullOrWhiteSpace(stopId) && string.Equals(inherit, stopId, StringComparison.OrdinalIgnoreCase))
                return json;
            if (depth >= 8 || !seen.Add(inherit))
            {
                if (ctx != null) ctx.Warn("版本继承链过深或出现重复，已停止合并：" + inherit);
                return json;
            }
            string parentPath = FindVersionJson(searchRoots, inherit);
            if (parentPath == null)
            {
                if (ctx != null) ctx.Warn("找不到继承版本 " + inherit + " 的 JSON，合并结果可能缺少父级依赖");
                return json;
            }
            var parent = LoadWithInherits(ctx, parentPath, stopId, searchRoots, seen, argsParts, depth + 1, true);
            parent.Merge(json);
            return parent;
        }

        private static string FindVersionJson(List<string> searchRoots, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || searchRoots == null) return null;
            foreach (var root in searchRoots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                try
                {
                    string path = Path.Combine(root, id, id + ".json");
                    if (File.Exists(path)) return path;
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// 旧版版本用 minecraftArguments 字符串描述启动参数，多个来源需要拼接后去重
        /// （与 PCL MergeJson 的处理一致：紧跟 - 开头的参数与其值合并为一个词，再去重）。
        /// </summary>
        private static string CombineArguments(List<string> parts)
        {
            var tokens = new List<string>();
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                foreach (var raw in part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var token = raw.Trim();
                    if (token.Length == 0) continue;
                    if (!token.StartsWith("-", StringComparison.Ordinal) && tokens.Count > 0)
                    {
                        var last = tokens[tokens.Count - 1];
                        if (last.StartsWith("-", StringComparison.Ordinal) && last.IndexOf(' ') < 0)
                        {
                            tokens[tokens.Count - 1] = last + " " + token;
                            continue;
                        }
                    }
                    tokens.Add(token);
                }
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();
            foreach (var token in tokens)
                if (seen.Add(token)) result.Add(token);
            return string.Join(" ", result.ToArray());
        }

        /// <summary>把原版客户端 jar 复制成 &lt;实例名&gt;.jar（合并后的版本没有 jar 字段，PCL 按实例名找主 jar）。</summary>
        private static void CopyClientJar(InstallContext ctx, string sourceJar, string instanceDir, string instanceName)
        {
            if (string.IsNullOrWhiteSpace(sourceJar) || !File.Exists(sourceJar))
            {
                if (ctx != null) ctx.Warn("未找到原版客户端主文件（" + (sourceJar ?? "null") + "），PCL 启动时会尝试自行补全");
                return;
            }
            string target = Path.Combine(instanceDir, instanceName + ".jar");
            try
            {
                File.Copy(sourceJar, target, true);
                if (ctx != null) ctx.Info("已复制客户端主文件：" + target + "（" + FormatSize(FileUtil.SizeOf(target)) + "）");
            }
            catch (Exception ex)
            {
                throw new Exception("复制客户端主文件失败：" + ex.Message, ex);
            }
        }

        /// <summary>
        /// 写入版本隔离等 PCL 实例配置（&lt;实例目录&gt;\PCL\Setup.ini，只写实例目录，不碰全局 PCL\Setup.ini）。
        /// 全部为“逐键合并”：保留整合包 overrides 自带或用户已有的其它键。
        /// 注意：PCL 对 Setup.ini 有内存缓存，因此本模块只创建全新实例名（AllocateInstanceName 重名加后缀），
        /// 不支持覆盖同名实例；否则 PCL 可能用旧缓存回写覆盖这些键。
        /// VersionModpackSource/Id/Version 目前 PCL 2.99 并不读取（写入仅便于本工具/主页展示）。
        /// 最后还会调用 JavaCompat 写入版本级 Java 区间（见 JavaCompat 的说明）。
        /// </summary>
        public static void WriteInstanceSetup(string instanceDir, string vanillaName, string customInfo,
            PackSourceInfo info, string modpackVersion, string loader = null, string loaderVersion = null,
            InstallContext ctx = null)
        {
            var iniDir = Path.Combine(instanceDir, "PCL");
            FileUtil.EnsureDir(iniDir);
            var ini = Path.Combine(iniDir, "Setup.ini");
            SetIniKey(ini, "VersionArgumentIndie", "1");      // 开启版本隔离
            SetIniKey(ini, "VersionArgumentIndieV2", "True"); // 使用新版隔离逻辑
            if (!string.IsNullOrWhiteSpace(vanillaName)) SetIniKey(ini, "VersionVanillaName", vanillaName);
            if (!string.IsNullOrWhiteSpace(customInfo)) SetIniKey(ini, "CustomInfo", customInfo);
            // 加载器缓存键（PCL 首次扫描时会自行写回，这里预写可让缓存路径直接显示加载器信息）
            if (!string.IsNullOrWhiteSpace(loader) && !string.IsNullOrWhiteSpace(loaderVersion))
            {
                var kind = loader.Trim().ToLowerInvariant();
                string key = null;
                if (kind == "fabric" || kind == "quilt") key = "VersionFabric"; // PCL 把 Quilt 也识别为 Fabric
                else if (kind == "forge") key = "VersionForge";
                else if (kind == "neoforge" || kind == "neo-forge") key = "VersionNeoForge";
                if (key != null) SetIniKey(ini, key, loaderVersion);
            }
            if (info != null)
            {
                if (!string.IsNullOrWhiteSpace(info.Source)) SetIniKey(ini, "VersionModpackSource", info.Source);
                if (!string.IsNullOrWhiteSpace(info.ProjectId)) SetIniKey(ini, "VersionModpackId", info.ProjectId);
            }
            if (!string.IsNullOrWhiteSpace(modpackVersion)) SetIniKey(ini, "VersionModpackVersion", modpackVersion);

            // 版本级 Java 区间：避免 PCL 在装有更新 Java 的机器上选到过新的 Java（见 JavaCompat）
            JavaCompat.Apply(ctx, instanceDir);
        }

        /// <summary>设置实例图标（PCL 使用 PCL\Logo.png + LogoCustom）。</summary>
        public static async System.Threading.Tasks.Task TrySetIconAsync(InstallContext ctx, string instanceDir, string iconUrl)
        {
            if (string.IsNullOrWhiteSpace(iconUrl)) return;
            // 整合包 overrides 自带 PCL/Logo.png（LogoCustom=True）时保留自带图标，避免被在线图标覆盖
            var existingIni = Path.Combine(instanceDir, "PCL", "Setup.ini");
            if (string.Equals(GetIniKey(existingIni, "LogoCustom"), "True", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(instanceDir, "PCL", "Logo.png")))
            {
                if (ctx != null) ctx.Info("整合包已自带图标，跳过覆盖");
                return;
            }
            try
            {
                var bytes = await ctx.Dl.GetBytesAsync(iconUrl, ctx.Ct, 20000, 2, false).ConfigureAwait(false);
                if (bytes == null || bytes.Length < 100) return;
                var logoDir = Path.Combine(instanceDir, "PCL");
                FileUtil.EnsureDir(logoDir);
                FileUtil.WriteBytesAtomic(Path.Combine(logoDir, "Logo.png"), bytes);
                SetIniKey(Path.Combine(logoDir, "Setup.ini"), "Logo", "PCL\\Logo.png");
                SetIniKey(Path.Combine(logoDir, "Setup.ini"), "LogoCustom", "True");
                if (ctx != null) ctx.Info("已设置整合包图标");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (ctx != null) ctx.Warn("下载整合包图标失败（不影响安装）：" + ex.Message);
            }
        }

        /// <summary>安装过程中写入 .pclignore，避免 PCL 把半成品实例列出来。</summary>
        public static void MarkInstalling(string instanceDir)
        {
            try
            {
                FileUtil.EnsureDir(instanceDir);
                File.WriteAllText(Path.Combine(instanceDir, ".pclignore"),
                    "安装中：此文件由 PCL 下载中心创建，安装完成后自动删除。", new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>安装完成后移除 .pclignore。</summary>
        public static void FinishInstalling(string instanceDir)
        {
            try { FileUtil.SafeDelete(Path.Combine(instanceDir, ".pclignore")); } catch { }
        }

        /// <summary>回滚：删除本次创建的不完整实例目录。</summary>
        public static void Rollback(InstallContext ctx, string instanceDir, string instanceName)
        {
            if (string.IsNullOrWhiteSpace(instanceDir)) return;
            try
            {
                if (Directory.Exists(instanceDir))
                {
                    if (ctx != null) ctx.Warn("安装未完成，正在清理不完整的实例目录：" + instanceName);
                    FileUtil.SafeDeleteDir(instanceDir);
                }
            }
            catch (Exception ex)
            {
                if (ctx != null) ctx.Warn("清理不完整实例目录失败，请手动删除：" + instanceDir + "（" + ex.Message + "）");
            }
        }

        // ---------------- Setup.ini ----------------

        /// <summary>读取 Setup.ini 的键值（找不到返回 null）。</summary>
        public static string GetIniKey(string iniPath, string key)
        {
            try
            {
                if (!File.Exists(iniPath)) return null;
                foreach (var line in File.ReadAllLines(iniPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var idx = line.IndexOf(':');
                    if (idx <= 0) continue;
                    if (string.Equals(line.Substring(0, idx).Trim(), key, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(idx + 1);
                }
            }
            catch { }
            return null;
        }

        /// <summary>设置 PCL 的 Setup.ini 键（格式为 Key:Value，保留其它键）。</summary>
        public static void SetIniKey(string iniPath, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || value == null) return;
            value = value.Replace("\r", "").Replace("\n", "");
            var lines = new List<string>();
            if (File.Exists(iniPath))
            {
                try
                {
                    foreach (var raw in File.ReadAllLines(iniPath, Encoding.UTF8))
                    {
                        // PCL 的 ini 解析要求“键:值”且冒号位置 > 0；顺手丢弃空行/无冒号行，保持文件干净
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        if (raw.IndexOf(':') <= 0) continue;
                        lines.Add(raw);
                    }
                }
                catch { }
            }
            var replaced = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null) continue;
                var idx = line.IndexOf(':');
                var k = idx < 0 ? line.Trim() : line.Substring(0, idx).Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = key + ":" + value;
                    replaced = true;
                }
            }
            if (!replaced) lines.Add(key + ":" + value);
            FileUtil.WriteAllTextAtomic(iniPath, string.Join("\r\n", lines) + "\r\n");
        }

        // ---------------- 压缩包辅助 ----------------

        /// <summary>在压缩包内按名称（忽略大小写）查找条目。</summary>
        public static ZipArchiveEntry FindEntry(ZipArchive zip, string entryName)
        {
            if (zip == null || string.IsNullOrWhiteSpace(entryName)) return null;
            var exact = zip.GetEntry(entryName);
            if (exact != null) return exact;
            var normalized = entryName.Replace('\\', '/');
            foreach (var e in zip.Entries)
                if (string.Equals(e.FullName.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        /// <summary>读取压缩包内某个文本条目（忽略大小写），找不到返回 null。</summary>
        public static string ReadEntryText(string zipPath, string entryName)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var entry = FindEntry(zip, entryName);
                if (entry == null) return null;
                using (var stream = entry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// 查找关键文件所在的前缀（"" 或 "目录/"）。
        /// 优先根目录；否则允许一层包裹目录。找不到返回 null。
        /// </summary>
        public static string FindEntryBaseFolder(string zipPath, string fileName)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var names = new List<string>();
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    names.Add(e.FullName.Replace('\\', '/'));
                }
                // 根目录
                foreach (var n in names)
                    if (string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase)) return "";
                // 任意层级的同名文件，取路径最短的
                string best = null;
                foreach (var n in names)
                {
                    if (!n.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (best == null || n.Length < best.Length) best = n;
                }
                if (best == null) return null;
                return best.Substring(0, best.Length - fileName.Length);
            }
        }

        /// <summary>返回压缩包的唯一顶层目录名；根目录有文件或没有顶层目录时返回 null。</summary>
        public static string GetSingleTopFolder(string zipPath)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                string first = null;
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    var n = e.FullName.Replace('\\', '/');
                    var idx = n.IndexOf('/');
                    if (idx <= 0) return null; // 根目录存在文件
                    var seg = n.Substring(0, idx);
                    if (first == null) first = seg;
                    else if (!string.Equals(first, seg, StringComparison.OrdinalIgnoreCase)) return null;
                }
                return first;
            }
        }

        /// <summary>判断压缩包条目里是否存在某个文件（忽略大小写，任意层级）。</summary>
        public static bool HasEntry(string zipPath, string fileName, bool rootOnly)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    var n = e.FullName.Replace('\\', '/');
                    if (rootOnly)
                    {
                        if (string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    else if (string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase) ||
                             n.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }

        // ---------------- 类型识别 ----------------

        /// <summary>识别压缩包属于哪种整合包格式。</summary>
        public static PackArchiveKind DetectArchiveKind(string zipPath)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                // 优先检查根目录
                if (FindEntry(zip, "modrinth.index.json") != null) return PackArchiveKind.Modrinth;
                if (FindEntry(zip, "mmc-pack.json") != null) return PackArchiveKind.Mmc;
                if (FindEntry(zip, "modpack.json") != null) return PackArchiveKind.Hmcl;
                if (FindEntry(zip, "mcbbs.packmeta") != null) return PackArchiveKind.Mcbbs;
                if (FindEntry(zip, "modpack.zip") != null || FindEntry(zip, "modpack.mrpack") != null)
                    return PackArchiveKind.LauncherBundle;

                var manifest = FindEntry(zip, "manifest.json");
                if (manifest != null)
                {
                    var text = ReadEntry(manifest);
                    return ClassifyManifest(text);
                }

                // 一层包裹目录
                foreach (var key in new[] { "modrinth.index.json", "mmc-pack.json", "modpack.json", "mcbbs.packmeta", "manifest.json" })
                {
                    ZipArchiveEntry nested = null;
                    foreach (var e in zip.Entries)
                    {
                        if (string.IsNullOrEmpty(e.Name)) continue;
                        var n = e.FullName.Replace('\\', '/');
                        if (!n.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase)) continue;
                        if (n.Count(c => c == '/') != 1) continue; // 仅一层
                        nested = e;
                        break;
                    }
                    if (nested == null) continue;
                    switch (key)
                    {
                        case "modrinth.index.json": return PackArchiveKind.Modrinth;
                        case "mmc-pack.json": return PackArchiveKind.Mmc;
                        case "modpack.json": return PackArchiveKind.Hmcl;
                        case "mcbbs.packmeta": return PackArchiveKind.Mcbbs;
                        case "manifest.json": return ClassifyManifest(ReadEntry(nested));
                    }
                }

                if (FindEntry(zip, "modpack.zip") != null || FindEntry(zip, "modpack.mrpack") != null)
                    return PackArchiveKind.LauncherBundle;
                return PackArchiveKind.PlainZip;
            }
        }

        private static PackArchiveKind ClassifyManifest(string text)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var obj = JObject.Parse(text);
                    if (obj["minecraft"] != null) return PackArchiveKind.CurseForge;
                    if (obj["addons"] != null) return PackArchiveKind.Mcbbs;
                }
            }
            catch { }
            // manifest.json 无法解析或不是已知格式：按普通压缩包处理，避免误导成“需要 CurseForge API Key”
            return PackArchiveKind.PlainZip;
        }

        private static string ReadEntry(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // ---------------- 版本与加载器 ----------------

        /// <summary>加载器显示名。</summary>
        public static string LoaderDisplay(string loader)
        {
            switch ((loader ?? "").Trim().ToLowerInvariant())
            {
                case "fabric": return "Fabric";
                case "quilt": return "Quilt";
                case "forge": return "Forge";
                case "neoforge":
                case "neo-forge": return "NeoForge";
                case "optifine": return "OptiFine";
                case "liteloader": return "LiteLoader";
                case "": return "原版";
                default: return loader;
            }
        }

        /// <summary>
        /// 把整合包依赖的原版与加载器先装进临时 versions 目录，返回中间结果；
        /// 合并成单版本实例后必须调用 <see cref="CleanupStage"/> 删除临时目录。
        /// lo~hi 为本次安装占用的总进度区间。
        /// </summary>
        public static async System.Threading.Tasks.Task<BaseInstall> InstallBaseStagedAsync(
            InstallContext ctx, string mcVersion, string loader, string loaderVersion, double lo, double hi)
        {
            if (ctx == null) throw new ArgumentNullException("ctx");
            if (string.IsNullOrWhiteSpace(mcVersion))
                throw new Exception("整合包未提供 Minecraft 版本信息，无法安装");

            mcVersion = mcVersion.Trim();
            var result = new BaseInstall
            {
                MinecraftVersion = mcVersion,
                Loader = (loader ?? "").Trim().ToLowerInvariant(),
                LoaderVersion = loaderVersion
            };
            var hasLoader = !string.IsNullOrWhiteSpace(result.Loader);
            if (hasLoader &&
                (string.Equals(result.Loader, "optifine", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(result.Loader, "liteloader", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("整合包所需的 " + LoaderDisplay(result.Loader) + " 加载器暂不支持，请改用其他整合包");
            var vanillaHi = hasLoader ? lo + (hi - lo) * 0.7 : hi;

            // 原版与加载器都装在临时 versions 目录里，合并成单版本实例后整目录删除；
            // 这样 .minecraft\versions 下不会残留原版 / 加载器两个独立版本（只留整合包一个版本）。
            result.StageRoot = ctx.Scratch("stage");
            result.StageVersionsDir = Path.Combine(result.StageRoot, "versions");
            FileUtil.EnsureDir(result.StageVersionsDir);
            var stageCtx = ctx.WithVersionsDir(result.StageVersionsDir);
            try
            {
                ctx.Info("原版与加载器将先安装到临时目录并合并为单个实例，不额外保留独立版本");
                ctx.Info("开始安装整合包依赖的原版游戏 " + mcVersion + "……");
                using (new ScaledProgress(stageCtx, lo, vanillaHi))
                {
                    result.VanillaId = await VanillaInstaller.InstallAsync(stageCtx, mcVersion).ConfigureAwait(false);
                }
                if (string.IsNullOrWhiteSpace(result.VanillaId)) result.VanillaId = mcVersion;
                result.VanillaJsonPath = Path.Combine(result.StageVersionsDir, result.VanillaId, result.VanillaId + ".json");
                result.VanillaJarPath = Path.Combine(result.StageVersionsDir, result.VanillaId, result.VanillaId + ".jar");
                ctx.Info("原版游戏已就绪（临时目录）：" + result.VanillaId);

                if (hasLoader)
                {
                    ctx.Info("正在安装 " + LoaderDisplay(result.Loader) + " 加载器" +
                             (string.IsNullOrWhiteSpace(loaderVersion) ? "（自动选择版本）" : " " + loaderVersion) + "……");
                    using (new ScaledProgress(stageCtx, vanillaHi, hi))
                    {
                        result.LoaderId = await LoaderRegistry.InstallAsync(stageCtx, result.Loader, mcVersion, loaderVersion).ConfigureAwait(false);
                    }
                    if (string.IsNullOrWhiteSpace(result.LoaderId))
                        throw new Exception(LoaderDisplay(result.Loader) + " 加载器安装未返回版本号");
                    result.LoaderInstalled = true;
                    result.LoaderJsonPath = Path.Combine(result.StageVersionsDir, result.LoaderId, result.LoaderId + ".json");
                    if (!File.Exists(result.LoaderJsonPath))
                        throw new Exception("加载器安装后未找到版本 JSON：" + result.LoaderJsonPath);
                    ctx.Info(LoaderDisplay(result.Loader) + " 加载器安装完成：" + result.LoaderId);
                }
                return result;
            }
            catch
            {
                CleanupStage(ctx, result);
                throw;
            }
        }

        /// <summary>删除整合包安装用的临时 versions 目录（原版 / 加载器已合并进实例，不再需要）。</summary>
        public static void CleanupStage(InstallContext ctx, BaseInstall baseInfo)
        {
            if (baseInfo == null || string.IsNullOrWhiteSpace(baseInfo.StageRoot)) return;
            try
            {
                if (Directory.Exists(baseInfo.StageRoot))
                {
                    FileUtil.SafeDeleteDir(baseInfo.StageRoot);
                    if (ctx != null) ctx.Info("已清理临时安装目录：" + baseInfo.StageRoot);
                }
            }
            catch (Exception ex)
            {
                if (ctx != null) ctx.Warn("清理临时安装目录失败，可手动删除：" + baseInfo.StageRoot + "（" + ex.Message + "）");
            }
        }

        // ---------------- 进度 ----------------

        /// <summary>把回调进度缩放到 [lo, hi] 区间，配合 using 使用。</summary>
        public sealed class ScaledProgress : IDisposable
        {
            private readonly InstallContext _ctx;
            private readonly Action<string, double> _previous;
            private readonly double _lo;
            private readonly double _hi;

            public ScaledProgress(InstallContext ctx, double lo, double hi)
            {
                _ctx = ctx;
                if (_ctx == null) return;
                _lo = lo;
                _hi = hi;
                _previous = _ctx.ProgressCallback;
                _ctx.ProgressCallback = (phase, ratio) =>
                {
                    if (_previous == null) return;
                    var value = ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
                    _previous(phase, _lo + (_hi - _lo) * value);
                };
            }

            public void Dispose()
            {
                if (_ctx != null) _ctx.ProgressCallback = _previous;
            }
        }

        /// <summary>把字节数格式化成易读文本。</summary>
        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "未知大小";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
        }

        /// <summary>截断长描述，用于日志/主页显示。</summary>
        public static string Shorten(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        /// <summary>
        /// 兜底：在已安装版本中寻找一个看起来是原版的版本（如 1.21.4）。
        /// 用于普通压缩包无法识别游戏版本时。
        /// </summary>
        public static string FindInstalledVanilla(InstallContext ctx)
        {
            try
            {
                if (!Directory.Exists(ctx.VersionsDir)) return null;
                string best = null;
                var bestVersion = -1.0;
                foreach (var dir in Directory.GetDirectories(ctx.VersionsDir))
                {
                    var name = Path.GetFileName(dir);
                    if (!Regex.IsMatch(name, @"^\d+\.\d+(\.\d+)?$")) continue;
                    if (!File.Exists(Path.Combine(dir, name + ".json"))) continue;
                    var v = VersionToNumber(name);
                    if (v > bestVersion) { bestVersion = v; best = name; }
                }
                return best;
            }
            catch { return null; }
        }

        private static double VersionToNumber(string version)
        {
            var parts = version.Split('.');
            double value = 0;
            for (int i = 0; i < parts.Length && i < 4; i++)
            {
                int n;
                if (!int.TryParse(parts[i], out n)) n = 0;
                value = value * 1000 + n;
            }
            return value;
        }
    }
}
