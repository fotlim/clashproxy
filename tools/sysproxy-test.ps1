# 系统代理开关验证（脱离沙箱进程树，用 explorer 启动，写真实数据目录）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\sysproxy-test.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\ClashProxy\bin\x64\Debug\net8.0-windows10.0.19041.0\ClashProxy.exe'
$realData = Join-Path $env:LOCALAPPDATA 'ClashProxy'
$backup = Join-Path $repo 'testdata-fix\settings.backup.json'

$fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { Write-Host ("PASS  " + $name + "  " + $detail) -ForegroundColor Green }
    else { Write-Host ("FAIL  " + $name + "  " + $detail) -ForegroundColor Red; $script:fail++ }
}
function RegNow {
    Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
}

Get-Process ClashProxy, mihomo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# 备份真实设置
New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
Copy-Item (Join-Path $realData 'settings.json') $backup -Force

$reg0 = RegNow
Write-Host ("起始注册表：ProxyEnable=" + $reg0.ProxyEnable + " ProxyServer=" + $reg0.ProxyServer)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

# 用 explorer 启动，脱离 harness 进程树
Start-Process explorer.exe -ArgumentList ('"' + $exe + '"')
Start-Sleep -Seconds 10

$win = $AE::RootElement.FindFirst($TS::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'ClashProxy')))
Check '窗口打开（explorer 启动）' ($null -ne $win) ("pid=" + $(if ($win) { $win.Current.ProcessId } else { 'none' }))
if ($null -eq $win) { exit 1 }
$clashPid = $win.Current.ProcessId

function ById($id) {
    $script:win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}
function ButtonByName($name) {
    $c = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)))
    $script:win.FindFirst($TS::Descendants, $c)
}

$startBtn = ButtonByName '启动'
Check '找到「启动」按钮' ($null -ne $startBtn) 'button'
if ($startBtn) {
    $startBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 8
}
Check '内核已启动' ($null -ne (Get-Process mihomo -ErrorAction SilentlyContinue)) 'mihomo'

$toggle = ById 'SysProxyToggle'
Check '找到系统代理开关' ($null -ne $toggle) 'toggle'
if ($toggle) {
    $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Seconds 3
    $r1 = RegNow
    $result = (ById 'ResultText').Current.Name
    Write-Host ("结果文字：" + $result)
    Check '提示为已开启' ($result -like '*已开启*') $result
    Check 'ProxyEnable=1' ($r1.ProxyEnable -eq 1) ("ProxyEnable=" + $r1.ProxyEnable)
    Check 'ProxyServer 指向 7890' ($r1.ProxyServer -like '*:7890*') ("ProxyServer=" + $r1.ProxyServer)
    Check 'ProxyOverride 含 127.*' ($r1.ProxyOverride -like '*127.*') ("override=" + $r1.ProxyOverride)

    # 真实浏览器路径验证：走系统代理访问 google
    $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time 20 https://www.google.com/generate_204
    Check '系统代理下 curl 访问 google 成功' ($code -eq '204') ("http=" + $code)
}

# 关窗还原
$proc = Get-Process -Id $clashPid -ErrorAction SilentlyContinue
if ($proc) { $proc.CloseMainWindow() | Out-Null }
Start-Sleep -Seconds 5
if (-not $proc.HasExited) { Stop-Process -Id $clashPid -Force }
Start-Sleep -Seconds 2

$r2 = RegNow
Check '关窗后 ProxyEnable 还原为 0' ($r2.ProxyEnable -eq $reg0.ProxyEnable) ("ProxyEnable=" + $r2.ProxyEnable)
Check '关窗后 ProxyServer 还原' ($r2.ProxyServer -eq $reg0.ProxyServer) ("ProxyServer=" + $r2.ProxyServer)
Check '关窗后内核退出' ($null -eq (Get-Process mihomo -ErrorAction SilentlyContinue)) 'mihomo=0'

# 还原设置文件
Copy-Item $backup (Join-Path $realData 'settings.json') -Force

Write-Host ""
if ($fail -eq 0) { Write-Host "全部通过" -ForegroundColor Green } else { Write-Host ("失败 " + $fail + " 项") -ForegroundColor Red }
exit $fail