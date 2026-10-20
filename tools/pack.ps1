# 打包发布目录 release/（PCL 插件 + 安装脚本 + 说明；不含 docs/）
param(
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'build'
if (-not (Test-Path (Join-Path $build 'PCLDownloadCenter.exe'))) { throw "请先运行 tools\build.ps1" }

$release = if ($OutDir) { $OutDir } else { Join-Path $root 'release' }
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
$plugin = Join-Path $release 'PCL\DownloadCenter'
New-Item -ItemType Directory -Force -Path $plugin | Out-Null

Copy-Item (Join-Path $build 'PCLDownloadCenter.exe') $plugin -Force
Copy-Item (Join-Path $build 'Newtonsoft.Json.dll') $plugin -Force

# 默认配置
@'
{
  "DownloadSource": "official",
  "Threads": 32,
  "ServerPort": 27321,
  "CurseForgeApiKey": "",
  "JavaPath": "",
  "HomepageMaxVersions": 30,
  "HomepageShowSnapshots": true,
  "HomepagePublish": true,
  "MaxConcurrentTasks": 1,
  "McDirOverride": "",
  "UseMirrorFallback": true
}
'@ | Set-Content -Path (Join-Path $plugin 'config.json') -Encoding UTF8

Copy-Item (Join-Path $root 'tools\install.ps1') $release -Force
Copy-Item (Join-Path $root 'tools\uninstall.ps1') $release -Force
if (Test-Path (Join-Path $root 'README.md')) { Copy-Item (Join-Path $root 'README.md') $release -Force }
# 许可与第三方声明：放在发布包根目录，同时复制到 exe 所在目录（随安装进入 PCL\DownloadCenter）
foreach ($lf in @('LICENSE','LICENCE-PCL.md','THIRD-PARTY-NOTICES.md')) {
    $lp = Join-Path $root $lf
    if (-not (Test-Path $lp)) { throw "缺少许可文件：$lf" }
    Copy-Item $lp $release -Force
    Copy-Item $lp $plugin -Force
}

# 注意：发布包**不包含** docs/（内部文档只在仓库里，不随包分发）

# 生成一份引导页 Custom.xaml（放在 bootstrap\，由 install.ps1 复制到 PCL\ 后再由后端重写为正式主页；
# 不直接放进 PCL\ 是为了让“把发布包复制进 PCL2 目录”这一步不会覆盖用户已有的主页）
$bootstrapDir = Join-Path $release 'bootstrap'
New-Item -ItemType Directory -Force -Path $bootstrapDir | Out-Null
$bootstrap = @'
<!-- PCL 下载中心（第三方修改版，由 PCLDownloadCenter 自动维护）-->
<local:MyCard Title="PCL 下载中心（第三方修改版）" Margin="0,0,0,15">
  <StackPanel Margin="25,40,23,15">
    <TextBlock TextWrapping="Wrap" Text="下载中心已安装，但后端尚未运行。请双击运行 PCL\DownloadCenter\PCLDownloadCenter.exe，然后点击下方按钮刷新主页。" />
    <local:MyButton Margin="0,12,0,0" Height="35" HorizontalAlignment="Left" Padding="25,0,25,0" ColorType="Highlight" Text="启动下载中心服务" EventType="执行命令" EventData="DownloadCenter\PCLDownloadCenter.exe|--ensure" />
    <local:MyButton Margin="0,10,0,0" Height="35" HorizontalAlignment="Left" Padding="25,0,25,0" Text="刷新主页" EventType="刷新主页" EventData="-" />
  </StackPanel>
</local:MyCard>
'@
Set-Content -Path (Join-Path $bootstrapDir 'Custom.xaml') -Value $bootstrap -Encoding UTF8

Write-Host "已生成发布目录：$release" -ForegroundColor Green
Get-ChildItem $release -Recurse -File | ForEach-Object { "  " + $_.FullName.Replace($release + '\','') + "  (" + [math]::Round($_.Length/1024,1) + " KB)" }