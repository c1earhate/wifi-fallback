#Requires -Version 5.1
# 简单自测：不连任何 WiFi，只验证决策逻辑和时间窗口。
# 运行：powershell -ExecutionPolicy Bypass -File .\tests\WifiFallback.Tests.ps1

$ErrorActionPreference = 'Stop'
$script:Pass = 0
$script:Fail = 0

$scriptPath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'WifiFallback.ps1'
. $scriptPath

function Assert-Equal {
    param($Expected, $Actual, [string]$Name)
    if ([string]$Expected -eq [string]$Actual) {
        $script:Pass++
        Write-Host ('  PASS  {0}' -f $Name) -ForegroundColor Green
    } else {
        $script:Fail++
        Write-Host ('  FAIL  {0}  (expected: {1}, actual: {2})' -f $Name, $Expected, $Actual) -ForegroundColor Red
    }
}

function New-TestNetwork {
    param(
        [string]$Ssid,
        [int]$Priority,
        [bool]$Enabled = $true,
        [bool]$AllowHidden = $false,
        [int]$MinSignal = 0
    )
    return [pscustomobject]@{
        Ssid        = $Ssid
        Profile     = $Ssid
        Priority    = $Priority
        Enabled     = $Enabled
        MinSignal   = $MinSignal
        AllowHidden = $AllowHidden
        Index       = $Priority
    }
}

function New-TestConfig {
    param($Networks, $Windows, [hashtable]$Overrides = @{})
    $behavior = [ordered]@{
        Preempt             = $true
        PreemptUnknown      = $true
        PreemptDelayChecks  = 2
        RespectManualMode   = $true
        ManualGraceMinutes  = 30
        FailedRetryMinutes  = 3
        MaxAttemptsPerRun   = 3
        ConnectTimeoutSec   = 15
    }
    foreach ($key in $Overrides.Keys) { $behavior[$key] = $Overrides[$key] }
    return [pscustomobject]@{
        Networks = @($Networks)
        Schedule = [pscustomobject]@{ Enabled = $true; Windows = @($Windows) }
        Behavior = [pscustomobject]$behavior
    }
}

function New-TestWindow {
    param([int[]]$Days = @(1, 2, 3, 4, 5, 6, 7), [string]$Start = '23:00', [string]$End = '07:00')
    return [pscustomobject]@{
        Days  = $Days
        Start = (ConvertTo-TimeOfDay $Start 'start')
        End   = (ConvertTo-TimeOfDay $End 'end')
    }
}

function New-TestCurrent {
    param([string]$Ssid = '', [int]$Signal = 80, [string]$Mode = 'Profile', $Connected = $null)
    if ($null -eq $Connected) { $Connected = -not [string]::IsNullOrWhiteSpace($Ssid) }
    return [pscustomobject]@{
        Found     = $true
        Name      = 'WLAN'
        Connected = [bool]$Connected
        Ssid      = $Ssid
        Profile   = $Ssid
        Signal    = $Signal
        Mode      = $Mode
        StateText = ''
        AllNames  = @('WLAN')
    }
}

function New-TestVisible {
    param([string]$Ssid, [int]$Signal = 80)
    return [pscustomobject]@{ Ssid = $Ssid; Signal = $Signal; Authentication = '' }
}

function Invoke-Plan {
    param($Config, $State, $Current, $Visible, [datetime]$Now, [switch]$IgnoreSchedule, $CurrentInternet = $null)
    return Get-NetworkPlan -Config $Config -State $State -Current $Current -VisibleNetworks $Visible -Now $Now -IgnoreSchedule:$IgnoreSchedule -CurrentInternet $CurrentInternet
}

# 2026-11-02 是周一
$mondayNoon = [datetime]'2026-11-02 12:00:00'
$tuesday1am = [datetime]'2026-11-03 01:00:00'
$sunday2330 = [datetime]'2026-11-01 23:30:00'

Write-Host ''
Write-Host '== 优先级兜底 ==' -ForegroundColor Cyan

$config = New-TestConfig -Networks @(
    (New-TestNetwork -Ssid 'A' -Priority 1),
    (New-TestNetwork -Ssid 'B' -Priority 2)
) -Windows @(New-TestWindow)

$state = New-DefaultState
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 90)) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'connect' $result.Action '断线时执行连接'
Assert-Equal 'fallback' $result.Reason '断线时使用 fallback 逻辑'
Assert-Equal 'A' $result.Target.Ssid '即使 B 信号更强也优先 A'
Assert-Equal 2 $result.Candidates.Count '候选包含 A/B 两个'

$config = New-TestConfig -Networks @(
    (New-TestNetwork -Ssid 'A' -Priority 1),
    (New-TestNetwork -Ssid 'B' -Priority 2)
) -Windows @(New-TestWindow)

$state = New-DefaultState
$result1 = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Ssid 'B') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 90)) -Now $mondayNoon -IgnoreSchedule
$result2 = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Ssid 'B') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 90)) -Now $mondayNoon.AddMinutes(1) -IgnoreSchedule
Assert-Equal 'none' $result1.Action '防抖：第一次只确认不切换'
Assert-Equal 'waiting' $result1.Reason '防抖原因 waiting'
Assert-Equal 'connect' $result2.Action '第二次确认后切换'
Assert-Equal 'preempt' $result2.Reason '抢占原因 preempt'
Assert-Equal 'A' $result2.Target.Ssid '切换到高优先级 A'

$state = New-DefaultState
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Ssid 'A') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 90)) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'already-best' $result.Reason '已连接最高优先级时不动作'

Write-Host ''
Write-Host '== 时间窗口 ==' -ForegroundColor Cyan

$state = New-DefaultState
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @(New-TestVisible 'A' 70) -Now $mondayNoon
Assert-Equal 'outside-window' $result.Reason '白天不在窗口内'

$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @(New-TestVisible 'A' 70) -Now $tuesday1am
Assert-Equal 'connect' $result.Action '周二凌晨 1 点处于周一 23 点开始的窗口'

$sundayOnly = New-TestConfig -Networks @((New-TestNetwork -Ssid 'A' -Priority 1)) `
    -Windows @(New-TestWindow -Days @(7) -Start '23:00' -End '07:00')
$state = New-DefaultState
$result = Invoke-Plan -Config $sundayOnly -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @(New-TestVisible 'A' 70) -Now $mondayNoon
Assert-Equal 'outside-window' $result.Reason '只配置周日时周一不生效'
$result = Invoke-Plan -Config $sundayOnly -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @(New-TestVisible 'A' 70) -Now $sunday2330
Assert-Equal 'connect' $result.Action '周日 23:30 在窗口内'

Write-Host ''
Write-Host ''
Write-Host '== Portal 认证导致的降级 ==' -ForegroundColor Cyan

$portalConfig = New-TestConfig -Networks @(
    (New-TestNetwork -Ssid 'A' -Priority 1),
    (New-TestNetwork -Ssid 'B' -Priority 2)
) -Windows @(New-TestWindow) -Overrides @{ VerifyInternet = $true }

$state = New-DefaultState
$result = Invoke-Plan -Config $portalConfig -State $state -Current (New-TestCurrent -Ssid 'B') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 90)) -Now $mondayNoon -CurrentInternet $false -IgnoreSchedule
Assert-Equal 'connect' $result.Action 'Portal 认证失败时执行切换'
Assert-Equal 'portal-fallback' $result.Reason 'Portal 失败使用 portal-fallback'
Assert-Equal 'A' $result.Target.Ssid 'Portal 失败时切到另一优先级'

$state = New-DefaultState
$result = Invoke-Plan -Config $portalConfig -State $state -Current (New-TestCurrent -Ssid 'A') `
    -Visible @((New-TestVisible 'A' 90)) -Now $mondayNoon -CurrentInternet $false -IgnoreSchedule
Assert-Equal 'portal-only' $result.Reason '只有当前网络需要认证时不反复切换'
Write-Host '== 暂停 / 手动保护 ==' -ForegroundColor Cyan

$state = New-DefaultState
$state.pausedUntil = $mondayNoon.AddMinutes(30)
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @(New-TestVisible 'A' 70) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'paused' $result.Reason '暂停期间不动作'

$state = New-DefaultState
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Ssid 'B' -Mode 'Manual') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 90)) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'manual' $result.Reason '检测到手动连接时尊重手动'
Assert-Equal $true ($null -ne $state.manualUntil) '设置了手动保护截止时间'

$state = New-DefaultState
$state.lastApplied = [ordered]@{ ssid = 'B'; profile = 'B'; time = $mondayNoon.AddMinutes(-1) }
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Ssid 'B' -Mode 'Manual') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 90)) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'waiting' $result.Reason '本工具自己连上的 Manual 连接不触发手动保护（进入防抖）'
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Ssid 'B' -Mode 'Manual') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 90)) -Now $mondayNoon.AddMinutes(1) -IgnoreSchedule
Assert-Equal 'connect' $result.Action '防抖确认后从 Manual 的 B 切到 A'

$state = New-DefaultState
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Ssid 'X') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'X' 80)) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'waiting' $result.Reason '从非列表网络接管也要先防抖确认'
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Ssid 'X') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'X' 80)) -Now $mondayNoon.AddMinutes(1) -IgnoreSchedule
Assert-Equal 'connect' $result.Action '默认会从非列表网络接管到列表网络'

$noUnknown = New-TestConfig -Networks @(
    (New-TestNetwork -Ssid 'A' -Priority 1),
    (New-TestNetwork -Ssid 'B' -Priority 2)
) -Windows @(New-TestWindow) -Overrides @{ PreemptUnknown = $false }
$state = New-DefaultState
$result = Invoke-Plan -Config $noUnknown -State $state -Current (New-TestCurrent -Ssid 'X') `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'X' 80)) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'unknown-network' $result.Reason '关闭 preemptUnknown 后不接管未知网络'

Write-Host ''
Write-Host '== 失败冷却 / 隐藏网络 ==' -ForegroundColor Cyan

$state = New-DefaultState
$state.failures['A'] = $mondayNoon.AddSeconds(-30)
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @((New-TestVisible 'A' 70), (New-TestVisible 'B' 60)) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'B' $result.Target.Ssid 'A 失败冷却时回退到 B'
Assert-Equal 1 $result.Candidates.Count '冷却中的 A 不在候选里'

$hiddenConfig = New-TestConfig -Networks @(
    (New-TestNetwork -Ssid 'A' -Priority 1),
    (New-TestNetwork -Ssid 'H' -Priority 2 -AllowHidden $true)
) -Windows @(New-TestWindow)
$state = New-DefaultState
$result = Invoke-Plan -Config $hiddenConfig -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @(New-TestVisible 'A' 70) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 2 $result.Candidates.Count '可见 A 在前，隐藏 H 作为后备'
Assert-Equal 'H' $result.Candidates[1].Net.Ssid '隐藏网络排在可见网络之后'

$state = New-DefaultState
$result = Invoke-Plan -Config $config -State $state -Current (New-TestCurrent -Connected $false) `
    -Visible @(New-TestVisible 'Z' 90) -Now $mondayNoon -IgnoreSchedule
Assert-Equal 'no-candidate' $result.Reason '没有列表内网络可选时保持不动'

Write-Host ''
Write-Host '== 配置解析 ==' -ForegroundColor Cyan
$liveConfigPath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'config.example.json'
$liveConfig = Read-Config -Path $liveConfigPath
Assert-Equal 4 $liveConfig.Networks.Count '示例配置解析出 4 个网络'
Assert-Equal 'CampusWiFi' $liveConfig.Networks[0].Ssid '优先级 1 是 CampusWiFi'
Assert-Equal 1 $liveConfig.Behavior.TaskIntervalMinutes '任务间隔默认 1 分钟'

Write-Host ''
if ($script:Fail -eq 0) {
    Write-Host ('全部通过：{0} 项' -f $script:Pass) -ForegroundColor Green
    exit 0
} else {
    Write-Host ('失败 {0} 项，通过 {1} 项' -f $script:Fail, $script:Pass) -ForegroundColor Red
    exit 1
}
