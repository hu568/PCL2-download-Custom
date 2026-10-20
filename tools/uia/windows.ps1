Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root=[System.Windows.Automation.AutomationElement]::RootElement
$ws=$root.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::TrueCondition)
foreach($w in $ws){
  $n=$w.Current.Name
  if($n -ne ''){ Write-Output ('WIN: [' + $n + '] class=' + $w.Current.ClassName + ' pid=' + $w.Current.ProcessId) }
}
