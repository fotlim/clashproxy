# ClashProxy 端到端测试
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\e2e-test.ps1
# 前置：本机装有 Python 3（用于起本地订阅源），并已构建 ClashProxy.exe

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $repo 'src\ClashProxy\bin\x64\Debug\net8.0-windows10.0.19041.0\ClashProxy.exe'
$dataDir = Join-Path $repo 'testdata'
$subDir = Join-Path (Split-Path -Parent $repo) 'proxy-channel'
$python = 'C:\Users\LinOcean\AppData\Local\Programs\Python\Python311\python.exe'

$fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { Write-Host ("PASS  " + $name + "  " + $detail) -ForegroundColor Green }
    else { Write-Host ("FAIL  " + $name + "  " + $detail) -ForegroundColor Red; $script:fail++ }
}

if (-not (Test-Path $exe)) { throw "找不到 $exe，先执行 dotnet build" }
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue

Get-Process ClashProxy, mihomo -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
    Where-Object { $_.CommandLine -like '*8899*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 1

# 本地订阅源
$srv = Start-Process $python -ArgumentList '-m', 'http.server', '8899', '--bind', '127.0.0.1' `
    -WorkingDirectory $subDir -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 2

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
Check '标题正确' ($win.Current.Name -eq 'ClashProxy') $win.Current.Name

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
function TextById($id) { (ById $id).Current.Name }

# --- 首次运行落盘 settings.json ---
Check '首次运行生成 settings.json' (Test-Path (Join-Path $dataDir 'settings.json')) $dataDir

# --- 订阅导入 ---
NavTo '订阅与节点'
(ById 'UrlBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('http://127.0.0.1:8899/sub.yaml')
(ById 'FetchBtn').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 10
$sub = TextById 'SubStatus'
Check '订阅下载解析' ($sub -match '下载成功') $sub
Check '原始订阅已保存' (Test-Path (Join-Path $dataDir 'profiles\current.yaml')) ''

# --- 应用到内核 ---
(ById 'ApplyBtn').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 6
$cfgPath = Join-Path $dataDir 'config.yaml'
Check '生成 config.yaml' (Test-Path $cfgPath) (TextById 'SubStatus')
if (Test-Path $cfgPath) {
    $cfg = Get-Content $cfgPath -Raw
    Check 'mixed-port=7890' ($cfg -match 'mixed-port:\s*7890') ''
    Check 'app-channel 监听' ($cfg -match 'name:\s*app-channel' -and $cfg -match 'proxy:\s*PROXY') ''
    Check '规则兜底 MATCH,DIRECT' ($cfg -match 'MATCH,DIRECT') ''
}

# --- 启动内核 ---
NavTo '总览'
$startBtn = ById 'StartBtn'
if (-not $startBtn) { $startBtn = ButtonByName '启动' }
$startBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 12

$kernel = @(Get-Process mihomo -ErrorAction SilentlyContinue)
Check '内核进程已启动' ($kernel.Count -ge 1) ('pid=' + ($kernel.Id -join ','))
foreach ($port in 7890, 7891, 9091) {
    $open = Test-NetConnection 127.0.0.1 -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue
    Check ("端口 $port 监听") $open ''
}

# --- 应用通道：全量走代理 ---
$appIp = (curl.exe -s --max-time 25 --proxy http://127.0.0.1:7891 https://api.ipify.org)
Check '应用通道出口是代理 IP' ($appIp -match '^\d{1,3}(\.\d{1,3}){3}$') ("ip=" + $appIp)

# --- 规则通道：未配规则时 ipify 直连（应为空/失败）---
$ruleBefore = (curl.exe -s --max-time 12 --proxy http://127.0.0.1:7890 https://api.ipify.org)
Check '规则通道未命中原样直连' ([string]::IsNullOrWhiteSpace($ruleBefore)) ("[" + $ruleBefore + "]")
$baidu = (curl.exe -s -o NUL -w '%{http_code}' --max-time 25 --proxy http://127.0.0.1:7890 https://www.baidu.com)
Check '规则通道访问国内站直连 200' ($baidu -eq '200') $baidu

# --- 加分流规则并保存（应自动重启内核）---
NavTo '分流规则'
(ById 'ValueBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('ipify.org')
(ButtonByName '添加').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 800
$before = (@(Get-Process mihomo -ErrorAction SilentlyContinue).Id -join ',')
(ButtonByName '保存并应用').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 14
$after = (@(Get-Process mihomo -ErrorAction SilentlyContinue).Id -join ',')
Check '保存后自动重启内核' ($before -ne $after) ("$before -> $after")

$ruleAfter = (curl.exe -s --max-time 25 --proxy http://127.0.0.1:7890 https://api.ipify.org)
Check '规则命中后走代理' ($ruleAfter -match '^\d{1,3}(\.\d{1,3}){3}$') ("ip=" + $ruleAfter)
$baidu2 = (curl.exe -s -o NUL -w '%{http_code}' --max-time 25 --proxy http://127.0.0.1:7890 https://www.baidu.com)
Check '规则通道其它站点仍直连' ($baidu2 -eq '200') $baidu2

# --- 帮助页开发者信息 ---
NavTo '使用说明'
$about = $win.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
    ForEach-Object { $_.Current.Name } | Where-Object { $_ }
Check '使用说明含开发者信息' (($about -join ' ') -match 'fotlim') ''
Check '使用说明标注学习项目' (($about -join ' ') -match '学习项目') ''

# --- 强杀不留残留 ---
Stop-Process -Id $proc.Id -Force
Start-Sleep -Seconds 4
Check '强杀后内核随之退出' (@(Get-Process mihomo -ErrorAction SilentlyContinue).Count -eq 0) ''
Check '强杀后端口释放' (-not (Test-NetConnection 127.0.0.1 -Port 7891 -InformationLevel Quiet -WarningAction SilentlyContinue)) ''

# 收尾
Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue
Remove-Item Env:CLASHPROXY_DATA -ErrorAction SilentlyContinue

Write-Host ''
if ($fail -eq 0) { Write-Host '全部通过 ✔' -ForegroundColor Green; exit 0 }
else { Write-Host ("失败 " + $fail + " 项 ✘") -ForegroundColor Red; exit 1 }