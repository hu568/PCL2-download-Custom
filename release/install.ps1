# 安装 PCL 下载中心插件
# 用法：先把发布包内容复制到 PCL2 程序所在目录（与 PCL 文件夹、PCL 主程序 exe 同级），再在该目录运行本脚本。
# 本脚本不扫描磁盘查找 PCL：安装位置只由脚本所在目录决定，或用 -PclDir 显式指定。
param(
    [string]$PclDir = '',
    [switch]$NoHomepage,
    [switch]$SkipCommandConfirm,
    [switch]$SkipStart,
    [switch]$NonInteractive
)
$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot

# PCL2 程序所在目录（PCL 文件夹的上一级），依据是目录里有 PCL 主程序 exe
function Test-PclRoot([string]$dir) {
    if ([string]::IsNullOrWhiteSpace($dir)) { return $false }
    if (-not (Test-Path $dir)) { return $false }
    foreach ($name in @('Plain Craft Launcher 2.exe','PCL2_CE_Release_x64.exe','Plain Craft Launcher Community Edition.exe','PCL.exe')) {
        if (Test-Path (Join-Path $dir $name)) { return $true }
    }
    return $false
}

# PCL 文件夹，依据是目录里有 Setup.ini 或 config.v1.yml
function Test-PclFolder([string]$dir) {
    if ([string]::IsNullOrWhiteSpace($dir)) { return $false }
    if (-not (Test-Path $dir)) { return $false }
    return ((Test-Path (Join-Path $dir 'Setup.ini')) -or (Test-Path (Join-Path $dir 'config.v1.yml')))
}

# 只按脚本所在目录判断（不扫描磁盘）：
#   脚本放在 PCL2 根目录（同级有 PCL 主程序 exe）→ <PCL2 根>\PCL
#   脚本直接放进 PCL 文件夹 → 该文件夹本身
#   脚本放在含 PCL 文件夹的目录 → <目录>\PCL
function Resolve-PclDir([string]$startDir) {
    if (Test-PclRoot $startDir) { return (Join-Path $startDir 'PCL') }
    if (Test-PclFolder $startDir) { return $startDir }
    if (Test-PclFolder (Join-Path $startDir 'PCL')) { return (Join-Path $startDir 'PCL') }
    return $null
}

# 停止目标目录里正在运行的后端（否则 Newtonsoft.Json.dll / exe 会被占用）
function Stop-ExistingBackend([string]$exePath) {
    if (-not (Test-Path $exePath)) { return }
    Write-Host "检测到已安装的后端，正在停止旧服务……" -ForegroundColor DarkGray
    # 用 Start-Process -Wait 且不接管标准流：直接 & 调用 GUI 子进程时管道会被子进程继承，脚本可能一直等下去（卡死）
    try { Start-Process -FilePath $exePath -ArgumentList '--stop' -WindowStyle Hidden -Wait -ErrorAction Stop } catch { }
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
    # 发布包直接复制到 PCL2 目录时，源目录就是目标目录，此时文件已就位，无需复制
    $srcFull = [System.IO.Path]::GetFullPath($source).TrimEnd('\')
    $dstFull = [System.IO.Path]::GetFullPath($dest).TrimEnd('\')
    if ($srcFull -ieq $dstFull) {
        Write-Host "插件文件已经在目标目录（无需复制）。" -ForegroundColor DarkGray
        return
    }
    $files = Get-ChildItem -Path $source -Recurse -File
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($source.Length).TrimStart('\')
        $dst = Join-Path $dest $rel
        if ([System.IO.Path]::GetFullPath($f.FullName) -ieq [System.IO.Path]::GetFullPath($dst)) { continue }
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

Write-Host "PCL 下载中心（第三方修改版）安装程序" -ForegroundColor Cyan
Write-Host ""

if (-not $PclDir) { $PclDir = Resolve-PclDir $scriptDir }
if (-not $PclDir) {
    Write-Host "未找到 PCL2 的位置：本脚本不扫描磁盘，只按脚本所在目录判断。" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "请先把发布包内容复制到 PCL2 程序所在目录（与 PCL 文件夹、PCL 主程序 exe 同级），例如：" -ForegroundColor Yellow
    Write-Host "  D:\PCL2\                        <- 复制到这里" -ForegroundColor DarkGray
    Write-Host "    Plain Craft Launcher 2.exe" -ForegroundColor DarkGray
    Write-Host "    PCL\                          （插件装进这个文件夹）" -ForegroundColor DarkGray
    Write-Host "    install.ps1  uninstall.ps1  README.md  docs\" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "然后在该目录运行：" -ForegroundColor Yellow
    Write-Host '  powershell -ExecutionPolicy Bypass -File .\install.ps1' -ForegroundColor DarkGray
    Write-Host "也可以显式指定 PCL 文件夹：" -ForegroundColor Yellow
    Write-Host '  powershell -ExecutionPolicy Bypass -File .\install.ps1 -PclDir "D:\PCL2\PCL"' -ForegroundColor DarkGray
    exit 1
}
$PclDir = $PclDir.TrimEnd('\')
if (-not (Test-PclFolder $PclDir) -and -not (Test-PclRoot (Split-Path -Parent $PclDir))) {
    Write-Host "警告：$PclDir 看起来不像 PCL 的 PCL 文件夹（没有 Setup.ini，上级目录也没有 PCL 主程序）。" -ForegroundColor Yellow
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
$custom = Join-Path $PclDir 'Custom.xaml'

# 先备份原主页：必须在停止旧后端之前做，因为 --stop 会把主页重写成「服务未运行」快照
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

New-Item -ItemType Directory -Force -Path $target | Out-Null
Stop-ExistingBackend (Join-Path $target 'PCLDownloadCenter.exe')
Copy-PluginFiles $src $target

# 写入引导页 Custom.xaml（后端运行后会重写为功能主页）
$bootstrapSrc = Join-Path $scriptDir 'bootstrap\Custom.xaml'
if (-not (Test-Path $bootstrapSrc)) { $bootstrapSrc = Join-Path $scriptDir 'PCL\Custom.xaml' }
# 发布包就在 PCL 文件夹里时，引导页和目标主页是同一个文件，无需复制
$bootstrapIsCustom = (Test-Path $bootstrapSrc) -and ([System.IO.Path]::GetFullPath($bootstrapSrc) -ieq [System.IO.Path]::GetFullPath($custom))
if ((Test-Path $bootstrapSrc) -and (-not $bootstrapIsCustom)) { Copy-Item $bootstrapSrc $custom -Force }

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