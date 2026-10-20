Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$ErrorActionPreference='Stop'

function Get-PclWindow {
  $proc = Get-Process -Name 'Plain Craft Launcher 2' -ErrorAction SilentlyContinue
  foreach ($p in $proc) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($win) { return $win }
  }
  return $null
}

$win = Get-PclWindow
if (-not $win) { Write-Output 'NOPCL'; exit 0 }
Write-Output ('WINDOW: ' + $win.Current.Name + ' | ' + $win.Current.ClassName + ' | ' + $win.Current.BoundingRectangle)

function Dump($el, $depth, $maxdepth) {
  if ($depth -gt $maxdepth) { return }
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $child = $walker.GetFirstChild($el)
  while ($child -ne $null) {
    try {
      $n = $child.Current.Name
      $ct = $child.Current.ControlType.ProgrammaticName
      $id = $child.Current.AutomationId
      $r = $child.Current.BoundingRectangle
      if ($n -ne '' -or $id -ne '') { Write-Output (('  ' * $depth) + $ct + ' | name=' + $n + ' | id=' + $id + ' | rect=' + $r) }
      Dump $child ($depth+1) $maxdepth
    } catch {}
    $child = $walker.GetNextSibling($child)
  }
}
Dump $win 0 4
