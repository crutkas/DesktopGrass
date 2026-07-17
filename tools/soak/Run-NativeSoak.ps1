[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ResultsRoot,
    [ValidateRange(60, 604800)] [int] $DurationSec = 14400,
    [ValidateRange(1, 300)] [double] $SampleIntervalSec = 5,
    [ValidateRange(0, 3600)] [int] $WarmupSec = 60,
    [ValidateRange(0, 86400)] [int] $SceneIntervalSec = 120,
    [ValidateRange(0, 86400)] [int] $LifecycleIntervalSec = 900,
    [ValidateRange(0, 86400)] [int] $DeviceLossIntervalSec = 1800,
    [ValidateRange(0, 86400)] [int] $SleepResumeIntervalSec = 3600,
    [ValidateRange(0, 86400)] [int] $MonitorChurnIntervalSec = 3600,
    [ValidateRange(5, 3600)] [int] $SleepSeconds = 30,
    [ValidateRange(1, 600)] [int] $OperationTimeoutSec = 30,
    [ValidateRange(0, 4096)] [double] $MaxWorkingSetGrowthMB = 64,
    [ValidateRange(0, 4096)] [double] $MaxPrivateGrowthMB = 64,
    [ValidateRange(0, 100000)] [int] $MaxHandleGrowth = 100,
    [ValidateRange(0, 1000)] [int] $MaxThreadGrowth = 8,
    [ValidateRange(0, 10000)] [int] $MaxUserObjectGrowth = 20,
    [ValidateRange(0, 10000)] [int] $MaxGdiObjectGrowth = 20,
    [ValidateRange(0, 1000)] [double] $MaxMeanCpuCorePercent = 15,
    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = $(if (
        [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
            [Runtime.InteropServices.Architecture]::Arm64
    ) { 'ARM64' } else { 'x64' }),
    [string] $Exe,
    [string] $MonitorChurnScript,
    [string] $MonitorRestoreScript,
    [switch] $AllowSystemTransitions,
    [switch] $EnableSleepResume,
    [switch] $SkipDeviceLossTests,
    [switch] $DiagnosticRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Import-Module (Join-Path $PSScriptRoot 'Soak.Common.psm1') -Force
Import-Module (Join-Path $repoRoot 'tools\benchmark\Benchmark.Common.psm1') -Force
Import-Module (
    Join-Path $repoRoot 'tools\benchmark\RuntimeQualification.Common.psm1'
) -Force
Import-Module (Join-Path $repoRoot 'tests\smoke\Smoke.Common.psm1') -Force

$minimumQualifyingDurationSec = 14400
$windowClass = 'DesktopGrass.Native.Window'
$messageClass = 'DesktopGrass.Native.MessageWindow'
$messageTitle = 'DesktopGrass.Msg'
$sceneNames = @('Grass', 'Desert', 'Winter', 'Autumn', 'Ocean')
$sceneCommands = @(1010, 1011, 1012, 1013, 1014)
$processName = 'DesktopGrass.Native'
$stateFile = Join-Path $env:LOCALAPPDATA 'DesktopGrass\state.json'

if (-not $PSBoundParameters.ContainsKey('Exe')) {
    $Exe = Join-Path $repoRoot (
        "src\DesktopGrass.Native\out\$Platform\Release\DesktopGrass.Native.exe")
}
if (-not (Test-Path -LiteralPath $Exe)) {
    throw "Native executable not found at '$Exe'. Build Release first or pass -Exe."
}
$Exe = (Resolve-Path -LiteralPath $Exe).Path
if ([IO.Path]::GetFileName($Exe) -ine 'DesktopGrass.Native.exe') {
    throw 'The production executable must be named DesktopGrass.Native.exe.'
}
$ResultsRoot = [IO.Path]::GetFullPath($ResultsRoot)
Assert-ResultsRootOutsideRepo -ResultsRoot $ResultsRoot -RepoRoot $repoRoot
Assert-NoOtherNativeProcess -ProcessName $processName

$hasMonitorScripts = -not [string]::IsNullOrWhiteSpace($MonitorChurnScript) -and
    -not [string]::IsNullOrWhiteSpace($MonitorRestoreScript)
if (($MonitorChurnScript -or $MonitorRestoreScript) -and -not $hasMonitorScripts) {
    throw 'Monitor churn requires both -MonitorChurnScript and -MonitorRestoreScript.'
}
foreach ($scriptPath in @($MonitorChurnScript, $MonitorRestoreScript)) {
    if ($scriptPath -and -not (Test-Path -LiteralPath $scriptPath)) {
        throw "Monitor transition script not found: '$scriptPath'."
    }
}
if (($EnableSleepResume -or $hasMonitorScripts) -and -not $AllowSystemTransitions) {
    throw (
        'Sleep/resume and monitor churn are destructive system transitions. ' +
        'Pass -AllowSystemTransitions explicitly after reviewing the scripts and docs.')
}
if (-not $DiagnosticRun) {
    if ($DurationSec -lt $minimumQualifyingDurationSec) {
        throw (
            "A qualification run requires at least $minimumQualifyingDurationSec " +
            'seconds. Use -DiagnosticRun for shorter harness checks.')
    }
    if (-not $AllowSystemTransitions -or
        -not $EnableSleepResume -or
        -not $hasMonitorScripts) {
        throw (
            'A qualification run requires explicitly gated sleep/resume and ' +
            'monitor churn. Use -DiagnosticRun for partial coverage.')
    }
    if ($SkipDeviceLossTests) {
        throw 'A qualification run cannot use -SkipDeviceLossTests.'
    }
    $requiredIntervals = [ordered]@{
        SceneIntervalSec = $SceneIntervalSec
        LifecycleIntervalSec = $LifecycleIntervalSec
        DeviceLossIntervalSec = $DeviceLossIntervalSec
        SleepResumeIntervalSec = $SleepResumeIntervalSec
        MonitorChurnIntervalSec = $MonitorChurnIntervalSec
    }
    foreach ($requiredInterval in $requiredIntervals.GetEnumerator()) {
        if ([int]$requiredInterval.Value -le 0 -or
            [int]$requiredInterval.Value -ge $DurationSec) {
            throw (
                "$($requiredInterval.Key) must be greater than zero and less " +
                'than DurationSec for a qualification run.')
        }
    }
}
if ($SampleIntervalSec -gt $DurationSec) {
    throw 'SampleIntervalSec cannot exceed DurationSec.'
}
if ($WarmupSec -ge $DurationSec) {
    throw 'WarmupSec must be less than DurationSec.'
}

$nativeTestRunner = Join-Path $repoRoot 'tests\DesktopGrass.Native.Tests\Run-Tests.ps1'
$nativeTestDll = Join-Path $repoRoot (
    "tests\DesktopGrass.Native.Tests\out\$Platform\Release\" +
    'DesktopGrass.Native.Tests.dll')
if (-not $SkipDeviceLossTests -and -not (Test-Path -LiteralPath $nativeTestDll)) {
    throw (
        "Native test DLL not found at '$nativeTestDll'. Build Native tests first " +
        'or use -SkipDeviceLossTests only for a diagnostic run.')
}

$stamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH-mm-ssZ')
$runId = [Guid]::NewGuid().ToString('N')
$runDir = Join-Path $ResultsRoot "$stamp-native-soak-$($runId.Substring(0, 8))"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$eventsPath = Join-Path $runDir 'events.ndjson'
$samplesPath = Join-Path $runDir 'samples.csv'
$manifestPath = Join-Path $runDir 'manifest.json'
$summaryPath = Join-Path $runDir 'summary.json'
$failurePath = Join-Path $runDir 'failures.log'
$testResultDir = Join-Path $runDir 'device-loss'
New-Item -ItemType Directory -Force -Path $testResultDir | Out-Null

$thresholds = [ordered]@{
    max_working_set_growth_mb = $MaxWorkingSetGrowthMB
    max_private_growth_mb = $MaxPrivateGrowthMB
    max_handle_growth = $MaxHandleGrowth
    max_thread_growth = $MaxThreadGrowth
    max_user_object_growth = $MaxUserObjectGrowth
    max_gdi_object_growth = $MaxGdiObjectGrowth
    max_mean_cpu_core_pct = $MaxMeanCpuCorePercent
}
$initialDisplay = Get-RuntimeDisplayContext
$initialTopology = Get-SoakDisplayTopologyFingerprint `
    -DisplayContext $initialDisplay
$intervals = [ordered]@{
    scene = $SceneIntervalSec
    lifecycle = $LifecycleIntervalSec
    device_loss = if ($SkipDeviceLossTests) { 0 } else { $DeviceLossIntervalSec }
    sleep_resume = if ($EnableSleepResume) { $SleepResumeIntervalSec } else { 0 }
    monitor_churn = if ($hasMonitorScripts) { $MonitorChurnIntervalSec } else { 0 }
}
$schedule = Get-SoakSchedule -DurationSec $DurationSec -Intervals $intervals
$manifest = [ordered]@{
    schema_version = 1
    issue = 29
    run_id = $runId
    created_utc = [DateTime]::UtcNow.ToString('o')
    completed_utc = $null
    status = 'running'
    diagnostic_run = [bool]$DiagnosticRun
    minimum_qualifying_duration_sec = $minimumQualifyingDurationSec
    parameters = [ordered]@{
        duration_sec = $DurationSec
        sample_interval_sec = $SampleIntervalSec
        warmup_sec = $WarmupSec
        intervals_sec = $intervals
        sleep_seconds = $SleepSeconds
        operation_timeout_sec = $OperationTimeoutSec
        platform = $Platform
        executable = $Exe
        executable_sha256 = (
            Get-FileHash -LiteralPath $Exe -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        allow_system_transitions = [bool]$AllowSystemTransitions
        enable_sleep_resume = [bool]$EnableSleepResume
        monitor_churn_script = $MonitorChurnScript
        monitor_churn_script_sha256 = if ($MonitorChurnScript) {
            (Get-FileHash `
                -LiteralPath $MonitorChurnScript `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        } else {
            $null
        }
        monitor_restore_script = $MonitorRestoreScript
        monitor_restore_script_sha256 = if ($MonitorRestoreScript) {
            (Get-FileHash `
                -LiteralPath $MonitorRestoreScript `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        } else {
            $null
        }
        skip_device_loss_tests = [bool]$SkipDeviceLossTests
        device_loss_test_dll = if ($SkipDeviceLossTests) {
            $null
        } else {
            $nativeTestDll
        }
        device_loss_test_dll_sha256 = if ($SkipDeviceLossTests) {
            $null
        } else {
            (Get-FileHash `
                -LiteralPath $nativeTestDll `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    thresholds = $thresholds
    schedule = $schedule
    machine = Get-RuntimeMachineFingerprint
    initial_display = $initialDisplay
    initial_topology_fingerprint = $initialTopology
}
Write-SoakJson -Path $manifestPath -Value $manifest

$sampleColumns = @(
    'sample_index', 'utc', 'elapsed_sec', 'generation', 'pid', 'scene',
    'cpu_core_pct', 'working_set_mb', 'private_mb', 'handles', 'threads',
    'user_objects', 'gdi_objects', 'gpu_busiest_engine_pct',
    'process_context_switches_per_sec', 'grass_window_count',
    'expected_monitor_count', 'message_window_present', 'responsive',
    'topology_valid', 'competing_process_count', 'competing_process_ids',
    'healthy', 'failure'
)
Write-CsvWithHeader -Path $samplesPath -Columns $sampleColumns -Rows @()

function Add-Failure {
    param([Parameter(Mandatory)] [string] $Message)
    $script:failures.Add($Message)
    "[$([DateTime]::UtcNow.ToString('o'))] $Message" |
        Add-Content -LiteralPath $failurePath -Encoding utf8
    Write-SoakEvent -Path $eventsPath -Type failure -Status error -Details @{
        message = $Message
    }
}

function Start-SoakApp {
    Assert-NoOtherNativeProcess -ProcessName $processName
    $process = Start-AppForSmoke -ExePath $Exe
    try {
        $message = Wait-ForMessageOnlyWindow `
            -Process $process `
            -ClassName $messageClass `
            -Title $messageTitle `
            -TimeoutSeconds $OperationTimeoutSec
        $grass = Wait-ForWindow `
            -Process $process `
            -ClassName $windowClass `
            -TimeoutSeconds $OperationTimeoutSec
        [void](Assert-MonitorSurfaceTopology `
            -Process $process `
            -WindowClass $windowClass `
            -TimeoutSeconds $OperationTimeoutSec)
        return [pscustomobject]@{
            process = $process
            message_hwnd = $message
            grass_hwnd = $grass
        }
    } catch {
        try {
            Stop-AppGracefully `
                -Process $process `
                -Hwnd ([IntPtr]::Zero) `
                -TimeoutSeconds 2
        } finally {
            $process.Dispose()
        }
        throw
    }
}

function Stop-SoakApp {
    param(
        [Parameter(Mandatory)] [pscustomobject] $App,
        [Parameter(Mandatory)] [bool] $FailOnHang
    )

    $process = $App.process
    $processId = $process.Id
    $hung = $false
    $exitCode = $null
    try {
        if (-not $process.HasExited) {
            [void][DesktopGrass.Smoke.Win32]::PostMessageW(
                $App.message_hwnd,
                [DesktopGrass.Smoke.Win32]::WM_CLOSE,
                [IntPtr]::Zero,
                [IntPtr]::Zero)
            if (-not $process.WaitForExit($OperationTimeoutSec * 1000)) {
                $hung = $true
                Stop-Process -Id $processId -Force -ErrorAction Stop
                if (-not $process.WaitForExit(2000)) {
                    throw "PID $processId remained alive after forced shutdown."
                }
            }
        }
        $exitCode = $process.ExitCode
        $remaining = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $remaining) {
            $remaining.Dispose()
            throw "Process lifecycle cleanup left PID $processId running."
        }
    } finally {
        $process.Dispose()
    }
    if ($FailOnHang -and $hung) {
        throw "PID $processId hung during graceful shutdown."
    }
    if ($FailOnHang -and $exitCode -ne 0) {
        throw "PID $processId exited with code $exitCode."
    }
    return $exitCode
}

function Set-SoakScene {
    param(
        [Parameter(Mandatory)] [pscustomobject] $App,
        [Parameter(Mandatory)] [int] $SceneIndex
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($OperationTimeoutSec)
    $lastError = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($App.process.HasExited) {
            throw (
                "Production app exited before scene '$($sceneNames[$SceneIndex])' " +
                'persisted.')
        }
        Send-SoakWindowCommand `
            -Hwnd $App.message_hwnd `
            -CommandId $sceneCommands[$SceneIndex] `
            -TimeoutMilliseconds ($OperationTimeoutSec * 1000)
        try {
            [void](Wait-ForPersistedScene `
                -StateFilePath $stateFile `
                -ExpectedScene $sceneNames[$SceneIndex] `
                -TimeoutSeconds 2)
            return
        } catch {
            $lastError = $_.Exception.Message
        }
    }
    throw (
        "Unable to select production scene '$($sceneNames[$SceneIndex])' " +
        "within ${OperationTimeoutSec}s. Last observation: $lastError")
}

function Invoke-TransitionScript {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Label,
        [Parameter(Mandatory)] [string] $LogPath
    )

    $result = Invoke-SoakLoggedProcess `
        -FilePath (Get-Command pwsh -ErrorAction Stop).Source `
        -Arguments @('-NoProfile', '-File', $Path) `
        -LogPath $LogPath `
        -TimeoutSec $OperationTimeoutSec
    if ($result.timed_out) {
        throw "$Label script timed out. See '$LogPath'."
    }
    if ($result.exit_code -ne 0) {
        throw "$Label script exited with code $($result.exit_code). See '$LogPath'."
    }
}

function Assert-DisplayContextRestored {
    $current = Get-SoakDisplayTopologyFingerprint `
        -DisplayContext (Get-RuntimeDisplayContext)
    $result = Test-SoakDisplayContextRestored `
        -Baseline $initialTopology `
        -Current $current
    if (-not $result.pass) {
        throw $result.reason
    }
}

function Assert-DisplayContextChanged {
    $current = Get-SoakDisplayTopologyFingerprint `
        -DisplayContext (Get-RuntimeDisplayContext)
    $result = Test-SoakDisplayContextChanged `
        -Baseline $initialTopology `
        -Current $current
    if (-not $result.pass) {
        throw $result.reason
    }
}

function Invoke-DeviceLossQualification {
    param([Parameter(Mandatory)] [int] $Sequence)

    $logName = "device-loss-$Sequence.trx"
    $logPath = Join-Path $testResultDir "device-loss-$Sequence.log"
    $markerPath = Join-Path $testResultDir "device-loss-$Sequence.marker"
    Remove-Item -LiteralPath $markerPath -Force -ErrorAction SilentlyContinue
    $filter = (
        'FullyQualifiedName~DeviceRecoveryTests|' +
        'FullyQualifiedName~RendererRecoveryIntegrationTests')
    $previousMarker = $env:DESKTOPGRASS_DEVICE_LOSS_MARKER
    try {
        $env:DESKTOPGRASS_DEVICE_LOSS_MARKER = $markerPath
        $result = Invoke-SoakLoggedProcess `
            -FilePath (Get-Command pwsh -ErrorAction Stop).Source `
            -Arguments @(
                '-NoProfile',
                '-File',
                $nativeTestRunner,
                '-Configuration',
                'Release',
                '-Platform',
                $Platform,
                '-TestCaseFilter',
                $filter,
                '-ResultsDirectory',
                $testResultDir,
                '-LogFileName',
                $logName
            ) `
            -LogPath $logPath `
            -TimeoutSec $OperationTimeoutSec
    } finally {
        $env:DESKTOPGRASS_DEVICE_LOSS_MARKER = $previousMarker
    }
    if ($result.timed_out) {
        throw "Device-loss qualification timed out. See '$logPath'."
    }
    if ($result.exit_code -ne 0) {
        throw "Device-loss qualification failed. See '$logPath'."
    }
    if ($result.output -match
        'Skipping real renderer recovery integration') {
        throw (
            'Real renderer recovery integration was unavailable; the policy ' +
            "tests ran, but this is not device-loss evidence. See '$logPath'.")
    }
    if (-not (Test-Path -LiteralPath $markerPath) -or
        (Get-Content -LiteralPath $markerPath -Raw).Trim() -ne
            'renderer-recovery-integration:pass') {
        throw (
            'The real renderer recovery integration did not emit its success ' +
            "marker. See '$logPath' and '$logName'.")
    }
}

$samples = [Collections.Generic.List[object]]::new()
$failures = [Collections.Generic.List[string]]::new()
$coverage = [ordered]@{
    scene_changes = 0
    lifecycle_cycles = 0
    device_loss_runs = 0
    sleep_resume_cycles = 0
    monitor_churn_cycles = 0
    samples = 0
    expected_samples = [int][Math]::Floor($DurationSec / $SampleIntervalSec)
    minimum_samples = [int][Math]::Floor(
        0.8 * [Math]::Floor($DurationSec / $SampleIntervalSec))
}
$stateBackup = $null
$autoStartBefore = $null
$app = $null
$monitorNeedsRestore = $false
$counterSampler = $null
$runStarted = [DateTime]::UtcNow
$stopwatch = [Diagnostics.Stopwatch]::StartNew()
$generation = 0
$sceneIndex = 0
$sampleIndex = 0
$scheduleIndex = 0
$previousCpu = $null
$previousCpuUtc = $null
$nextSampleAt = $SampleIntervalSec
$runError = $null

try {
    $stateBackup = Backup-QualificationStateFile -Path $stateFile
    $autoStartBefore = Get-AutoStartRegistryValue
    Assert-AutoStartLaunchSafe `
        -StateFilePath $stateFile `
        -ExePath $Exe `
        -RegistryValue $autoStartBefore | Out-Null
    $counterSampler = New-BenchmarkCounterSampler
    $app = Start-SoakApp
    $generation++
    Set-SoakScene -App $app -SceneIndex 0
    Write-SoakEvent -Path $eventsPath -Type launch -Status pass -Details @{
        generation = $generation
        pid = $app.process.Id
    }

    while ($stopwatch.Elapsed.TotalSeconds -lt $DurationSec) {
        while ($scheduleIndex -lt $schedule.Count -and
            $schedule[$scheduleIndex].at_sec -le
                $stopwatch.Elapsed.TotalSeconds) {
            $operation = $schedule[$scheduleIndex]
            try {
                switch ($operation.operation) {
                    'scene' {
                        $sceneIndex = ($sceneIndex + 1) % $sceneNames.Count
                        Set-SoakScene -App $app -SceneIndex $sceneIndex
                        $coverage.scene_changes++
                    }
                    'lifecycle' {
                        $oldPid = $app.process.Id
                        [void](Stop-SoakApp -App $app -FailOnHang $true)
                        $app = $null
                        $app = Start-SoakApp
                        $generation++
                        $previousCpu = $null
                        $previousCpuUtc = $null
                        $coverage.lifecycle_cycles++
                        Write-SoakEvent `
                            -Path $eventsPath `
                            -Type hook_cleanup `
                            -Status pass `
                            -Details @{
                                exited_pid = $oldPid
                                replacement_pid = $app.process.Id
                                reason = (
                                    'Windows removes process-owned low-level hooks ' +
                                    'when the owning process exits; no stale process ' +
                                    'or window remained.')
                            }
                    }
                    'device_loss' {
                        Invoke-DeviceLossQualification `
                            -Sequence ($coverage.device_loss_runs + 1)
                        $coverage.device_loss_runs++
                    }
                    'sleep_resume' {
                        Invoke-WakeableSuspend -WakeAfterSeconds $SleepSeconds
                        $session = Get-InteractiveSessionState
                        if (-not $session.interactive_active) {
                            throw (
                                'The resumed session is not active and unlocked: ' +
                                "$($session.connect_state)/$($session.lock_state).")
                        }
                        $app.process.Refresh()
                        if ($app.process.HasExited) {
                            throw 'DesktopGrass exited during sleep/resume.'
                        }
                        [void](Assert-MonitorSurfaceTopology `
                            -Process $app.process `
                            -WindowClass $windowClass `
                            -TimeoutSeconds $OperationTimeoutSec)
                        $coverage.sleep_resume_cycles++
                    }
                    'monitor_churn' {
                        $churnLog = Join-Path $runDir (
                            "monitor-churn-$($coverage.monitor_churn_cycles + 1).log")
                        $restoreLog = Join-Path $runDir (
                            "monitor-restore-$($coverage.monitor_churn_cycles + 1).log")
                        $monitorNeedsRestore = $true
                        Invoke-TransitionScript `
                            -Path $MonitorChurnScript `
                            -Label 'Monitor churn' `
                            -LogPath $churnLog
                        Assert-DisplayContextChanged
                        [void](Assert-MonitorSurfaceTopology `
                            -Process $app.process `
                            -WindowClass $windowClass `
                            -TimeoutSeconds $OperationTimeoutSec)
                        Invoke-TransitionScript `
                            -Path $MonitorRestoreScript `
                            -Label 'Monitor restore' `
                            -LogPath $restoreLog
                        [void](Assert-MonitorSurfaceTopology `
                            -Process $app.process `
                            -WindowClass $windowClass `
                            -TimeoutSeconds $OperationTimeoutSec)
                        Assert-DisplayContextRestored
                        $monitorNeedsRestore = $false
                        $coverage.monitor_churn_cycles++
                    }
                }
                Write-SoakEvent `
                    -Path $eventsPath `
                    -Type $operation.operation `
                    -Status pass `
                    -Details @{ scheduled_at_sec = $operation.at_sec }
            } catch {
                Add-Failure (
                    "$($operation.operation) operation failed: " +
                    $_.Exception.Message)
                throw
            } finally {
                $scheduleIndex++
            }
        }

        $sleepMs = [Math]::Ceiling(
            ($nextSampleAt - $stopwatch.Elapsed.TotalSeconds) * 1000)
        if ($sleepMs -gt 0) {
            Start-Sleep -Milliseconds ([int]$sleepMs)
        }
        if ($stopwatch.Elapsed.TotalSeconds -gt $DurationSec) {
            break
        }

        $failure = $null
        $responsive = $false
        $topologyValid = $false
        $competingProcessIds = @()
        if ($app.process.HasExited) {
            $failure = "Process crashed/exited with code $($app.process.ExitCode)."
        } else {
            $app.process.Refresh()
            $competingProcesses = @(
                Get-Process -Name $processName -ErrorAction SilentlyContinue |
                    Where-Object { $_.Id -ne $app.process.Id }
            )
            $competingProcessIds = @(
                $competingProcesses | ForEach-Object Id
            )
            foreach ($competingProcess in $competingProcesses) {
                $competingProcess.Dispose()
            }
            if ($competingProcessIds.Count -gt 0) {
                $failure = (
                    'Competing DesktopGrass.Native process(es) appeared: ' +
                    ($competingProcessIds -join ', '))
            }
            $grassWindows = @(
                [DesktopGrass.Smoke.Win32]::EnumerateWindowsForProcess(
                    [uint32]$app.process.Id,
                    $windowClass))
            $messageHwnd = Find-MessageOnlyWindow `
                -Process $app.process `
                -ClassName $messageClass `
                -Title $messageTitle
            $responsive = Test-SoakWindowResponsive `
                -Hwnd $messageHwnd `
                -TimeoutMilliseconds ($OperationTimeoutSec * 1000)
            if ($responsive) {
                foreach ($hwnd in $grassWindows) {
                    if (-not (Test-SoakWindowResponsive `
                        -Hwnd $hwnd `
                        -TimeoutMilliseconds ($OperationTimeoutSec * 1000))) {
                        $responsive = $false
                        break
                    }
                }
            }
            try {
                [void](Assert-MonitorSurfaceTopology `
                    -Process $app.process `
                    -WindowClass $windowClass `
                    -TimeoutSeconds 1)
                $topologyValid = $true
            } catch {
                $failure = "Stale/missing window topology: $($_.Exception.Message)"
            }
            if (-not $responsive -and -not $failure) {
                $failure = 'A DesktopGrass window failed the hang probe.'
            }
        }

        $now = [DateTime]::UtcNow
        $cpu = $null
        if (-not $app.process.HasExited) {
            if ($null -ne $previousCpu -and $null -ne $previousCpuUtc) {
                $wallSec = ($now - $previousCpuUtc).TotalSeconds
                if ($wallSec -gt 0) {
                    $cpu = (
                        ($app.process.TotalProcessorTime - $previousCpu).TotalSeconds /
                        $wallSec) * 100.0
                }
            }
            $previousCpu = $app.process.TotalProcessorTime
            $previousCpuUtc = $now
        }
        $counter = if (-not $app.process.HasExited) {
            Get-BenchmarkCounterSample `
                -Sampler $counterSampler `
                -ProcessId $app.process.Id
        } else {
            $null
        }
        $gui = if (-not $app.process.HasExited) {
            Get-SoakGuiResources -Process $app.process
        } else {
            $null
        }
        $grassCount = if ($app.process.HasExited) {
            0
        } else {
            @(
                [DesktopGrass.Smoke.Win32]::EnumerateWindowsForProcess(
                    [uint32]$app.process.Id,
                    $windowClass)
            ).Count
        }
        $messagePresent = if ($app.process.HasExited) {
            $false
        } else {
            (Find-MessageOnlyWindow `
                -Process $app.process `
                -ClassName $messageClass `
                -Title $messageTitle) -ne [IntPtr]::Zero
        }
        $sample = [pscustomobject]@{
            sample_index = $sampleIndex
            utc = $now.ToString('o')
            elapsed_sec = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
            generation = $generation
            pid = if ($app.process.HasExited) { $app.process.Id } else { $app.process.Id }
            scene = $sceneNames[$sceneIndex]
            cpu_core_pct = $cpu
            working_set_mb = if ($app.process.HasExited) {
                $null
            } else {
                [Math]::Round($app.process.WorkingSet64 / 1MB, 3)
            }
            private_mb = if ($app.process.HasExited) {
                $null
            } else {
                [Math]::Round($app.process.PrivateMemorySize64 / 1MB, 3)
            }
            handles = if ($app.process.HasExited) {
                $null
            } else {
                $app.process.HandleCount
            }
            threads = if ($app.process.HasExited) {
                $null
            } else {
                $app.process.Threads.Count
            }
            user_objects = if ($null -eq $gui) { $null } else { $gui.user_objects }
            gdi_objects = if ($null -eq $gui) { $null } else { $gui.gdi_objects }
            gpu_busiest_engine_pct = if ($null -eq $counter) {
                $null
            } else {
                $counter.gpu_busiest_engine_pct
            }
            process_context_switches_per_sec = if ($null -eq $counter) {
                $null
            } else {
                $counter.process_context_switches_per_sec
            }
            grass_window_count = $grassCount
            expected_monitor_count =
                [DesktopGrass.Smoke.Win32]::GetSystemMetrics(
                    [DesktopGrass.Smoke.Win32]::SM_CMONITORS)
            message_window_present = $messagePresent
            responsive = $responsive
            topology_valid = $topologyValid
            competing_process_count = $competingProcessIds.Count
            competing_process_ids = ($competingProcessIds -join ';')
            healthy = [string]::IsNullOrWhiteSpace($failure)
            failure = $failure
        }
        $samples.Add($sample)
        $coverage.samples++
        $sample |
            Select-Object -Property $sampleColumns |
            Export-Csv `
                -LiteralPath $samplesPath `
                -Append `
                -NoTypeInformation `
                -Encoding utf8
        if ($failure) {
            Add-Failure $failure
            break
        }
        $sampleIndex++
        $nextSampleAt += $SampleIntervalSec
        if ($nextSampleAt -le $stopwatch.Elapsed.TotalSeconds) {
            $nextSampleAt = $stopwatch.Elapsed.TotalSeconds + $SampleIntervalSec
        }
    }
} catch {
    $runError = $_.Exception.Message
    if ($failures.Count -eq 0 -or $failures[$failures.Count - 1] -ne $runError) {
        Add-Failure $runError
    }
} finally {
    $stopwatch.Stop()
    if ($monitorNeedsRestore) {
        try {
            Invoke-TransitionScript `
                -Path $MonitorRestoreScript `
                -Label 'Emergency monitor restore' `
                -LogPath (Join-Path $runDir 'monitor-emergency-restore.log')
            Assert-DisplayContextRestored
            $monitorNeedsRestore = $false
        } catch {
            Add-Failure "Emergency monitor restore failed: $($_.Exception.Message)"
        }
    }
    if ($null -ne $app) {
        try {
            [void](Stop-SoakApp -App $app -FailOnHang $true)
        } catch {
            Add-Failure "Final process cleanup failed: $($_.Exception.Message)"
        }
    }
    if ($null -ne $stateBackup) {
        try {
            Restore-QualificationStateFile -Backup $stateBackup
        } catch {
            Add-Failure "State restoration failed: $($_.Exception.Message)"
        }
    }
    if ($null -ne $autoStartBefore) {
        try {
            $autoStartAfter = Get-AutoStartRegistryValue
            $autoStartChange = Test-AutoStartChanged `
                -Before $autoStartBefore `
                -After $autoStartAfter
            if ($autoStartChange.changed) {
                Restore-AutoStartRegistryValue -Backup $autoStartBefore
            }
        } catch {
            Add-Failure "Autostart restoration failed: $($_.Exception.Message)"
        }
    }
    if ($hasMonitorScripts -and -not $monitorNeedsRestore) {
        try {
            Assert-DisplayContextRestored
        } catch {
            Add-Failure "Final display restoration check failed: $($_.Exception.Message)"
        }
    }
}

$budgetSamples = @(
    $samples |
        Where-Object { [double]$_.elapsed_sec -ge $WarmupSec }
)
$resourceBudget = Measure-SoakResourceBudget `
    -Samples $budgetSamples `
    -Thresholds $thresholds
$qualification = Get-SoakQualification `
    -DurationSec ([int][Math]::Floor($stopwatch.Elapsed.TotalSeconds)) `
    -MinimumDurationSec $minimumQualifyingDurationSec `
    -Coverage $coverage `
    -ResourceBudget $resourceBudget `
    -RuntimeHealthy ($failures.Count -eq 0) `
    -DiagnosticRun ([bool]$DiagnosticRun)

try {
    $applicationEvents = @(
        Get-WinEvent `
            -FilterHashtable @{
                LogName = 'Application'
                StartTime = $runStarted
            } `
            -MaxEvents 2000 `
            -ErrorAction Stop |
            Where-Object {
                $_.Message -match 'DesktopGrass|DesktopGrass.Native'
            } |
            Select-Object TimeCreated, Id, LevelDisplayName, ProviderName, Message
    )
    Write-SoakJson `
        -Path (Join-Path $runDir 'windows-application-events.json') `
        -Value $applicationEvents
} catch {
    Write-SoakJson `
        -Path (Join-Path $runDir 'windows-application-events.json') `
        -Value @{
            status = 'unavailable'
            reason = $_.Exception.Message
        }
}

$summary = [ordered]@{
    schema_version = 1
    issue = 29
    run_id = $runId
    started_utc = $runStarted.ToString('o')
    completed_utc = [DateTime]::UtcNow.ToString('o')
    requested_duration_sec = $DurationSec
    observed_duration_sec = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
    diagnostic_run = [bool]$DiagnosticRun
    coverage = $coverage
    failures = @($failures)
    resource_budget = $resourceBudget
    qualification = $qualification
    short_run_disclaimer = if (
        $DurationSec -lt $minimumQualifyingDurationSec -or $DiagnosticRun
    ) {
        'This run validates harness behavior only; it is not multi-hour soak evidence.'
    } else {
        $null
    }
}
Write-SoakJson -Path $summaryPath -Value $summary
$manifest.completed_utc = $summary.completed_utc
$manifest.status = $qualification.status
$manifest['summary'] = 'summary.json'
Write-SoakJson -Path $manifestPath -Value $manifest

$archivePath = "$runDir.zip"
Compress-Archive -Path (Join-Path $runDir '*') -DestinationPath $archivePath -Force
Write-Host "Soak results: $runDir" -ForegroundColor Cyan
Write-Host "Artifact: $archivePath" -ForegroundColor Cyan
Write-Host "Status: $($qualification.status)" -ForegroundColor Yellow

if ($qualification.status -eq 'fail' -or
    ($qualification.status -eq 'not_qualified' -and -not $DiagnosticRun)) {
    exit 1
}
exit 0
