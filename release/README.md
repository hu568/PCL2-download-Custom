# PCL 下载中心（PCL2 主页插件复活版）

> 面向 **PCL2 2.99.0.0 官方版**（已移除游戏下载功能的版本）。
> 通过 **PCL 主页自定义插件（Custom.xaml）** + **独立后端（PCLDownloadCenter.exe，HTTP 服务 + Web UI）**，
> 在本机恢复“下载并安装 Minecraft”的完整能力：原版游戏、加载器、整合包、社区资源入口。

---

## 1. 它是什么

| 组成 | 说明 |
| --- | --- |
| `PCL\DownloadCenter\PCLDownloadCenter.exe` | 后端：下载器 + 安装器 + 本地 HTTP 服务（默认 `http://127.0.0.1:27321/`） |
| `PCL\Custom.xaml` | PCL 主页插件：状态卡片、版本列表、一键安装、任务进度、设置 |
| Web 下载中心 | 浏览器里更完整的界面：版本筛选、加载器、整合包搜索、实时进度与日志 |
| CLI | 主页按钮与脚本调用的命令行接口 |

后端只与本机 `127.0.0.1` 通信，不上传任何数据；所有文件都来自 Minecraft 官方、Fabric/Quilt 官方、Forge/NeoForge 官方 Maven 与 Modrinth 等公开接口（可选 BMCLAPI 镜像加速）。

---

## 2. 安装

### 方式一：发布包（推荐）

1. 关闭 PCL。
2. 双击运行发布包里的 `install.ps1`（或在 PowerShell 中执行）：
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1 -PclDir "D:\PCL2\PCL"
   ```
   脚本会：复制插件到 `PCL\DownloadCenter\`、备份原 `Custom.xaml`、写入引导主页、把 `Setup.ini` 的 `UiCustomType` 设为 1。
3. 启动 PCL，在主页点一次「刷新主页」即可看到下载中心。
   > 如果主页还是旧的，请在 PCL「设置 → 界面 → 主页」中选择“使用本地自定义主页”。

### 方式二：手动

1. 把 `DownloadCenter` 文件夹放到 `<PCL根>\PCL\` 下（即 `PCL\DownloadCenter\PCLDownloadCenter.exe`）。
2. 运行一次 `PCLDownloadCenter.exe --homepage publish` 生成 `PCL\Custom.xaml`。
3. `PCL\Setup.ini` 中加入 `UiCustomType:1`。

首次在主页点“安装”时，PCL 会弹出「执行确认」对话框，选择 **继续且今后不再要求确认** 即可（PCL 的安全机制，仅首次）。

---

## 3. 使用

### 3.1 PCL 主页（插件）

顶栏可切换视图：

- **首页**：服务状态、最新正式版 / 快照一键安装、当前任务摘要、打开 Web 下载中心、社区资源入口。
- **原版游戏**：版本列表（可分页/筛选），点任意版本即可安装。
- **加载器**：Fabric / Quilt / Forge / NeoForge 推荐版本一键安装。
- **整合包**：推荐整合包、导入本地整合包文件。
- **任务**：进度（`████░░ 45%`）、阶段、取消按钮。
- **设置**：切换下载源（官方 / BMCLAPI 镜像）、打开 Web 设置。

> 主页内容由后端自动重写，安装过程中点「刷新主页」即可看到最新进度。

### 3.2 Web 下载中心

浏览器打开 <http://127.0.0.1:27321/>（或主页点「打开 Web 下载中心」）。

- 原版游戏：版本筛选、搜索、分页安装
- 加载器：选类型 + MC 版本 → 安装
- 整合包：Modrinth 搜索与安装、URL/本地文件安装
- 任务：SSE 实时进度与日志、取消
- 设置：下载源、并发数、端口、CurseForge API Key、Java 路径

### 3.3 命令行

```text
PCLDownloadCenter.exe --server                     前台运行服务
PCLDownloadCenter.exe --ensure                     确保后台服务在运行
PCLDownloadCenter.exe --stop                       关闭服务
PCLDownloadCenter.exe --status                     查看状态
PCLDownloadCenter.exe --install vanilla 1.21.4     安装原版游戏
PCLDownloadCenter.exe --install loader fabric 1.21.4
PCLDownloadCenter.exe --install loader forge 1.20.1
PCLDownloadCenter.exe --install modpack modrinth <项目ID> [版本ID]
PCLDownloadCenter.exe --import pack <整合包文件>
PCLDownloadCenter.exe --open-ui                    打开 Web 下载中心
PCLDownloadCenter.exe --view task                  切换主页视图
PCLDownloadCenter.exe --cancel [任务ID]            取消任务
PCLDownloadCenter.exe --set-source bmclapi         切换下载源
PCLDownloadCenter.exe --homepage publish           重写主页
```

---

## 4. 常见问题

**Q：主页点按钮没有反应？**
A：首次需要确认 PCL 的「执行确认」弹窗，并选择“继续且今后不再要求确认”。之后可在 PCL 日志（`PCL\Log1.txt`）里看到“执行自定义事件：执行命令”。

**Q：安装很慢 / 下载失败？**
A：在设置里切换下载源为 BMCLAPI 镜像；或降低并发线程数。后端会自动重试并回退镜像。

**Q：PCL 版本列表里看不到刚装的版本？**
A：点 PCL 左下角版本选择处的刷新（或重启 PCL）。

**Q：整合包（CurseForge）安装提示缺少 API Key？**
A：CurseForge 需要 API Key，请在 Web 设置里填写；Modrinth 整合包无需 Key。

**Q：端口被占用？**
A：在 Web 设置或 `config.json` 里修改 `ServerPort`，然后重启服务。

**Q：卸载？**
A：运行发布包里的 `uninstall.ps1`（或 `uninstall.ps1 -PclDir "D:\PCL2\PCL"`），会恢复备份的主页并删除插件目录。

---

## 5. 目录结构

```text
PCL\DownloadCenter\
  PCLDownloadCenter.exe     后端
  Newtonsoft.Json.dll
  config.json               运行期配置（首次运行后生成/可手改）
  data\
    logs\                   运行日志
    tasks.json              任务历史
    cache\                  版本清单缓存
    temp\                   安装临时目录
```

`config.json` 主要项：

| 键 | 说明 |
| --- | --- |
| `DownloadSource` | `official` / `bmclapi` |
| `Threads` | 并发下载线程数（默认 32） |
| `ServerPort` | HTTP 端口（默认 27321） |
| `CurseForgeApiKey` | CurseForge API Key（可选） |
| `JavaPath` | 指定 Java（留空自动探测） |
| `HomepagePublish` | 是否自动重写主页 |
| `McDirOverride` | 覆盖 .minecraft 路径（留空自动从 PCL 配置推断） |

---

## 6. 自行构建

依赖：Windows + .NET Framework 4.8 运行时（系统自带即可），无需安装 .NET SDK。

```powershell
powershell -ExecutionPolicy Bypass -File tools\build.ps1       # → build\PCLDownloadCenter.exe
powershell -ExecutionPolicy Bypass -File tools\deploy.ps1      # 部署到 D:\PCL2\PCL\DownloadCenter
powershell -ExecutionPolicy Bypass -File tools\pack.ps1       # → release\（发布包）
```

源码结构见 `docs/ARCH.md`。

---

## 7. 验证情况

已在真机 PCL2 2.99.0.0 上完成端到端验证，包括：原版 1.21.4 / 1.21.3 / 1.8.9 等、Fabric / Quilt / Forge / NeoForge、Modrinth 整合包、PCL 主页 6 视图与任务进度、**1.21.4 启动进入主菜单**。
详细结果、截图与已知限制见发布包内 `docs/DELIVERY.md`、`docs/TEST-REPORT-*.md`。

## 8. 声明

- 本项目通过公开接口下载 Minecraft 与开源社区资源，不包含游戏本体，不分发任何受版权保护的游戏文件。
- 使用者应自行遵守所在地法律法规与 Minecraft 最终用户许可协议（EULA）；下载即视为同意 EULA。
- 本项目与 Mojang / Microsoft / PCL 官方无关；因使用本项目产生的一切后果由使用者自行承担。
- 请勿将本项目用于商业用途或二次分发游戏文件。