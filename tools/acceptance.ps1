# 验收测试：真实调用后端安装流程并校验产物
param(
    [string]$Port = '27361',
    [string]$McDir = 'D:\PCL2\.minecraft',
    [string]$Only = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$work = Join-Path $repo 'build\accept'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$cfg = Join-Path $work 'config.json'
@{ ServerPort = [int]$Port; McDirOverride = $McDir; DownloadSource = 'official'; Threads = 32; HomepagePublish = $false } |
    ConvertTo-Json | Set-Content -Path $cfg -Encoding UTF8

if (-not $SkipBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build.ps1') -OutDir 'build\accept' | Out-Null
}
$exe = Join-Path $work 'PCLDownloadCenter.exe'
if (-not (Test-Path $exe)) { throw "找不到 $exe" }
$base = "http://127.0.0.1:$Port"

function Invoke-Json($method, $path, $body) {
    $params = @{ Uri = ($base + $path); Method = $method; TimeoutSec = 30; UseBasicParsing = $true }
    if ($body) { $params['Body'] = $body; $params['ContentType'] = 'application/json' }
    return (Invoke-RestMethod @params)
}

function Stop-Server {
    try { Invoke-Json 'POST' '/api/shutdown' $null | Out-Null } catch { }
    Start-Sleep -Milliseconds 700
}

Stop-Server
Start-Process -FilePath $exe -ArgumentList '--server' -WindowStyle Hidden | Out-Null
$ok = $false
for ($i = 0; $i -lt 40; $i++) {
    try { Invoke-Json 'GET' '/api/ping' $null | Out-Null; $ok = $true; break } catch { Start-Sleep -Milliseconds 250 }
}
if (-not $ok) { throw "后端未能启动（端口 $Port）" }

$scenarios = @(
    @{ Name = '原版 1.21.4';        Kind = 'vanilla'; Args = @('1.21.4') },
    @{ Name = 'Fabric 1.21.4';      Kind = 'loader';  Args = @('fabric', '1.21.4') },
    @{ Name = 'Quilt 1.21.4';       Kind = 'loader';  Args = @('quilt', '1.21.4') },
    @{ Name = 'Forge 1.20.1';       Kind = 'loader';  Args = @('forge', '1.20.1') },
    @{ Name = 'NeoForge 1.21.1';    Kind = 'loader';  Args = @('neoforge', '1.21.1') },
    @{ Name = 'Modrinth 整合包';     Kind = 'modpack'; Args = @('modrinth', $env:DC_MODPACK_ID) }
)

$results = @()
foreach ($s in $scenarios) {
    if ($Only -and $s.Name -notmatch $Only) { continue }
    if ($s.Kind -eq 'modpack' -and [string]::IsNullOrWhiteSpace($env:DC_MODPACK_ID)) {
        $results += [pscustomobject]@{ 场景 = $s.Name; 结果 = 'SKIP'; 任务 = ''; 耗时 = ''; 说明 = '未设置 DC_MODPACK_ID' }
        continue
    }
    Write-Host ""
    Write-Host ("=== " + $s.Name + " ===") -ForegroundColor Cyan
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-Json 'POST' '/api/install' (@{ kind = $s.Kind; args = $s.Args } | ConvertTo-Json -Compress)
        $taskId = $r.taskId
        $last = $null
        for ($i = 0; $i -lt 3600; $i++) {
            Start-Sleep -Milliseconds 800
            $t = (Invoke-Json 'GET' ("/api/task?id=" + $taskId) $null).task
            if ($t.status -ne 'Running' -and $t.status -ne 'Pending') { $last = $t; break }
            if ($i % 15 -eq 0) { Write-Host ("  [" + [int]($t.progress * 100) + "%] " + $t.phase) }
        }
        $sw.Stop()
        if (-not $last) { throw "任务超时" }
        if ($last.status -ne 'Success') { throw ($last.error + " @ " + $last.phase) }
        $vid = $last.versionId
        $vjson = Join-Path $McDir ("versions\" + $vid + "\" + $vid + ".json")
        $detail = "versionId=$vid"
        if (-not (Test-Path $vjson)) { throw "缺少版本 JSON：$vjson" }
        if ($s.Kind -eq 'vanilla') {
            $jar = Join-Path $McDir ("versions\" + $vid + "\" + $vid + ".jar")
            if (-not (Test-Path $jar)) { throw "缺少客户端 jar：$jar" }
            $detail += " jar=" + [math]::Round((Get-Item $jar).Length / 1MB, 1) + "MB"
        }
        if ($s.Kind -eq 'modpack') {
            $mods = Join-Path $McDir ("versions\" + $vid + "\mods")
            $cnt = 0
            if (Test-Path $mods) { $cnt = (Get-ChildItem $mods -Filter *.jar -ErrorAction SilentlyContinue).Count }
            $detail += " mods=$cnt"
        }
        $results += [pscustomobject]@{ 场景 = $s.Name; 结果 = 'PASS'; 任务 = $taskId; 耗时 = [math]::Round($sw.Elapsed.TotalSeconds, 1); 说明 = $detail }
        Write-Host ("  PASS  " + $detail) -ForegroundColor Green
    } catch {
        $sw.Stop()
        $results += [pscustomobject]@{ 场景 = $s.Name; 结果 = 'FAIL'; 任务 = ''; 耗时 = [math]::Round($sw.Elapsed.TotalSeconds, 1); 说明 = $_.Exception.Message }
        Write-Host ("  FAIL  " + $_.Exception.Message) -ForegroundColor Red
    }
}

Stop-Server
Write-Host ""
Write-Host "===== 验收结果 =====" -ForegroundColor Cyan
$results | Format-Table -AutoSize | Out-String -Width 200
$fail = @($results | Where-Object { $_.结果 -eq 'FAIL' }).Count
Write-Host ("通过 " + @($results | Where-Object { $_.结果 -eq 'PASS' }).Count + " / 失败 " + $fail) -ForegroundColor ($(if ($fail) { 'Red' } else { 'Green' }))
if ($fail) { exit 1 }
