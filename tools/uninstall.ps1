# 卸载 PCL 下载中心插件
param(
    [string]$PclDir = '',
    [switch]$KeepData
)
$ErrorActionPreference = 'Stop'

function Test-PclDir([string]$dir) {
    if ([string]::IsNullOrWhiteSpace($dir)) { return $false }
    if (-not (Test-Path $dir)) { return $false }
    if (Test-Path (Join-Path $dir 'DownloadCenter\PCLDownloadCenter.exe')) { return $true }
    if (Test-Path (Join-Path $dir 'Setup.ini')) { return $true }
    return $false
}

# 优先按脚本所在目录判断（发布包复制到 PCL2 程序所在目录后直接运行）：
#   脚本在 PCL2 程序所在目录 → 同级 PCL 文件夹；脚本直接放在 PCL 文件夹里 → 该文件夹
function Resolve-PclDir([string]$startDir) {
    if (Test-PclDir (Join-Path $startDir 'PCL')) { return (Join-Path $startDir 'PCL') }
    if (Test-PclDir $startDir) { return $startDir }
    return $null
}

# 兜底：脚本被单独放到别处时，扫描常见安装位置
function Find-PclDir {
    foreach ($d in @('C','D','E','F')) {
        foreach ($c in @("$($d):\PCL2\PCL", "$($d):\PCL\PCL", "$($d):\Program Files\PCL2\PCL")) {
            if (Test-PclDir $c) { return $c }
        }
    }
    return $null
}

if (-not $PclDir) { $PclDir = Resolve-PclDir $PSScriptRoot }
if (-not $PclDir) { $PclDir = Find-PclDir }
if (-not $PclDir) {
    $input = Read-Host "请输入 PCL 文件夹路径（例如 D:\PCL2\PCL，直接回车退出）"
    if ([string]::IsNullOrWhiteSpace($input)) { Write-Host "已取消。"; exit 1 }
    $PclDir = $input.Trim('"', "'", ' ')
}
$PclDir = $PclDir.TrimEnd('\')
$target = Join-Path $PclDir 'DownloadCenter'
$exe = Join-Path $target 'PCLDownloadCenter.exe'

Write-Host "卸载 PCL 下载中心（第三方修改版）：$target" -ForegroundColor Cyan
# 同上：不要用管道调用 GUI 子进程，否则可能卡住
if (Test-Path $exe) { try { Start-Process -FilePath $exe -ArgumentList '--stop' -WindowStyle Hidden -Wait -ErrorAction Stop } catch { } }
Start-Sleep -Milliseconds 1000
$procs = Get-CimInstance Win32_Process -Filter "Name='PCLDownloadCenter.exe'" -ErrorAction SilentlyContinue
foreach ($proc in $procs) {
    if ($proc.CommandLine -and $proc.CommandLine -like ("*" + $exe + "*")) {
        try { Stop-Process -Id $proc.ProcessId -Force -ErrorAction Stop } catch { }
    }
}
Start-Sleep -Milliseconds 400

$running = Get-Process -Name 'Plain Craft Launcher 2','Plain Craft Launcher Community Edition' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "PCL 正在运行，若删除失败请先关闭 PCL 后重试。" -ForegroundColor Yellow
}

# 恢复最近一次备份的主页
$custom = Join-Path $PclDir 'Custom.xaml'
$backup = Get-ChildItem $PclDir -Filter 'Custom.xaml.bak-*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$restored = $false
if ($backup) {
    Copy-Item $backup.FullName $custom -Force
    Write-Host "已恢复主页备份：$($backup.Name)" -ForegroundColor Green
    $restored = $true
} elseif (Test-Path $custom) {
    Remove-Item $custom -Force
    Write-Host "已删除自定义主页" -ForegroundColor Green
}
if (-not $restored) {
    # 没有备份可恢复：关闭「使用本地自定义主页」，回到 PCL 默认主页
    $setupIni = Join-Path $PclDir 'Setup.ini'
    if (Test-Path $setupIni) {
        $lines = @(Get-Content $setupIni) | ForEach-Object { if ($_ -match '^UiCustomType:') { 'UiCustomType:0' } else { $_ } }
        Set-Content -Path $setupIni -Value $lines -Encoding UTF8
        Write-Host "已将 UiCustomType 重置为 0（PCL 默认主页）" -ForegroundColor Green
    }
}

if (Test-Path $target) {
    if ($KeepData) {
        Get-ChildItem $target -Exclude 'data' | Remove-Item -Recurse -Force
        Write-Host "已删除插件文件（保留 data 目录）" -ForegroundColor Green
    } else {
        Remove-Item $target -Recurse -Force
        Write-Host "已删除插件目录：$target" -ForegroundColor Green
    }
}
Write-Host "卸载完成。重启 PCL 生效。" -ForegroundColor Yellow