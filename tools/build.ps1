# 编译 PCLDownloadCenter（.NET Framework 4.8，csc 直编，无需 MSBuild）
param(
    [switch]$Clean,
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
$tools = $PSScriptRoot
$root = Split-Path -Parent $tools
$src = Join-Path $root 'src\PCLDownloadCenter'
$outDir = if ($OutDir) { if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir } } else { Join-Path $root 'build' }
$csc = Join-Path $tools 'nuget\compilers\tasks\net472\csc.exe'
$refDir = Join-Path $tools 'nuget\refasm\build\.NETFramework\v4.8'
$newtonsoft = Join-Path $tools 'nuget\newtonsoft\lib\net45\Newtonsoft.Json.dll'
$fwDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'

if ($Clean -and (Test-Path $outDir)) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

if (-not (Test-Path $csc)) { throw "找不到编译器：$csc" }
if (-not (Test-Path $refDir)) { throw "找不到引用程序集：$refDir" }
if (-not (Test-Path $newtonsoft)) { throw "找不到 Newtonsoft.Json：$newtonsoft" }

$refNames = @(
    'mscorlib.dll','System.dll','System.Core.dll','System.Net.Http.dll',
    'System.Web.Extensions.dll','System.Xml.dll','System.Xml.Linq.dll',
    'System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll',
    'System.Configuration.dll','System.Numerics.dll'
)
$refs = @()
foreach ($n in $refNames) {
    $p = Join-Path $refDir $n
    if (Test-Path $p) { $refs += $p }
}
$refs += (Join-Path $fwDir 'System.IO.Compression.dll')
$refs += (Join-Path $fwDir 'System.IO.Compression.FileSystem.dll')
$refs += $newtonsoft

$files = Get-ChildItem -Path $src -Recurse -Filter *.cs | Sort-Object FullName | ForEach-Object { $_.FullName }
if ($files.Count -eq 0) { throw "找不到任何 .cs 文件：$src" }

$outExe = Join-Path $outDir 'PCLDownloadCenter.exe'
$rsp = Join-Path $outDir ('build-' + $PID + '-' + [guid]::NewGuid().ToString('N').Substring(0,6) + '.rsp')
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('/nologo')
$lines.Add('/target:winexe')
$lines.Add('/platform:anycpu')
$lines.Add('/langversion:latest')
$lines.Add('/debug-')
$lines.Add('/optimize+')
$lines.Add('/warn:4')
$lines.Add('/nowarn:1591,0168,0169,0219,0067,0649')
$lines.Add('/out:"' + $outExe + '"')
foreach ($r in $refs) { $lines.Add('/r:"' + $r + '"') }
foreach ($f in $files) { $lines.Add('"' + $f + '"') }
[System.IO.File]::WriteAllLines($rsp, $lines, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "编译 $($files.Count) 个源文件 ..." -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $csc "@$rsp"
$code = $LASTEXITCODE
$sw.Stop()
if ($code -ne 0) {
    Write-Host "编译失败（退出码 $code）" -ForegroundColor Red
    exit $code
}
Copy-Item $newtonsoft (Join-Path $outDir 'Newtonsoft.Json.dll') -Force
Write-Host ("编译成功：" + $outExe + "  (" + [math]::Round($sw.Elapsed.TotalSeconds,1) + "s, " + [math]::Round((Get-Item $outExe).Length/1024,1) + " KB)") -ForegroundColor Green