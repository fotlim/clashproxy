# 下载 mihomo 内核与 GeoIP 数据到 Assets\bin（仓库里不存这两个大文件）
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\fetch-kernel.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$bin = Join-Path $repo 'src\ClashProxy\Assets\bin'
New-Item -ItemType Directory -Force -Path $bin | Out-Null

$kernelExe = Join-Path $bin 'mihomo.exe'
$geoDb = Join-Path $bin 'geoip.metadb'

if (-not (Test-Path $kernelExe)) {
    Write-Host '正在查询 mihomo 最新版本…'
    $rel = Invoke-RestMethod 'https://api.github.com/repos/MetaCubeX/mihomo/releases/latest' `
        -Headers @{ 'User-Agent' = 'clashproxy-build' }
    $asset = $rel.assets | Where-Object { $_.name -like 'mihomo-windows-amd64-compatible-*.zip' } | Select-Object -First 1
    if (-not $asset) { throw '没找到 windows-amd64-compatible 资产' }
    $zip = Join-Path $env:TEMP $asset.name
    Write-Host ("下载 " + $asset.name + " …")
    Invoke-WebRequest $asset.browser_download_url -OutFile $zip -UseBasicParsing
    $tmp = Join-Path $env:TEMP 'mihomo-extract'
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive $zip -DestinationPath $tmp -Force
    $exe = Get-ChildItem $tmp -Filter *.exe -Recurse | Select-Object -First 1
    Copy-Item $exe.FullName $kernelExe -Force
    Write-Host ("内核已就位：" + $kernelExe)
} else {
    Write-Host ("内核已存在：" + $kernelExe)
}

if (-not (Test-Path $geoDb)) {
    Write-Host '下载 geoip.metadb …'
    Invoke-WebRequest 'https://github.com/MetaCubeX/meta-rules-dat/releases/download/latest/geoip.metadb' `
        -OutFile $geoDb -UseBasicParsing
    Write-Host ("GeoIP 数据已就位：" + $geoDb)
} else {
    Write-Host ("GeoIP 数据已存在：" + $geoDb)
}

Write-Host '完成。'