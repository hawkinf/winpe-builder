Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$p=Get-Process PEBakery|select -First 1
$root=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$root.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Normal')
function All{$root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)}
(All|?{$_.Current.ControlType.ProgrammaticName -eq 'ControlType.TreeItem' -and $_.Current.Name -eq 'Source Config'}|select -First 1).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep 2
(All|?{$_.Current.HelpText -eq 'Rescan Source'}|select -First 1).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
$end=(Get-Date).AddSeconds(150); $done=$false
while((Get-Date) -lt $end -and -not $done){ Start-Sleep 3; $done = [bool](All|?{$_.Current.Name -match 'btn_SrcSave\] processed'}) }
$o=@(); foreach($e in All){$c=$e.Current;$t=$c.ControlType.ProgrammaticName;$v='';try{$v=$e.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value}catch{}
 if($t -match 'ComboBox|Edit'){$o+="$t|$v"}; if($t -eq 'ControlType.Text' -and $c.Name -match 'processed|Windows|Pro|Home|Edition|Language|x64'){$o+="TXT|$($c.Name)"}}
$o|Set-Content C:\develop\WinPE\build\scan.txt
