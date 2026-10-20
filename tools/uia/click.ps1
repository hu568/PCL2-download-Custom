param([string]$Name, [int]$Index = 0, [int]$Depth = 5, [switch]$Dump)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class M {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  public const uint LEFTDOWN = 0x02, LEFTUP = 0x04;
  public static void Click(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(60); mouse_event(LEFTDOWN,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(40); mouse_event(LEFTUP,0,0,0,IntPtr.Zero); }
}
"@
function Get-Win {
  $p = Get-Process -Name 'Plain Craft Launcher 2' -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $p) { return $null }
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
  return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}
function Find-All($el, $name, $list) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $child = $walker.GetFirstChild($el)
  while ($child -ne $null) {
    try { if ($child.Current.Name -eq $name) { $list.Add($child) | Out-Null }; Find-All $child $name $list } catch {}
    $child = $walker.GetNextSibling($child)
  }
}
$win = Get-Win
if (-not $win) { Write-Output 'NOPCL'; exit 1 }
$list = New-Object System.Collections.ArrayList
Find-All $win $Name $list
Write-Output ('MATCHES: ' + $list.Count)
if ($list.Count -eq 0) { exit 2 }
$el = $list[$Index]
$r = $el.Current.BoundingRectangle
Write-Output ('TARGET: ' + $el.Current.ControlType.ProgrammaticName + ' | ' + $el.Current.Name + ' | ' + $r + ' | id=' + $el.Current.AutomationId)
$cx = [int]($r.X + $r.Width/2); $cy = [int]($r.Y + $r.Height/2)
Write-Output ('CLICK at ' + $cx + ',' + $cy)
[M]::Click($cx, $cy)
Start-Sleep -Milliseconds 900
if ($Dump) {
  function Dump($e, $d, $md) {
    if ($d -gt $md) { return }
    $w = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $c = $w.GetFirstChild($e)
    while ($c -ne $null) {
      try {
        $n = $c.Current.Name; $ct = $c.Current.ControlType.ProgrammaticName
        if ($n -ne '') { Write-Output (('  ' * $d) + $ct + ' | ' + $n + ' | id=' + $c.Current.AutomationId) }
        Dump $c ($d+1) $md
      } catch {}
      $c = $w.GetNextSibling($c)
    }
  }
  $win = Get-Win
  if ($win) { Dump $win 0 $Depth }
}
