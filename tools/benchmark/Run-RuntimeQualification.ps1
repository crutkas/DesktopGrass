# Run-RuntimeQualification.ps1
#
# Production-path qualification driver for issue #14. This script never uses
# --benchmark and never changes power, display, lock, or session policy.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultsRoot,

    [ValidateSet(
        'visible',
        'fullscreen-suppression',
        'occlusion-suppression',
        'no-app-control'
    )]
    [string] $Scenario = 'visible',

    # 0=Grass, 1=Desert, 2=Winter, 3=Autumn, 4=Ocean.
    [int[]] $Scenes = @(0, 1, 2, 3, 4),

    [ValidateRange(3, 100)]
    [int] $Runs = 3,

    [ValidateRange(1, 86400)]
    [int] $DurationSec = 60,

    [ValidateRange(0.25, 60.0)]
    [double] $SampleIntervalSec = 1.0,

    [ValidateRange(0, 300)]
    [int] $WarmupSec = 5,

    [ValidateRange(0, 30)]
    [int] $ProbeSettleSec = 2,

    [ValidateRange(2, 60)]
    [int] $PresentControlDurationSec = 5,

    [ValidateRange(1, 240)]
    [int] $RequiredTargetFps = 24,

    [uint64] $Seed = 1,

    [ValidateSet('any', 'ac', 'battery')]
    [string] $ExpectedPowerSource = 'any',

    [ValidateSet('any', 'on', 'off')]
    [string] $ExpectedBatterySaver = 'any',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = $(if (
        [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
            [Runtime.InteropServices.Architecture]::Arm64
    ) {
        'ARM64'
    } else {
        'x64'
    }),

    [string] $Exe,

    [string] $ConfigFilePath = (
        Join-Path $env:LOCALAPPDATA 'DesktopGrass\config.json'
    ),

    [string] $StateFilePath = (
        Join-Path $env:LOCALAPPDATA 'DesktopGrass\state.json'
    ),

    # Optional external PresentMon console executable. Missing capture tooling
    # is recorded as unavailable and can never pass a present budget.
    [string] $PresentMonExe,

    [ValidateRange(1, 120)]
    [int] $WindowTimeoutSeconds = 10,

    [ValidateRange(1, 120)]
    [int] $ProbeTimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'Benchmark.Common.psm1') -Force
Import-Module (
    Join-Path $PSScriptRoot 'RuntimeQualification.Common.psm1'
) -Force
Import-Module (
    Join-Path $PSScriptRoot '..\..\tests\smoke\Smoke.Common.psm1'
) -Force

$ResultsRoot = [IO.Path]::GetFullPath($ResultsRoot)
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Assert-ResultsRootOutsideRepo -ResultsRoot $ResultsRoot -RepoRoot $repoRoot
Assert-NoOtherNativeProcess -ProcessName 'DesktopGrass.Native'

if ($SampleIntervalSec -gt $DurationSec) {
    throw 'SampleIntervalSec cannot exceed DurationSec.'
}

$isNoAppControl = $Scenario -eq 'no-app-control'
if (-not $PSBoundParameters.ContainsKey('Exe')) {
    $Exe = Join-Path $PSScriptRoot (
        "..\..\src\DesktopGrass.Native\out\$Platform\Release\" +
        'DesktopGrass.Native.exe'
    )
}
if (-not (Test-Path -LiteralPath $Exe)) {
    throw "Production exe not found at $Exe. Build Release first or pass -Exe."
}
$Exe = (Resolve-Path -LiteralPath $Exe).Path
if ([IO.Path]::GetFileName($Exe) -ine 'DesktopGrass.Native.exe') {
    throw 'The production executable must be named DesktopGrass.Native.exe.'
}
$targetProcessName = 'DesktopGrass.Native'

$productionConfigPath = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'DesktopGrass\config.json')
)
$productionStatePath = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'DesktopGrass\state.json')
)
if (-not [string]::Equals(
    [IO.Path]::GetFullPath($ConfigFilePath),
    $productionConfigPath,
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw (
        'ConfigFilePath cannot be overridden: the production App always reads ' +
        "'$productionConfigPath'."
    )
}
if (-not [string]::Equals(
    [IO.Path]::GetFullPath($StateFilePath),
    $productionStatePath,
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw (
        'StateFilePath cannot be overridden: the production App always reads ' +
        "'$productionStatePath'."
    )
}
$ConfigFilePath = $productionConfigPath
$StateFilePath = $productionStatePath

Assert-ProductionTargetFps `
    -ConfigPath $ConfigFilePath `
    -ExpectedTargetFps $RequiredTargetFps | Out-Null

$sceneNames = @('Grass', 'Desert', 'Winter', 'Autumn', 'Ocean')
$sceneCommandIds = @(1010, 1011, 1012, 1013, 1014)
$windowClass = 'DesktopGrass.Native.Window'
$msgWindowClass = 'DesktopGrass.Native.MessageWindow'
$msgWindowTitle = 'DesktopGrass.Msg'
$wmCommand = 0x0111

foreach ($scene in $Scenes) {
    if ($scene -lt 0 -or $scene -ge $sceneNames.Count) {
        throw "Scene index $scene out of range (0..$($sceneNames.Count - 1))."
    }
}

function Get-PowerStateKey {
    param([Parameter(Mandatory)] [pscustomobject] $Power)

    if ($Power.ac_line_status -eq 'ac') {
        return 'ac'
    }
    if ($Power.ac_line_status -eq 'battery' -and
        $Power.battery_saver -eq $true) {
        return 'battery-saver'
    }
    if ($Power.ac_line_status -eq 'battery') {
        return 'battery'
    }
    return 'unknown'
}

function Test-PowerSnapshot {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Baseline,
        [Parameter(Mandatory)] [pscustomobject] $Sample
    )

    $reasons = [System.Collections.Generic.List[string]]::new()
    if ($Sample.status -ne 'available') {
        $reasons.Add("power status is '$($Sample.status)'")
    }
    if ($Sample.ac_line_status -notin @('ac', 'battery')) {
        $reasons.Add("power source is '$($Sample.ac_line_status)'")
    }
    if ($null -eq $Sample.battery_saver) {
        $reasons.Add('Battery Saver state is unavailable')
    }
    if ([string]::IsNullOrWhiteSpace(
        [string]$Sample.active_power_scheme_guid)) {
        $reasons.Add('active power scheme is unavailable')
    }
    if ($Sample.ac_line_status -ne $Baseline.ac_line_status) {
        $reasons.Add(
            "power source changed from '$($Baseline.ac_line_status)' to " +
            "'$($Sample.ac_line_status)'"
        )
    }
    if ($Sample.battery_saver -ne $Baseline.battery_saver) {
        $reasons.Add('Battery Saver state changed')
    }
    if ($Sample.active_power_scheme_guid -ne
        $Baseline.active_power_scheme_guid) {
        $reasons.Add('active power scheme changed')
    }
    if ($ExpectedPowerSource -ne 'any' -and
        $Sample.ac_line_status -ne $ExpectedPowerSource) {
        $reasons.Add(
            "expected power source '$ExpectedPowerSource' was not present"
        )
    }
    if ($ExpectedBatterySaver -ne 'any') {
        $expectedSaver = $ExpectedBatterySaver -eq 'on'
        if ($Sample.battery_saver -ne $expectedSaver) {
            $reasons.Add(
                "expected Battery Saver '$ExpectedBatterySaver' was not present"
            )
        }
    }
    return [pscustomobject]@{
        valid = $reasons.Count -eq 0
        reason = if ($reasons.Count -eq 0) {
            $null
        } else {
            $reasons -join '; '
        }
    }
}

function Test-SessionSnapshot {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Baseline,
        [Parameter(Mandatory)] [pscustomobject] $Sample
    )

    $reasons = [Collections.Generic.List[string]]::new()
    if ($Sample.status -ne 'available') {
        $reasons.Add("session status is '$($Sample.status)'")
    }
    if ($Sample.connect_state -ne 'active') {
        $reasons.Add("session connect state is '$($Sample.connect_state)'")
    }
    if ($Sample.lock_state -ne 'unlocked') {
        $reasons.Add("session lock state is '$($Sample.lock_state)'")
    }
    if ($Sample.session_id -ne $Baseline.session_id) {
        $reasons.Add(
            "session ID changed from '$($Baseline.session_id)' to " +
            "'$($Sample.session_id)'"
        )
    }
    return [pscustomobject]@{
        valid = $reasons.Count -eq 0
        reason = if ($reasons.Count -eq 0) {
            $null
        } else {
            $reasons -join '; '
        }
    }
}

function Get-WindowBounds {
    param([Parameter(Mandatory)] [IntPtr] $Hwnd)

    $rect = [DesktopGrass.Smoke.Win32+RECT]::new()
    if (-not [DesktopGrass.Smoke.Win32]::GetWindowRect($Hwnd, [ref]$rect)) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "GetWindowRect failed for hwnd=$Hwnd (Win32 error $errorCode)."
    }
    return [System.Drawing.Rectangle]::FromLTRB(
        $rect.Left,
        $rect.Top,
        $rect.Right,
        $rect.Bottom
    )
}

function Test-BoundsEqual {
    param(
        [Parameter(Mandatory)] [System.Drawing.Rectangle] $Expected,
        [Parameter(Mandatory)] [System.Drawing.Rectangle] $Actual
    )

    return (
        $Expected.X -eq $Actual.X -and
        $Expected.Y -eq $Actual.Y -and
        $Expected.Width -eq $Actual.Width -and
        $Expected.Height -eq $Actual.Height
    )
}

function Get-GrassWindows {
    param([Parameter(Mandatory)] [Diagnostics.Process] $Process)

    return @(
        [DesktopGrass.Smoke.Win32]::EnumerateWindowsForProcess(
            [uint32]$Process.Id,
            $windowClass
        )
    )
}

function Wait-GrassWindowsVisible {
    param(
        [Parameter(Mandatory)] [IntPtr[]] $Windows,
        [Parameter(Mandatory)] [bool] $Visible
    )

    foreach ($hwnd in $Windows) {
        Wait-ForWindowVisibility `
            -Hwnd $hwnd `
            -Visible $Visible `
            -TimeoutSeconds $ProbeTimeoutSeconds
    }
}

function Get-GrassVisibility {
    param([Parameter(Mandatory)] [IntPtr[]] $Windows)

    $visibleCount = @(
        $Windows | Where-Object {
            [DesktopGrass.Smoke.Win32]::IsWindowVisible($_)
        }
    ).Count
    return [pscustomobject]@{
        total = $Windows.Count
        visible = $visibleCount
        hidden = $Windows.Count - $visibleCount
    }
}

function Send-SceneCommand {
    param(
        [Parameter(Mandatory)] [IntPtr] $MsgHwnd,
        [Parameter(Mandatory)] [int] $CommandId
    )

    $messageResult = [IntPtr]::Zero
    $sendResult = [DesktopGrass.Smoke.Win32]::SendMessageTimeoutW(
        $MsgHwnd,
        $wmCommand,
        [IntPtr]$CommandId,
        [IntPtr]::Zero,
        0x0002,
        2000,
        [ref]$messageResult
    )
    if ($sendResult -eq [IntPtr]::Zero) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw (
            "SendMessageTimeoutW(WM_COMMAND) failed (Win32 error $errorCode)."
        )
    }
}

function Set-ProductionScene {
    param(
        [Parameter(Mandatory)] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] [IntPtr] $MsgHwnd,
        [Parameter(Mandatory)] [int] $CommandId,
        [Parameter(Mandatory)] [string] $SceneName
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($WindowTimeoutSeconds)
    $lastError = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($Process.HasExited) {
            throw (
                "Production app exited before scene '$SceneName' persisted."
            )
        }
        Send-SceneCommand -MsgHwnd $MsgHwnd -CommandId $CommandId
        try {
            Wait-ForPersistedScene `
                -StateFilePath $StateFilePath `
                -ExpectedScene $SceneName `
                -TimeoutSeconds 2 | Out-Null
            return
        } catch {
            $lastError = $_.Exception.Message
        }
    }
    throw (
        "Unable to select production scene '$SceneName' within " +
        "${WindowTimeoutSeconds}s. Last observation: $lastError"
    )
}

function Get-VirtualScreenBounds {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    return [Windows.Forms.SystemInformation]::VirtualScreen
}

function Start-PresentMonCapture {
    param(
        [AllowNull()] [string] $ToolPath,
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [string] $OutputPath,
        [Parameter(Mandatory)] [int] $CaptureDurationSec,
        [Parameter(Mandatory)] [string] $Label
    )

    $stdoutPath = "$OutputPath.stdout.log"
    $stderrPath = "$OutputPath.stderr.log"
    if (-not $ToolPath -or -not (Test-Path -LiteralPath $ToolPath)) {
        return [pscustomobject]@{
            Label = $Label
            TargetProcessId = $ProcessId
            DurationSec = $CaptureDurationSec
            Status = 'unavailable'
            Reason = 'PresentMon executable was not supplied or was not found.'
            Process = $null
            OutputPath = $OutputPath
            StdoutPath = $stdoutPath
            StderrPath = $stderrPath
            StartUtc = $null
            EndUtc = $null
            Arguments = @()
            ExitCode = $null
        }
    }

    $sessionName = 'DesktopGrassQualification-' +
        [Guid]::NewGuid().ToString('N')
    $arguments = @(
        '--process_id', "$ProcessId",
        '--output_file', "`"$OutputPath`"",
        '--date_time',
        '--no_console_stats',
        '--session_name', $sessionName,
        '--timed', "$CaptureDurationSec",
        '--terminate_after_timed'
    )
    $startUtc = [DateTime]::UtcNow
    try {
        $presentProcess = Start-Process `
            -FilePath $ToolPath `
            -ArgumentList $arguments `
            -PassThru `
            -WindowStyle Hidden `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath
        return [pscustomobject]@{
            Label = $Label
            TargetProcessId = $ProcessId
            DurationSec = $CaptureDurationSec
            Status = 'running'
            Reason = $null
            Process = $presentProcess
            OutputPath = $OutputPath
            StdoutPath = $stdoutPath
            StderrPath = $stderrPath
            StartUtc = $startUtc
            EndUtc = $null
            Arguments = $arguments
            ExitCode = $null
        }
    } catch {
        return [pscustomobject]@{
            Label = $Label
            TargetProcessId = $ProcessId
            DurationSec = $CaptureDurationSec
            Status = 'error'
            Reason = $_.Exception.Message
            Process = $null
            OutputPath = $OutputPath
            StdoutPath = $stdoutPath
            StderrPath = $stderrPath
            StartUtc = $startUtc
            EndUtc = [DateTime]::UtcNow
            Arguments = $arguments
            ExitCode = $null
        }
    }
}

function Complete-PresentMonCapture {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Capture,
        [switch] $Abort
    )

    if ($null -eq $Capture.Process) {
        return $Capture
    }
    $presentProcess = $Capture.Process
    try {
        if ($Abort -and -not $presentProcess.HasExited) {
            $presentProcess.Kill()
            $presentProcess.WaitForExit()
            $Capture.Status = 'aborted'
            $Capture.Reason = 'Capture stopped because the cell became invalid.'
        } elseif (-not $presentProcess.WaitForExit(
            [int](($Capture.DurationSec + 20) * 1000)
        )) {
            $presentProcess.Kill()
            $presentProcess.WaitForExit()
            $Capture.Status = 'error'
            $Capture.Reason = 'PresentMon did not exit after its timed capture.'
        } else {
            # Ensure redirected output is flushed before reading logs/ExitCode.
            $presentProcess.WaitForExit()
            if ($presentProcess.ExitCode -eq 0 -and
                (Test-Path -LiteralPath $Capture.OutputPath)) {
                $Capture.Status = 'available'
                $Capture.Reason = $null
            } else {
                $Capture.Status = 'error'
                $Capture.Reason = (
                    "PresentMon exited $($presentProcess.ExitCode); output exists=" +
                    "$(Test-Path -LiteralPath $Capture.OutputPath)."
                )
            }
        }
        if ($presentProcess.HasExited) {
            $Capture.ExitCode = $presentProcess.ExitCode
        }
        $Capture.EndUtc = [DateTime]::UtcNow
        return $Capture
    } finally {
        $Capture.Process = $null
        $presentProcess.Dispose()
    }
}

function ConvertTo-CaptureManifest {
    param([Parameter(Mandatory)] [pscustomobject] $Capture)

    return [ordered]@{
        label = $Capture.Label
        target_process_id = $Capture.TargetProcessId
        duration_sec = $Capture.DurationSec
        status = $Capture.Status
        reason = $Capture.Reason
        output_path = $Capture.OutputPath
        stdout_path = $Capture.StdoutPath
        stderr_path = $Capture.StderrPath
        start_utc = if ($null -ne $Capture.StartUtc) {
            $Capture.StartUtc.ToUniversalTime().ToString('o')
        } else {
            $null
        }
        end_utc = if ($null -ne $Capture.EndUtc) {
            $Capture.EndUtc.ToUniversalTime().ToString('o')
        } else {
            $null
        }
        arguments = $Capture.Arguments
        exit_code = $Capture.ExitCode
    }
}

function New-TelemetryPrime {
    param(
        [AllowNull()] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] $CounterSampler
    )

    $targetProcessId = if ($null -ne $Process) { $Process.Id } else { 0 }
    $liveProcess = if ($null -ne $Process) {
        $Process.Refresh()
        $Process
    } else {
        $null
    }
    [void](Get-BenchmarkCounterSample `
        -Sampler $CounterSampler `
        -ProcessId $targetProcessId)
    return [pscustomobject]@{
        Cpu = if ($null -ne $liveProcess) {
            $liveProcess.TotalProcessorTime
        } else {
            $null
        }
        TimeUtc = [DateTime]::UtcNow
    }
}

function Get-TargetProcessAbsence {
    param([Parameter(Mandatory)] [string] $ProcessName)

    $existing = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    return [pscustomobject]@{
        absent = $existing.Count -eq 0
        process_count = $existing.Count
        process_ids = @($existing | ForEach-Object { $_.Id })
        reason = if ($existing.Count -eq 0) {
            $null
        } else {
            "$ProcessName appeared with PID(s): " +
                (($existing | ForEach-Object { $_.Id }) -join ', ')
        }
    }
}

function Invoke-TelemetryCapture {
    param(
        [AllowNull()] [Diagnostics.Process] $Process,
        [Parameter(Mandatory)] $CounterSampler,
        [Parameter(Mandatory)] [pscustomobject] $Prime,
        [Parameter(Mandatory)] [pscustomobject] $PowerBaseline,
        [Parameter(Mandatory)] [pscustomobject] $SessionBaseline,
        [AllowNull()] [string] $AbsentProcessName
    )

    $samples = [Collections.Generic.List[object]]::new()
    $gpuSamples = [Collections.Generic.List[object]]::new()
    $powerSamples = [Collections.Generic.List[object]]::new()
    $energySamples = [Collections.Generic.List[object]]::new()
    $throttleSamples = [Collections.Generic.List[object]]::new()
    $contextReasons = [Collections.Generic.List[string]]::new()
    $sessionReasons = [Collections.Generic.List[string]]::new()
    $processAbsenceReasons = [Collections.Generic.List[string]]::new()
    $expectedSamples = [int][Math]::Floor(
        $DurationSec / $SampleIntervalSec
    )
    $targetProcessId = if ($null -ne $Process) { $Process.Id } else { 0 }
    $startUtc = [DateTime]::UtcNow
    $previousCpu = $Prime.Cpu
    $previousTime = $Prime.TimeUtc
    $lastPower = $PowerBaseline
    $lastSession = $SessionBaseline

    for ($sampleIndex = 0; $sampleIndex -lt $expectedSamples; $sampleIndex++) {
        $targetUtc = $startUtc.AddSeconds(
            ($sampleIndex + 1) * $SampleIntervalSec
        )
        $remainingMs = [Math]::Ceiling(
            ($targetUtc - [DateTime]::UtcNow).TotalMilliseconds
        )
        if ($remainingMs -gt 0) {
            Start-Sleep -Milliseconds ([int]$remainingMs)
        }

        if ($null -ne $Process -and $Process.HasExited) {
            throw (
                "Production app exited unexpectedly (code=$($Process.ExitCode))."
            )
        }
        $now = [DateTime]::UtcNow
        $liveProcess = if ($null -ne $Process) {
            $Process.Refresh()
            $Process
        } else {
            $null
        }
        $counterSample = Get-BenchmarkCounterSample `
            -Sampler $CounterSampler `
            -ProcessId $targetProcessId
        $powerSample = Get-BenchmarkPowerSnapshot
        $powerMatch = Test-PowerSnapshot `
            -Baseline $PowerBaseline `
            -Sample $powerSample
        if (-not $powerMatch.valid) {
            $contextReasons.Add(
                "sample $sampleIndex`: $($powerMatch.reason)"
            )
        }
        $lastPower = $powerSample
        $sessionSample = Get-InteractiveSessionState
        $sessionMatch = Test-SessionSnapshot `
            -Baseline $SessionBaseline `
            -Sample $sessionSample
        if (-not $sessionMatch.valid) {
            $sessionReasons.Add(
                "sample $sampleIndex`: $($sessionMatch.reason)"
            )
        }
        $lastSession = $sessionSample
        $processAbsenceSample = if ($AbsentProcessName) {
            Get-TargetProcessAbsence -ProcessName $AbsentProcessName
        } else {
            $null
        }
        if ($null -ne $processAbsenceSample -and
            -not $processAbsenceSample.absent) {
            $processAbsenceReasons.Add(
                "sample $sampleIndex`: $($processAbsenceSample.reason)"
            )
        }

        $cpuCorePct = $null
        if ($null -ne $liveProcess -and $null -ne $previousCpu) {
            $intervalSec = ($now - $previousTime).TotalSeconds
            if ($intervalSec -gt 0) {
                $cpuCorePct = (
                    ($liveProcess.TotalProcessorTime - $previousCpu).TotalSeconds /
                    $intervalSec
                ) * 100.0
            }
        }
        $throttleSample = if ($null -ne $Process) {
            Get-ProcessPowerThrottlingState -ProcessId $Process.Id
        } else {
            $null
        }

        $samples.Add([pscustomobject]@{
            sample_index = $sampleIndex
            sample_measured = $true
            t_sec = ($now - $startUtc).TotalSeconds
            sample_utc = $now.ToString('o')
            cpu_core_pct = $cpuCorePct
            working_set_mb = if ($null -ne $liveProcess) {
                [Math]::Round($liveProcess.WorkingSet64 / 1MB, 3)
            } else {
                $null
            }
            private_mb = if ($null -ne $liveProcess) {
                [Math]::Round($liveProcess.PrivateMemorySize64 / 1MB, 3)
            } else {
                $null
            }
            threads = if ($null -ne $liveProcess) {
                $liveProcess.Threads.Count
            } else {
                $null
            }
            handles = if ($null -ne $liveProcess) {
                $liveProcess.HandleCount
            } else {
                $null
            }
            gpu_busiest_engine_pct = if ($null -ne $Process) {
                $counterSample.gpu_busiest_engine_pct
            } else {
                $null
            }
            process_context_switches_per_sec = if ($null -ne $Process) {
                $counterSample.process_context_switches_per_sec
            } else {
                $null
            }
            system_context_switches_per_sec =
                $counterSample.system_context_switches_per_sec
            system_interrupts_per_sec =
                $counterSample.system_interrupts_per_sec
            system_dpc_rate = $counterSample.system_dpc_rate
            processor_queue_length = $counterSample.processor_queue_length
            gpu_status = if ($null -ne $Process) {
                $counterSample.gpu_status
            } else {
                'not_applicable'
            }
            context_switch_status = if ($null -ne $Process) {
                $counterSample.context_switch_status
            } else {
                'not_applicable'
            }
            energy_status = $counterSample.energy_meter_status
            counter_status = $counterSample.status
        })

        foreach ($gpuRow in $counterSample.gpu_engines) {
            if ($null -eq $Process) {
                break
            }
            $gpuSamples.Add([pscustomobject]@{
                sample_index = $sampleIndex
                t_sec = ($now - $startUtc).TotalSeconds
                sample_utc = $now.ToString('o')
                pid = $gpuRow.pid
                instance_name = $gpuRow.instance_name
                adapter_luid = $gpuRow.adapter_luid
                physical_adapter = $gpuRow.physical_adapter
                engine_index = $gpuRow.engine_index
                engine_type = $gpuRow.engine_type
                utilization_pct = $gpuRow.utilization_pct
                counter_status = $gpuRow.counter_status
            })
        }
        foreach ($energyRow in $counterSample.energy_meters) {
            $energySamples.Add([pscustomobject]@{
                sample_index = $sampleIndex
                t_sec = ($now - $startUtc).TotalSeconds
                sample_utc = $now.ToString('o')
                meter = $energyRow.meter
                power_mw = $energyRow.power_mw
                counter_status = $energyRow.counter_status
            })
        }

        $powerSamples.Add([pscustomobject]@{
            sample_index = $sampleIndex
            sample_measured = $true
            t_sec = ($now - $startUtc).TotalSeconds
            sample_utc = $now.ToString('o')
            ac_line_status = $powerSample.ac_line_status
            battery_present = $powerSample.battery_present
            battery_percent = $powerSample.battery_percent
            battery_saver = $powerSample.battery_saver
            active_power_scheme_guid =
                $powerSample.active_power_scheme_guid
            battery_telemetry_status =
                $powerSample.battery_telemetry_status
            battery_remaining_capacity_mwh =
                $powerSample.battery_remaining_capacity_mwh
            battery_discharge_rate_mw =
                $powerSample.battery_discharge_rate_mw
            power_status = $powerSample.status
            context_matches = $powerMatch.valid
            context_reason = $powerMatch.reason
            session_status = $sessionSample.status
            session_id = $sessionSample.session_id
            session_connect_state = $sessionSample.connect_state
            session_lock_state = $sessionSample.lock_state
            session_context_matches = $sessionMatch.valid
            session_context_reason = $sessionMatch.reason
            target_process_absent = if ($null -ne $processAbsenceSample) {
                $processAbsenceSample.absent
            } else {
                $null
            }
            target_process_presence_reason = if (
                $null -ne $processAbsenceSample
            ) {
                $processAbsenceSample.reason
            } else {
                $null
            }
        })

        if ($null -ne $throttleSample) {
            $throttleSamples.Add([pscustomobject]@{
                sample_index = $sampleIndex
                sample_measured = $true
                t_sec = ($now - $startUtc).TotalSeconds
                sample_utc = $now.ToString('o')
                status = $throttleSample.status
                reason = $throttleSample.reason
                execution_speed_throttled =
                    $throttleSample.execution_speed_throttled
                control_mask = $throttleSample.control_mask
                state_mask = $throttleSample.state_mask
            })
        }

        if ($null -ne $liveProcess) {
            $previousCpu = $liveProcess.TotalProcessorTime
        }
        $previousTime = $now
        if (-not $powerMatch.valid -or
            -not $sessionMatch.valid -or
            ($null -ne $processAbsenceSample -and
                -not $processAbsenceSample.absent)) {
            break
        }
    }

    return [pscustomobject]@{
        Samples = $samples
        GpuSamples = $gpuSamples
        PowerSamples = $powerSamples
        EnergySamples = $energySamples
        ThrottleSamples = $throttleSamples
        ExpectedSamples = $expectedSamples
        StartUtc = $startUtc
        EndUtc = [DateTime]::UtcNow
        PowerContextValid = $contextReasons.Count -eq 0
        PowerContextReason = if ($contextReasons.Count -eq 0) {
            $null
        } else {
            $contextReasons -join ' | '
        }
        PowerContextEnd = $lastPower
        SessionContextValid = $sessionReasons.Count -eq 0
        SessionContextReason = if ($sessionReasons.Count -eq 0) {
            $null
        } else {
            $sessionReasons -join ' | '
        }
        SessionContextEnd = $lastSession
        ProcessAbsenceValid = $processAbsenceReasons.Count -eq 0
        ProcessAbsenceReason = if ($processAbsenceReasons.Count -eq 0) {
            $null
        } else {
            $processAbsenceReasons -join ' | '
        }
    }
}

function Write-TelemetryFiles {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Telemetry,
        [Parameter(Mandatory)] [string] $SampleCsv,
        [Parameter(Mandatory)] [string] $GpuCsv,
        [Parameter(Mandatory)] [string] $PowerCsv,
        [Parameter(Mandatory)] [string] $EnergyCsv,
        [AllowNull()] [string] $ThrottleCsv
    )

    $sampleColumns = @(
        'sample_index', 'sample_measured', 't_sec', 'sample_utc',
        'cpu_core_pct', 'working_set_mb', 'private_mb', 'threads', 'handles',
        'gpu_busiest_engine_pct', 'process_context_switches_per_sec',
        'system_context_switches_per_sec', 'system_interrupts_per_sec',
        'system_dpc_rate', 'processor_queue_length', 'gpu_status',
        'context_switch_status', 'energy_status', 'counter_status'
    )
    $gpuColumns = @(
        'sample_index', 't_sec', 'sample_utc', 'pid', 'instance_name',
        'adapter_luid', 'physical_adapter', 'engine_index', 'engine_type',
        'utilization_pct', 'counter_status'
    )
    $powerColumns = @(
        'sample_index', 'sample_measured', 't_sec', 'sample_utc',
        'ac_line_status', 'battery_present', 'battery_percent',
        'battery_saver', 'active_power_scheme_guid',
        'battery_telemetry_status', 'battery_remaining_capacity_mwh',
        'battery_discharge_rate_mw', 'power_status', 'context_matches',
        'context_reason', 'session_status', 'session_id',
        'session_connect_state', 'session_lock_state',
        'session_context_matches', 'session_context_reason',
        'target_process_absent', 'target_process_presence_reason'
    )
    $energyColumns = @(
        'sample_index', 't_sec', 'sample_utc', 'meter', 'power_mw',
        'counter_status'
    )
    $throttleColumns = @(
        'sample_index', 'sample_measured', 't_sec', 'sample_utc',
        'status', 'reason', 'execution_speed_throttled', 'control_mask',
        'state_mask'
    )
    Write-CsvWithHeader `
        -Path $SampleCsv `
        -Columns $sampleColumns `
        -Rows $Telemetry.Samples
    Write-CsvWithHeader `
        -Path $GpuCsv `
        -Columns $gpuColumns `
        -Rows $Telemetry.GpuSamples
    Write-CsvWithHeader `
        -Path $PowerCsv `
        -Columns $powerColumns `
        -Rows $Telemetry.PowerSamples
    Write-CsvWithHeader `
        -Path $EnergyCsv `
        -Columns $energyColumns `
        -Rows $Telemetry.EnergySamples
    if ($ThrottleCsv) {
        Write-CsvWithHeader `
            -Path $ThrottleCsv `
            -Columns $throttleColumns `
            -Rows $Telemetry.ThrottleSamples
    }
}

function Write-RuntimeManifest {
    param(
        [Parameter(Mandatory)] [Collections.IDictionary] $Document,
        [Parameter(Mandatory)] [string] $Path
    )

    $Document['UpdatedUtc'] = [DateTime]::UtcNow.ToString('o')
    $Document |
        ConvertTo-Json -Depth 16 |
        Out-File -LiteralPath $Path -Encoding utf8
}

$counterSampler = New-BenchmarkCounterSampler
$stateBackup = $null
$autoStartBefore = $null
$process = $null
$grassHwnd = [IntPtr]::Zero
$probeHwnds = @()
$activePresentCapture = $null
$cleanupErrors = [Collections.Generic.List[string]]::new()
$runError = $null
$resultDir = $null
$manifestPath = $null
$manifestDocument = $null
$abortReason = $null
$presentCaptureLeadInSec = 1
$presentCaptureTailSec = 1

try {
    $initialSession = Get-InteractiveSessionState
    $initialSessionCheck = Test-SessionSnapshot `
        -Baseline $initialSession `
        -Sample $initialSession
    if (-not $initialSessionCheck.valid) {
        throw (
            'Interactive session must be active and unlocked: ' +
            $initialSessionCheck.reason
        )
    }

    $initialPower = Get-BenchmarkPowerSnapshot
    $initialPowerCheck = Test-PowerSnapshot `
        -Baseline $initialPower `
        -Sample $initialPower
    if (-not $initialPowerCheck.valid) {
        throw (
            'Initial power context cannot be qualified: ' +
            $initialPowerCheck.reason
        )
    }

    $machineFingerprint = Get-RuntimeMachineFingerprint
    if ($machineFingerprint.status -ne 'available') {
        throw "Machine fingerprint unavailable: $($machineFingerprint.reason)"
    }
    $displayContext = Get-RuntimeDisplayContext
    if ($displayContext.status -ne 'available') {
        throw "Display context unavailable: $($displayContext.reason)"
    }

    $captureId = [Guid]::NewGuid().ToString('N')
    $normalizedResultsRoot = [IO.Path]::GetFullPath(
        $ResultsRoot
    ).TrimEnd('\').ToLowerInvariant()
    $qualificationSetId = Get-StringSha256 $normalizedResultsRoot
    $exeHash = (
        Get-FileHash -LiteralPath $Exe -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $configHash = (
        Get-FileHash -LiteralPath $ConfigFilePath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $expectedSampleCount = [int][Math]::Floor(
        $DurationSec / $SampleIntervalSec
    )

    $stamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH-mm-ssZ')
    $resultDir = Join-Path $ResultsRoot (
        "$stamp-$Scenario-$($captureId.Substring(0, 8))"
    )
    New-Item -ItemType Directory -Force -Path $resultDir | Out-Null
    $manifestPath = Join-Path $resultDir 'manifest.json'
    Write-Host "== Results dir: $resultDir ==" -ForegroundColor Cyan

    $sweepParameters = [ordered]@{
        scenario = $Scenario
        duration_sec = $DurationSec
        sample_interval_sec = $SampleIntervalSec
        expected_sample_count = $expectedSampleCount
        warmup_sec = $WarmupSec
        probe_settle_sec = $ProbeSettleSec
        present_control_duration_sec = $PresentControlDurationSec
        present_capture_lead_in_sec = $presentCaptureLeadInSec
        present_capture_tail_sec = $presentCaptureTailSec
        required_target_fps = $RequiredTargetFps
        seed = $Seed
        scenes = $Scenes
        runs = $Runs
        expected_power_source = $ExpectedPowerSource
        expected_battery_saver = $ExpectedBatterySaver
        present_mon_exe = $PresentMonExe
        production_entry_point = 'App (no --benchmark)'
    }
    $machine = Get-BenchmarkMachineContext `
        -Exe $Exe `
        -Platform $Platform `
        -CounterSampler $counterSampler `
        -SweepParameters $sweepParameters
    $machine['runtime_qualification'] = [ordered]@{
        issue = 14
        capture_id = $captureId
        qualification_set_id = $qualificationSetId
        machine_fingerprint = $machineFingerprint
        display_context = $displayContext
        interactive_session_context = $initialSession
        exe_sha256 = $exeHash
        config_sha256 = $configHash
        state_file_path = $StateFilePath
        config_file_path = $ConfigFilePath
        present_mon_available = [bool](
            $PresentMonExe -and (Test-Path -LiteralPath $PresentMonExe)
        )
    }
    $machine |
        ConvertTo-Json -Depth 12 |
        Out-File `
            -LiteralPath (Join-Path $resultDir 'machine.json') `
            -Encoding utf8

    $stateBackup = Backup-QualificationStateFile -Path $StateFilePath
    $autoStartBefore = Get-AutoStartRegistryValue
    if (-not $isNoAppControl) {
        Assert-AutoStartLaunchSafe `
            -StateFilePath $StateFilePath `
            -ExePath $Exe `
            -RegistryValue $autoStartBefore | Out-Null
    }

    $sceneOrder = if ($isNoAppControl) {
        @()
    } else {
        Get-QualificationSceneOrder -Scenes $Scenes -Seed $Seed
    }
    $cells = [Collections.Generic.List[object]]::new()
    $manifestDocument = [ordered]@{
        QualificationSchemaVersion = 2
        BenchmarkSchemaVersion = Get-BenchmarkSchemaVersion
        CaptureId = $captureId
        QualificationSetId = $qualificationSetId
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        UpdatedUtc = $null
        CompletedUtc = $null
        Aborted = $false
        AbortReason = $null
        Scenario = $Scenario
        SceneOrder = $sceneOrder
        Seed = $Seed
        Runs = $Runs
        DurationSec = $DurationSec
        SampleIntervalSec = $SampleIntervalSec
        ExpectedSampleCount = $expectedSampleCount
        WarmupSec = $WarmupSec
        ProbeSettleSec = $ProbeSettleSec
        PresentControlDurationSec = $PresentControlDurationSec
        PresentCaptureLeadInSec = $presentCaptureLeadInSec
        PresentCaptureTailSec = $presentCaptureTailSec
        RequiredTargetFps = $RequiredTargetFps
        Platform = $Platform
        ProductionEntryPoint = 'App (no --benchmark)'
        ExecutablePath = $Exe
        ExecutableSha256 = $exeHash
        ConfigPath = $ConfigFilePath
        ConfigSha256 = $configHash
        MachineFingerprint = $machineFingerprint
        DisplayContext = $displayContext
        InteractiveSessionContext = $initialSession
        PresentMon = [ordered]@{
            path = $PresentMonExe
            available = [bool](
                $PresentMonExe -and
                (Test-Path -LiteralPath $PresentMonExe)
            )
            version = if (
                $PresentMonExe -and
                (Test-Path -LiteralPath $PresentMonExe)
            ) {
                (Get-Item -LiteralPath $PresentMonExe).VersionInfo.FileVersion
            } else {
                $null
            }
        }
        Cells = $cells
    }
    Write-RuntimeManifest `
        -Document $manifestDocument `
        -Path $manifestPath

    $cellNumber = 0
    $totalCells = if ($isNoAppControl) {
        $Runs
    } else {
        $sceneOrder.Count * $Runs
    }

    if (-not $isNoAppControl) {
        Write-Host (
            "Deterministic scene order (seed=$Seed): " +
            (($sceneOrder | ForEach-Object { $sceneNames[$_] }) -join ', ')
        ) -ForegroundColor Cyan

        $process = Start-AppForSmoke -ExePath $Exe
        $msgHwnd = Wait-ForMessageOnlyWindow `
            -Process $process `
            -ClassName $msgWindowClass `
            -Title $msgWindowTitle `
            -TimeoutSeconds $WindowTimeoutSeconds
        $grassHwnd = Wait-ForWindow `
            -Process $process `
            -ClassName $windowClass `
            -TimeoutSeconds $WindowTimeoutSeconds
        [IntPtr[]]$grassWindows = Get-GrassWindows -Process $process
        if ($grassWindows.Count -eq 0) {
            throw 'Production app created no grass surfaces.'
        }
        $virtualScreen = Get-VirtualScreenBounds

        :sceneLoop foreach ($scene in $sceneOrder) {
            $sceneName = $sceneNames[$scene]
            for ($run = 1; $run -le $Runs; $run++) {
                $cellNumber++
                $powerBefore = Get-BenchmarkPowerSnapshot
                $preCellPower = Test-PowerSnapshot `
                    -Baseline $initialPower `
                    -Sample $powerBefore
                if (-not $preCellPower.valid) {
                    $abortReason = (
                        "Power context changed before cell $cellNumber`: " +
                        $preCellPower.reason
                    )
                    break sceneLoop
                }
                $sessionBefore = Get-InteractiveSessionState
                $preCellSession = Test-SessionSnapshot `
                    -Baseline $initialSession `
                    -Sample $sessionBefore
                if (-not $preCellSession.valid) {
                    $abortReason = (
                        "Session context changed before cell $cellNumber`: " +
                        $preCellSession.reason
                    )
                    break sceneLoop
                }
                $powerStateKey = Get-PowerStateKey $powerBefore
                $cellTag = (
                    'scene{0}-{1}-{2}-{3}-run{4}' -f
                        $scene,
                        $sceneName.ToLowerInvariant(),
                        $Scenario,
                        $powerStateKey,
                        $run
                )
                Write-Host (
                    "[$cellNumber/$totalCells] $cellTag"
                ) -ForegroundColor Yellow

                Set-ProductionScene `
                    -Process $process `
                    -MsgHwnd $msgHwnd `
                    -CommandId $sceneCommandIds[$scene] `
                    -SceneName $sceneName
                if ($WarmupSec -gt 0) {
                    Start-Sleep -Seconds $WarmupSec
                }

                [IntPtr[]]$grassWindows = Get-GrassWindows -Process $process
                if ($grassWindows.Count -eq 0) {
                    throw "No grass windows were found for $cellTag."
                }
                Wait-GrassWindowsVisible `
                    -Windows $grassWindows `
                    -Visible $true
                $visibleBefore = Get-GrassVisibility -Windows $grassWindows

                $sampleCsv = Join-Path $resultDir "$cellTag.samples.csv"
                $gpuCsv = Join-Path $resultDir "$cellTag.gpu.csv"
                $powerCsv = Join-Path $resultDir "$cellTag.power.csv"
                $energyCsv = Join-Path $resultDir "$cellTag.energy.csv"
                $throttleCsv = Join-Path $resultDir "$cellTag.throttling.csv"
                $presentCsv = Join-Path $resultDir "$cellTag.presentmon.csv"
                $beforeCsv = Join-Path $resultDir (
                    "$cellTag.control-before.presentmon.csv"
                )
                $afterCsv = Join-Path $resultDir (
                    "$cellTag.control-after.presentmon.csv"
                )

                $isSuppression = $Scenario -in @(
                    'fullscreen-suppression',
                    'occlusion-suppression'
                )
                $beforeCapture = [pscustomobject]@{
                    Label = 'visible-control-before'
                    TargetProcessId = $process.Id
                    DurationSec = 0
                    Status = 'not_applicable'
                    Reason = 'Visible scenario needs no paired control.'
                    Process = $null
                    OutputPath = $beforeCsv
                    StdoutPath = "$beforeCsv.stdout.log"
                    StderrPath = "$beforeCsv.stderr.log"
                    StartUtc = $null
                    EndUtc = $null
                    Arguments = @()
                    ExitCode = $null
                }
                $afterCapture = [pscustomobject]@{
                    Label = 'visible-control-after'
                    TargetProcessId = $process.Id
                    DurationSec = 0
                    Status = 'not_applicable'
                    Reason = 'Visible scenario needs no paired resume control.'
                    Process = $null
                    OutputPath = $afterCsv
                    StdoutPath = "$afterCsv.stdout.log"
                    StderrPath = "$afterCsv.stderr.log"
                    StartUtc = $null
                    EndUtc = $null
                    Arguments = @()
                    ExitCode = $null
                }
                $controlPowerValid = $true
                $controlPowerReasons =
                    [Collections.Generic.List[string]]::new()
                $controlSessionValid = $true
                $controlSessionReasons =
                    [Collections.Generic.List[string]]::new()

                if ($isSuppression) {
                    $activePresentCapture = Start-PresentMonCapture `
                        -ToolPath $PresentMonExe `
                        -ProcessId $process.Id `
                        -OutputPath $beforeCsv `
                        -CaptureDurationSec $PresentControlDurationSec `
                        -Label 'visible-control-before'
                    $beforeCapture = Complete-PresentMonCapture `
                        -Capture $activePresentCapture
                    $activePresentCapture = $null
                    $beforeControlPower = Get-BenchmarkPowerSnapshot
                    $beforeControlPowerCheck = Test-PowerSnapshot `
                        -Baseline $powerBefore `
                        -Sample $beforeControlPower
                    if (-not $beforeControlPowerCheck.valid) {
                        $controlPowerValid = $false
                        $controlPowerReasons.Add(
                            "before control: $($beforeControlPowerCheck.reason)"
                        )
                    }
                    $beforeControlSession = Get-InteractiveSessionState
                    $beforeControlSessionCheck = Test-SessionSnapshot `
                        -Baseline $sessionBefore `
                        -Sample $beforeControlSession
                    if (-not $beforeControlSessionCheck.valid) {
                        $controlSessionValid = $false
                        $controlSessionReasons.Add(
                            "before control: " +
                            $beforeControlSessionCheck.reason
                        )
                    }
                }

                $cellProbes = @()
                $probeMetadata = [Collections.Generic.List[object]]::new()
                $intendedVisible = $true
                try {
                    switch ($Scenario) {
                        'fullscreen-suppression' {
                            $probe = New-OpaqueProbeWindow `
                                -Title (
                                    'DesktopGrass Qualification Fullscreen Probe'
                                ) `
                                -Bounds $virtualScreen `
                                -Activate
                            $actualBounds = Get-WindowBounds -Hwnd $probe
                            if (-not (Test-BoundsEqual `
                                -Expected $virtualScreen `
                                -Actual $actualBounds)) {
                                throw 'Fullscreen probe bounds did not match.'
                            }
                            if (
                                [DesktopGrass.Smoke.Win32]::GetForegroundWindow() -ne
                                $probe
                            ) {
                                $activationSession =
                                    Get-InteractiveSessionState
                                $activationSessionCheck =
                                    Test-SessionSnapshot `
                                        -Baseline $sessionBefore `
                                        -Sample $activationSession
                                if (-not $activationSessionCheck.valid) {
                                    throw (
                                        'Interactive session changed before ' +
                                        'fullscreen activation: ' +
                                        $activationSessionCheck.reason
                                    )
                                }
                                throw (
                                    'Fullscreen probe could not become the ' +
                                    'foreground window.'
                                )
                            }
                            $cellProbes += $probe
                            $probeMetadata.Add([ordered]@{
                                hwnd = $probe.ToInt64()
                                bounds = $actualBounds
                                topmost = $false
                                foreground = $true
                            })
                            $intendedVisible = $false
                        }
                        'occlusion-suppression' {
                            foreach ($hwnd in $grassWindows) {
                                $expectedBounds = Get-WindowBounds -Hwnd $hwnd
                                $probe = New-OpaqueProbeWindow `
                                    -Title (
                                        'DesktopGrass Qualification Occlusion ' +
                                        "Probe $($hwnd.ToInt64())"
                                    ) `
                                    -Bounds $expectedBounds `
                                    -Topmost
                                $actualBounds = Get-WindowBounds -Hwnd $probe
                                if (-not (Test-BoundsEqual `
                                    -Expected $expectedBounds `
                                    -Actual $actualBounds)) {
                                    throw (
                                        "Occlusion probe bounds did not match " +
                                        "grass hwnd $($hwnd.ToInt64())."
                                    )
                                }
                                $cellProbes += $probe
                                $probeMetadata.Add([ordered]@{
                                    hwnd = $probe.ToInt64()
                                    target_hwnd = $hwnd.ToInt64()
                                    bounds = $actualBounds
                                    topmost = $true
                                    foreground = $false
                                })
                            }
                            $intendedVisible = $false
                        }
                    }
                    $probeHwnds = $cellProbes
                    Wait-GrassWindowsVisible `
                        -Windows $grassWindows `
                        -Visible $intendedVisible
                    if ($ProbeSettleSec -gt 0) {
                        Start-Sleep -Seconds $ProbeSettleSec
                    }
                    $visibilityDuring = Get-GrassVisibility `
                        -Windows $grassWindows
                    $displayStart = Get-RuntimeDisplayContext
                    $prime = New-TelemetryPrime `
                        -Process $process `
                        -CounterSampler $counterSampler
                    $activePresentCapture = Start-PresentMonCapture `
                        -ToolPath $PresentMonExe `
                        -ProcessId $process.Id `
                        -OutputPath $presentCsv `
                        -CaptureDurationSec (
                            $DurationSec +
                            $presentCaptureLeadInSec +
                            $presentCaptureTailSec
                        ) `
                        -Label 'measurement'
                    if ($activePresentCapture.Status -eq 'running') {
                        Start-Sleep -Seconds $presentCaptureLeadInSec
                    }
                    $telemetry = Invoke-TelemetryCapture `
                        -Process $process `
                        -CounterSampler $counterSampler `
                        -Prime $prime `
                        -PowerBaseline $powerBefore `
                        -SessionBaseline $sessionBefore
                    $measurementCapture = Complete-PresentMonCapture `
                        -Capture $activePresentCapture `
                        -Abort:(-not (
                            $telemetry.PowerContextValid -and
                            $telemetry.SessionContextValid
                        ))
                    $activePresentCapture = $null
                } finally {
                    foreach ($probe in $cellProbes) {
                        try {
                            Remove-ProbeWindow -Hwnd $probe
                        } catch {
                            $cleanupErrors.Add(
                                "probe $($probe.ToInt64()): " +
                                $_.Exception.Message
                            )
                        }
                    }
                    $cellProbes = @()
                    $probeHwnds = @()
                }

                $resumeWindowsVisible = $true
                try {
                    Wait-GrassWindowsVisible `
                        -Windows $grassWindows `
                        -Visible $true
                } catch {
                    $resumeWindowsVisible = $false
                }
                if ($resumeWindowsVisible -and $ProbeSettleSec -gt 0) {
                    Start-Sleep -Seconds $ProbeSettleSec
                }
                if ($isSuppression -and $resumeWindowsVisible) {
                    $activePresentCapture = Start-PresentMonCapture `
                        -ToolPath $PresentMonExe `
                        -ProcessId $process.Id `
                        -OutputPath $afterCsv `
                        -CaptureDurationSec $PresentControlDurationSec `
                        -Label 'visible-control-after'
                    $afterCapture = Complete-PresentMonCapture `
                        -Capture $activePresentCapture
                    $activePresentCapture = $null
                    $afterControlPower = Get-BenchmarkPowerSnapshot
                    $afterControlPowerCheck = Test-PowerSnapshot `
                        -Baseline $powerBefore `
                        -Sample $afterControlPower
                    if (-not $afterControlPowerCheck.valid) {
                        $controlPowerValid = $false
                        $controlPowerReasons.Add(
                            "after control: $($afterControlPowerCheck.reason)"
                        )
                    }
                    $afterControlSession = Get-InteractiveSessionState
                    $afterControlSessionCheck = Test-SessionSnapshot `
                        -Baseline $sessionBefore `
                        -Sample $afterControlSession
                    if (-not $afterControlSessionCheck.valid) {
                        $controlSessionValid = $false
                        $controlSessionReasons.Add(
                            "after control: " +
                            $afterControlSessionCheck.reason
                        )
                    }
                }

                $displayEnd = Get-RuntimeDisplayContext
                $displayContextValid = (
                    $displayStart.status -eq 'available' -and
                    $displayEnd.status -eq 'available' -and
                    $displayStart.hash -eq $displayEnd.hash -and
                    $displayStart.hash -eq $displayContext.hash
                )
                $finalPower = Get-BenchmarkPowerSnapshot
                $finalPowerCheck = Test-PowerSnapshot `
                    -Baseline $powerBefore `
                    -Sample $finalPower
                $finalSession = Get-InteractiveSessionState
                $finalSessionCheck = Test-SessionSnapshot `
                    -Baseline $sessionBefore `
                    -Sample $finalSession
                $powerContextValid = (
                    $telemetry.PowerContextValid -and
                    $controlPowerValid -and
                    $finalPowerCheck.valid
                )
                $powerContextReason = @(
                    $telemetry.PowerContextReason
                    if (-not $controlPowerValid) {
                        $controlPowerReasons -join ' | '
                    }
                    if (-not $finalPowerCheck.valid) {
                        "final: $($finalPowerCheck.reason)"
                    }
                ) | Where-Object { $_ }
                $sessionContextValid = (
                    $telemetry.SessionContextValid -and
                    $controlSessionValid -and
                    $finalSessionCheck.valid
                )
                $sessionContextReason = @(
                    $telemetry.SessionContextReason
                    if (-not $controlSessionValid) {
                        $controlSessionReasons -join ' | '
                    }
                    if (-not $finalSessionCheck.valid) {
                        "final: $($finalSessionCheck.reason)"
                    }
                ) | Where-Object { $_ }

                Write-TelemetryFiles `
                    -Telemetry $telemetry `
                    -SampleCsv $sampleCsv `
                    -GpuCsv $gpuCsv `
                    -PowerCsv $powerCsv `
                    -EnergyCsv $energyCsv `
                    -ThrottleCsv $throttleCsv

                $entry = [pscustomobject]@{
                    SchemaVersion = Get-BenchmarkSchemaVersion
                    CaptureId = $captureId
                    RunId = "$captureId/$run"
                    CellTag = $cellTag
                    Scene = $scene
                    SceneName = $sceneName
                    Scenario = $Scenario
                    PowerState = $powerStateKey
                    Run = $run
                    DurationSec = $DurationSec
                    SampleIntervalSec = $SampleIntervalSec
                    ExpectedSampleCount = $telemetry.ExpectedSamples
                    RequiredTargetFps = $RequiredTargetFps
                    SampleCsv = Split-Path $sampleCsv -Leaf
                    GpuCsv = Split-Path $gpuCsv -Leaf
                    PowerCsv = Split-Path $powerCsv -Leaf
                    EnergyCsv = Split-Path $energyCsv -Leaf
                    ThrottleCsv = Split-Path $throttleCsv -Leaf
                    ProcessId = $process.Id
                    ExpectedVisibleWindowCount = $grassWindows.Count
                    IntendedGrassVisible = $intendedVisible
                    VisibilityBefore = $visibleBefore
                    VisibilityDuring = $visibilityDuring
                    ProbeWindows = $probeMetadata
                    ResumeWindowsVisible = $resumeWindowsVisible
                    MeasurementStartUtc =
                        $telemetry.StartUtc.ToString('o')
                    MeasurementEndUtc = $telemetry.EndUtc.ToString('o')
                    SampledWallSec = (
                        $telemetry.EndUtc - $telemetry.StartUtc
                    ).TotalSeconds
                    PresentCapture = ConvertTo-CaptureManifest `
                        -Capture $measurementCapture
                    VisibleControlBefore = ConvertTo-CaptureManifest `
                        -Capture $beforeCapture
                    VisibleControlAfter = ConvertTo-CaptureManifest `
                        -Capture $afterCapture
                    PowerContextStart = $powerBefore
                    PowerContextEnd = $finalPower
                    PowerContextValid = $powerContextValid
                    PowerContextReason = $powerContextReason -join ' | '
                    SessionContextStart = $sessionBefore
                    SessionContextEnd = $finalSession
                    SessionContextValid = $sessionContextValid
                    SessionContextReason = $sessionContextReason -join ' | '
                    NoAppProcessAbsent = $null
                    NoAppProcessAbsenceReason = $null
                    DisplayContextStart = $displayStart
                    DisplayContextEnd = $displayEnd
                    DisplayContextValid = $displayContextValid
                }
                $cells.Add($entry)
                Write-RuntimeManifest `
                    -Document $manifestDocument `
                    -Path $manifestPath

                if (-not $powerContextValid) {
                    $abortReason = (
                        "Power context changed during $cellTag`: " +
                        $entry.PowerContextReason
                    )
                    break sceneLoop
                }
                if (-not $sessionContextValid) {
                    $abortReason = (
                        "Session context changed during $cellTag`: " +
                        $entry.SessionContextReason
                    )
                    break sceneLoop
                }
                if (-not $displayContextValid) {
                    $abortReason = (
                        "Display context changed during $cellTag."
                    )
                    break sceneLoop
                }
            }
        }
    } else {
        for ($run = 1; $run -le $Runs; $run++) {
            $cellNumber++
            $powerBefore = Get-BenchmarkPowerSnapshot
            $preCellPower = Test-PowerSnapshot `
                -Baseline $initialPower `
                -Sample $powerBefore
            if (-not $preCellPower.valid) {
                $abortReason = (
                    "Power context changed before no-app run $run`: " +
                    $preCellPower.reason
                )
                break
            }
            $sessionBefore = Get-InteractiveSessionState
            $preCellSession = Test-SessionSnapshot `
                -Baseline $initialSession `
                -Sample $sessionBefore
            if (-not $preCellSession.valid) {
                $abortReason = (
                    "Session context changed before no-app run $run`: " +
                    $preCellSession.reason
                )
                break
            }
            $preCellAbsence = Get-TargetProcessAbsence `
                -ProcessName $targetProcessName
            if (-not $preCellAbsence.absent) {
                $abortReason = (
                    "Target process appeared before no-app run $run`: " +
                    $preCellAbsence.reason
                )
                break
            }
            $powerStateKey = Get-PowerStateKey $powerBefore
            $cellTag = "noapp-$powerStateKey-run$run"
            Write-Host (
                "[$cellNumber/$totalCells] $cellTag"
            ) -ForegroundColor Yellow

            $sampleCsv = Join-Path $resultDir "$cellTag.samples.csv"
            $gpuCsv = Join-Path $resultDir "$cellTag.gpu.csv"
            $powerCsv = Join-Path $resultDir "$cellTag.power.csv"
            $energyCsv = Join-Path $resultDir "$cellTag.energy.csv"
            $displayStart = Get-RuntimeDisplayContext
            $prime = New-TelemetryPrime `
                -Process $null `
                -CounterSampler $counterSampler
            $telemetry = Invoke-TelemetryCapture `
                -Process $null `
                -CounterSampler $counterSampler `
                -Prime $prime `
                -PowerBaseline $powerBefore `
                -SessionBaseline $sessionBefore `
                -AbsentProcessName $targetProcessName
            $displayEnd = Get-RuntimeDisplayContext
            $displayContextValid = (
                $displayStart.status -eq 'available' -and
                $displayEnd.status -eq 'available' -and
                $displayStart.hash -eq $displayEnd.hash -and
                $displayStart.hash -eq $displayContext.hash
            )
            $finalPower = Get-BenchmarkPowerSnapshot
            $finalPowerCheck = Test-PowerSnapshot `
                -Baseline $powerBefore `
                -Sample $finalPower
            $finalSession = Get-InteractiveSessionState
            $finalSessionCheck = Test-SessionSnapshot `
                -Baseline $sessionBefore `
                -Sample $finalSession
            $finalProcessAbsence = Get-TargetProcessAbsence `
                -ProcessName $targetProcessName
            $processAbsenceValid = (
                $telemetry.ProcessAbsenceValid -and
                $finalProcessAbsence.absent
            )
            $processAbsenceReason = @(
                $telemetry.ProcessAbsenceReason
                if (-not $finalProcessAbsence.absent) {
                    "final: $($finalProcessAbsence.reason)"
                }
            ) | Where-Object { $_ }
            $powerContextValid = (
                $telemetry.PowerContextValid -and
                $finalPowerCheck.valid
            )
            $powerContextReason = @(
                $telemetry.PowerContextReason
                if (-not $finalPowerCheck.valid) {
                    "final: $($finalPowerCheck.reason)"
                }
            ) | Where-Object { $_ }
            $sessionContextValid = (
                $telemetry.SessionContextValid -and
                $finalSessionCheck.valid
            )
            $sessionContextReason = @(
                $telemetry.SessionContextReason
                if (-not $finalSessionCheck.valid) {
                    "final: $($finalSessionCheck.reason)"
                }
            ) | Where-Object { $_ }

            Write-TelemetryFiles `
                -Telemetry $telemetry `
                -SampleCsv $sampleCsv `
                -GpuCsv $gpuCsv `
                -PowerCsv $powerCsv `
                -EnergyCsv $energyCsv `
                -ThrottleCsv $null

            $notApplicableCapture = [pscustomobject]@{
                Label = 'not-applicable'
                TargetProcessId = 0
                DurationSec = 0
                Status = 'not_applicable'
                Reason = 'no-app-control has no target process.'
                Process = $null
                OutputPath = $null
                StdoutPath = $null
                StderrPath = $null
                StartUtc = $null
                EndUtc = $null
                Arguments = @()
                ExitCode = $null
            }
            $entry = [pscustomobject]@{
                SchemaVersion = Get-BenchmarkSchemaVersion
                CaptureId = $captureId
                RunId = "$captureId/$run"
                CellTag = $cellTag
                Scene = $null
                SceneName = 'none'
                Scenario = $Scenario
                PowerState = $powerStateKey
                Run = $run
                DurationSec = $DurationSec
                SampleIntervalSec = $SampleIntervalSec
                ExpectedSampleCount = $telemetry.ExpectedSamples
                RequiredTargetFps = $RequiredTargetFps
                SampleCsv = Split-Path $sampleCsv -Leaf
                GpuCsv = Split-Path $gpuCsv -Leaf
                PowerCsv = Split-Path $powerCsv -Leaf
                EnergyCsv = Split-Path $energyCsv -Leaf
                ThrottleCsv = $null
                ProcessId = $null
                ExpectedVisibleWindowCount = 0
                IntendedGrassVisible = $null
                VisibilityBefore = $null
                VisibilityDuring = $null
                ProbeWindows = @()
                ResumeWindowsVisible = $null
                MeasurementStartUtc = $telemetry.StartUtc.ToString('o')
                MeasurementEndUtc = $telemetry.EndUtc.ToString('o')
                SampledWallSec = (
                    $telemetry.EndUtc - $telemetry.StartUtc
                ).TotalSeconds
                PresentCapture = ConvertTo-CaptureManifest `
                    -Capture $notApplicableCapture
                VisibleControlBefore = ConvertTo-CaptureManifest `
                    -Capture $notApplicableCapture
                VisibleControlAfter = ConvertTo-CaptureManifest `
                    -Capture $notApplicableCapture
                PowerContextStart = $powerBefore
                PowerContextEnd = $finalPower
                PowerContextValid = $powerContextValid
                PowerContextReason = $powerContextReason -join ' | '
                SessionContextStart = $sessionBefore
                SessionContextEnd = $finalSession
                SessionContextValid = $sessionContextValid
                SessionContextReason = $sessionContextReason -join ' | '
                NoAppProcessAbsent = $processAbsenceValid
                NoAppProcessAbsenceReason =
                    $processAbsenceReason -join ' | '
                DisplayContextStart = $displayStart
                DisplayContextEnd = $displayEnd
                DisplayContextValid = $displayContextValid
            }
            $cells.Add($entry)
            Write-RuntimeManifest `
                -Document $manifestDocument `
                -Path $manifestPath

            if (-not $powerContextValid) {
                $abortReason = (
                    "Power context changed during $cellTag`: " +
                    $entry.PowerContextReason
                )
                break
            }
            if (-not $sessionContextValid) {
                $abortReason = (
                    "Session context changed during $cellTag`: " +
                    $entry.SessionContextReason
                )
                break
            }
            if (-not $processAbsenceValid) {
                $abortReason = (
                    "Target process contaminated $cellTag`: " +
                    $entry.NoAppProcessAbsenceReason
                )
                break
            }
            if (-not $displayContextValid) {
                $abortReason = "Display context changed during $cellTag."
                break
            }
        }
    }

    $finalExeHash = (
        Get-FileHash -LiteralPath $Exe -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $finalConfigHash = (
        Get-FileHash -LiteralPath $ConfigFilePath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($finalExeHash -ne $exeHash) {
        $abortReason = 'Executable hash changed during the sweep.'
    }
    if ($finalConfigHash -ne $configHash) {
        $abortReason = 'Production config hash changed during the sweep.'
    }

    $manifestDocument['CompletedUtc'] = [DateTime]::UtcNow.ToString('o')
    $manifestDocument['Aborted'] = $null -ne $abortReason
    $manifestDocument['AbortReason'] = $abortReason
    Write-RuntimeManifest `
        -Document $manifestDocument `
        -Path $manifestPath

    if ($abortReason) {
        throw $abortReason
    }
    Write-Host ''
    Write-Host (
        "== Qualification sweep complete: $cellNumber cells =="
    ) -ForegroundColor Green
    Write-Host (
        'Aggregate the shared evidence root with: ' +
        "tools\benchmark\Aggregate-RuntimeQualification.ps1 " +
        "-ResultsRoot `"$ResultsRoot`""
    ) -ForegroundColor Cyan
} catch {
    $runError = $_
    if ($null -ne $manifestDocument -and $null -ne $manifestPath) {
        try {
            $manifestDocument['CompletedUtc'] = [DateTime]::UtcNow.ToString('o')
            $manifestDocument['Aborted'] = $true
            $manifestDocument['AbortReason'] = $_.Exception.Message
            Write-RuntimeManifest `
                -Document $manifestDocument `
                -Path $manifestPath
        } catch {
            $cleanupErrors.Add(
                "manifest finalization: $($_.Exception.Message)"
            )
        }
    }
} finally {
    if ($null -ne $activePresentCapture -and
        $null -ne $activePresentCapture.Process) {
        try {
            [void](Complete-PresentMonCapture `
                -Capture $activePresentCapture `
                -Abort)
        } catch {
            $cleanupErrors.Add(
                "PresentMon cleanup: $($_.Exception.Message)"
            )
        }
    }
    foreach ($probe in $probeHwnds) {
        try {
            Remove-ProbeWindow -Hwnd $probe
        } catch {
            $cleanupErrors.Add(
                "probe cleanup: $($_.Exception.Message)"
            )
        }
    }
    if ($null -ne $process -and -not $process.HasExited) {
        try {
            Stop-AppGracefully `
                -Process $process `
                -Hwnd $grassHwnd `
                -TimeoutSeconds 5
        } catch {
            $cleanupErrors.Add(
                "app shutdown: $($_.Exception.Message)"
            )
        }
    }
    if ($null -ne $process) {
        try {
            $process.Dispose()
        } catch {
            $cleanupErrors.Add(
                "process disposal: $($_.Exception.Message)"
            )
        }
    }
    if ($null -ne $stateBackup) {
        try {
            Restore-QualificationStateFile -Backup $stateBackup
        } catch {
            $cleanupErrors.Add(
                "state restoration: $($_.Exception.Message)"
            )
        }
    }
    if ($null -ne $autoStartBefore) {
        try {
            $autoStartAfter = Get-AutoStartRegistryValue
            $changed = Test-AutoStartChanged `
                -Before $autoStartBefore `
                -After $autoStartAfter
            if ($changed.changed) {
                Restore-AutoStartRegistryValue -Backup $autoStartBefore
            }
        } catch {
            $cleanupErrors.Add(
                "autostart restoration: $($_.Exception.Message)"
            )
        }
    }
    if ($counterSampler -and $counterSampler.query) {
        try {
            $counterSampler.query.Dispose()
        } catch {
            $cleanupErrors.Add(
                "counter sampler disposal: $($_.Exception.Message)"
            )
        }
    }
}

if ($cleanupErrors.Count -gt 0) {
    $cleanupMessage = $cleanupErrors -join ' | '
    if ($null -ne $runError) {
        throw (
            "$($runError.Exception.Message) Cleanup also failed: " +
            $cleanupMessage
        )
    }
    throw "Qualification cleanup failed: $cleanupMessage"
}
if ($null -ne $runError) {
    throw $runError
}
