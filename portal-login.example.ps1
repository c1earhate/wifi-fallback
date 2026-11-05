<#
.SYNOPSIS
    校园网 Portal 认证脚本模板（复制为 portal-login.ps1 后修改）。

.DESCRIPTION
    这个文件不会自动工作，需要先抓取你所在学校的登录请求。
    使用步骤：
      1. 复制本文件为 portal-login.ps1（该文件已被 .gitignore 忽略）；
      2. 手动连接校园网，打开认证页；
      3. 浏览器 F12 -> Network，提交登录，找到真正的 POST 请求；
      4. 把 URL、表单字段和必要的 header 填到下面；
      5. 在 config.json 的 behavior.onConnectCommand 里调用它。

    环境变量（由 WifiFallback.ps1 注入）：
      WIFI_SSID        刚连上的 SSID
      WIFI_PROFILE     配置文件名称
      WIFI_PORTAL_URL  探测到的认证页地址，可能为空
#>
[CmdletBinding()]
param(
    [string]$PortalUrl = $env:WIFI_PORTAL_URL,
    [string]$Username = $env:PORTAL_USERNAME,
    [string]$Password = $env:PORTAL_PASSWORD
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PortalUrl)) {
    $PortalUrl = 'http://10.0.0.1/'
}

Write-Output "Portal URL: $PortalUrl"

# 有些认证页需要先 GET 一次拿 Cookie/会话，再 POST 登录。
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$page = Invoke-WebRequest -Uri $PortalUrl -UseBasicParsing -WebSession $session -TimeoutSec 10
Write-Output "GET $PortalUrl -> HTTP $($page.StatusCode), length=$($page.Content.Length)"

# ------------------------------------------------------------------
# 下面只是示例，必须换成你用 F12 抓到的真实登录请求。
# 不要把真实密码提交到公开仓库；portal-login.ps1 已被 .gitignore 忽略。
# ------------------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($Username) -or [string]::IsNullOrWhiteSpace($Password)) {
    throw '请先设置 PORTAL_USERNAME / PORTAL_PASSWORD，或修改本脚本里的账号密码。'
}

# $loginUrl = 'http://10.0.0.1/login'
# $body = @{
#     username = $Username
#     password = $Password
#     service  = 'internet'
# }
# $login = Invoke-WebRequest -Uri $loginUrl -Method Post -Body $body -WebSession $session -UseBasicParsing -TimeoutSec 10
# if ($login.Content -notmatch '成功|success|登录成功') {
#     throw "Portal 登录失败：$($login.Content)"
# }

# 最终确认：只有能拿到期望内容才算成功。
$check = Invoke-WebRequest -Uri 'http://www.msftconnecttest.com/connecttest.txt' -UseBasicParsing -TimeoutSec 10
if ($check.Content -notmatch 'Microsoft Connect Test') {
    throw '认证后仍无法访问外网，请检查登录 URL/表单字段。'
}

Write-Output 'Portal 认证成功'
