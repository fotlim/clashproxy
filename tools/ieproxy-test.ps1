# 用 Windows PowerShell 5.1（.NET Framework，默认读取 IE/系统代理设置）验证系统代理路径
$ErrorActionPreference = 'Continue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$proxy = [System.Net.WebRequest]::GetSystemWebProxy()
Write-Host ("system web proxy: " + $proxy.GetProxy([Uri]'https://www.google.com'))

function Try-Url($url) {
    try {
        $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 25
        return "HTTP " + $r.StatusCode
    }
    catch {
        return "FAIL " + $_.Exception.Message
    }
}

Write-Host ("google  : " + (Try-Url 'https://www.google.com/generate_204'))
Write-Host ("baidu   : " + (Try-Url 'https://www.baidu.com'))