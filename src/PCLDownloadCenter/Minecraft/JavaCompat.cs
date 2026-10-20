using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace PCLDownloadCenter
{
    /// <summary>
    /// 版本级 Java 兼容处理：在安装收尾时给「声明 Java 17 的版本」（MC 1.18 ~ 1.20.4）
    /// 写入 PCL 的版本级 Java 区间，避免 PCL 自动选到过新的 Java 导致启动即崩。
    ///
    /// 背景（2026-10-20 用户崩溃报告，TMP\err-2026-10-20）：
    /// MC 1.19.2 + Forge 43.4.0 的整合包启动即崩，崩溃输出为
    ///   java.lang.IllegalArgumentException: Unsupported class file major version 69（Java 25 的类文件）
    ///   → MixinTransformerError → Caused by: ClassMetadataNotFoundException: java.lang.System
    /// 原因不是安装产物：该实例的版本 JSON 已正确声明 javaVersion.majorVersion = 17。
    /// 是 PCL 的 Java 自动选择：ModJava.GetJavaRequirement 对 1.18+ 只追加下界 [17.0, +∞)，
    /// 而 SelectJavaFromJavaList 取「Java 列表里第一个满足区间」的 Java，于是装了 Java 25 的机器
    /// 就被选到 Java 25；这段时期的 Forge（38.x ~ 49.x，ModLauncher 10 / Mixin 0.8.5 / 旧版 ASM）
    /// 读不了 Java 21+ 的类文件。PCL 自己只对 1.16.5 Forge（< 24）、1.19.4 ~ 1.20.1 Forge（< 22）、
    /// 旧版 Fabric（< 25）等做过上界约束，漏掉了 1.18 ~ 1.20.4 的 Forge。
    ///
    /// 做法：向 &lt;实例目录&gt;\PCL\Setup.ini（PCL 的版本独立设置就存在这里）写入
    ///   VersionArgumentJavaV2:1                  → 版本设置里的「自动选择指定版本的 Java」
    ///   VersionArgumentJavaRange:[17.0, 18.0)    → 只接受 Java 17（区间语法见 PCL 版本设置页提示）
    /// 本机没有 Java 17 时 PCL 会按该区间自动下载 java-runtime-gamma（17），不会变成「找不到 Java」。
    /// 只在这两个键都不存在时写入，用户/PCL 已设置过的值一律不覆盖；失败只警告，不影响安装结果。
    ///
    /// 还必须同时写 InstanceMigratedJava = true 到 &lt;实例目录&gt;\PCL\config.json：
    /// PCL 在 SelectOrDownloadJava 开头会跑一次 MigrateInstanceJavaSettings（ModJava.vb:485），
    /// 若该实例的 InstanceMigratedJava 尚未为 true，而 VersionArgumentJavaSelect 又不是旧版 JSON 格式，
    /// 它会走 NoForcedJava 分支执行 Settings.Set("VersionArgumentJavaV2", 0)，把刚写入的 1 覆盖成 0
    /// （区间键还在，但模式回到“自动选择”，Java 25 又会被选中）。实测证据：已崩溃实例的
    /// &lt;版本&gt;\PCL\config.json 内容正是 { "InstanceMigratedJava": true }。
    /// </summary>
    public static class JavaCompat
    {
        /// <summary>PCL 版本设置：Java 选择方式（1 = 自动选择指定版本的 Java）。</summary>
        public const string ModeKey = "VersionArgumentJavaV2";

        /// <summary>PCL 版本设置：允许的 Java 版本区间。</summary>
        public const string RangeKey = "VersionArgumentJavaRange";

        /// <summary>MC 1.18 ~ 1.20.4 声明的 Java 主版本为 17，这里只接受 Java 17。</summary>
        public const string Java17Range = "[17.0, 18.0)";

        /// <summary>
        /// 该 Java 主版本是否需要写入区间；不需要时返回 null。
        /// 只处理 17：Java 8 时代的版本 JSON 没有 javaVersion 字段；1.17（16）与 1.20.5+/1.21（21）
        /// 由 PCL 自己的规则覆盖，替它们写入反而会绕过 PCL 已有的加载器特有约束。
        /// </summary>
        public static string RangeForMajor(int major)
        {
            return major == 17 ? Java17Range : null;
        }

        /// <summary>实例目录里的版本 JSON 路径（PCL/MC 约定 JSON 与目录同名）；找不到返回 null。</summary>
        public static string FindVersionJson(string instanceDir)
        {
            if (string.IsNullOrWhiteSpace(instanceDir)) return null;
            try
            {
                string name = Path.GetFileName(instanceDir.TrimEnd('\\', '/'));
                string path = Path.Combine(instanceDir, name + ".json");
                if (File.Exists(path)) return path;
                string[] files = Directory.GetFiles(instanceDir, "*.json");
                if (files.Length == 1) return files[0];
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 读取版本 JSON 声明的 Java 主版本；加载器版本只声明 inheritsFrom，会回溯到父版本 JSON。
        /// 读不到时返回 0。
        /// </summary>
        public static int ReadDeclaredMajor(string versionJsonPath)
        {
            return ReadDeclaredMajor(versionJsonPath, 0);
        }

        private static int ReadDeclaredMajor(string versionJsonPath, int depth)
        {
            if (depth > 3 || string.IsNullOrWhiteSpace(versionJsonPath) || !File.Exists(versionJsonPath)) return 0;
            JObject json;
            try { json = JObject.Parse(File.ReadAllText(versionJsonPath, Encoding.UTF8)); }
            catch { return 0; }
            if (json == null) return 0;

            int major = (int)JsonUtil.Long(json["javaVersion"] as JObject, "majorVersion", 0);
            if (major > 0) return major;

            string parent = JsonUtil.Str(json, "inheritsFrom");
            if (string.IsNullOrWhiteSpace(parent)) parent = JsonUtil.Str(json, "clientVersion");
            if (string.IsNullOrWhiteSpace(parent)) parent = JsonUtil.Str(json, "jar");
            if (string.IsNullOrWhiteSpace(parent)) return 0;

            try
            {
                string versionsDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(versionJsonPath)));
                if (string.IsNullOrWhiteSpace(versionsDir)) return 0;
                string parentJson = Path.Combine(versionsDir, parent, parent + ".json");
                if (string.Equals(Path.GetFullPath(parentJson), Path.GetFullPath(versionJsonPath), StringComparison.OrdinalIgnoreCase))
                    return 0;
                return ReadDeclaredMajor(parentJson, depth + 1);
            }
            catch { return 0; }
        }

        /// <summary>
        /// 确保实例的 PCL\config.json 里 InstanceMigratedJava 为 true，避免 PCL 首次启动时
        /// 把 VersionArgumentJavaV2 迁移回 0（详见类注释）。已为 true 或文件不可解析时返回 false。
        /// </summary>
        public static bool MarkJavaMigrated(InstallContext ctx, string instanceDir)
        {
            string path = Path.Combine(Path.Combine(instanceDir, "PCL"), "config.json");
            JObject root = null;
            bool exists = false;
            try
            {
                if (File.Exists(path))
                {
                    exists = true;
                    root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                }
            }
            catch (Exception ex)
            {
                // 已有文件但解析失败：不敢覆盖，保持原样
                if (ctx != null) ctx.Warn("读取实例的 PCL\\config.json 失败，跳过 Java 迁移标记（" + ex.Message + "）");
                return false;
            }
            if (root == null) root = new JObject();
            JToken flag = root["InstanceMigratedJava"];
            if (flag != null && flag.Type == JTokenType.Boolean && flag.Value<bool>()) return false;
            root["InstanceMigratedJava"] = true;
            FileUtil.EnsureDir(Path.GetDirectoryName(path));
            FileUtil.WriteAllTextAtomic(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
            if (ctx != null)
                ctx.Info("已标记该实例的 Java 设置迁移已完成（PCL\\config.json: InstanceMigratedJava=true）" +
                         (exists ? "" : "（新建文件）") + "：否则 PCL 首次启动会把上面的 Java 设置迁移回“自动选择”。");
            return true;
        }

        /// <summary>
        /// 给实例写入版本级 Java 区间（已设置过或无需处理时返回 false）。
        /// 安装收尾调用，失败只警告，不影响安装结果。
        /// </summary>
        public static bool Apply(InstallContext ctx, string instanceDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(instanceDir) || !Directory.Exists(instanceDir)) return false;

                int major = ReadDeclaredMajor(FindVersionJson(instanceDir));
                string range = RangeForMajor(major);
                if (range == null) return false;

                string iniDir = Path.Combine(instanceDir, "PCL");
                string ini = Path.Combine(iniDir, "Setup.ini");
                string existingMode = PackCore.GetIniKey(ini, ModeKey);
                string existingRange = PackCore.GetIniKey(ini, RangeKey);
                if (!string.IsNullOrWhiteSpace(existingRange) || !string.IsNullOrWhiteSpace(existingMode))
                {
                    // 已有设置一律不覆盖。但若它已经是“区间模式”（V2=1 且区间非空），仍要补上迁移标记：
                    // 否则 PCL 首次启动的 MigrateInstanceJavaSettings 会把 V2 迁移回 0，这条设置当场失效。
                    bool rangeMode = string.Equals((existingMode ?? "").Trim(), "1", StringComparison.OrdinalIgnoreCase) &&
                                     !string.IsNullOrWhiteSpace(existingRange);
                    if (rangeMode)
                    {
                        if (ctx != null)
                            ctx.Info("该版本已设置为 Java 区间模式（" + ModeKey + "=1），补写迁移标记，避免 PCL 首次启动把它迁回“自动选择”");
                        MarkJavaMigrated(ctx, instanceDir);
                    }
                    else if (ctx != null)
                    {
                        ctx.Info("该版本已有 Java 设置（" + ModeKey + " / " + RangeKey + "），保持用户设置不变");
                    }
                    return false;
                }

                FileUtil.EnsureDir(iniDir);
                PackCore.SetIniKey(ini, ModeKey, "1");
                PackCore.SetIniKey(ini, RangeKey, range);
                MarkJavaMigrated(ctx, instanceDir);
                if (ctx != null)
                {
                    ctx.Info("已为该版本写入 Java 区间 " + range + "（PCL → 版本设置 → Java → 自动选择指定版本的 Java）：" +
                             "该版本要求 Java " + major + "，PCL 在装有更新 Java 的机器上会自动选到最新的 Java，" +
                             "而 1.18~1.20.4 的 Forge 自带的 Mixin/ASM 读不了 Java 21+ 的类文件（会以 Unsupported class file major version 崩溃）。" +
                             "如需改用其它 Java，在 PCL 的版本设置里直接改即可。");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (ctx != null) ctx.Warn("写入版本级 Java 区间失败（不影响安装）：" + ex.Message);
                return false;
            }
        }
    }
}
