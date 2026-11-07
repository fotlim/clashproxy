# ClashProxy 一键构建发布包：publish → 便携版 zip → 安装包 → 校验和
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1 [-Version 1.0.0]
# 前置：.NET 8 SDK；Inno Setup 6（C:\Program Files (x86)\Inno Setup 6\ISCC.exe）

param(
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$src = Join-Path $repo 'src\ClashProxy'
$dist = Join-Path $repo 'dist\ClashProxy'
$out = Join-Path $repo 'release'
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

New-Item -ItemType Directory -Force -Path $out | Out-Null
Get-Process ClashProxy, mihomo -ErrorAction SilentlyContinue | Stop-Process -Force

# 1) 发布
Write-Host '== 1/4 发布 ==' -ForegroundColor Cyan
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
Push-Location $src
try {
    dotnet publish -c Release -p:Platform=x64 -r win-x64 --self-contained false -o $dist
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }
} finally { Pop-Location }

# 附带文档
Copy-Item (Join-Path $repo 'README.md') $dist -Force
Copy-Item (Join-Path $repo 'LICENSE') $dist -Force
if (Test-Path (Join-Path $repo 'screenshot.png')) { Copy-Item (Join-Path $repo 'screenshot.png') $dist -Force }

# 2) 便携版 zip
Write-Host '== 2/4 便携版 zip ==' -ForegroundColor Cyan
$zip = Join-Path $out ("ClashProxy-$Version-portable-win-x64.zip")
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("  -> " + $zip)

# 3) 安装包
Write-Host '== 3/4 安装包 ==' -ForegroundColor Cyan
if (-not (Test-Path $iscc)) { throw "找不到 Inno Setup：$iscc" }
& $iscc "/DAppVersion=$Version" (Join-Path $repo 'installer\ClashProxy.iss') | Out-String | Write-Host
$setup = Join-Path $out ("ClashProxy-$Version-setup-x64.exe")
if (-not (Test-Path $setup)) { throw '安装包没有生成' }
Write-Host ("  -> " + $setup)

# 4) 校验和
Write-Host '== 4/4 SHA256 ==' -ForegroundColor Cyan
$sums = Join-Path $out 'SHA256SUMS.txt'
$lines = @()
foreach ($f in @($zip, $setup)) {
    $hash = (Get-FileHash $f -Algorithm SHA256).Hash.ToLower()
    $lines += ($hash + '  ' + (Split-Path $f -Leaf))
}
$lines | Set-Content $sums -Encoding ASCII
Get-Content $sums | Write-Host

Write-Host ''
Write-Host '完成。产物：' -ForegroundColor Green
Get-ChildItem $out -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize | Out-String | Write-Host