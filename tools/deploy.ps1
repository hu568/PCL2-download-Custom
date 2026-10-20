# 部署 PCL 下载中心到测试用 PCL（D:\PCL2）
param(
    [string]$Target = 'D:\PCL2\PCL\DownloadCenter',
    [switch]$NoHomepage,
    [switch]$NoCustom
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'build'
$exe = Join-Path $build 'PCLDownloadCenter.exe'
if (-not (Test-Path $exe)) { throw "找不到 $exe，请先运行 tools\build.ps1" }

New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item $exe $Target -Force
$Newtonsoft = Join-Path $build 'Newtonsoft.Json.dll'
if (Test-Path $Newtonsoft) { Copy-Item $Newtonsoft $Target -Force }

$cfg = Join-Path $Target 'config.json'
if (-not (Test-Path $cfg)) {
    @'
{
  "DownloadSource": "official",
  "Threads": 32,
  "ServerPort": 27321,
  "HomepageMaxVersions": 30,
  "HomepageShowSnapshots": true,
  "HomepagePublish": true,
  "MaxConcurrentTasks": 1
}
'@ | Set-Content -Path $cfg -Encoding UTF8
}

# 确保 PCL 使用本地自定义主页
$pclDir = Split-Path -Parent $Target
$setupIni = Join-Path $pclDir 'Setup.ini'
if (Test-Path $setupIni) {
    $lines = Get-Content $setupIni
    if (-not ($lines | Where-Object { $_ -match '^UiCustomType:' })) {
        $lines = @('UiCustomType:1') + $lines
    } else {
        $lines = $lines | ForEach-Object { if ($_ -match '^UiCustomType:') { 'UiCustomType:1' } else { $_ } }
    }
    Set-Content -Path $setupIni -Value $lines -Encoding UTF8
} else {
    'UiCustomType:1' | Set-Content -Path $setupIni -Encoding UTF8
}

$deployed = Join-Path $Target 'PCLDownloadCenter.exe'
Write-Host "已部署到：$Target" -ForegroundColor Green
if (-not $NoHomepage) {
    & $deployed --homepage publish
    if ($LASTEXITCODE -ne 0) { Write-Host "重写主页失败（退出码 $LASTEXITCODE）" -ForegroundColor Yellow }
}
Write-Host "PCL 目录：$pclDir（Custom.xaml 位于此）" -ForegroundColor Green