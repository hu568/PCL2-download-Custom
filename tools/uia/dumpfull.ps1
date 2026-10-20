param([int]$MaxDepth = 12, [string]$Filter = '')
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$p = Get-Process -Name 'Plain Craft Launcher 2' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output 'NOPCL'; exit 1 }
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
Write-Output ('WINDOWS: ' + $wins.Count)
function Dump($el, $d, $md) {
  if ($d -gt $md) { return }
  $w = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  $c = $w.GetFirstChild($el)
  while ($c -ne $null) {
    try {
      $n = $c.Current.Name; $ct = $c.Current.ControlType.ProgrammaticName; $id = $c.Current.AutomationId
      if ($n -ne '' -or $id -ne '') {
        $line = ('  ' * $d) + $ct + ' | ' + $n + ' | id=' + $id
        if ($Filter -eq '' -or $line -match $Filter) { Write-Output $line }
      }
      Dump $c ($d+1) $md
    } catch {}
    $c = $w.GetNextSibling($c)
  }
}
foreach($win in $wins){ Write-Output ('=== WINDOW: ' + $win.Current.Name); Dump $win 0 $MaxDepth }
