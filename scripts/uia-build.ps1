Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$p=Get-Process PEBakery|select -First 1
$root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
function All{$root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)}
(All|?{$_.Current.AutomationId -eq 'ProjectBuildStartButton'}|select -First 1).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
$start=Get-Date
while($true){ Start-Sleep 20
 $o=@(); foreach($e in All){$c=$e.Current; if($c.ControlType.ProgrammaticName -eq 'ControlType.Text' -and $c.Name -match '%|Build|Error|Halt|processed|Running|Message'){$o+=$c.Name}}
 ("[{0}] " -f ((Get-Date)-$start).ToString('hh\:mm\:ss')) + ($o -join ' || ') | Set-Content C:\develop\WinPE\build\buildstatus.txt
 if(-not (Get-Process PEBakery -ErrorAction SilentlyContinue)){break} }
