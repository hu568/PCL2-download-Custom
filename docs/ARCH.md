# PCL 下载中心（主页插件复活版）— 架构与协作约定

> 目标：在 **PCL2 2.99.0.0 官方版**（下载功能被移除）中，通过 **主页自定义（Custom.xaml）插件** 复活完整的下载能力。
> 参考源码：`ref/PCL`（Meloong-Git/PCL main @ 2.99.0.0 正式版，含 WPF/VB 源码）。
> 工作目录：`D:\CODE\PCL2-download-Custom`。测试用 PCL：`D:\PCL2`（已安装，.minecraft = `D:\PCL2\.minecraft`）。

## 0. 关键事实（已实测）

1. PCL 2.99 只移除了 **原版游戏下载/安装**（PageDownloadInstall 构造函数直接抛异常）与 **整合包安装/导入**（ModModpack 抛异常）。
   **社区资源（Mod / 资源包 / 光影包 / 数据包）的搜索与下载仍然完好**（CurseForge + Modrinth 均可用，实测有结果）。
2. 主页插件 = `<PCL目录>\Custom.xaml`（`PCL\Custom.xaml`），由 PCL 用 `XamlReader` 解析（`PageLaunchRight.LoadContent`）。
   - 只允许使用内置命名空间：`local` = PCL 程序集、`core` = MeloongCore、`corewpf` = MeloongCore.Wpf；**任何自定义 xmlns 声明都会被删除**。
   - 禁用类型：WebBrowser、Frame、MediaElement、ObjectDataProvider、XamlReader、Window、XmlDataProvider、SettingService；禁用成员名：Code、FactoryMethod、Static。
   - 可用控件：`local:MyCard`、`local:MyHint`、`local:MyButton`、`local:MyTextButton`、`local:MyListItem`、`local:MyImage`、`local:MyIconButton`、`local:MyIconTextButton`，以及 WPF 的 StackPanel/Grid/TextBlock/Path/Border 等。
   - 变量替换：`{variable:Name:Default}` 读取 `HKCU\Software\PCL` 下的 `CustomEventName`；`修改变量` 事件写入该值；每次 `刷新主页` 都重新读取。
3. 自定义事件（`Modules/ModEvent.vb`）是插件执行能力的关键：`打开网页 / 打开文件 / 执行命令 / 切换页面 / 下载文件 / 修改变量 / 修改设置 / 弹出提示 / 弹出窗口 / 刷新主页 / 启动游戏` 等。
   - `执行命令`：EventData = `<程序名或相对路径>|<参数>`，工作目录 = `<PCL目录>`；首次会弹安全确认，确认框选“继续且今后不再要求确认”后写入 `HintCustomCommand=True`，之后不再询问。
   - `切换页面`：EventData = `PageType|PageSubType`，例如 `Download|DownloadMod`、`Download|DownloadPack`（实测可用，直达 PCL 内置社区资源页）。
   - 相对路径规则：EventData 中的相对路径会相对 `<PCL目录>`（即 PCL\ 文件夹）解析，例如 `DownloadCenter\PCLDownloadCenter.exe`。
   - 一个控件可挂多个事件：`<local:CustomEventService.Events><local:CustomEventCollection><local:CustomEvent Type="..." Data="..."/>...</local:CustomEventCollection></local:CustomEventService.Events>`，顺序执行。
4. 主页 `刷新主页` 会强制重读 `Custom.xaml`，内容 hash 变化才重建 UI（实测：改文件→点刷新→新内容生效）。
   - **重要**：`执行命令` 是异步的（RunInThread），其后紧跟的 `刷新主页` 可能读到旧文件 → 涉及"动作后立即可见"的交互，必须用 PCL 侧纯变量（修改变量 + 刷新主页）实现，或依赖用户再点一次刷新。
5. PCL 设置存储：`UiCustomType` 等存在 `<PCL目录>\Setup.ini`（`Key:Value` 行），注册表类设置存在 `HKCU\Software\PCL`（值可能是加密的，**不要直接写加密设置**）。
   - 安装插件：`PCL\Custom.xaml` + `PCL\DownloadCenter\*`，并在 PCL 关闭时把 `Setup.ini` 的 `UiCustomType` 设为 1（或让用户手动在设置中选择自定义主页）。

## 1. 交付物

```
release/
  PCL/
    Custom.xaml                     # 主页插件入口（由安装器生成/拷贝的引导页）
    DownloadCenter/
      PCLDownloadCenter.exe         # 后端（net48 WinExe，单文件 + Newtonsoft.Json.dll）
      Newtonsoft.Json.dll
      config.json                   # 运行期生成
      data/                         # 运行期生成（tasks.json / state.json / logs/）
  install.ps1                       # 安装脚本（拷贝 + 设置 UiCustomType=1）
  uninstall.ps1                     # 卸载脚本（恢复原 Custom.xaml / UiCustomType=0）
  README.md                         # 使用说明
```

## 2. 后端工程

- 语言/框架：**C# 12（Roslyn 编译器）→ .NET Framework 4.8**；不用 dotnet SDK、不用 MSBuild；由 `tools/build.ps1` 用 csc.exe 直接编译。
- 编译器：`tools/nuget/compilers/tasks/net472/csc.exe`（Roslyn 4.11）
- 引用程序集：`tools/nuget/refasm/build/.NETFramework/v4.8`
- 唯一第三方库：`tools/nuget/newtonsoft/lib/net45/Newtonsoft.Json.dll`
- 目标：`PCLDownloadCenter.exe`（WinExe，无控制台窗口；调试可用 `--console` 时 AttachConsole）。
- **注意**：`System.IO.Compression` 必须引用框架目录 4.0.0.0 版本（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.dll`），不要用 refasm 里的 4.2.0.0，否则运行期加载失败。

### 2.1 目录与文件归属（每人只改自己的文件）

```
src/PCLDownloadCenter/
  Program.cs, Cli.cs, AppConfig.cs, Paths.cs, Logger.cs, JsonUtil.cs     # Lead
  Net/Downloader.cs, Net/SourceRewriter.cs                              # Lead
  Tasks/TaskManager.cs, Tasks/TaskItem.cs                               # Lead
  Web/HttpServer.cs                                                     # Lead
  Web/Api.cs, Web/UiAssets.cs (+ Web/assets/*)                          # webui
  Homepage/HomepageRenderer.cs, Homepage/HomepageState.cs               # homepage
  Minecraft/VersionManifest.cs, VersionInfo.cs, VanillaInstaller.cs, JavaLocator.cs   # vanilla
  Minecraft/Loaders/*.cs                                                # loaders
  Minecraft/Modpacks/*.cs, Resources/ModrinthApi.cs, Resources/CurseForgeApi.cs       # pack
```

## 3. 核心接口（Lead 提供，先按此签名编码；Lead 会同步实现）

```csharp
namespace PCLDownloadCenter {

  public sealed class AppConfig {
      public string PclDir;          // 例如 D:\PCL2\PCL  （PCL\ 文件夹）
      public string McDir;           // 例如 D:\PCL2\.minecraft
      public string DownloadSource = "official"; // official | bmclapi
      public int Threads = 32;
      public int ServerPort = 27321;
      public string CurseForgeApiKey = "";
      public string JavaPath = "";   // 空 = 自动探测
      public int HomepageMaxVersions = 30;
      public bool HomepageShowSnapshots = true;
      public bool HomepagePublish = true;      // 是否自动重写 Custom.xaml
  }

  public static class Paths {
      public static string PluginDir;  // ...\PCL\DownloadCenter
      public static string PclDir;     // ...\PCL
      public static string BaseDir;    // PCL 根（exe 所在）
      public static string DataDir;    // PluginDir\data
      public static string LogDir;
      public static AppConfig Config;
      public static void Init();
      public static string McDir { get; }
  }

  public sealed class Logger {
      public void Info(string msg); public void Warn(string msg); public void Error(string msg, Exception ex = null);
      public string Tail(int lines);
      public event Action<string> OnLine;    // 供 SSE 推送
  }

  public sealed class DownloadItem {
      public string Url; public string Path; public string Sha1; public long Size = -1; public string Name;
  }

  public sealed class Downloader {
      public Downloader(Logger log, AppConfig cfg);
      public Task<string> GetStringAsync(string url, CancellationToken ct = default, int timeoutMs = 30000);
      public Task<byte[]> GetBytesAsync(string url, CancellationToken ct = default, int timeoutMs = 30000);
      public Task<string> DownloadFileAsync(string url, string path, string sha1 = null, long size = -1,
          Action<long> onProgress = null, CancellationToken ct = default, int retry = 3, bool allowMirror = true);
      public Task DownloadManyAsync(IList<DownloadItem> items, Action<int, int> onProgress = null,
          CancellationToken ct = default, Action<string> onItemFailed = null);
  }

  public sealed class InstallContext {
      public AppConfig Cfg; public Logger Log; public Downloader Dl; public CancellationToken Ct;
      public string McDir; public string ScratchDir;      // 临时目录，自动清理
      public Action<string, double> Progress;             // 阶段名, 0~1
  }

  public sealed class InstallResult { public bool Ok; public string VersionId; public string Error; }
}
```

**模块契约**

- vanilla：`Minecraft/VersionManifest.cs`
  ```csharp
  public static class VersionManifest {
      public static Task<ManifestData> GetAsync(InstallContext ctx, bool forceRefresh = false);
      public static Task<VersionInfo> GetVersionInfoAsync(InstallContext ctx, ManifestEntry e);
      public static Task<string> InstallAsync(InstallContext ctx, string versionId); // 返回 versionId，vanilla 原版安装
  }
  ```
- loaders：`Minecraft/Loaders`
  ```csharp
  public static class LoaderRegistry {
      public static readonly string[] Supported = { "fabric", "quilt", "forge", "neoforge", "optifine", "liteloader" };
      public static Task<List<LoaderVersion>> GetVersionsAsync(InstallContext ctx, string loader, string mcVersion);
      public static Task<string> InstallAsync(InstallContext ctx, string loader, string mcVersion, string loaderVersion);
  }
  ```
- pack：`Minecraft/Modpacks`
  ```csharp
  public static class ModpackInstaller {
      // source: modrinth | curseforge | url | file
      public static Task<string> InstallAsync(InstallContext ctx, string source, string id, string versionId = null);
      public static Task<string> InstallFromFileAsync(InstallContext ctx, string filePath);
  }
  ```
- webui：`Web/Api.cs` + `Web/UiAssets.cs`（Lead 的 HttpServer 负责监听与路由，Api 负责处理）
  ```csharp
  public static class WebApi {
      public static void Register(Dictionary<string, Func<HttpRequest, HttpResponse>> routes); // 由 HttpServer 调用
  }
  ```
- homepage：`Homepage/HomepageRenderer.cs`
  ```csharp
  public static class HomepageRenderer {
      public static string Render(HomepageState state);            // 生成完整 Custom.xaml 文本
      public static string RenderBootstrap(AppConfig cfg, string status); // 首次/异常时使用的引导页
  }
  ```

## 4. CLI 与运行时

`PCLDownloadCenter.exe` 同时是 CLI 与后台服务：
- 无参数 / `--server`：前台运行 HTTP 服务（主页按钮用 `--start` 启动分离进程）。
- `--start` / `--ensure`：确保后台服务运行（已运行则直接返回；否则创建分离进程）。
- `--install vanilla <versionId>`
- `--install loader <loader> <mcVersion> <loaderVersion>`
- `--install modpack <modrinthProjectId> [versionId]`
- `--install modpack-url <url> <name>`
- `--import pack`（弹文件选择框）
- `--view <viewName>`：修改主页当前视图（写注册表变量 DCView）
- `--open-ui [子页]`：用默认浏览器打开 http://127.0.0.1:<port>/
- `--homepage publish`：重写 Custom.xaml
- `--stop`、`--status`

CLI 通过 HTTP 与已运行的服务通信（`POST /api/...`）；服务未运行则先 `--start` 再重试。
主页按钮统一使用（相对路径，工作目录 = PCL\）：
`EventData="DownloadCenter\PCLDownloadCenter.exe|--install vanilla 1.21.8"`

**HttpServer（Lead）**：基于 TcpListener 的极简 HTTP/1.1 服务（不用 HttpListener，避免 URL ACL），
路由：`WebApi.Register(dict)`；静态资源由 `UiAssets` 提供；SSE `/api/events`。

## 5. 主页插件 UI（homepage 负责）

页面结构（单文件，视图用 `{variable:DCView:home}` 控制 Visibility）：
- 顶栏卡片：状态（服务运行/任务数）+ 视图切换按钮（原版游戏 / 加载器 / 整合包 / 任务 / 社区资源 / 设置）
- `home`：欢迎、一键安装最新正式版/快照、当前任务摘要、社区资源入口（切换页面到 PCL 内置页）
- `vanilla`：版本列表（分页，变量 `DCVerPage`，每页 ~30 个），每个版本一个 MyListItem/MyButton → `--install vanilla <id>`
- `loader`：已安装版本 + 各加载器（Fabric/Quilt/Forge/NeoForge）版本选择 → `--install loader ...`
- `pack`：推荐整合包（Modrinth，后端生成时缓存）+ 导入本地整合包 + 打开完整下载中心（浏览器）
- `task`：任务列表（进度条用文本 `████░░ 45%` 表示）、取消按钮、刷新按钮（`刷新主页`）
- `settings`：下载源切换、打开 Web UI、帮助

约束：单页节点数 < ~400，生成后体积 < 200KB；变量缺省值必须写在 `{variable:X:default}` 中；
所有"本地状态切换"用 `修改变量` + `刷新主页` 双事件，**不要**用 执行命令 + 刷新主页（竞态）。
后端在任务状态变化时重写 Custom.xaml（节流 ≥ 1s），用户点刷新即可看到。

## 6. Web UI（webui 负责）

- `http://127.0.0.1:<port>/`：完整下载中心（原生 HTML/CSS/JS，无外部 CDN，内嵌到程序集或 UiAssets.cs 常量）。
- 页面：原版游戏（版本搜索/筛选/安装）、加载器安装、整合包（Modrinth 搜索 + 安装）、任务（SSE 实时进度/日志/取消）、设置（MC 路径、下载源、并发、CF Key）、帮助。
- 由 `WebApi` 提供 JSON API；未知路由 404；请求均限 127.0.0.1。

## 7. 构建与测试

- 构建：`powershell -File tools/build.ps1` → 输出 `build/PCLDownloadCenter.exe`（+ Newtonsoft.Json.dll）。
- 打包：`powershell -File tools/pack.ps1` → 生成 `release/` 目录（含 exe、Custom.xaml、install.ps1、README）。
- 单元/集成自测：`powershell -File tools/test-cli.ps1`（直接跑 CLI 安装指定版本/整合包并校验文件）。
- 主页 UI 自动化：`tools/uia/click.ps1 -Name "<控件文本>"`（已实现，见 tools/uia），配合 `dumpfull.ps1` 检查 PCL 界面；
  PCL 日志：`D:\PCL2\PCL\Log1.txt`（最新），可 `Select-String` 检查事件是否执行。
- 安装各模块后必须：`D:\PCL2\.minecraft\versions\<id>\<id>.json` 存在，PCL 能列出该版本（截图/日志）。

## 8. 协作规则

- 只改自己负责的文件；需要改公共文件（Program/Cli/Downloader/HttpServer）先找 Lead。
- 每完成一个里程碑，用 `team_task_update` 更新任务状态；构建失败时先修自己文件。
- 网络不可达时优先参考 PCL 的镜像逻辑（`Modules/Base/ModNet.vb`、`Modules/Minecraft/ModDownload.vb` 中的 `DlSourceGet`/镜像替换）。
- 不要修改 `ref/PCL` 源码；不要修改 D:\PCL2 下 PCL 程序本体，只改 `D:\PCL2\PCL\Custom.xaml`（测试主页）与插件目录。
- 中文注释/文案统一用 UTF-8（无 BOM 的 .cs 文件请用 `write` 工具写入）。
