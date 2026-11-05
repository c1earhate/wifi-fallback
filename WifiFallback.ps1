#Requires -Version 5.1
<#
.SYNOPSIS
    Windows 多 WiFi 优先级兜底 + 定时窗口工具。

.DESCRIPTION
    在 config.json 设定的时间窗口内，按优先级自动连接可用的最高优先级 WiFi；
    高优先级网络断开或不可用时，依次尝试较低优先级网络。
    所有 WiFi 密码都使用系统里已经保存的配置文件，本工具不保存任何密码。

.PARAMETER Run
    执行一次检查（计划任务每分钟调用一次）。默认受时间窗口限制。

.PARAMETER Force
    配合 -Run 使用：忽略时间窗口，强制执行一次。

.PARAMETER Status
    显示当前连接、优先级列表、时间窗口和本次决策，不做任何修改。

.PARAMETER Plan
    模拟运行：忽略时间窗口，显示“如果现在触发会怎么做”，不会真的连接。

.PARAMETER InstallTask
    注册 Windows 计划任务（当前用户，无需管理员权限）。

.PARAMETER UninstallTask
    删除 Windows 计划任务。

.PARAMETER Pause
    暂停自动切换，配合 -Minutes 指定分钟数（默认 60）。

.PARAMETER Resume
    取消暂停和手动连接保护。

.PARAMETER ConnectTo
    立即连接 config.json 中的某个 SSID（忽略时间窗口），用于测试。

.PARAMETER ConfigPath
    指定配置文件路径，默认是本脚本同目录的 config.json。

.PARAMETER TaskName
    计划任务名称，默认 WifiFallback。

.EXAMPLE
    .\WifiFallback.ps1 -Status

.EXAMPLE
    .\WifiFallback.ps1 -Plan

.EXAMPLE
    .\WifiFallback.ps1 -ConnectTo "ZstuWlan"

.EXAMPLE
    .\WifiFallback.ps1 -InstallTask
#>
[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Status,
    [switch]$Plan,
    [switch]$Force,
    [switch]$InstallTask,
    [switch]$UninstallTask,
    [switch]$Pause,
    [int]$Minutes = 60,
    [switch]$Resume,
    [string]$ConnectTo = '',
    [string]$ConfigPath = '',
    [string]$TaskName = 'WifiFallback',
    [string]$SnapshotPath = ''
)

$ErrorActionPreference = 'Stop'

$script:Root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $script:Root 'config.json'
} elseif (-not [IO.Path]::IsPathRooted($ConfigPath)) {
    $ConfigPath = Join-Path (Get-Location).Path $ConfigPath
}
$script:ConfigPath = [IO.Path]::GetFullPath($ConfigPath)
$script:StatePath = Join-Path $script:Root 'state.json'
$script:LogDir = Join-Path $script:Root 'logs'
$script:LogFile = Join-Path $script:LogDir ('wifi-fallback-{0}.log' -f (Get-Date -Format 'yyyy-MM'))
$script:ShowConsole = $true
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$script:Invariant = [System.Globalization.CultureInfo]::InvariantCulture

# ---------------------------------------------------------------------------
# 基础工具
# ---------------------------------------------------------------------------

function Get-JsonValue {
    param($Object, [string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $Default }
    if ($null -eq $prop.Value) { return $Default }
    return $prop.Value
}

function ConvertTo-BoolValue {
    param($Value, [bool]$Default = $false)
    if ($null -eq $Value) { return $Default }
    if ($Value -is [bool]) { return $Value }
    $text = [string]$Value
    return ($text -match '^(?i:true|yes|on|1|启用|是|开)$')
}

function ConvertTo-TimeOfDay {
    param($Value, [string]$FieldName)
    $text = [string]$Value
    if ($text -notmatch '^\s*(\d{1,2}):(\d{2})(?::(\d{2}))?\s*$') {
        throw "$FieldName 的时间格式不对：'$text'，应为 HH:mm，例如 23:00"
    }
    $hour = [int]$Matches[1]
    $minute = [int]$Matches[2]
    $second = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
    if ($hour -gt 23 -or $minute -gt 59 -or $second -gt 59) {
        throw "$FieldName 的时间超出范围：'$text'"
    }
    return New-TimeSpan -Hours $hour -Minutes $minute -Seconds $second
}

function ConvertTo-Days {
    param($Value, [string]$FieldName)
    $allDays = @(1, 2, 3, 4, 5, 6, 7)
    $map = @{
        'mon' = 1; 'monday' = 1; '周一' = 1; '星期一' = 1; '一' = 1
        'tue' = 2; 'tuesday' = 2; '周二' = 2; '星期二' = 2; '二' = 2
        'wed' = 3; 'wednesday' = 3; '周三' = 3; '星期三' = 3; '三' = 3
        'thu' = 4; 'thursday' = 4; '周四' = 4; '星期四' = 4; '四' = 4
        'fri' = 5; 'friday' = 5; '周五' = 5; '星期五' = 5; '五' = 5
        'sat' = 6; 'saturday' = 6; '周六' = 6; '星期六' = 6; '六' = 6
        'sun' = 7; 'sunday' = 7; '周日' = 7; '周天' = 7; '星期日' = 7; '星期天' = 7; '日' = 7; '七' = 7
    }

    if ($null -eq $Value) { return $allDays }

    if ($Value -is [string]) {
        $text = $Value.Trim()
        if ($text -match '^(?i:\*|all|daily|everyday|每天|每日|全天)$') { return $allDays }
        $tokens = @($text -split '[,，;；\s]+' | Where-Object { $_ -ne '' })
    } else {
        $tokens = @($Value)
    }

    $days = @()
    foreach ($token in $tokens) {
        $key = ([string]$token).Trim().ToLowerInvariant().TrimEnd('.')
        if ($key -match '^\d+$') {
            $number = [int]$key
            if ($number -ge 1 -and $number -le 7) {
                $days += $number
                continue
            }
        }
        if ($map.ContainsKey($key)) {
            $days += $map[$key]
            continue
        }
        throw "$FieldName 里有无法识别的星期：'$token'（可用 Mon..Sun 或 周一..周日 或 *）"
    }

    if ($days.Count -eq 0) { throw "$FieldName 不能为空" }
    return @($days | Sort-Object -Unique)
}

function ConvertTo-IsoDay {
    param([System.DayOfWeek]$Day)
    if ($Day -eq [System.DayOfWeek]::Sunday) { return 7 }
    return [int]$Day
}

function Format-Days {
    param([int[]]$Days)
    if ($null -eq $Days -or $Days.Count -eq 0) { return '' }
    if ($Days.Count -eq 7) { return '每天' }
    $names = @('', '周一', '周二', '周三', '周四', '周五', '周六', '周日')
    $picked = @()
    foreach ($day in @($Days | Sort-Object)) {
        if ($day -ge 1 -and $day -le 7) { $picked += $names[$day] }
    }
    return ($picked -join '、')
}

function Format-ScheduleWindow {
    param($Window)
    $start = $Window.Start.ToString('hh\:mm')
    $end = $Window.End.ToString('hh\:mm')
    $cross = if ($Window.End -le $Window.Start) { '（次日）' } else { '' }
    return ('{0} {1}-{2}{3}' -f (Format-Days -Days $Window.Days), $start, $end, $cross)
}

function ConvertFrom-IsoDate {
    param($Value)
    if ($null -eq $Value) { return $null }
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    try {
        return [datetime]::Parse($text, $script:Invariant, [System.Globalization.DateTimeStyles]::RoundtripKind)
    } catch {
        return $null
    }
}

function Write-Log {
    param([string]$Message, [ValidateSet('INFO', 'WARN', 'ERROR', 'DEBUG')][string]$Level = 'INFO')
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    try {
        if (-not (Test-Path -LiteralPath $script:LogDir)) {
            New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null
        }
        Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 -ErrorAction Stop
    } catch {
        # 日志写不进去不能影响主流程
    }
    if ($script:ShowConsole) { Write-Host $line }
}

# ---------------------------------------------------------------------------
# 配置 / 状态
# ---------------------------------------------------------------------------

function Read-Config {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "配置文件不存在：$Path"
    }

    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    try {
        $json = $raw | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "配置文件不是合法 JSON：$($_.Exception.Message)"
    }

    $interfaceName = [string](Get-JsonValue $json 'interface' '')
    $networks = @()
    $index = 0
    $seen = @{}

    foreach ($item in @(Get-JsonValue $json 'networks' @())) {
        $index++
        $ssid = [string](Get-JsonValue $item 'ssid' '')
        if ([string]::IsNullOrWhiteSpace($ssid)) {
            throw "networks 第 $index 项缺少 ssid"
        }
        if ($seen.ContainsKey($ssid)) {
            throw "networks 中 SSID 重复：$ssid"
        }
        $seen[$ssid] = $true

        $profileName = [string](Get-JsonValue $item 'profile' $ssid)
        if ([string]::IsNullOrWhiteSpace($profileName)) { $profileName = $ssid }

        $priority = $index
        $priorityRaw = Get-JsonValue $item 'priority' $null
        if ($null -ne $priorityRaw) {
            $parsedPriority = 0
            if (-not [int]::TryParse([string]$priorityRaw, [ref]$parsedPriority)) {
                throw "networks 第 $index 项 priority 不是整数：$priorityRaw"
            }
            $priority = $parsedPriority
        }

        $minSignal = 0
        $minSignalRaw = Get-JsonValue $item 'minSignal' $null
        if ($null -ne $minSignalRaw) {
            $parsedSignal = 0
            if (-not [int]::TryParse([string]$minSignalRaw, [ref]$parsedSignal)) {
                throw "networks 第 $index 项 minSignal 不是整数：$minSignalRaw"
            }
            $minSignal = [Math]::Min(100, [Math]::Max(0, $parsedSignal))
        }

        $networks += [pscustomobject]@{
            Ssid        = $ssid
            Profile     = $profileName
            Priority    = $priority
            Enabled     = (ConvertTo-BoolValue (Get-JsonValue $item 'enabled' $true) $true)
            MinSignal   = $minSignal
            AllowHidden = (ConvertTo-BoolValue (Get-JsonValue $item 'allowHidden' $false) $false)
            Index       = $index
        }
    }

    if ($networks.Count -eq 0) {
        throw 'networks 至少要配置一个 WiFi'
    }
    $networks = @($networks | Sort-Object -Property Priority, Index)

    $scheduleRaw = Get-JsonValue $json 'schedule' $null
    $scheduleEnabled = ConvertTo-BoolValue (Get-JsonValue $scheduleRaw 'enabled' $true) $true
    $windows = @()
    $windowIndex = 0
    foreach ($window in @(Get-JsonValue $scheduleRaw 'windows' @())) {
        $windowIndex++
        $windows += [pscustomobject]@{
            Days  = (ConvertTo-Days (Get-JsonValue $window 'days' '*') "schedule.windows[$windowIndex].days")
            Start = (ConvertTo-TimeOfDay (Get-JsonValue $window 'start' '23:00') "schedule.windows[$windowIndex].start")
            End   = (ConvertTo-TimeOfDay (Get-JsonValue $window 'end' '07:00') "schedule.windows[$windowIndex].end")
        }
    }
    if ($scheduleEnabled -and $windows.Count -eq 0) {
        throw 'schedule.enabled 为 true 时，windows 不能为空。若想全天生效，请把 enabled 设为 false。'
    }

    $behaviorRaw = Get-JsonValue $json 'behavior' $null
    $taskInterval = 1
    $taskIntervalRaw = Get-JsonValue $behaviorRaw 'taskIntervalMinutes' 1
    $parsedInterval = 0
    if ([int]::TryParse([string]$taskIntervalRaw, [ref]$parsedInterval)) {
        $taskInterval = [Math]::Min(15, [Math]::Max(1, $parsedInterval))
    }

    $behavior = [pscustomobject]@{
        Preempt              = (ConvertTo-BoolValue (Get-JsonValue $behaviorRaw 'preempt' $true) $true)
        PreemptUnknown       = (ConvertTo-BoolValue (Get-JsonValue $behaviorRaw 'preemptUnknown' $true) $true)
        PreemptDelayChecks   = [Math]::Max(1, [int](Get-JsonValue $behaviorRaw 'preemptDelayChecks' 2))
        RespectManualMode    = (ConvertTo-BoolValue (Get-JsonValue $behaviorRaw 'respectManualMode' $true) $true)
        ManualGraceMinutes   = [Math]::Max(0, [int](Get-JsonValue $behaviorRaw 'manualGraceMinutes' 30))
        ConnectTimeoutSec    = [Math]::Max(5, [int](Get-JsonValue $behaviorRaw 'connectTimeoutSec' 15))
        PostConnectDelaySec  = [Math]::Max(0, [int](Get-JsonValue $behaviorRaw 'postConnectDelaySec' 4))
        FailedRetryMinutes   = [Math]::Max(1, [int](Get-JsonValue $behaviorRaw 'failedRetryMinutes' 3))
        MaxAttemptsPerRun    = [Math]::Max(1, [int](Get-JsonValue $behaviorRaw 'maxAttemptsPerRun' 3))
        VerifyInternet       = (ConvertTo-BoolValue (Get-JsonValue $behaviorRaw 'verifyInternet' $false) $false)
        VerifyHost           = [string](Get-JsonValue $behaviorRaw 'verifyHost' '223.5.5.5')
        VerifyPort           = [int](Get-JsonValue $behaviorRaw 'verifyPort' 443)
        OnConnectCommand     = [string](Get-JsonValue $behaviorRaw 'onConnectCommand' '')
        LogKeepDays          = [Math]::Max(1, [int](Get-JsonValue $behaviorRaw 'logKeepDays' 30))
        TaskIntervalMinutes  = $taskInterval
    }

    $config = [pscustomobject]@{
        Path      = $Path
        Interface = $interfaceName
        Networks  = @($networks)
        Schedule  = [pscustomobject]@{ Enabled = $scheduleEnabled; Windows = @($windows) }
        Behavior  = $behavior
    }
    return $config
}

function New-DefaultState {
    return [ordered]@{
        version     = 1
        lastApplied = $null
        bestStreak  = $null
        failures    = @{}
        pausedUntil = $null
        manualUntil = $null
        lastRun     = $null
        lastResult  = $null
        lastCleanup = $null
    }
}

function Read-StateFile {
    param([string]$Path)
    $state = New-DefaultState
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $state }

    try {
        $json = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    } catch {
        return $state
    }

    if ($json.lastApplied) {
        $appliedTime = ConvertFrom-IsoDate $json.lastApplied.time
        if ($appliedTime) {
            $state.lastApplied = [ordered]@{
                ssid    = [string]$json.lastApplied.ssid
                profile = [string]$json.lastApplied.profile
                time    = $appliedTime
            }
        }
    }
    if ($json.bestStreak) {
        $state.bestStreak = [ordered]@{
            ssid  = [string]$json.bestStreak.ssid
            count = [int]$json.bestStreak.count
        }
    }
    if ($json.failures) {
        foreach ($prop in $json.failures.PSObject.Properties) {
            $parsed = ConvertFrom-IsoDate $prop.Value
            if ($parsed) { $state.failures[$prop.Name] = $parsed }
        }
    }
    $state.pausedUntil = ConvertFrom-IsoDate $json.pausedUntil
    $state.manualUntil = ConvertFrom-IsoDate $json.manualUntil
    $state.lastRun = ConvertFrom-IsoDate $json.lastRun
    $state.lastCleanup = ConvertFrom-IsoDate $json.lastCleanup
    if ($null -ne $json.lastResult) { $state.lastResult = [string]$json.lastResult }
    return $state
}

function Save-StateFile {
    param($State, [string]$Path)
    $failures = [ordered]@{}
    foreach ($key in @($State.failures.Keys)) {
        $value = $State.failures[$key]
        if ($value) { $failures[$key] = $value.ToString('o') }
    }
    $lastApplied = $null
    if ($State.lastApplied -and $State.lastApplied.time) {
        $lastApplied = [ordered]@{
            ssid    = $State.lastApplied.ssid
            profile = $State.lastApplied.profile
            time    = $State.lastApplied.time.ToString('o')
        }
    }
    $bestStreak = $null
    if ($State.bestStreak -and $State.bestStreak.ssid) {
        $bestStreak = [ordered]@{ ssid = $State.bestStreak.ssid; count = $State.bestStreak.count }
    }
    $dto = [ordered]@{
        version     = 1
        lastApplied = $lastApplied
        bestStreak  = $bestStreak
        failures    = $failures
        pausedUntil = if ($State.pausedUntil) { $State.pausedUntil.ToString('o') } else { $null }
        manualUntil = if ($State.manualUntil) { $State.manualUntil.ToString('o') } else { $null }
        lastRun     = if ($State.lastRun) { $State.lastRun.ToString('o') } else { $null }
        lastResult  = $State.lastResult
        lastCleanup = if ($State.lastCleanup) { $State.lastCleanup.ToString('o') } else { $null }
    }
    $json = $dto | ConvertTo-Json -Depth 6
    $tempPath = "$Path.tmp"
    [IO.File]::WriteAllText($tempPath, $json, $script:Utf8NoBom)
    Move-Item -LiteralPath $tempPath -Destination $Path -Force
}

# ---------------------------------------------------------------------------
# netsh 读取
# ---------------------------------------------------------------------------

# 按 Windows 命令行规则给单个参数加引号。
function ConvertTo-NetshArgument {
    param([string]$Value)
    if ($null -eq $Value) { $Value = '' }
    if ($Value -notmatch '[\s"]') { return $Value }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $backslashes++
            continue
        }
        if ($character -eq '"') {
            [void]$builder.Append(('\' * (2 * $backslashes + 1)))
            [void]$builder.Append('"')
            $backslashes = 0
        } else {
            if ($backslashes -gt 0) {
                [void]$builder.Append(('\' * $backslashes))
                $backslashes = 0
            }
            [void]$builder.Append($character)
        }
    }
    if ($backslashes -gt 0) {
        [void]$builder.Append(('\' * (2 * $backslashes)))
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

# 直接读取 netsh 的原始字节再按 UTF-8/ANSI 解码，避免无控制台时
# PowerShell 按错误的代码页解码导致中文乱码（进而解析不到网卡）。
function Invoke-Netsh {
    param([string[]]$Arguments)

    $process = $null
    $memory = $null
    try {
        $startInfo = New-Object System.Diagnostics.ProcessStartInfo
        $startInfo.FileName = 'netsh.exe'
        $startInfo.UseShellExecute = $false
        $startInfo.CreateNoWindow = $true
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NetshArgument $_ }) -join ' ')

        $process = [System.Diagnostics.Process]::Start($startInfo)
        $errorTask = $process.StandardError.ReadToEndAsync()

        $memory = New-Object System.IO.MemoryStream
        $process.StandardOutput.BaseStream.CopyTo($memory)
        $bytes = $memory.ToArray()
        $process.WaitForExit()

        $text = ''
        if ($bytes.Length -gt 0) {
            $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
            try {
                $text = $strictUtf8.GetString($bytes)
            } catch {
                $text = [System.Text.Encoding]::Default.GetString($bytes)
            }
        }

        $errorText = ''
        try { $errorText = $errorTask.Result } catch { }
        if (-not [string]::IsNullOrWhiteSpace($errorText)) {
            $text = ($text + "`n" + $errorText).Trim()
        }

        $text = $text.TrimStart([char]0xFEFF)
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output   = (($text -replace "`r`n", "`n").Trim())
        }
    } catch {
        return [pscustomobject]@{
            ExitCode = -1
            Output   = $_.Exception.Message
        }
    } finally {
        if ($null -ne $memory) { $memory.Dispose() }
        if ($null -ne $process) { $process.Dispose() }
    }
}

function Get-Field {
    param($Block, [string[]]$Keys)
    if ($null -eq $Block) { return '' }
    foreach ($key in $Keys) {
        if ($Block.Contains($key)) { return [string]$Block[$key] }
    }
    return ''
}

function Get-WifiState {
    param([string]$InterfaceName = '')

    $result = Invoke-Netsh @('wlan', 'show', 'interfaces')
    $blocks = @()
    $currentBlock = $null

    foreach ($line in ($result.Output -split "`n")) {
        if ($line -match '^\s{2,}(?:Name|名称|名稱)\s*:\s*(?<value>.+?)\s*$') {
            if ($null -ne $currentBlock) { $blocks += $currentBlock }
            $currentBlock = [ordered]@{}
            $currentBlock['Name'] = $Matches['value']
            continue
        }
        if ($null -ne $currentBlock -and $line -match '^\s*(?<key>[^:]+?)\s*:\s*(?<value>.*?)\s*$') {
            $key = $Matches['key']
            $value = $Matches['value']
            if (-not $currentBlock.Contains($key)) { $currentBlock[$key] = $value }
        }
    }
    if ($null -ne $currentBlock) { $blocks += $currentBlock }

    $selected = $null
    if ($InterfaceName) {
        $selected = @($blocks | Where-Object { $_['Name'] -eq $InterfaceName }) | Select-Object -First 1
    }
    if (-not $selected) {
        $selected = @($blocks | Where-Object { (Get-Field $_ @('SSID')) -ne '' }) | Select-Object -First 1
    }
    if (-not $selected -and $blocks.Count -gt 0) { $selected = $blocks[0] }

    if (-not $selected) {
        return [pscustomobject]@{
            Found = $false; Name = ''; Connected = $false; Ssid = ''; Profile = ''
            Signal = 0; Mode = ''; StateText = ''; AllNames = @()
        }
    }

    $ssid = Get-Field $selected @('SSID')
    $stateText = Get-Field $selected @('State', '状态', '狀態')
    $signalText = Get-Field $selected @('Signal', '信号', '訊號')
    $signal = 0
    if ($signalText -match '(\d+)') { $signal = [int]$Matches[1] }

    $connected = -not [string]::IsNullOrWhiteSpace($ssid)
    if ($connected -and $stateText -match '(?i)disconnect|断开|中斷|未连接|not connected') {
        $connected = $false
    }

    return [pscustomobject]@{
        Found     = $true
        Name      = (Get-Field $selected @('Name', '名称', '名稱'))
        Connected = $connected
        Ssid      = $ssid
        Profile   = (Get-Field $selected @('Profile', '配置文件', '設定檔'))
        Signal    = $signal
        Mode      = (Get-Field $selected @('Connection mode', '连接模式', '連線模式'))
        StateText = $stateText
        AllNames  = @($blocks | ForEach-Object { $_['Name'] })
    }
}

function Get-VisibleNetworks {
    $result = Invoke-Netsh @('wlan', 'show', 'networks', 'mode=bssid')
    $networks = @()
    $pending = $null

    foreach ($line in ($result.Output -split "`n")) {
        if ($line -match '^\s*SSID\s+\d+\s*:\s*(?<value>.*)$') {
            if ($null -ne $pending) { $networks += $pending }
            $pending = [pscustomobject]@{
                Ssid           = $Matches['value'].Trim()
                Signal         = 0
                Authentication = ''
            }
            continue
        }
        if ($null -eq $pending) { continue }

        if ($line -match '^\s*(?:Signal|信号|訊號)\s*:\s*(?<value>\d+)\s*%') {
            $percent = [int]$Matches['value']
            if ($percent -gt $pending.Signal) { $pending.Signal = $percent }
        } elseif ($line -match '^\s*(?:Authentication|身份验证|驗證)\s*:\s*(?<value>.+?)\s*$') {
            if (-not $pending.Authentication) { $pending.Authentication = $Matches['value'] }
        }
    }
    if ($null -ne $pending) { $networks += $pending }

    $map = @{}
    foreach ($network in $networks) {
        if ([string]::IsNullOrWhiteSpace($network.Ssid)) { continue }
        if (-not $map.ContainsKey($network.Ssid)) {
            $map[$network.Ssid] = $network
        } elseif ($network.Signal -gt $map[$network.Ssid].Signal) {
            $map[$network.Ssid].Signal = $network.Signal
        }
    }
    return @($map.Values | Sort-Object -Property Signal -Descending)
}

function Get-SavedProfiles {
    $result = Invoke-Netsh @('wlan', 'show', 'profiles')
    $profiles = @()
    foreach ($line in ($result.Output -split "`n")) {
        if ($line -match '^\s{4,}[^:]+:\s+(?<name>\S.*?)\s*$') {
            $profiles += $Matches['name']
        }
    }
    return @($profiles)
}

# ---------------------------------------------------------------------------
# 时间窗口
# ---------------------------------------------------------------------------

function Get-ScheduleInfo {
    param($Schedule, [datetime]$Now)

    if (-not $Schedule.Enabled) {
        return [pscustomobject]@{
            Active     = $true
            CurrentEnd = $null
            NextStart  = $null
            Text       = '未启用时间限制（全天生效）'
        }
    }

    $active = $false
    $currentEnd = $null
    $nextStart = $null

    for ($offset = -1; $offset -le 8; $offset++) {
        $date = $Now.Date.AddDays($offset)
        $isoDay = ConvertTo-IsoDay $date.DayOfWeek
        foreach ($window in $Schedule.Windows) {
            if ($window.Days -notcontains $isoDay) { continue }
            $start = $date.Add($window.Start)
            if ($window.End -gt $window.Start) {
                $end = $date.Add($window.End)
            } else {
                $end = $date.AddDays(1).Add($window.End)
            }

            if ($Now -ge $start -and $Now -lt $end) {
                $active = $true
                if ($null -eq $currentEnd -or $end -lt $currentEnd) { $currentEnd = $end }
            } elseif ($start -gt $Now) {
                if ($null -eq $nextStart -or $start -lt $nextStart) { $nextStart = $start }
            }
        }
    }

    $parts = @()
    foreach ($window in $Schedule.Windows) { $parts += (Format-ScheduleWindow -Window $window) }

    return [pscustomobject]@{
        Active     = $active
        CurrentEnd = $currentEnd
        NextStart  = $nextStart
        Text       = ($parts -join '；')
    }
}

# ---------------------------------------------------------------------------
# 核心决策（纯逻辑，方便测试）
# ---------------------------------------------------------------------------

function New-PlanResult {
    param(
        [string]$Action = 'none',
        [string]$Reason = '',
        [string]$Message = '',
        $Target = $null,
        $Candidates = @(),
        $Schedule = $null
    )
    return [pscustomobject]@{
        Action     = $Action
        Reason     = $Reason
        Message    = $Message
        Target     = $Target
        Candidates = @($Candidates)
        Schedule   = $Schedule
    }
}

function Get-NetworkPlan {
    param(
        $Config,
        $State,
        $Current,
        $VisibleNetworks,
        [datetime]$Now,
        [switch]$IgnoreSchedule
    )

    $scheduleInfo = Get-ScheduleInfo -Schedule $Config.Schedule -Now $Now

    if ($State.pausedUntil -and $State.pausedUntil -gt $Now) {
        return New-PlanResult -Reason 'paused' `
            -Message ('已暂停到 ' + $State.pausedUntil.ToString('yyyy-MM-dd HH:mm')) -Schedule $scheduleInfo
    }

    if (-not $IgnoreSchedule -and -not $scheduleInfo.Active) {
        $nextText = if ($scheduleInfo.NextStart) { $scheduleInfo.NextStart.ToString('MM-dd HH:mm') } else { '未知' }
        return New-PlanResult -Reason 'outside-window' `
            -Message ("不在触发时间段，下次开始：$nextText") -Schedule $scheduleInfo
    }

    if (-not $Current.Found) {
        return New-PlanResult -Reason 'no-adapter' -Message '没有找到无线网卡' -Schedule $scheduleInfo
    }

    # 手动连接保护：系统报告该连接是手动发起，且不是本工具刚连上的那个 SSID
    $isManual = $Current.Mode -match '(?i)manual|手动|手動'
    $lastSsid = if ($State.lastApplied) { [string]$State.lastApplied.ssid } else { '' }
    $isOurs = $Current.Connected -and $lastSsid -and ($Current.Ssid -eq $lastSsid)
    if ($Current.Connected -and $isManual -and -not $isOurs -and $Config.Behavior.RespectManualMode) {
        $State.manualUntil = $Now.AddMinutes($Config.Behavior.ManualGraceMinutes)
        return New-PlanResult -Reason 'manual' `
            -Message ('检测到手动连接，暂停自动切换 ' + $Config.Behavior.ManualGraceMinutes + ' 分钟') `
            -Schedule $scheduleInfo
    }
    if ($Current.Connected -and $State.manualUntil -and $State.manualUntil -gt $Now) {
        return New-PlanResult -Reason 'manual-grace' `
            -Message ('手动连接保护中，到 ' + $State.manualUntil.ToString('HH:mm')) -Schedule $scheduleInfo
    }
    if (-not $Current.Connected -and $State.manualUntil) {
        $State.manualUntil = $null
    }

    $visibleMap = @{}
    foreach ($visible in @($VisibleNetworks)) {
        if ($null -eq $visible -or [string]::IsNullOrWhiteSpace($visible.Ssid)) { continue }
        if (-not $visibleMap.ContainsKey($visible.Ssid) -or $visible.Signal -gt $visibleMap[$visible.Ssid].Signal) {
            $visibleMap[$visible.Ssid] = $visible
        }
    }

    $visibleCandidates = @()
    $hiddenCandidates = @()
    foreach ($network in $Config.Networks) {
        if (-not $network.Enabled) { continue }

        $visible = $visibleMap[$network.Ssid]
        $failedAt = $null
        if ($State.failures.ContainsKey($network.Ssid)) { $failedAt = $State.failures[$network.Ssid] }
        $inBackoff = $failedAt -and (($Now - $failedAt).TotalMinutes -lt $Config.Behavior.FailedRetryMinutes)

        if ($null -ne $visible) {
            if ($visible.Signal -lt $network.MinSignal) { continue }
            if ($inBackoff) { continue }
            $visibleCandidates += [pscustomobject]@{
                Net     = $network
                Signal  = [int]$visible.Signal
                Visible = $true
            }
        } elseif ($network.AllowHidden -and -not $inBackoff) {
            $hiddenCandidates += [pscustomobject]@{
                Net     = $network
                Signal  = 0
                Visible = $false
            }
        }
    }

    $visibleCandidates = @($visibleCandidates | Sort-Object { $_.Net.Priority })
    $hiddenCandidates = @($hiddenCandidates | Sort-Object { $_.Net.Priority })

    # 已连接时只考虑可见网络，避免为了一个“隐藏但当前不存在”的配置而断开现有连接
    $candidates = if ($Current.Connected) {
        $visibleCandidates
    } else {
        @($visibleCandidates + $hiddenCandidates)
    }

    if ($candidates.Count -eq 0) {
        $State.bestStreak = $null
        $message = if ($Current.Connected) {
            '当前连接不在候选列表内（可能信号不足或都处于失败冷却）'
        } else {
            '列表内的 WiFi 都不可见/信号不足/处于失败冷却'
        }
        return New-PlanResult -Reason 'no-candidate' -Message $message -Schedule $scheduleInfo
    }

    $currentNet = $null
    foreach ($network in $Config.Networks) {
        if ($network.Ssid -eq $Current.Ssid) { $currentNet = $network; break }
    }

    $best = $candidates[0]

    if ($Current.Connected -and $currentNet -and $currentNet.Priority -le $best.Net.Priority) {
        $State.bestStreak = $null
        return New-PlanResult -Reason 'already-best' `
            -Message ("当前已连接最高优先级的 $($Current.Ssid)") -Schedule $scheduleInfo
    }

    if ($Current.Connected -and -not $currentNet -and -not $Config.Behavior.PreemptUnknown) {
        $State.bestStreak = $null
        return New-PlanResult -Reason 'unknown-network' `
            -Message ("当前连接 $($Current.Ssid) 不在列表内，按配置不主动接管") -Schedule $scheduleInfo
    }

    if ($Current.Connected -and -not $Config.Behavior.Preempt) {
        $State.bestStreak = $null
        return New-PlanResult -Reason 'preempt-disabled' `
            -Message '已连接较低优先级网络，但 preempt=false，保持现状' -Schedule $scheduleInfo
    }

    if ($Current.Connected) {
        $needed = [Math]::Max(1, [int]$Config.Behavior.PreemptDelayChecks)
        if ($State.bestStreak -and $State.bestStreak.ssid -eq $best.Net.Ssid) {
            $State.bestStreak.count++
        } else {
            $State.bestStreak = [ordered]@{ ssid = $best.Net.Ssid; count = 1 }
        }
        if ($State.bestStreak.count -lt $needed) {
            return New-PlanResult -Reason 'waiting' `
                -Message ("发现更高优先级的 $($best.Net.Ssid)，确认中（$($State.bestStreak.count)/$needed）") `
                -Target $best.Net -Candidates $candidates -Schedule $scheduleInfo
        }
        return New-PlanResult -Action 'connect' -Reason 'preempt' `
            -Message ("切换到更高优先级的 $($best.Net.Ssid)（信号 $($best.Signal)%）") `
            -Target $best.Net -Candidates $candidates -Schedule $scheduleInfo
    }

    $State.bestStreak = $null
    return New-PlanResult -Action 'connect' -Reason 'fallback' `
        -Message ("当前未连接，按优先级尝试 $($best.Net.Ssid)（信号 $($best.Signal)%）") `
        -Target $best.Net -Candidates $candidates -Schedule $scheduleInfo
}

# ---------------------------------------------------------------------------
# 连接
# ---------------------------------------------------------------------------

function Connect-ToNetwork {
    param($Net, [string]$InterfaceName, [int]$TimeoutSec)

    $profileName = [string]$Net.Profile
    if ([string]::IsNullOrWhiteSpace($profileName)) { $profileName = [string]$Net.Ssid }

    $netshArgs = @(
        'wlan', 'connect',
        ('name={0}' -f $profileName),
        ('ssid={0}' -f $Net.Ssid)
    )
    if (-not [string]::IsNullOrWhiteSpace($InterfaceName)) {
        $netshArgs += ('interface={0}' -f $InterfaceName)
    }

    $result = Invoke-Netsh $netshArgs
    $outputLine = ($result.Output -replace '\s+', ' ').Trim()
    if ($outputLine) { Write-Log ("netsh: " + $outputLine) 'DEBUG' }

    $deadline = (Get-Date).AddSeconds([Math]::Max(5, $TimeoutSec))
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 1000
        $state = Get-WifiState -InterfaceName $InterfaceName
        if ($state.Connected -and $state.Ssid -eq $Net.Ssid) { return $true }
    }
    return $false
}

function Test-Internet {
    param($Config)
    $hostName = $Config.Behavior.VerifyHost
    $port = [int]$Config.Behavior.VerifyPort
    if ([string]::IsNullOrWhiteSpace($hostName) -or $port -le 0) { return $true }
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $task = $client.ConnectAsync($hostName, $port)
        if (-not $task.Wait(4000)) { return $false }
        return $client.Connected
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

function Invoke-OnConnectCommand {
    param($Config, $Net)
    $command = [string]$Config.Behavior.OnConnectCommand
    if ([string]::IsNullOrWhiteSpace($command)) { return }

    Write-Log '执行连接后命令（校园网 Portal 认证等）...'
    $env:WIFI_SSID = [string]$Net.Ssid
    $env:WIFI_PROFILE = [string]$Net.Profile
    try {
        $output = & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command $command 2>&1
        foreach ($line in @($output)) {
            if ($null -ne $line -and [string]$line -ne '') {
                Write-Log ('[onConnect] ' + [string]$line)
            }
        }
    } catch {
        Write-Log ('连接后命令执行失败：' + $_.Exception.Message) 'WARN'
    } finally {
        Remove-Item Env:\WIFI_SSID -ErrorAction SilentlyContinue
        Remove-Item Env:\WIFI_PROFILE -ErrorAction SilentlyContinue
    }
}

function Invoke-ConnectCandidates {
    param($Plan, $Config, $State, $Current, [datetime]$Now)

    $interfaceName = $Current.Name
    $attempts = 0
    $maxAttempts = [Math]::Max(1, [int]$Config.Behavior.MaxAttemptsPerRun)

    foreach ($candidate in @($Plan.Candidates)) {
        if ($attempts -ge $maxAttempts) { break }
        $attempts++
        $network = $candidate.Net
        Write-Log ("尝试连接 #{0} {1}（配置文件 {2}，信号 {3}%）" -f $network.Priority, $network.Ssid, $network.Profile, $candidate.Signal)

        $ok = Connect-ToNetwork -Net $network -InterfaceName $interfaceName -TimeoutSec $Config.Behavior.ConnectTimeoutSec
        if ($ok) {
            $State.lastApplied = [ordered]@{
                ssid    = $network.Ssid
                profile = $network.Profile
                time    = Get-Date
            }
            if ($State.failures.ContainsKey($network.Ssid)) { $State.failures.Remove($network.Ssid) }
            $State.bestStreak = $null
            Write-Log ("已连接到 $($network.Ssid)（优先级 $($network.Priority)）")

            if ($Config.Behavior.PostConnectDelaySec -gt 0) {
                Start-Sleep -Seconds $Config.Behavior.PostConnectDelaySec
            }
            if ($Config.Behavior.VerifyInternet) {
                if (Test-Internet -Config $Config) {
                    Write-Log '外网连通性检查：通过'
                } else {
                    Write-Log '外网连通性检查：未通过（可能还需要 Portal 认证）' 'WARN'
                }
            }
            Invoke-OnConnectCommand -Config $Config -Net $network
            return $true
        }

        $State.failures[$network.Ssid] = Get-Date
        Write-Log ("连接 $($network.Ssid) 失败，进入 $($Config.Behavior.FailedRetryMinutes) 分钟冷却") 'WARN'
    }

    # 如果原来有可用连接，尽量恢复，避免“抢网失败反而断网”
    if ($Current.Connected) {
        $nowState = Get-WifiState -InterfaceName $interfaceName
        if (-not $nowState.Connected) {
            Write-Log ("所有候选都失败，尝试恢复原连接 $($Current.Ssid)") 'WARN'
            $temporary = [pscustomobject]@{
                Ssid    = $Current.Ssid
                Profile = $Current.Profile
            }
            $restored = Connect-ToNetwork -Net $temporary -InterfaceName $interfaceName `
                -TimeoutSec ([Math]::Max(8, [int]$Config.Behavior.ConnectTimeoutSec))
            if ($restored) {
                Write-Log ("已恢复原连接 $($Current.Ssid)")
            } else {
                Write-Log '恢复原连接失败' 'WARN'
            }
        }
    }
    return $false
}

# ---------------------------------------------------------------------------
# 日志清理 / 进程锁
# ---------------------------------------------------------------------------

function Invoke-LogCleanup {
    param($Config, $State)
    $today = (Get-Date).Date
    if ($State.lastCleanup -and $State.lastCleanup.Date -eq $today) { return }
    $State.lastCleanup = Get-Date
    if (-not (Test-Path -LiteralPath $script:LogDir)) { return }
    $cutoff = (Get-Date).AddDays(-[Math]::Max(1, [int]$Config.Behavior.LogKeepDays))
    Get-ChildItem -LiteralPath $script:LogDir -Filter 'wifi-fallback-*.log' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $cutoff } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Enter-WifiLock {
    param([int]$TimeoutMs = 0)
    $hash = [Math]::Abs([int64]$script:ConfigPath.ToLowerInvariant().GetHashCode())
    $name = 'Local\WifiFallback-' + $hash
    $mutex = $null
    $acquired = $false
    try {
        $mutex = New-Object System.Threading.Mutex($false, $name)
        try {
            $acquired = $mutex.WaitOne([Math]::Max(0, $TimeoutMs))
        } catch [System.Threading.AbandonedMutexException] {
            $acquired = $true
        }
    } catch {
        if ($null -ne $mutex) { try { $mutex.Dispose() } catch { } }
        return [pscustomobject]@{ Acquired = $false; Mutex = $null }
    }
    return [pscustomobject]@{ Acquired = $acquired; Mutex = $mutex }
}

# ---------------------------------------------------------------------------
# 单次运行
# ---------------------------------------------------------------------------

function Invoke-Cycle {
    param([switch]$ForceRun)

    $lock = Enter-WifiLock
    if (-not $lock.Acquired) {
        Write-Log '已有另一个实例在运行，跳过本次检查'
        if ($null -ne $lock.Mutex) { try { $lock.Mutex.Dispose() } catch { } }
        return 0
    }

    try {
        $config = Read-Config -Path $script:ConfigPath
        $state = Read-StateFile -Path $script:StatePath
        $now = Get-Date

        $current = Get-WifiState -InterfaceName $config.Interface
        if (-not $current.Found) {
            Write-Log '没有找到无线网卡，或 WLAN 服务未运行' 'WARN'
            return 2
        }

        $visible = @(Get-VisibleNetworks)
        $plan = Get-NetworkPlan -Config $config -State $state -Current $current `
            -VisibleNetworks $visible -Now $now -IgnoreSchedule:$ForceRun

        $currentText = if ($current.Connected) {
            '{0}（信号 {1}%，模式 {2}）' -f $current.Ssid, $current.Signal, $current.Mode
        } else {
            '未连接'
        }
        Write-Log ("检查: 当前=$currentText；决策=$($plan.Reason)；$($plan.Message)")

        $success = $true
        if ($plan.Action -eq 'connect') {
            $success = Invoke-ConnectCandidates -Plan $plan -Config $config -State $state -Current $current -Now $now
        }

        $state.lastRun = Get-Date
        $state.lastResult = if ($plan.Action -eq 'connect') {
            if ($success) { 'connected:' + $plan.Target.Ssid } else { 'failed:' + $plan.Reason }
        } else {
            'idle:' + $plan.Reason
        }
        Invoke-LogCleanup -Config $config -State $state
        Save-StateFile -State $state -Path $script:StatePath

        if ($plan.Action -eq 'connect' -and -not $success) { return 1 }
        return 0
    } catch {
        Write-Log ('运行失败：' + $_.Exception.Message) 'ERROR'
        return 2
    } finally {
        if ($lock.Acquired) { try { $lock.Mutex.ReleaseMutex() } catch { } }
        if ($null -ne $lock.Mutex) { try { $lock.Mutex.Dispose() } catch { } }
    }
}

# ---------------------------------------------------------------------------
# 显示 / 状态
# ---------------------------------------------------------------------------

function Get-ReasonText {
    param([string]$Reason)
    switch ($Reason) {
        'paused' { return '已暂停' }
        'outside-window' { return '不在触发时间段' }
        'no-adapter' { return '没有无线网卡' }
        'manual' { return '手动连接保护' }
        'manual-grace' { return '手动连接保护期' }
        'no-candidate' { return '没有可用候选' }
        'already-best' { return '已是最优网络' }
        'unknown-network' { return '当前为非列表网络' }
        'preempt-disabled' { return '保持当前连接' }
        'waiting' { return '等待确认更高优先级' }
        'preempt' { return '切换到更高优先级' }
        'fallback' { return '断线兜底连接' }
        default { return $Reason }
    }
}

function Show-WifiStatus {
    param($Config, $State, $Current, $Visible, $Plan, [switch]$PlanMode)

    $now = Get-Date
    Write-Host ''
    Write-Host '=== WiFi 优先级兜底 ===' -ForegroundColor Cyan
    Write-Host ('配置文件 : {0}' -f $Config.Path)

    if ($Current.Found) {
        $adapterText = if ($Current.StateText) { $Current.StateText } else { '未知' }
        Write-Host ('无线网卡 : {0}（{1}）' -f $Current.Name, $adapterText)
    } else {
        Write-Host '无线网卡 : 未找到' -ForegroundColor Red
    }

    if ($Current.Connected) {
        Write-Host ('当前连接 : {0}  信号 {1}%  模式 {2}' -f $Current.Ssid, $Current.Signal, $Current.Mode) -ForegroundColor Green
    } else {
        Write-Host '当前连接 : 未连接' -ForegroundColor Yellow
    }

    $sched = Get-ScheduleInfo -Schedule $Config.Schedule -Now $now
    Write-Host ('触发时间 : {0}' -f $sched.Text)
    if ($Config.Schedule.Enabled) {
        if ($sched.Active) {
            $endText = if ($sched.CurrentEnd) { $sched.CurrentEnd.ToString('MM-dd HH:mm') } else { '未知' }
            Write-Host ('           当前在窗口内，结束于 {0}' -f $endText) -ForegroundColor Green
        } else {
            $startText = if ($sched.NextStart) { $sched.NextStart.ToString('MM-dd HH:mm') } else { '未知' }
            Write-Host ('           当前不在窗口内，下次开始 {0}' -f $startText) -ForegroundColor DarkYellow
        }
    }

    if ($State.pausedUntil -and $State.pausedUntil -gt $now) {
        Write-Host ('暂停状态 : 到 {0}（-Resume 可取消）' -f $State.pausedUntil.ToString('MM-dd HH:mm')) -ForegroundColor Yellow
    }
    if ($State.manualUntil -and $State.manualUntil -gt $now -and $Current.Connected) {
        Write-Host ('手动保护 : 到 {0}' -f $State.manualUntil.ToString('HH:mm')) -ForegroundColor Yellow
    }
    if ($State.lastApplied) {
        Write-Host ('上次动作 : {0} -> {1}' -f $State.lastApplied.time.ToString('MM-dd HH:mm:ss'), $State.lastApplied.ssid)
    }
    if ($State.lastResult) {
        Write-Host ('上次结果 : {0}' -f $State.lastResult)
    }

    Write-Host ''
    Write-Host '优先级列表：'
    foreach ($network in $Config.Networks) {
        $visibleItem = @($Visible | Where-Object { $_.Ssid -eq $network.Ssid }) | Select-Object -First 1
        $flags = @()
        if ($Current.Connected -and $Current.Ssid -eq $network.Ssid) { $flags += '当前' }
        if ($visibleItem) {
            $flags += ('可见 {0}%' -f $visibleItem.Signal)
        } else {
            $flags += '不可见'
        }
        if (-not $network.Enabled) { $flags += '已禁用' }
        if ($State.failures.ContainsKey($network.Ssid) -and
            ((Get-Date) - $State.failures[$network.Ssid]).TotalMinutes -lt $Config.Behavior.FailedRetryMinutes) {
            $flags += '失败冷却'
        }

        $line = '  #{0}  {1}  [{2}]' -f $network.Priority, $network.Ssid, ($flags -join '，')
        if ($Current.Connected -and $Current.Ssid -eq $network.Ssid) {
            Write-Host $line -ForegroundColor Green
        } else {
            Write-Host $line
        }
    }

    Write-Host ''
    Write-Host ('决策 : {0}' -f (Get-ReasonText -Reason $Plan.Reason)) -ForegroundColor Cyan
    Write-Host ('说明 : {0}' -f $Plan.Message)

    if ($Plan.Action -eq 'connect') {
        Write-Host '候选顺序：'
        $number = 0
        foreach ($candidate in $Plan.Candidates) {
            $number++
            $signalText = if ($candidate.Visible) { '信号 {0}%' -f $candidate.Signal } else { '隐藏/未扫描到' }
            Write-Host ('  {0}. {1}（{2}）' -f $number, $candidate.Net.Ssid, $signalText)
        }
    } else {
        Write-Host '候选顺序 : 无需连接'
    }

    if ($PlanMode) {
        Write-Host ''
        Write-Host '（-Plan 为模拟运行，已忽略时间窗口限制，不会真正连接）' -ForegroundColor DarkGray
    }
    Write-Host ('日志 : {0}' -f $script:LogFile)
    Write-Host ''
}

function Invoke-Status {
    param([switch]$PlanMode)
    try {
        $config = Read-Config -Path $script:ConfigPath
    } catch {
        Write-Host ('读取配置失败：{0}' -f $_.Exception.Message) -ForegroundColor Red
        return 2
    }

    $state = Read-StateFile -Path $script:StatePath
    $current = Get-WifiState -InterfaceName $config.Interface
    $visible = @()
    if ($current.Found) { $visible = @(Get-VisibleNetworks) }
    $plan = Get-NetworkPlan -Config $config -State $state -Current $current `
        -VisibleNetworks $visible -Now (Get-Date) -IgnoreSchedule:$PlanMode

    Show-WifiStatus -Config $config -State $state -Current $current -Visible $visible -Plan $plan -PlanMode:$PlanMode

    if ($PlanMode -and $plan.Action -eq 'connect') { return 0 }
    if (-not $current.Found) { return 2 }
    return 0
}

# ---------------------------------------------------------------------------
# 暂停 / 恢复 / 手动连接
# ---------------------------------------------------------------------------

function Set-PauseState {
    param([int]$PauseMinutes)
    $lock = Enter-WifiLock -TimeoutMs 5000
    if (-not $lock.Acquired) {
        Write-Host '另一个实例正在运行，请稍后再试。' -ForegroundColor Yellow
        if ($null -ne $lock.Mutex) { try { $lock.Mutex.Dispose() } catch { } }
        return 1
    }
    try {
        $state = Read-StateFile -Path $script:StatePath
        if ($PauseMinutes -le 0) {
            $state.pausedUntil = $null
            $state.manualUntil = $null
            Save-StateFile -State $state -Path $script:StatePath
            Write-Host '已取消暂停。' -ForegroundColor Green
            return 0
        }
        $state.pausedUntil = (Get-Date).AddMinutes($PauseMinutes)
        Save-StateFile -State $state -Path $script:StatePath
        Write-Host ('已暂停自动切换，到 {0}。' -f $state.pausedUntil.ToString('yyyy-MM-dd HH:mm')) -ForegroundColor Yellow
        return 0
    } finally {
        if ($lock.Acquired) { try { $lock.Mutex.ReleaseMutex() } catch { } }
        if ($null -ne $lock.Mutex) { try { $lock.Mutex.Dispose() } catch { } }
    }
}

function Clear-PauseState {
    $lock = Enter-WifiLock -TimeoutMs 5000
    if (-not $lock.Acquired) {
        Write-Host '另一个实例正在运行，请稍后再试。' -ForegroundColor Yellow
        if ($null -ne $lock.Mutex) { try { $lock.Mutex.Dispose() } catch { } }
        return 1
    }
    try {
        $state = Read-StateFile -Path $script:StatePath
        $state.pausedUntil = $null
        $state.manualUntil = $null
        Save-StateFile -State $state -Path $script:StatePath
        Write-Host '已恢复自动切换。' -ForegroundColor Green
        return 0
    } finally {
        if ($lock.Acquired) { try { $lock.Mutex.ReleaseMutex() } catch { } }
        if ($null -ne $lock.Mutex) { try { $lock.Mutex.Dispose() } catch { } }
    }
}

function Invoke-ManualConnect {
    param([string]$Target)
    $lock = Enter-WifiLock
    if (-not $lock.Acquired) {
        Write-Host '另一个实例正在运行，请稍后再试。' -ForegroundColor Yellow
        if ($null -ne $lock.Mutex) { try { $lock.Mutex.Dispose() } catch { } }
        return 1
    }
    try {
        $config = Read-Config -Path $script:ConfigPath
        $state = Read-StateFile -Path $script:StatePath
        $network = @($config.Networks | Where-Object { $_.Ssid -eq $Target }) | Select-Object -First 1
        if (-not $network) {
            Write-Host ("配置里没有 SSID：{0}" -f $Target) -ForegroundColor Red
            return 1
        }

        $current = Get-WifiState -InterfaceName $config.Interface
        if (-not $current.Found) {
            Write-Host '没有找到无线网卡。' -ForegroundColor Red
            return 2
        }
        if ($current.Connected -and $current.Ssid -eq $network.Ssid) {
            Write-Host ("已经连接到 {0}。" -f $network.Ssid) -ForegroundColor Green
            return 0
        }

        $visible = @(Get-VisibleNetworks)
        $isVisible = @($visible | Where-Object { $_.Ssid -eq $network.Ssid }).Count -gt 0
        if (-not $isVisible -and -not $network.AllowHidden) {
            Write-Host ("当前扫描不到 {0}。若这是隐藏网络，请在该条配置里加 allowHidden: true。" -f $network.Ssid) -ForegroundColor Yellow
            return 1
        }

        Write-Host ("正在连接 {0}（配置文件 {1}）..." -f $network.Ssid, $network.Profile) -ForegroundColor Cyan
        $ok = Connect-ToNetwork -Net $network -InterfaceName $current.Name -TimeoutSec $config.Behavior.ConnectTimeoutSec
        if (-not $ok) {
            Write-Host ("连接 {0} 失败。" -f $network.Ssid) -ForegroundColor Red
            return 1
        }

        $state.lastApplied = [ordered]@{ ssid = $network.Ssid; profile = $network.Profile; time = Get-Date }
        if ($state.failures.ContainsKey($network.Ssid)) { $state.failures.Remove($network.Ssid) }
        Save-StateFile -State $state -Path $script:StatePath
        Write-Host ("已连接到 {0}。" -f $network.Ssid) -ForegroundColor Green

        if ($config.Behavior.PostConnectDelaySec -gt 0) { Start-Sleep -Seconds $config.Behavior.PostConnectDelaySec }
        if ($config.Behavior.VerifyInternet) {
            if (Test-Internet -Config $config) {
                Write-Host '外网连通性检查：通过' -ForegroundColor Green
            } else {
                Write-Host '外网连通性检查：未通过（可能还需要 Portal 认证）' -ForegroundColor Yellow
            }
        }
        Invoke-OnConnectCommand -Config $config -Net $network
        return 0
    } catch {
        Write-Host ('连接失败：{0}' -f $_.Exception.Message) -ForegroundColor Red
        return 2
    } finally {
        if ($lock.Acquired) { try { $lock.Mutex.ReleaseMutex() } catch { } }
        if ($null -ne $lock.Mutex) { try { $lock.Mutex.Dispose() } catch { } }
    }
}

# ---------------------------------------------------------------------------
# 计划任务
# ---------------------------------------------------------------------------

function Write-VbsLauncher {
    param([string]$Path)
    $content = @'
Option Explicit
Dim fso, shell, base, cmd
Set fso = CreateObject("Scripting.FileSystemObject")
base = fso.GetParentFolderName(WScript.ScriptFullName)
Set shell = CreateObject("WScript.Shell")
cmd = "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File """ & base & "\WifiFallback.ps1"" -Run"
shell.Run cmd, 0, True
'@
    [IO.File]::WriteAllText($Path, $content, $script:Utf8NoBom)
}

function Install-WifiTask {
    param([string]$Name)
    $config = Read-Config -Path $script:ConfigPath

    $savedProfiles = @(Get-SavedProfiles)
    foreach ($network in $config.Networks) {
        if ($savedProfiles -notcontains $network.Profile) {
            Write-Host ("警告：系统里还没有保存 {0} 的配置文件，请先手动连接一次。" -f $network.Profile) -ForegroundColor Yellow
        }
    }

    $vbsPath = Join-Path $script:Root 'run-hidden.vbs'
    Write-VbsLauncher -Path $vbsPath

    $wscript = Join-Path $env:SystemRoot 'System32\wscript.exe'
    if (-not (Test-Path -LiteralPath $wscript)) {
        throw "找不到 wscript.exe：$wscript"
    }

    $action = New-ScheduledTaskAction -Execute $wscript -Argument ('"{0}"' -f $vbsPath)
    # 注意：AtLogOn 触发器需要管理员权限，这里只用“每分钟重复”的触发器。
    # 任务本身以当前用户身份运行，登录后一分钟内就会开始第一次检查。
    $repeatTrigger = New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(1)) `
        -RepetitionInterval (New-TimeSpan -Minutes $config.Behavior.TaskIntervalMinutes) `
        -RepetitionDuration (New-TimeSpan -Days 3650)
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30)

    $existing = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if ($existing) {
        Unregister-ScheduledTask -TaskName $Name -Confirm:$false -ErrorAction SilentlyContinue
    }

    Register-ScheduledTask -TaskName $Name -Action $action -Trigger $repeatTrigger `
        -Settings $settings -Description 'WiFi 优先级兜底（定时窗口）' -Force | Out-Null

    $task = Get-ScheduledTask -TaskName $Name -ErrorAction Stop
    Write-Host ('已安装计划任务：{0}（状态：{1}）' -f $task.TaskName, $task.State) -ForegroundColor Green
    Write-Host ('执行间隔：每 {0} 分钟（登录后约一分钟内开始）。' -f $config.Behavior.TaskIntervalMinutes)
    Write-Host ('触发时间：{0}' -f (Get-ScheduleInfo -Schedule $config.Schedule -Now (Get-Date)).Text)
    Write-Host ''
    Write-Host '现在可以运行下面命令立即验证一次：' -ForegroundColor Cyan
    Write-Host ('  powershell -ExecutionPolicy Bypass -File "{0}" -Run -Force' -f (Join-Path $script:Root 'WifiFallback.ps1'))
    Write-Host '取消：-UninstallTask；临时暂停：-Pause -Minutes 120'
}

function Uninstall-WifiTask {
    param([string]$Name)
    $task = Get-ScheduledTask -TaskName $Name -ErrorAction SilentlyContinue
    if (-not $task) {
        Write-Host ("计划任务不存在：{0}" -f $Name)
        return 0
    }
    Unregister-ScheduledTask -TaskName $Name -Confirm:$false
    Write-Host ("已删除计划任务：{0}" -f $Name) -ForegroundColor Green
    return 0
}

# ---------------------------------------------------------------------------
# 给图形界面使用的 JSON 快照（只读，不执行任何连接动作）
# ---------------------------------------------------------------------------

function Write-Snapshot {
    param([string]$Path)

    $now = Get-Date
    $data = [ordered]@{
        ok          = $false
        error       = $null
        generatedAt = $now.ToString('o')
        configPath  = $script:ConfigPath
        pausedUntil = $null
        manualUntil = $null
        lastResult  = $null
        lastRun     = $null
        lastApplied = $null
        task        = [ordered]@{ installed = $false; state = ''; name = $TaskName }
        adapter     = [ordered]@{ found = $false; name = ''; connected = $false; ssid = ''; profile = ''; signal = 0; mode = ''; state = '' }
        schedule    = [ordered]@{ enabled = $false; active = $false; text = ''; currentEnd = $null; nextStart = $null }
        plan        = [ordered]@{ action = 'none'; reason = ''; message = ''; target = ''; candidates = @() }
        networks    = @()
        visible     = @()
        profiles    = @()
    }

    try {
        $config = Read-Config -Path $script:ConfigPath
        $state = Read-StateFile -Path $script:StatePath
        $current = Get-WifiState -InterfaceName $config.Interface
        $visible = @()
        if ($current.Found) { $visible = @(Get-VisibleNetworks) }

        $plan = Get-NetworkPlan -Config $config -State $state -Current $current `
            -VisibleNetworks $visible -Now $now
        $scheduleInfo = Get-ScheduleInfo -Schedule $config.Schedule -Now $now

        $taskInstalled = $false
        $taskState = ''
        try {
            $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
            if ($task) {
                $taskInstalled = $true
                $taskState = [string]$task.State
            }
        } catch { }

        $lastApplied = $null
        if ($state.lastApplied) {
            $lastApplied = [ordered]@{
                ssid    = [string]$state.lastApplied.ssid
                profile = [string]$state.lastApplied.profile
                time    = $state.lastApplied.time.ToString('o')
            }
        }

        $visibleMap = @{}
        foreach ($item in $visible) {
            if ($item -and -not [string]::IsNullOrWhiteSpace($item.Ssid)) {
                $visibleMap[$item.Ssid] = $item
            }
        }

        $networkList = @()
        foreach ($network in $config.Networks) {
            $visibleItem = $visibleMap[$network.Ssid]
            $failedAt = $null
            if ($state.failures.ContainsKey($network.Ssid)) {
                $failedAt = $state.failures[$network.Ssid]
            }
            $cooling = $failedAt -and (($now - $failedAt).TotalMinutes -lt $config.Behavior.FailedRetryMinutes)
            $networkList += [ordered]@{
                ssid        = [string]$network.Ssid
                profile     = [string]$network.Profile
                priority    = [int]$network.Priority
                enabled     = [bool]$network.Enabled
                minSignal   = [int]$network.MinSignal
                allowHidden = [bool]$network.AllowHidden
                current     = [bool]($current.Connected -and $current.Ssid -eq $network.Ssid)
                visible     = [bool]($null -ne $visibleItem)
                signal      = if ($null -ne $visibleItem) { [int]$visibleItem.Signal } else { 0 }
                failure     = [bool]$cooling
            }
        }

        $visibleList = @()
        foreach ($item in $visible) {
            if ($item) {
                $visibleList += [ordered]@{
                    ssid   = [string]$item.Ssid
                    signal = [int]$item.Signal
                }
            }
        }

        $candidates = @()
        foreach ($candidate in @($plan.Candidates)) {
            if ($candidate -and $candidate.Net) { $candidates += [string]$candidate.Net.Ssid }
        }

        $data.adapter = [ordered]@{
            found     = [bool]$current.Found
            name      = [string]$current.Name
            connected = [bool]$current.Connected
            ssid      = [string]$current.Ssid
            profile   = [string]$current.Profile
            signal    = [int]$current.Signal
            mode      = [string]$current.Mode
            state     = [string]$current.StateText
        }
        $data.schedule = [ordered]@{
            enabled    = [bool]$config.Schedule.Enabled
            active     = [bool]$scheduleInfo.Active
            text       = [string]$scheduleInfo.Text
            currentEnd = if ($scheduleInfo.CurrentEnd) { $scheduleInfo.CurrentEnd.ToString('o') } else { $null }
            nextStart  = if ($scheduleInfo.NextStart) { $scheduleInfo.NextStart.ToString('o') } else { $null }
        }
        $data.plan = [ordered]@{
            action     = [string]$plan.Action
            reason     = [string]$plan.Reason
            message    = [string]$plan.Message
            target     = if ($plan.Target) { [string]$plan.Target.Ssid } else { '' }
            candidates = @($candidates)
        }
        $data.task = [ordered]@{
            installed = [bool]$taskInstalled
            state     = [string]$taskState
            name      = [string]$TaskName
        }
        $data.networks = @($networkList)
        $data.visible = @($visibleList)
        $data.profiles = @(Get-SavedProfiles)
        $data.pausedUntil = if ($state.pausedUntil) { $state.pausedUntil.ToString('o') } else { $null }
        $data.manualUntil = if ($state.manualUntil) { $state.manualUntil.ToString('o') } else { $null }
        $data.lastRun = if ($state.lastRun) { $state.lastRun.ToString('o') } else { $null }
        $data.lastResult = [string]$state.lastResult
        $data.lastApplied = $lastApplied
        $data.ok = $true
    } catch {
        $data.error = $_.Exception.Message
    }

    try {
        $json = $data | ConvertTo-Json -Depth 8
        [IO.File]::WriteAllText($Path, $json, $script:Utf8NoBom)
    } catch {
        return 2
    }
    if ($data.ok) { return 0 }
    return 2
}

# ---------------------------------------------------------------------------
# 帮助 / 入口
# ---------------------------------------------------------------------------

function Show-Usage {
    $scriptPath = Join-Path $script:Root 'WifiFallback.ps1'
    Write-Host ''
    Write-Host 'Windows 多 WiFi 优先级兜底（定时窗口）' -ForegroundColor Cyan
    Write-Host ''
    Write-Host '常用命令：'
    Write-Host ('  查看状态    powershell -ExecutionPolicy Bypass -File "{0}" -Status' -f $scriptPath)
    Write-Host ('  模拟运行    powershell -ExecutionPolicy Bypass -File "{0}" -Plan' -f $scriptPath)
    Write-Host ('  立即连接    powershell -ExecutionPolicy Bypass -File "{0}" -ConnectTo "ZstuWlan"' -f $scriptPath)
    Write-Host ('  安装任务    powershell -ExecutionPolicy Bypass -File "{0}" -InstallTask' -f $scriptPath)
    Write-Host ('  删除任务    powershell -ExecutionPolicy Bypass -File "{0}" -UninstallTask' -f $scriptPath)
    Write-Host ('  暂停 2 小时 powershell -ExecutionPolicy Bypass -File "{0}" -Pause -Minutes 120' -f $scriptPath)
    Write-Host ('  恢复        powershell -ExecutionPolicy Bypass -File "{0}" -Resume' -f $scriptPath)
    Write-Host ''
    Write-Host ('配置文件：{0}' -f $script:ConfigPath)
    Write-Host '第一次使用请先编辑 config.json，至少确认 networks 和 schedule.windows。'
    Write-Host ''
}

$dotSourced = $MyInvocation.InvocationName -eq '.'
if ($dotSourced) { return }

try {
    if (-not [string]::IsNullOrWhiteSpace($SnapshotPath)) {
        $code = Write-Snapshot -Path $SnapshotPath
        exit $code
    }
    if ($Status -or $Plan) {
        $code = Invoke-Status -PlanMode:$Plan
        exit $code
    }
    if ($InstallTask) {
        Install-WifiTask -Name $TaskName
        exit 0
    }
    if ($UninstallTask) {
        $code = Uninstall-WifiTask -Name $TaskName
        exit $code
    }
    if ($Pause -and $Minutes -gt 0) {
        $code = Set-PauseState -PauseMinutes $Minutes
        exit $code
    }
    if ($Resume) {
        $code = Clear-PauseState
        exit $code
    }
    if (-not [string]::IsNullOrWhiteSpace($ConnectTo)) {
        $code = Invoke-ManualConnect -Target $ConnectTo
        exit $code
    }
    if ($Run) {
        $code = Invoke-Cycle -ForceRun:$Force
        exit $code
    }
    Show-Usage
    exit 0
} catch {
    Write-Host ('出错：{0}' -f $_.Exception.Message) -ForegroundColor Red
    exit 2
}
