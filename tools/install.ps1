# 安装 PCL 下载中心插件
param(
    [string]$PclDir = '',
    [switch]$NoHomepage,
    [switch]$SkipCommandConfirm,
    [switch]$SkipStart,
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot

function Test-PclDir([string]$dir) {
    if ([string]::IsNullOrWhiteSpace($dir)) { return $false }
    if (-not (Test-Path $dir)) { return $false }
    $parent = Split-Path -Parent $dir
    $hasExe = $false
    foreach ($name in @('Plain Craft Launcher 2.exe','PCL2_CE_Release_x64.exe','Plain Craft Launcher Community Edition.exe','PCL.exe')) {
        if (Test-Path (Join-Path $parent $name)) { $hasExe = $true; break }
    }
    $hasIni = Test-Path (Join-Path $dir 'Setup.ini')
    $hasConfig = Test-Path (Join-Path $dir 'config.v1.yml')
    return ($hasExe -or $hasIni -or $hasConfig)
}

function Find-PclDir {
    $cands = New-Object System.Collections.Generic.List[string]
    if ($env:PCL_DIR) { $cands.Add($env:PCL_DIR) }
    foreach ($d in @('C','D','E','F')) {
        $cands.Add("$($d):\PCL2\PCL")
        $cands.Add("$($d):\PCL\PCL")
        $cands.Add("$($d):\Program Files\PCL2\PCL")
        $cands.Add("$($d):\Games\PCL2\PCL")
        $cands.Add("$($d):\PCL2")
    }
    foreach ($c in $cands) {
        if (Test-PclDir $c) { return $c }
    }
    return $null
}

# 停止目标目录里正在运行的后端（否则 Newtonsoft.Json.dll / exe 会被占用）
function Stop-ExistingBackend([string]$exePath) {
    if (-not (Test-Path $exePath)) { return }
    Write-Host "检测到已安装的后端，正在停止旧服务……" -ForegroundColor DarkGray
    try { & $exePath --stop | Out-Null } catch { }
    Start-Sleep -Milliseconds 1200
    $procs = Get-CimInstance Win32_Process -Filter "Name='PCLDownloadCenter.exe'" -ErrorAction SilentlyContinue
    foreach ($proc in $procs) {
        if ($proc.CommandLine -and $proc.CommandLine -like ("*" + $exePath + "*")) {
            try { Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop; Write-Host ("已结束进程 " + $proc.ProcessId) -ForegroundColor DarkGray } catch { }
        }
    }
    Start-Sleep -Milliseconds 600
}

# 逐文件复制 + 重试（避免单个被占用文件导致整个安装失败）
function Copy-PluginFiles([string]$source, [string]$dest) {
    $files = Get-ChildItem -Path $source -Recurse -File
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($source.Length).TrimStart('\')
        $dst = Join-Path $dest $rel
        $dir = Split-Path -Parent $dst
        if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        $ok = $false
        $lastErr = $null
        for ($i = 0; $i -lt 6; $i++) {
            try { Copy-Item $f.FullName $dst -Force -ErrorAction Stop; $ok = $true; break }
            catch { $lastErr = $_.Exception.Message; Start-Sleep -Milliseconds 400 }
        }
        if (-not $ok) {
            throw ("复制文件失败（可能仍被占用）：" + $dst + [Environment]::NewLine + "  " + $lastErr + [Environment]::NewLine + "  请关闭 PCL 后重试，或手动结束 PCLDownloadCenter.exe 进程。")
        }
    }
}

Write-Host "PCL 下载中心安装程序" -ForegroundColor Cyan
Write-Host ""

if (-not $PclDir) { $PclDir = Find-PclDir }
if (-not $PclDir) {
    if ($NonInteractive) { throw "未能自动找到 PCL 文件夹，请用 -PclDir 指定（例如 D:\PCL2\PCL）" }
    Write-Host "未能自动找到 PCL 的安装位置（PCL 文件夹）。" -ForegroundColor Yellow
    Write-Host "它通常位于 PCL 程序所在目录下的 PCL 文件夹，例如 D:\PCL2\PCL。" -ForegroundColor Yellow
    $input = Read-Host "请输入 PCL 文件夹路径（例如 D:\PCL2\PCL，直接回车退出）"
    if ([string]::IsNullOrWhiteSpace($input)) { Write-Host "已取消。"; exit 1 }
    $PclDir = $input.Trim('"', "'", ' ')
}
$PclDir = $PclDir.TrimEnd('\')
if (-not (Test-PclDir $PclDir)) {
    Write-Host "警告：$PclDir 看起来不像是 PCL 的 PCL 文件夹（没找到 Setup.ini 或上级目录的 PCL 程序）。" -ForegroundColor Yellow
    if (-not $NonInteractive) {
        $go = Read-Host "仍要继续安装吗？(y/N)"
        if ($go -notmatch '^[yY]') { Write-Host "已取消。"; exit 1 }
    }
}

$src = Join-Path $scriptDir 'PCL\DownloadCenter'
if (-not (Test-Path $src)) { $src = Join-Path $scriptDir 'DownloadCenter' }
if (-not (Test-Path $src)) { throw "找不到 DownloadCenter 目录，请在发布包根目录运行本脚本" }

# 注意：PCL 不需要关闭（安装只写 DownloadCenter 目录、Custom.xaml 与 Setup.ini），装完在主页点一次「刷新主页」即可
$running = Get-Process -Name 'Plain Craft Launcher 2','Plain Craft Launcher Community Edition' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "PCL 正在运行：安装完成后请在 PCL 主页点一次「刷新主页」。" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "安装到：$PclDir" -ForegroundColor Cyan
$target = Join-Path $PclDir 'DownloadCenter'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Stop-ExistingBackend (Join-Path $target 'PCLDownloadCenter.exe')
Copy-PluginFiles $src $target

# 备份并写入引导页 Custom.xaml（后端运行后会重写为功能主页）
$custom = Join-Path $PclDir 'Custom.xaml'
if (Test-Path $custom) {
    $existing = ""
    try { $existing = Get-Content $custom -Raw -Encoding UTF8 } catch { }
    if ($existing -notmatch 'PCL 下载中心') {
        # 只有在覆盖“非本插件生成”的主页时才备份，避免重复安装产生一堆备份
        $backup = Join-Path $PclDir ('Custom.xaml.bak-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        Copy-Item $custom $backup -Force
        Write-Host "已备份原主页：$backup" -ForegroundColor DarkGray
    } else {
        Write-Host "现有主页已由下载中心生成，跳过备份。" -ForegroundColor DarkGray
    }
}
$bootstrapSrc = Join-Path $scriptDir 'PCL\Custom.xaml'
if (Test-Path $bootstrapSrc) { Copy-Item $bootstrapSrc $custom -Force }

# 开启“使用本地自定义主页”
$setupIni = Join-Path $PclDir 'Setup.ini'
$lines = @()
if (Test-Path $setupIni) { $lines = @(Get-Content $setupIni) }
if (-not ($lines | Where-Object { $_ -match '^UiCustomType:' })) {
    $lines = @('UiCustomType:1') + $lines
} else {
    $lines = $lines | ForEach-Object { if ($_ -match '^UiCustomType:') { 'UiCustomType:1' } else { $_ } }
}
Set-Content -Path $setupIni -Value $lines -Encoding UTF8

if ($SkipCommandConfirm) {
    try {
        New-Item -Path 'HKCU:\Software\PCL' -Force | Out-Null
        Set-ItemProperty -Path 'HKCU:\Software\PCL' -Name 'HintCustomCommand' -Value 'True'
        Write-Host "已跳过 PCL 的「执行命令」首次确认。" -ForegroundColor DarkGray
    } catch { }
}

$exe = Join-Path $target 'PCLDownloadCenter.exe'
if (-not (Test-Path $exe)) { throw "安装后找不到 $exe" }

Write-Host "启动后端并生成主页内容 ..." -ForegroundColor Cyan
if (-not $SkipStart) {
    # 用 Start-Process 启动（不等待子进程树），再自己轮询端口，避免脚本卡住
    Start-Process -FilePath $exe -ArgumentList '--ensure' -WorkingDirectory $PclDir -WindowStyle Hidden | Out-Null
    $port = 27321
    $cfgPath = Join-Path $target 'config.json'
    if (Test-Path $cfgPath) {
        try { $c = Get-Content $cfgPath -Raw | ConvertFrom-Json; if ($c.ServerPort) { $port = [int]$c.ServerPort } } catch { }
    }
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 300
        try {
            $r = Invoke-WebRequest -Uri ("http://127.0.0.1:" + $port + "/api/ping") -UseBasicParsing -TimeoutSec 2
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
    }
    if ($ready) { Write-Host ("后端已就绪：http://127.0.0.1:" + $port + "/") -ForegroundColor DarkGray }
    else { Write-Host "提示：后端启动较慢或端口被占用，可稍后访问 Web 页面或看日志。" -ForegroundColor Yellow }
}
Start-Process -FilePath $exe -ArgumentList '--homepage publish' -WorkingDirectory $PclDir -WindowStyle Hidden | Out-Null
Start-Sleep -Milliseconds 1200

Write-Host ""
Write-Host "安装完成！" -ForegroundColor Green
Write-Host "  1. 启动 PCL"
Write-Host "  2. 在 PCL 主页点一次「刷新主页」，即可看到下载中心"
Write-Host "  3. Web 下载中心：http://127.0.0.1:27321/"
Write-Host ('  4. 卸载：运行 uninstall.ps1（或 uninstall.ps1 -PclDir "' + $PclDir + '"）')