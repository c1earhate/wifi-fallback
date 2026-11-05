# 编译 WifiFallbackUI.exe（.NET Framework 4.8，无需管理员权限）
# 用法：powershell -ExecutionPolicy Bypass -File .\build.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
$output = Join-Path $root 'WifiFallbackUI.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "找不到 csc.exe：$csc（需要 .NET Framework 4.x）"
}

$references = @(
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Drawing.dll'),
    (Join-Path $framework 'System.Windows.Forms.dll'),
    (Join-Path $framework 'System.Web.Extensions.dll')
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) {
        throw "缺少引用程序集：$reference"
    }
}

$sources = @(Get-ChildItem -LiteralPath $src -Filter '*.cs' -File | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) {
    throw "src 目录里没有 .cs 文件"
}

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/codepage:65001',
    '/optimize+',
    '/debug-',
    ('/out:' + $output),
    ('/win32icon:' + (Join-Path $src 'app.ico')),
    ('/win32manifest:' + (Join-Path $src 'app.manifest'))
)
foreach ($reference in $references) {
    $arguments += ('/r:' + $reference)
}
$arguments += $sources

& $csc $arguments
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码 $LASTEXITCODE"
}

$info = Get-Item -LiteralPath $output
Write-Host ('已生成：{0}（{1:N0} 字节）' -f $info.FullName, $info.Length) -ForegroundColor Green
