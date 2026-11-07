# 应用通道（指定程序走代理）注入测试
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\app-channel-test.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\ClashProxy\bin\x64\Debug\net8.0-windows10.0.19041.0\ClashProxy.exe'
$dataDir = Join-Path $repo 'testdata'
$tools = $PSScriptRoot
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

$fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { Write-Host ("PASS  " + $name + "  " + $detail) -ForegroundColor Green }
    else { Write-Host ("FAIL  " + $name + "  " + $detail) -ForegroundColor Red; $script:fail++ }
}

# 编译被测程序
$dumpExe = Join-Path $tools 'envdump.exe'
& $csc /nologo /out:$dumpExe (Join-Path $tools 'envdump.cs') | Out-Null
Check '编译 envdump.exe' (Test-Path $dumpExe) $dumpExe

# 往 settings.json 里塞一条「应用通道」记录
$settingsPath = Join-Path $dataDir 'settings.json'
if (-not (Test-Path $settingsPath)) { throw "先跑一遍 e2e-test.ps1 生成 settings.json" }
$json = Get-Content $settingsPath -Raw | ConvertFrom-Json
$json.Apps = @([pscustomobject]@{ Name = 'envdump'; ExePath = $dumpExe; Enabled = $true })
$json | ConvertTo-Json -Depth 8 | Set-Content $settingsPath -Encoding UTF8

Get-Process ClashProxy, mihomo -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item (Join-Path $tools 'envdump.txt') -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

$env:CLASHPROXY_DATA = $dataDir
$proc = Start-Process $exe -PassThru
Start-Sleep -Seconds 8
$win = $AE::RootElement.FindFirst($TS::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)))

function NavTo($name) {
    $c = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::ListItem)))
    $script:win.FindFirst($TS::Descendants, $c).GetCurrentPattern(
        [System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1200
}
function ButtonByName($name) {
    $c = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Button)))
    $script:win.FindFirst($TS::Descendants, $c)
}
function ById($id) {
    $script:win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}

# 先启动内核，让注入的端口真实可用
NavTo '总览'
$startBtn = ById 'StartBtn'
if (-not $startBtn) { $startBtn = ButtonByName '启动' }
$startBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 12
Check '内核已启动' (@(Get-Process mihomo -ErrorAction SilentlyContinue).Count -ge 1) ''

# 进应用通道点「走代理启动」
NavTo '应用通道'
$launch = ButtonByName '走代理启动'
Check '找到「走代理启动」按钮' ($null -ne $launch) ''
$launch.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 6

$txtPath = Join-Path $tools 'envdump.txt'
Check '目标程序被启动' (Test-Path $txtPath) $txtPath
if (Test-Path $txtPath) {
    $content = Get-Content $txtPath -Raw
    Write-Host "--- envdump.txt ---" -ForegroundColor DarkGray
    Write-Host $content -ForegroundColor DarkGray
    Check 'HTTP_PROXY 指向 7891'  ($content -match 'HTTP_PROXY=http://127\.0\.0\.1:7891') ''
    Check 'HTTPS_PROXY 指向 7891' ($content -match 'HTTPS_PROXY=http://127\.0\.0\.1:7891') ''
    Check 'ALL_PROXY 指向 7891'   ($content -match 'ALL_PROXY=http://127\.0\.0\.1:7891') ''
    Check 'NO_PROXY 含本地回环'   ($content -match 'NO_PROXY=.*127\.0\.0\.1') ''
}

Stop-Process -Id $proc.Id -Force
Start-Sleep -Seconds 3
Check '退出后内核消失' (@(Get-Process mihomo -ErrorAction SilentlyContinue).Count -eq 0) ''
Remove-Item Env:CLASHPROXY_DATA -ErrorAction SilentlyContinue

Write-Host ''
if ($fail -eq 0) { Write-Host '全部通过 ✔' -ForegroundColor Green; exit 0 }
else { Write-Host ("失败 " + $fail + " 项 ✘") -ForegroundColor Red; exit 1 }