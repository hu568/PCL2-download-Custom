# AGENTS.md

面向 **PCL2 2.99.0.0** 的「PCL 下载中心」：.NET Framework 4.8 后端（HTTP 服务 + Web UI + CLI）+ PCL 主页插件（Custom.xaml），用于恢复 PCL 被移除的 Minecraft 下载/安装能力。仅 Windows。

## 目录

- `src/PCLDownloadCenter/` — 全部 C# 源码（47 个 .cs，统一 `namespace PCLDownloadCenter`）
  - 根：`Program`/`Cli`/`App`/`AppConfig`/`Paths`/`Logger`/`JsonUtil`/`Contracts`/`Installers`
  - `Net/` 下载与镜像；`Tasks/` 任务队列/进度/取消；`Web/` HttpServer + REST/SSE + 内嵌 Web UI
  - `Minecraft/` 原版与 Java；`Minecraft/Loaders/` 加载器；`Minecraft/Modpacks/` 整合包
  - `Homepage/` 生成 Custom.xaml；`Util/` 文件/注册表/zip
- `tools/` 脚本：build / deploy / pack / acceptance / install / uninstall / screenshot / uia
- `docs/` 文档：`ARCH.md`（架构）、`IMPLEMENTATION.md`（实现说明）、`LICENSE-ANALYSIS.md`（许可分析）随仓库公开；`DELIVERY.md` 与 `TEST-REPORT-*.md` 属**内部文档**（已加入 `.gitignore`，只在开发机保留）；`release/` 打包产物；`ref/` PCL 参考源码（只读，独立 git 仓库）
- `build/`、`TMP/`、`PLAN*`、`ref/`、`tools/nuget/` 都被 gitignore

## 命令（在仓库根执行）

- 构建：`powershell -ExecutionPolicy Bypass -File tools\build.ps1` → `build\PCLDownloadCenter.exe`
  - csc 直编（不用 MSBuild/.NET SDK），递归收录 `src` 下所有 .cs，新增文件无需改脚本；`-Clean`、`-OutDir` 可用
- 部署到本机测试 PCL：`tools\deploy.ps1`（默认 `D:\PCL2\PCL\DownloadCenter`，会改 `Setup.ini` 的 `UiCustomType`）
- 打包：`tools\pack.ps1` → 重建 `release\`（先删除整个目录；**不含 `docs/`**）
- 端到端验收：`tools\acceptance.ps1`（默认端口 27361，真实下载，需网络与 `D:\PCL2\.minecraft`；整合包用例需 `$env:DC_MODPACK_ID`）
- 运行/调试：`build\PCLDownloadCenter.exe --console`；服务端口 27321，仅监听 127.0.0.1；`--stop`、`--status`、`--help`，安装命令见 README §3.3
- 没有单元测试、lint、formatter、CI；`build.ps1` 的 `/warn:4` 编译是唯一静态检查

## 约定

- 只用 .NET Framework 4.8 可用的 API；语法用 csc `/langversion:latest`。
- 4 空格缩进、Allman 大括号、`_camelCase` 私有字段、PascalCase 公共成员；中文注释与文案；UTF-8 无 BOM。
- 依赖通过 `InstallContext`（Cfg/Log/Dl/McDir/ScratchDir/Ct/Progress）传入；全局单例在 `App`；JSON 统一用 Newtonsoft（`JsonUtil` 封装）。
- 边界处捕获异常并写 `Logger`；CLI 参数错误 `return 2`，运行失败 `return 1`。
- 主页生成必须保留 `HomepageRenderer` 的 XML 校验与节点/体积上限（约 400/520 节点、200KB），只用 PCL 内置控件与 xmlns（见 `docs/ARCH.md` §0）。
- 路径一律走 `Paths`（从 `config.v1.yml` 推断 .minecraft），不要硬编码；HTTP 只绑定回环地址。
- 许可合规：本项目以 GPL-3.0 发布（`LICENSE`）；上游 PCL 是非 OSI 许可（《PCL 分发有限许可》+《PCL 存储库合理使用指南》，原文见 `LICENCE-PCL.md`），本项目属「重度使用」。不得把 PCL 源码/exe/内嵌资源打进发布物，不得改成官方样式的名称，不得删除「关于」页首位的龙腾猫跃署名与赞助链接，不得加入赞助解锁类功能（主色相保持蓝色）；发布包必须带 `LICENSE`/`LICENCE-PCL.md`/`THIRD-PARTY-NOTICES.md`（`pack.ps1` 已处理）。分析见 `docs/LICENSE-ANALYSIS.md`。

## 坑

1. `tools/nuget/`（csc、refasm、Newtonsoft）被 gitignore 但是构建必需；全新 clone 直接 build 会抛「找不到编译器」（获取方式未核实）。
2. `System.IO.Compression` 必须引用框架目录的 4.0.0.0 版本，不能用 refasm 的 4.2.0.0（build.ps1 已处理，别改）。
3. exe 是 WinExe：调试加 `--console`；不 `--stop` 会占用 27321 并锁住 exe/dll，导致部署/安装复制失败。
4. `<PCL>\Custom.xaml` 由后端自动重写，手改会被覆盖；根目录 `Custom.xaml` 只是开发期残留，不是安装产物。
5. 不要改 `ref/`（独立仓库）；不要动 `D:\PCL2` 的 PCL 本体，只操作 `Custom.xaml` 与 `DownloadCenter\`。
6. 验收/测试会真实下载并写入 `D:\PCL2\.minecraft`；测试证据在 `build/`（gitignored，不随仓库分发）。
7. 已知限制不是 bug：CurseForge 需 API Key，OptiFine/LiteLoader 未实现，Java 不自动下载。
8. `docs/ARCH.md` 是早期规划，个别路径已过时（如 `tools/test-cli.ps1`、`Homepage/HomepageState.cs` 不存在），实现以 `docs/IMPLEMENTATION.md` 和实际文件为准。
9. 启动游戏用哪个 Java 由 **PCL** 决定，不由本项目决定：`ModJava.GetJavaRequirement` 对 1.18~1.20.4（版本 JSON 声明 Java 17）只追加下界 `[17.0, +∞)`，再取 Java 列表里第一个满足者，于是装了 Java 21/25 的机器会选到过新的 Java，而这段时期的 Forge（38.x~49.x，ModLauncher 10 / Mixin 0.8.5 / 旧版 ASM）读不了 Java 21+ 的类文件（实测崩溃：`Unsupported class file major version 69`）。因此安装收尾由 `JavaCompat` 写入 `<实例>\PCL\Setup.ini` 的 `VersionArgumentJavaV2:1` + `VersionArgumentJavaRange:[17.0, 18.0)`，**并且必须同时写** `<实例>\PCL\config.json` 的 `InstanceMigratedJava=true`——否则 PCL 首次启动的 `MigrateInstanceJavaSettings`（`ModJava.vb:485`）会把 `VersionArgumentJavaV2` 覆盖回 0，区间键失效。改动这套逻辑后必须真机确认 PCL 日志出现 `已选定 Java：Java 17`。注意保护范围只覆盖**本项目安装**的版本；用 PCL 自己装的加载器版本（如 PCL 里给 1.19.2 装 Forge）不会带这个设置。

发布流程：build → pack；`release/*.zip` 由手工 `Compress-Archive` 生成。远端 `hu568/PCL2-download-Custom`。
