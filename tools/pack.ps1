# 打包发布目录 release/（PCL 插件 + 安装脚本 + 说明）
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
# 文档
$docsOut = Join-Path $release 'docs'
New-Item -ItemType Directory -Force -Path $docsOut | Out-Null
foreach ($doc in @('DELIVERY.md','IMPLEMENTATION.md','ARCH.md','TEST-REPORT-vanilla.md','TEST-REPORT-cli.md','TEST-REPORT-pack.md')) {
    $p = Join-Path $root ('docs\' + $doc)
    if (Test-Path $p) { Copy-Item $p $docsOut -Force }
}

# 生成一份引导页 Custom.xaml（安装时会由后端重写为正式主页）
$bootstrap = @'
<!-- PCL 下载中心（由 PCLDownloadCenter 自动维护）-->
<local:MyCard Title="PCL 下载中心" Margin="0,0,0,15">
  <StackPanel Margin="25,40,23,15">
    <TextBlock TextWrapping="Wrap" Text="下载中心已安装，但后端尚未运行。请双击运行 PCL\DownloadCenter\PCLDownloadCenter.exe，然后点击下方按钮刷新主页。" />
    <local:MyButton Margin="0,12,0,0" Height="35" HorizontalAlignment="Left" Padding="25,0,25,0" ColorType="Highlight" Text="启动下载中心服务" EventType="执行命令" EventData="DownloadCenter\PCLDownloadCenter.exe|--ensure" />
    <local:MyButton Margin="0,10,0,0" Height="35" HorizontalAlignment="Left" Padding="25,0,25,0" Text="刷新主页" EventType="刷新主页" EventData="-" />
  </StackPanel>
</local:MyCard>
'@
Set-Content -Path (Join-Path $release 'PCL\Custom.xaml') -Value $bootstrap -Encoding UTF8

Write-Host "已生成发布目录：$release" -ForegroundColor Green
Get-ChildItem $release -Recurse -File | ForEach-Object { "  " + $_.FullName.Replace($release + '\','') + "  (" + [math]::Round($_.Length/1024,1) + " KB)" }