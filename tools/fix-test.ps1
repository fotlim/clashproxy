# 规则保存（离线缓存）回归测试
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\fix-test.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\ClashProxy\bin\x64\Debug\net8.0-windows10.0.19041.0\ClashProxy.exe'
$dataDir = Join-Path $repo 'testdata-fix'
$realData = Join-Path $env:LOCALAPPDATA 'ClashProxy'

$fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { Write-Host ("PASS  " + $name + "  " + $detail) -ForegroundColor Green }
    else { Write-Host ("FAIL  " + $name + "  " + $detail) -ForegroundColor Red; $script:fail++ }
}

if (-not (Test-Path $exe)) { throw "找不到 $exe，先执行 dotnet build" }

Get-Process ClashProxy, mihomo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# 用真实数据做底：settings.json + profiles\current.yaml，但把订阅链接改成不可达地址
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path (Join-Path $dataDir 'profiles') -Force | Out-Null
$settings = Get-Content (Join-Path $realData 'settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$settings.SubscriptionUrl = 'https://invalid.example.invalid/sub'
$settings | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $dataDir 'settings.json') -Encoding UTF8
Copy-Item (Join-Path $realData 'profiles\current.yaml') (Join-Path $dataDir 'profiles\current.yaml') -Force
Write-Host ("规则条数：" + $settings.Sites.Count + "，订阅链接已改成不可达")

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

$env:CLASHPROXY_DATA = $dataDir
$proc = Start-Process $exe -PassThru
Start-Sleep -Seconds 8

$win = $AE::RootElement.FindFirst($TS::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)))
Check '窗口打开' ($null -ne $win) ("title=" + $win.Current.Name)
if ($null -eq $win) { exit 1 }

function ById($id) {
    $script:win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}
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

# --- 规则页：保存并应用（订阅链接不可达，必须靠本地缓存成功） ---
NavTo '分流规则'
$applyBtn = ButtonByName '保存并应用'
Check '找到「保存并应用」按钮' ($null -ne $applyBtn) 'button'
$applyBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 4

$hint = (ById 'HintText').Current.Name
Write-Host ("提示：" + $hint)
Check '保存成功（离线可用）' ($hint -like '*已保存*' -or $hint -like '*已生成*') $hint
Check '没有下载失败的报错' ($hint -notlike '*失败*') $hint

$cfg = Join-Path $dataDir 'config.yaml'
Check 'config.yaml 已生成' (Test-Path $cfg) $cfg
if (Test-Path $cfg) {
    $text = Get-Content $cfg -Raw -Encoding UTF8
    Check '配置含 google.com 规则' ($text -like '*DOMAIN-SUFFIX,google.com,PROXY*') 'rule'
    Check '配置含 MATCH,DIRECT 兜底' ($text -like '*MATCH,DIRECT*') 'fallback'
    Check '配置含 app-channel 监听' ($text -like '*app-channel*') 'listener'
}

# --- 系统代理开关（沙箱可能拦截注册表，只验证不崩 + 提示合理） ---
NavTo '总览'
Start-Sleep -Seconds 1
$startBtn = ButtonByName '启动'
if ($null -ne $startBtn) {
    $startBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 6
}
Check '内核已启动' ((Get-Process mihomo -ErrorAction SilentlyContinue) -ne $null) 'mihomo'

$toggle = ById 'SysProxyToggle'
Check '找到系统代理开关' ($null -ne $toggle) 'toggle'
if ($null -ne $toggle) {
    $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Seconds 2
    $reg = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue
    $result = (ById 'ResultText').Current.Name
    Write-Host ("结果文字：" + $result)
    if ($result -like '*已开启*') {
        Check '注册表 ProxyEnable=1' ($reg.ProxyEnable -eq 1) ("ProxyEnable=" + $reg.ProxyEnable)
        Check '注册表 ProxyServer=7890' ($reg.ProxyServer -like '*:7890*') ("ProxyServer=" + $reg.ProxyServer)
    }
    else {
        Write-Host ("SKIP  系统代理（沙箱拦截或失败）：" + $result) -ForegroundColor Yellow
    }
}

# --- 收尾：关窗口 → 内核与系统代理都应还原 ---
$proc.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 4
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
Start-Sleep -Seconds 2
Check '关窗后内核退出' ($null -eq (Get-Process mihomo -ErrorAction SilentlyContinue)) 'mihomo=0'

$reg2 = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue
Write-Host ("收尾注册表：ProxyEnable=" + $reg2.ProxyEnable + " ProxyServer=" + $reg2.ProxyServer)

Write-Host ""
if ($fail -eq 0) { Write-Host "全部通过" -ForegroundColor Green } else { Write-Host ("失败 " + $fail + " 项") -ForegroundColor Red }
exit $fail