# 开源许可检查与结论（PCL 下载中心）

> 结论速查：上游 PCL **不是** MIT / Apache / GPL 等标准开源许可，而是《PCL 分发有限许可》+《PCL 存储库合理使用指南》；
> 本项目属于其定义的「**重度使用**」。因此本项目**自身**的代码采用 **GNU GPL-3.0**（copyleft，强制公开源码，与上游防倒卖的意图一致），
> 并完整保留上游指南、给出署名与赞助链接、明确第三方二次创作身份。详见下文表格。

## 1. 上游许可实际是什么

| 项 | 结论 | 依据 |
| --- | --- | --- |
| 上游项目 | PCL（Plain Craft Launcher 2），作者龙腾猫跃（LTCat） | `ref/PCL`（Meloong-Git/PCL @ 2.99.0.0） |
| 许可文件 | `ref/PCL/LICENCE`：**《PCL 分发有限许可》**（正文见 https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-X0bo）+**《PCL 存储库合理使用指南》** | 原文件已收录于本仓库 [LICENCE-PCL.md](LICENCE-PCL.md) |
| 是否 OSI 开源许可 | **否**。除明确授予的权利外，开发者保留全部权利；指南自称「不是法律文件或协议」，但是开发者的善意免责承诺 | 上游原文 |
| 我们能否改用 MIT/Apache/GPL 覆盖 PCL 代码 | **不能**。任何人都没有被授予这样的权利；我们只能就**自己的**代码选择许可 | 同上 |
| 上游是否分发我们的代码 | 否。本项目是独立仓库、独立进程 | — |
| 我们是否分发 PCL 代码/二进制 | **否**。发布包只有我们编译的 exe + MIT 的 Newtonsoft.Json.dll；Forge/NeoForge 安装时从**用户本机**的 PCL 程序内提取其内嵌补丁安装器（与 PCL 自身做法一致） | [PclForgeInstaller.cs](src/PCLDownloadCenter/Minecraft/Loaders/PclForgeInstaller.cs) · [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) |

## 2. 我们为什么属于「重度使用」

上游指南对重度使用的定义包括：「**基于本存储库进行修改**」「**参考存储库中较大量的内容，以制作或重新实现 PCL 的部分实质功能（例如启动或下载 Java 版 Minecraft，下载或管理 Mod 等）**」。

本项目的实际行为：

- 重新实现了 Minecraft 原版 / 加载器（Fabric、Quilt、Forge、NeoForge）/ 整合包的下载与安装 —— 上游明确点名的实质功能；
- 开发期大量参考 `ref/PCL` 源码中 `ModDownloadLib.vb`（`McInstallLoadersGet` / `MergeJson` / `McDownloadClientLoader`）、`McVersion.vb`、主页插件机制与事件类型等；
- 运行时还直接使用 PCL 内嵌的补丁版加载器安装器。

因此**不能**按「轻度使用」只署名了事，必须满足重度使用的全部要求。

## 3. 重度使用的要求 → 本项目的落实

| 上游要求（原文摘要） | 本项目的落实 | 位置 |
| --- | --- | --- |
| 1. 明确表示是第三方基于 PCL 独立二次创作的产物 | README、第三方声明、插件「关于」页均写明 | [README.md](README.md) · [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) · [HomepageViews.cs](src/PCLDownloadCenter/Homepage/HomepageViews.cs) |
| 2. 名称以 Plain Craft Launcher (PCL) 开头，并带足以表现第三方修改的后缀 | 对外标题/横幅统一用常量 `App.Name`＝「**PCL 下载中心（第三方修改版）**」：主页顶栏与关于页、Web UI 标题与页头、命令行横幅、启动失败对话框、安装/卸载脚本横幅、引导页卡片；描述性正文中仍简称「PCL 下载中心」 | [App.cs](src/PCLDownloadCenter/App.cs) · [HomepageRenderer.cs](src/PCLDownloadCenter/Homepage/HomepageRenderer.cs) · [UiAssets.cs](src/PCLDownloadCenter/Web/UiAssets.cs) · [Cli.cs](src/PCLDownloadCenter/Cli.cs) |
| 3. 关于页面首位给出龙腾猫跃署名与赞助链接，公开源代码，并继续使用本指南文件 | 插件主页新增「**关于**」视图，**首位**即原作者署名与「赞助 PCL 作者」按钮（https://meloong.com/afd/a/LTCat）；同时给出 PCL 源码、分发有限许可、本项目源码链接；上游指南原文随仓库与发布包分发 | [LICENCE-PCL.md](LICENCE-PCL.md) · [HomepageViews.cs](src/PCLDownloadCenter/Homepage/HomepageViews.cs) |
| 4. 若有 Minecraft 启动功能，需实现与 `McLaunchPrecheck` 一致（或更激进）的正版购买/赞助劝导 | **不适用**：本插件不提供启动功能，启动与劝导由 PCL 本体负责（我们只做下载与安装） | [HomepageViews.cs](src/PCLDownloadCenter/Homepage/HomepageViews.cs) 关于页说明 |
| 5. 不得有与 PCL 赞助解锁类似的功能或表现（界面主要色相必须为蓝色） | 无任何赞助/解锁/内购功能；主页只使用 PCL 内置控件，保留其默认蓝色高亮（`ColorType="Highlight"`），不改变主题色相 | [HomepageXaml.cs](src/PCLDownloadCenter/Homepage/HomepageXaml.cs) |

## 4. 本项目自己的许可为什么选 GPL-3.0

| 候选 | 是否可满足「公开源代码 + 继续沿用上游指南」 | 问题 |
| --- | --- | --- |
| MIT / BSD | 不满足（允许闭源再分发） | 与上游「防倒卖、防山寨」意图相悖，也无法约束衍生版本继续带署名与指南 |
| Apache-2.0 | 部分满足（专利授权更好，但仍允许闭源） | 同上；另外 PCL2-CE 用的是 Apache-2.0，容易与本项目混淆 |
| CC BY-SA 4.0 | 不推荐用于代码（无专利/源码分发条款） | 上游指南只是「可参考 CC BY-SA」用于指南文件本身 |
| **GPL-3.0（采用）** | **满足**：分发衍生版本必须提供源码、保留许可与声明 | 与 MIT 依赖兼容；与上游限制不冲突（我们不分发 PCL 代码） |

补充说明：

- GPL-3.0 **不会**延伸到 PCL 的代码：PCL 代码不在本项目的分发范围（用户自行安装 PCL）。
- 上游指南与《PCL 分发有限许可》属于「使用本插件所依附生态」的额外条件，已原样收录并在 README 中声明，任何人再分发本项目都应同时带上它们。
- 文档（README / docs）随仓库一并按 GPL-3.0 提供；上游指南文件本身按上游要求原样保留。

## 5. 红线清单（以后改代码/打包时不要碰）

1. **不要把 PCL 的源码、exe、内嵌资源打包进发布物**；只能像现在这样「在用户本机运行时提取」。
2. **不要把 `ref/` 纳入仓库或发布包**（它已被 `.gitignore` 忽略，且是上游代码）。
3. **不要改软件名称为官方样式**（如 `PCL 3`、`PCL Pro`、`PCL 官方版`），也不要使用与 PCL 混淆的名字；应保持「PCL 下载中心（第三方修改版）」这类「PCL + 第三方标识」形式。
4. **不要删除或替换「关于」页首位的作者署名与赞助链接**，也不要删掉 `LICENCE-PCL.md`。
5. **不要引入赞助解锁、内购、主题色相解锁之类与 PCL 相似的功能**；界面主色相保持蓝色。
6. **不要用非 copyleft 许可重新授权本项目的衍生版本**（GPL-3.0 已定，衍生版本必须同样开源）。
7. 发布包必须包含 `LICENSE`、`LICENCE-PCL.md`、`THIRD-PARTY-NOTICES.md`（`tools/pack.ps1` 已自动带上，且会复制到 `PCL\DownloadCenter\`）。

## 6. 依赖许可清单（随包分发的部分）

| 组件 | 版本 | 许可 | 是否随包分发 | 声明位置 |
| --- | --- | --- | --- | --- |
| PCLDownloadCenter（本项目） | — | GPL-3.0 | 是 | [LICENSE](LICENSE) |
| Newtonsoft.Json | 13.0.3 | MIT | 是（`Newtonsoft.Json.dll`） | [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) §3 |
| Roslyn csc / .NET Framework 参考程序集 | — | 微软许可 | 否（仅构建期，`tools/nuget` 被 gitignore） | [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) §4 |
| Minecraft 游戏文件与清单 | — | Mojang / Microsoft | 否（运行时由使用者本机下载） | [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) §5 |

> 本文档为工程角度的许可梳理，不构成法律意见。上游《PCL 分发有限许可》正文以官方文档为准：
> https://shimo.im/docs/rGrd8pY8xWkt6ryW#anchor-X0bo
