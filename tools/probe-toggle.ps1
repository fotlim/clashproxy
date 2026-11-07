Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
$win = $AE::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'ClashProxy')))
if ($null -eq $win) { Write-Host "no window"; exit 1 }

function ById($id) {
    $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}
function Btn($n) {
    $c1 = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $n)
    $c2 = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)
    $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.AndCondition($c1, $c2)))
}

$kb = "C:\Users\LinOcean\AppData\Local\ClashProxy\logs\kernel.log"
Write-Host ("kernel.log before: " + (Get-Item $kb).LastWriteTime)
(Btn '启动').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 8
Write-Host ("kernel.log after: " + (Get-Item $kb).LastWriteTime)
Write-Host ("mihomo count: " + ((Get-Process mihomo -ErrorAction SilentlyContinue) | Measure-Object).Count)

$t = ById 'SysProxyToggle'
$tp = $t.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
Write-Host ("toggle found, state=" + $tp.Current.ToggleState)
$tp.Toggle()
Start-Sleep -Seconds 3
Write-Host ("ResultText: " + (ById 'ResultText').Current.Name)
Write-Host ("toggle state now: " + $tp.Current.ToggleState)

Write-Host "--- logs dir ---"
Get-ChildItem "C:\Users\LinOcean\AppData\Local\ClashProxy\logs" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize | Out-String
Write-Host "--- sysproxy.log ---"
Get-Content "C:\Users\LinOcean\AppData\Local\ClashProxy\logs\sysproxy.log" -ErrorAction SilentlyContinue | Out-String
Write-Host ("--- registry: ProxyEnable=" + (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings').ProxyEnable)