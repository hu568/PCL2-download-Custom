# 实现说明（PCL 下载中心 · 主页插件复活版）

> 本文档描述**实际交付的实现**（与 docs/ARCH.md 的规划对照），以及运行机制、关键决策与已知限制。
> 目标版本：PCL2 2.99.0.0 官方版（原版游戏下载 / 整合包安装被禁用）。

## 1. 总体结构

```text
PCL 2.99（官方 exe，未修改）
 └─ PCL\Custom.xaml                 主页插件（由后端自动生成/重写）
 └─ PCL\DownloadCenter\
      PCLDownloadCenter.exe         后端：下载器 + 安装器 + 本地 HTTP 服务 + Web UI
      Newtonsoft.Json.dll
      config.json                   配置
      data\                         日志 / 任务历史 / 清单缓存 / 临时目录
```

- 插件不是“改 PCL 程序”，而是利用 PCL 的主页自定义能力（XamlReader + 自定义事件）驱动一个独立后端进程；
  PCL 程序本体与签名均未改动，卸载即恢复。
- 后端只监听 `127.0.0.1`，不对外开放端口；所有下载来自官方/开源公开接口。

## 2. 通信与交互链路

```text
用户点击主页按钮
  → PCL 执行自定义事件「执行命令」
  → DownloadCenter\PCLDownloadCenter.exe --install vanilla 1.21.4
  → （若服务未运行则先 --server 拉起分离进程）
  → POST http://127.0.0.1:27321/api/install  → 任务入队
  → 后端执行安装并节流重写 PCL\Custom.xaml（任务状态变化时）
  → 用户点「刷新主页」→ PCL 重新读取 Custom.xaml（内容 hash 变化才重建 UI）
```

- 主页视图切换用 `修改变量`（写 `HKCU\Software\PCL\CustomEventDCV*`）+ `刷新主页` 两个事件，
  事件按 XAML 中声明顺序执行（`CustomEventService` 集合先执行、EventType 最后），保证“先改状态后刷新”。
- 主页所有按钮都只是“发命令 + 刷新”，不依赖 PCL 内部 API，天然兼容后续 PCL 版本。

## 3. 后端模块

| 模块 | 文件 | 说明 |
| --- | --- | --- |
| 核心 | `Program/Cli/AppConfig/Paths/Logger/JsonUtil/Contracts` | 入口、CLI、配置、路径推断、日志、共享契约 |
| 下载 | `Net/Downloader.cs`、`Net/SourceRewriter.cs` | 并发下载、SHA1 校验、重试、镜像回退（BMCLAPI） |
| 任务 | `Tasks/TaskManager.cs`、`TaskItem.cs` | 队列、进度、取消、持久化、变更事件 |
| 服务 | `Web/HttpServer.cs`、`Web/ApiCore.cs` | TcpListener 极简 HTTP/1.1、核心 REST API、SSE |
| 原版 | `Minecraft/VersionManifest/VersionInfo/VanillaInstaller/LibraryInstaller/JavaLocator` | 版本清单、依赖库、资源对象、legacy natives/virtual assets、Java 探测 |
| 加载器 | `Minecraft/Loaders/*` | Fabric / Quilt / Forge / NeoForge；Forge 系复用 PCL 内嵌的 BMCLAPI 补丁安装器 |
| 整合包 | `Minecraft/Modpacks/*` | Modrinth mrpack、CurseForge zip（需 API Key）、URL、本地文件、HMCL/MultiMC/普通 zip |
| Web UI | `Web/Api.cs`、`Web/Ui*.cs` | 单页应用（内联 HTML/CSS/JS，无外部依赖）+ 扩展 API |
| 主页 UI | `Homepage/*.cs` | Custom.xaml 生成（视图、版本列表、任务、设置），XmlReader 校验后原子写入 |

## 4. 关键技术点

1. **依赖库与资源**：按 rules（os/features）过滤；`assets/objects` 按 hash 全局共享，已存在则跳过；
   legacy 版本支持 `virtual`（assets/virtual/legacy）与 `map_to_resources`（.minecraft/resources），与原版 PCL 一致。
2. **下载源**：默认官方；可切 BMCLAPI。失败自动回退镜像；Forge/NeoForge 安装器走 PCL 内嵌补丁安装器
   （`com.bangbang93.ForgeInstaller`，从 PCL 程序资源的 `PCL.Resources/ForgeInstaller` 提取，注入 BMCLAPI maven 镜像），
   失败再回退“预取依赖库 + 官方 installer --installClient”。
3. **整合包实例（单版本）**：原版与加载器先装进 `<scratch>\stage\versions` 临时目录，再合并成一个独立版本
   `versions/<实例名>/<实例名>.json`（以原版 JSON 为底合并加载器 JSON、数组拼接、加载器项覆盖；去掉 `inheritsFrom`/`jar`，
   `id` 写实例名、保留 `clientVersion`），原版客户端 jar 复制为 `<实例名>.jar`，最后删除临时目录；
   因此 `versions` 下只剩整合包一个版本，不再额外产生原版/加载器目录。整合包安装时 Forge/NeoForge 安装器改在
   `<scratch>\mc-stage` 暂存目录运行。实例隔离配置写入 `versions/<实例名>/PCL/Setup.ini`（逐键合并，不动全局配置）。
4. **主页稳定性**：生成前 XmlReader 校验；节点数 < 400、体积 ~40KB；写入用临时文件 + Replace 原子替换；
   内容无变化不落盘，避免 PCL 无意义重建。
5. **实时性**：Web UI 用 SSE 推送任务进度/日志；主页受 PCL 限制只能“点刷新”，因此后端在任务变化时节流（1.5s）重写文件，用户点一次即可看到最新进度。

## 5. 已知限制

- **CurseForge 在线安装**：CurseForge API 需要 Key（官方无 key 返回 403）；PCL 曾用的第三方代理当前 503。
  因此 CF 在线安装与“带 files[] 的 CF zip 导入”在无 Key 时不可用（会回滚并提示）；Modrinth 完全可用。
- **主页进度非实时**：受 PCL 主页机制限制，需点「刷新主页」；Web UI 为实时。
- **Java**：不自动下载 Java 运行时；缺 Java 时提示用户通过 PCL 的 Java 管理下载（PCL 2.99 仍保留该功能）。
- OptiFine / LiteLoader 安装未实现（会返回明确提示）。
- 首次从主页执行命令时，PCL 会弹「执行确认」，需选“继续且今后不再要求确认”（可用 `install.ps1 -SkipCommandConfirm` 预置）。

## 6. 构建与发布

```powershell
tools\build.ps1            # → build\PCLDownloadCenter.exe（csc 直编，无需 .NET SDK）
tools\deploy.ps1           # 部署到 D:\PCL2\PCL\DownloadCenter 并重写主页（测试用）
tools\pack.ps1             # → release\（插件 + install/uninstall + README）
tools\acceptance.ps1       # 端到端验收（原版/加载器/整合包）
tools\screenshot.ps1       # 截取 PCL 窗口
tools\uia\*.ps1            # PCL 控件树 / 点击
```

## 7. 目录归属（开发期协作）

- 核心文件（Contracts/App/Paths/Net/Tasks/Web\HttpServer/Web\ApiCore/Cli/Program/Installers）：Lead
- 原版：Minecraft\VersionManifest|VersionInfo|VanillaInstaller|LibraryInstaller|JavaLocator
- 加载器：Minecraft\Loaders\
- 整合包：Minecraft\Modpacks\
- Web UI：Web\Api.cs、Web\Ui*.cs
- 主页：Homepage\
