# 截取 PCL 主窗口（或整个屏幕）到 PNG
param(
    [string]$Out = 'D:\CODE\PCL2-download-Custom\build\shot.png',
    [string]$TitleMatch = '',
    [switch]$FullScreen
)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$rect = $null
if (-not $FullScreen) {
    $p = Get-Process -Name 'Plain Craft Launcher 2' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($p) {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
        $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
        foreach ($w in $wins) {
            $b = $w.Current.BoundingRectangle
            if ($b.Width -gt 100 -and $b.Height -gt 100) {
                if ($TitleMatch -eq '' -or $w.Current.Name -match $TitleMatch) { $rect = $b; break }
            }
        }
    }
}
if (-not $rect) {
    $rect = [System.Windows.Rect]::new(0, 0, [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width, [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height)
}
$width = [int]$rect.Width
$height = [int]$rect.Height
if ($width -le 0 -or $height -le 0) { throw "窗口尺寸无效" }
$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0, $bmp.Size)
$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output ("SAVED " + $Out + " " + $width + "x" + $height)
