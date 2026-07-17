$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$toolsRoot = Join-Path $repoRoot 'tools\benchmark'
$commonPath = Join-Path $toolsRoot 'RuntimeQualification.Common.psm1'
$aggregatePath = Join-Path $toolsRoot 'Aggregate-RuntimeQualification.ps1'

Import-Module $commonPath -Force

$script:passed = 0
$script:failed = 0

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($Condition) {
        $script:passed++
        Write-Host "PASS $Name" -ForegroundColor Green
    } else {
        $script:failed++
        Write-Host "FAIL $Name" -ForegroundColor Red
    }
}

function Assert-Equal {
    param(
        [AllowNull()] $Expected,
        [AllowNull()] $Actual,
        [Parameter(Mandatory)] [string] $Name
    )

    Assert-True `
        -Condition ($Expected -eq $Actual) `
        -Name "$Name (expected='$Expected', actual='$Actual')"
}

function Write-TestCsv {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $Columns,
        [AllowEmptyCollection()] [object[]] $Rows
    )

    Write-CsvWithHeader -Path $Path -Columns $Columns -Rows $Rows
}

function Format-FixtureUtc {
    param([Parameter(Mandatory)] [DateTimeOffset] $Value)

    return $Value.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ')
}

function New-CaptureRecord {
    param(
        [Parameter(Mandatory)] [string] $Status,
        [AllowNull()] [string] $Path,
        [Parameter(Mandatory)] [DateTimeOffset] $StartUtc,
        [Parameter(Mandatory)] [DateTimeOffset] $EndUtc,
        [int] $TargetProcessId = 4242
    )

    return [ordered]@{
        label = 'fixture'
        target_process_id = $TargetProcessId
        duration_sec = ($EndUtc - $StartUtc).TotalSeconds
        status = $Status
        reason = if ($Status -eq 'available') { $null } else { 'fixture' }
        output_path = $Path
        stdout_path = $null
        stderr_path = $null
        start_utc = Format-FixtureUtc $StartUtc
        end_utc = Format-FixtureUtc $EndUtc
        arguments = @(
            '--process_id',
            "$TargetProcessId",
            '--date_time'
        )
        exit_code = if ($Status -eq 'available') { 0 } else { $null }
    }
}

function Write-PresentCsv {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [DateTimeOffset] $StartUtc,
        [int] $Count = 4,
        [string] $SwapChain = '0x1'
    )

    $columns = @(
        'Application',
        'ProcessID',
        'SwapChainAddress',
        'CPUStartDateTime',
        'MsBetweenPresents'
    )
    $rows = @(
        for ($i = 0; $i -lt $Count; $i++) {
            [pscustomobject]@{
                Application = 'DesktopGrass.Native.exe'
                ProcessID = $ProcessId
                SwapChainAddress = $SwapChain
                CPUStartDateTime = $StartUtc.AddMilliseconds(
                    100 + 40 * $i
                ).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffffffZ')
                MsBetweenPresents = 40
            }
        }
    )
    Write-TestCsv -Path $Path -Columns $columns -Rows $rows
}

function New-FixtureCell {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string] $CaptureId,
        [string] $Scenario = 'visible',
        [string] $SceneName = 'Grass',
        [string] $PowerState = 'ac',
        [string] $PowerScheme = 'scheme-a',
        [int] $Run = 1,
        [int] $ExpectedSamples = 4,
        [int] $ActualSamples = 4,
        [int] $ProcessId = 4242,
        [DateTimeOffset] $StartUtc = (
            [DateTimeOffset]::UtcNow.AddMinutes(-5)
        ),
        [switch] $TransientPower,
        [switch] $TransientSession,
        [switch] $DuplicateSample,
        [switch] $UnavailableGpuStatus,
        [switch] $ContaminatedNoApp,
        [switch] $SparseBatteryTelemetry,
        [switch] $NoSysEnergy,
        [switch] $MixedThrottleState,
        [switch] $DuplicateSys,
        [switch] $Suppressed,
        [switch] $ResumeHasNoPresents,
        [switch] $MismatchedControlPid,
        [switch] $NoPresentCapture
    )

    $tag = "$Scenario-$SceneName-$PowerState-run$Run"
    $samplePath = Join-Path $Directory "$tag.samples.csv"
    $gpuPath = Join-Path $Directory "$tag.gpu.csv"
    $powerPath = Join-Path $Directory "$tag.power.csv"
    $energyPath = Join-Path $Directory "$tag.energy.csv"
    $throttlePath = Join-Path $Directory "$tag.throttling.csv"
    $presentPath = Join-Path $Directory "$tag.presentmon.csv"
    $beforePath = Join-Path $Directory "$tag.before.presentmon.csv"
    $afterPath = Join-Path $Directory "$tag.after.presentmon.csv"
    $endUtc = $StartUtc.AddSeconds($ExpectedSamples)
    $isNoApp = $Scenario -eq 'no-app-control'
    if ($isNoApp) {
        $ProcessId = 0
        $SceneName = 'none'
    }

    $sampleRows = @(
        for ($i = 0; $i -lt $ActualSamples; $i++) {
            [pscustomobject]@{
                sample_index = $i
                sample_measured = $true
                t_sec = $i + 1
                sample_utc = Format-FixtureUtc (
                    $StartUtc.AddSeconds($i + 1)
                )
                cpu_core_pct = if ($isNoApp) { $null } else { 10 + $i }
                working_set_mb = if ($isNoApp) { $null } else { 50 + $i }
                private_mb = if ($isNoApp) { $null } else { 40 + $i }
                threads = if ($isNoApp) { $null } else { 4 }
                handles = if ($isNoApp) { $null } else { 20 }
                gpu_busiest_engine_pct = if ($isNoApp) {
                    $null
                } else {
                    5 + $i
                }
                process_context_switches_per_sec = if ($isNoApp) {
                    $null
                } else {
                    100 + $i
                }
                system_context_switches_per_sec = 1000 + $i
                system_interrupts_per_sec = 100 + $i
                system_dpc_rate = 50 + $i
                processor_queue_length = 0
                gpu_status = if ($isNoApp) {
                    'not_applicable'
                } elseif ($UnavailableGpuStatus) {
                    'unavailable'
                } else {
                    'available'
                }
                context_switch_status = if ($isNoApp) {
                    'not_applicable'
                } else {
                    'available'
                }
                energy_status = 'available'
                counter_status = 'available'
            }
        }
    )
    if ($DuplicateSample -and $sampleRows.Count -gt 0) {
        $sampleRows += ($sampleRows[0] | Select-Object *)
    }
    Write-TestCsv `
        -Path $samplePath `
        -Columns @(
            'sample_index', 'sample_measured', 't_sec', 'sample_utc',
            'cpu_core_pct', 'working_set_mb', 'private_mb', 'threads',
            'handles', 'gpu_busiest_engine_pct',
            'process_context_switches_per_sec',
            'system_context_switches_per_sec',
            'system_interrupts_per_sec', 'system_dpc_rate',
            'processor_queue_length', 'gpu_status',
            'context_switch_status', 'energy_status', 'counter_status'
        ) `
        -Rows $sampleRows

    $gpuRows = @(
        if (-not $isNoApp) {
            for ($i = 0; $i -lt $ActualSamples; $i++) {
                [pscustomobject]@{
                    sample_index = $i
                    t_sec = $i + 1
                    sample_utc = Format-FixtureUtc (
                        $StartUtc.AddSeconds($i + 1)
                    )
                    pid = $ProcessId
                    instance_name = "pid_$ProcessId"
                    adapter_luid = '0x1'
                    physical_adapter = 0
                    engine_index = 0
                    engine_type = '3D'
                    utilization_pct = 5 + $i
                    counter_status = 'available'
                }
            }
        }
    )
    Write-TestCsv `
        -Path $gpuPath `
        -Columns @(
            'sample_index', 't_sec', 'sample_utc', 'pid', 'instance_name',
            'adapter_luid', 'physical_adapter', 'engine_index',
            'engine_type', 'utilization_pct', 'counter_status'
        ) `
        -Rows $gpuRows

    $source = if ($PowerState -eq 'ac') { 'ac' } else { 'battery' }
    $saver = $PowerState -eq 'battery-saver'
    $powerRows = @(
        for ($i = 0; $i -lt $ActualSamples; $i++) {
            $rowSource = if ($TransientPower -and $i -eq 1) {
                if ($source -eq 'ac') { 'battery' } else { 'ac' }
            } else {
                $source
            }
            [pscustomobject]@{
                sample_index = $i
                sample_measured = $true
                t_sec = $i + 1
                sample_utc = Format-FixtureUtc (
                    $StartUtc.AddSeconds($i + 1)
                )
                ac_line_status = $rowSource
                battery_present = $true
                battery_percent = 80
                battery_saver = $saver
                active_power_scheme_guid = $PowerScheme
                battery_telemetry_status = if (
                    $SparseBatteryTelemetry -and $i -ge 2
                ) {
                    'unavailable'
                } else {
                    'available'
                }
                battery_remaining_capacity_mwh = 50000 - $i
                battery_discharge_rate_mw = 10000
                power_status = 'available'
                context_matches = -not (
                    $TransientPower -and $i -eq 1
                )
                context_reason = if (
                    $TransientPower -and $i -eq 1
                ) {
                    'transient source'
                } else {
                    $null
                }
                session_status = 'available'
                session_id = 1
                session_connect_state = if (
                    $TransientSession -and $i -eq 1
                ) {
                    'connected'
                } else {
                    'active'
                }
                session_lock_state = if (
                    $TransientSession -and $i -eq 1
                ) {
                    'locked'
                } else {
                    'unlocked'
                }
                session_context_matches = -not (
                    $TransientSession -and $i -eq 1
                )
                session_context_reason = if (
                    $TransientSession -and $i -eq 1
                ) {
                    'transient lock'
                } else {
                    $null
                }
                target_process_absent = if ($isNoApp) {
                    -not ($ContaminatedNoApp -and $i -eq 1)
                } else {
                    $null
                }
                target_process_presence_reason = if (
                    $isNoApp -and $ContaminatedNoApp -and $i -eq 1
                ) {
                    'fixture process appeared'
                } else {
                    $null
                }
            }
        }
    )
    Write-TestCsv `
        -Path $powerPath `
        -Columns @(
            'sample_index', 'sample_measured', 't_sec', 'sample_utc',
            'ac_line_status', 'battery_present', 'battery_percent',
            'battery_saver', 'active_power_scheme_guid',
            'battery_telemetry_status',
            'battery_remaining_capacity_mwh',
            'battery_discharge_rate_mw', 'power_status',
            'context_matches', 'context_reason', 'session_status',
            'session_id', 'session_connect_state', 'session_lock_state',
            'session_context_matches', 'session_context_reason',
            'target_process_absent', 'target_process_presence_reason'
        ) `
        -Rows $powerRows

    $energyRows = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $ActualSamples; $i++) {
        if (-not $NoSysEnergy) {
            $energyRows.Add([pscustomobject]@{
                sample_index = $i
                t_sec = $i + 1
                sample_utc = Format-FixtureUtc (
                    $StartUtc.AddSeconds($i + 1)
                )
                meter = 'SYS'
                power_mw = 20000 + $i
                counter_status = 'available'
            })
        }
        $energyRows.Add([pscustomobject]@{
            sample_index = $i
            t_sec = $i + 1
            sample_utc = Format-FixtureUtc (
                $StartUtc.AddSeconds($i + 1)
            )
            meter = 'CPU_CORE'
            power_mw = 5000 + $i
            counter_status = 'available'
        })
        if ($DuplicateSys -and -not $NoSysEnergy -and $i -eq 0) {
            $energyRows.Add([pscustomobject]@{
                sample_index = $i
                t_sec = $i + 1
                sample_utc = Format-FixtureUtc (
                    $StartUtc.AddSeconds($i + 1)
                )
                meter = 'SYS'
                power_mw = 99999
                counter_status = 'available'
            })
        }
    }
    Write-TestCsv `
        -Path $energyPath `
        -Columns @(
            'sample_index', 't_sec', 'sample_utc', 'meter', 'power_mw',
            'counter_status'
        ) `
        -Rows $energyRows

    $throttleRows = @(
        if (-not $isNoApp) {
            for ($i = 0; $i -lt $ActualSamples; $i++) {
                [pscustomobject]@{
                    sample_index = $i
                    sample_measured = $true
                    t_sec = $i + 1
                    sample_utc = Format-FixtureUtc (
                        $StartUtc.AddSeconds($i + 1)
                    )
                    status = 'available'
                    reason = $null
                    execution_speed_throttled = (
                        $MixedThrottleState -and $i -eq 0
                    )
                    control_mask = 0
                    state_mask = 0
                }
            }
        }
    )
    if (-not $isNoApp) {
        Write-TestCsv `
            -Path $throttlePath `
            -Columns @(
                'sample_index', 'sample_measured', 't_sec', 'sample_utc',
                'status', 'reason', 'execution_speed_throttled',
                'control_mask', 'state_mask'
            ) `
            -Rows $throttleRows
    }

    if ($NoPresentCapture) {
        $presentCapture = New-CaptureRecord `
            -Status 'unavailable' `
            -Path $presentPath `
            -StartUtc $StartUtc `
            -EndUtc $endUtc `
            -TargetProcessId $ProcessId
    } elseif ($Suppressed) {
        Write-TestCsv `
            -Path $presentPath `
            -Columns @(
                'Application', 'ProcessID', 'SwapChainAddress',
                'CPUStartDateTime', 'MsBetweenPresents'
            ) `
            -Rows @()
        $presentCapture = New-CaptureRecord `
            -Status 'available' `
            -Path $presentPath `
            -StartUtc $StartUtc `
            -EndUtc $endUtc `
            -TargetProcessId $ProcessId
    } elseif (-not $isNoApp) {
        Write-PresentCsv `
            -Path $presentPath `
            -ProcessId $ProcessId `
            -StartUtc $StartUtc
        $presentCapture = New-CaptureRecord `
            -Status 'available' `
            -Path $presentPath `
            -StartUtc $StartUtc `
            -EndUtc $endUtc `
            -TargetProcessId $ProcessId
    } else {
        $presentCapture = New-CaptureRecord `
            -Status 'not_applicable' `
            -Path $null `
            -StartUtc $StartUtc `
            -EndUtc $endUtc `
            -TargetProcessId $ProcessId
    }

    $beforeCapture = New-CaptureRecord `
        -Status 'not_applicable' `
        -Path $beforePath `
        -StartUtc $StartUtc.AddSeconds(-5) `
        -EndUtc $StartUtc `
        -TargetProcessId $ProcessId
    $afterCapture = New-CaptureRecord `
        -Status 'not_applicable' `
        -Path $afterPath `
        -StartUtc $endUtc `
        -EndUtc $endUtc.AddSeconds(5) `
        -TargetProcessId $ProcessId
    if ($Suppressed) {
        $controlPid = if ($MismatchedControlPid) {
            $ProcessId + 1
        } else {
            $ProcessId
        }
        Write-PresentCsv `
            -Path $beforePath `
            -ProcessId $controlPid `
            -StartUtc $StartUtc.AddSeconds(-5)
        $beforeCapture = New-CaptureRecord `
            -Status 'available' `
            -Path $beforePath `
            -StartUtc $StartUtc.AddSeconds(-5) `
            -EndUtc $StartUtc `
            -TargetProcessId $ProcessId
        if ($ResumeHasNoPresents) {
            Write-TestCsv `
                -Path $afterPath `
                -Columns @(
                    'Application', 'ProcessID', 'SwapChainAddress',
                    'CPUStartDateTime', 'MsBetweenPresents'
                ) `
                -Rows @()
        } else {
            Write-PresentCsv `
                -Path $afterPath `
                -ProcessId $ProcessId `
                -StartUtc $endUtc
        }
        $afterCapture = New-CaptureRecord `
            -Status 'available' `
            -Path $afterPath `
            -StartUtc $endUtc `
            -EndUtc $endUtc.AddSeconds(5) `
            -TargetProcessId $ProcessId
    }

    $visibilityBefore = if ($isNoApp) {
        $null
    } else {
        [ordered]@{ total = 1; visible = 1; hidden = 0 }
    }
    $visibilityDuring = if ($Suppressed) {
        [ordered]@{ total = 1; visible = 0; hidden = 1 }
    } elseif ($isNoApp) {
        $null
    } else {
        [ordered]@{ total = 1; visible = 1; hidden = 0 }
    }

    return [pscustomobject]@{
        SchemaVersion = 2
        CaptureId = $CaptureId
        RunId = "$CaptureId/$Run"
        CellTag = $tag
        Scene = if ($isNoApp) { $null } else { 0 }
        SceneName = $SceneName
        Scenario = $Scenario
        PowerState = $PowerState
        Run = $Run
        DurationSec = $ExpectedSamples
        SampleIntervalSec = 1
        ExpectedSampleCount = $ExpectedSamples
        RequiredTargetFps = 24
        SampleCsv = Split-Path $samplePath -Leaf
        GpuCsv = Split-Path $gpuPath -Leaf
        PowerCsv = Split-Path $powerPath -Leaf
        EnergyCsv = Split-Path $energyPath -Leaf
        ThrottleCsv = if ($isNoApp) {
            $null
        } else {
            Split-Path $throttlePath -Leaf
        }
        ProcessId = if ($isNoApp) { $null } else { $ProcessId }
        ExpectedVisibleWindowCount = if ($isNoApp) { 0 } else { 1 }
        IntendedGrassVisible = if ($Suppressed) { $false } else { $true }
        VisibilityBefore = $visibilityBefore
        VisibilityDuring = $visibilityDuring
        ProbeWindows = @()
        ResumeWindowsVisible = if ($Suppressed) { $true } else { $null }
        MeasurementStartUtc = Format-FixtureUtc $StartUtc
        MeasurementEndUtc = Format-FixtureUtc $endUtc
        SampledWallSec = $ExpectedSamples
        PresentCapture = $presentCapture
        VisibleControlBefore = $beforeCapture
        VisibleControlAfter = $afterCapture
        PowerContextStart = $powerRows[0]
        PowerContextEnd = $powerRows[-1]
        PowerContextValid = $true
        PowerContextReason = $null
        SessionContextStart = [ordered]@{
            status = 'available'
            session_id = 1
            connect_state = 'active'
            lock_state = 'unlocked'
            interactive_active = $true
        }
        SessionContextEnd = [ordered]@{
            status = 'available'
            session_id = 1
            connect_state = 'active'
            lock_state = 'unlocked'
            interactive_active = $true
        }
        SessionContextValid = -not $TransientSession
        SessionContextReason = if ($TransientSession) {
            'transient lock'
        } else {
            $null
        }
        NoAppProcessAbsent = if ($isNoApp) {
            -not $ContaminatedNoApp
        } else {
            $null
        }
        NoAppProcessAbsenceReason = if (
            $isNoApp -and $ContaminatedNoApp
        ) {
            'fixture process appeared'
        } else {
            $null
        }
        DisplayContextStart = [ordered]@{
            status = 'available'
            hash = 'display-a'
        }
        DisplayContextEnd = [ordered]@{
            status = 'available'
            hash = 'display-a'
        }
        DisplayContextValid = $true
    }
}

function Write-FixtureManifest {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string] $CaptureId,
        [Parameter(Mandatory)] [object[]] $Cells,
        [string] $ExeHash = 'exe-a',
        [string] $ConfigHash = 'config-a',
        [string] $DisplayHash = 'display-a',
        [string] $MachineHash = 'machine-a',
        [string] $QualificationSetId = 'set-a',
        [bool] $Aborted = $false
    )

    $manifest = [ordered]@{
        QualificationSchemaVersion = 2
        BenchmarkSchemaVersion = 2
        CaptureId = $CaptureId
        QualificationSetId = $QualificationSetId
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        UpdatedUtc = [DateTime]::UtcNow.ToString('o')
        CompletedUtc = [DateTime]::UtcNow.ToString('o')
        Aborted = $Aborted
        AbortReason = if ($Aborted) { 'fixture abort' } else { $null }
        Scenario = if ($Cells.Count -gt 0) {
            $Cells[0].Scenario
        } else {
            'visible'
        }
        SceneOrder = @(0)
        Seed = 1
        Runs = if ($Cells.Count -gt 0) {
            [int](($Cells.Run | Measure-Object -Maximum).Maximum)
        } else {
            1
        }
        DurationSec = if ($Cells.Count -gt 0) {
            $Cells[0].DurationSec
        } else {
            4
        }
        SampleIntervalSec = if ($Cells.Count -gt 0) {
            $Cells[0].SampleIntervalSec
        } else {
            1
        }
        ExpectedSampleCount = if ($Cells.Count -gt 0) {
            $Cells[0].ExpectedSampleCount
        } else {
            4
        }
        WarmupSec = 0
        ProbeSettleSec = 0
        PresentControlDurationSec = 5
        RequiredTargetFps = 24
        Platform = 'ARM64'
        ProductionEntryPoint = 'App (no --benchmark)'
        ExecutablePath = 'C:\fixture\DesktopGrass.Native.exe'
        ExecutableSha256 = $ExeHash
        ConfigPath = 'C:\fixture\config.json'
        ConfigSha256 = $ConfigHash
        MachineFingerprint = [ordered]@{
            status = 'available'
            reason = $null
            hash = $MachineHash
            details = @{}
        }
        DisplayContext = [ordered]@{
            status = 'available'
            reason = $null
            hash = $DisplayHash
            details = @{}
        }
        PresentMon = [ordered]@{
            path = 'C:\fixture\PresentMon.exe'
            available = $true
            version = '2.3.1'
        }
        Cells = $Cells
    }
    $manifest |
        ConvertTo-Json -Depth 16 |
        Out-File `
            -LiteralPath (Join-Path $Directory 'manifest.json') `
            -Encoding utf8
}

function Invoke-Aggregator {
    param([Parameter(Mandatory)] [string] $Root)

    $outCsv = Join-Path $Root 'out.csv'
    $outBudgets = Join-Path $Root 'budgets.csv'
    $outJson = Join-Path $Root 'out.json'
    $outMarkdown = Join-Path $Root 'out.md'
    try {
        & $aggregatePath `
            -ResultsRoot $Root `
            -OutCsv $outCsv `
            -OutBudgetsCsv $outBudgets `
            -OutJson $outJson `
            -OutMarkdown $outMarkdown |
            Out-Null
        return [pscustomobject]@{
            success = $true
            error = $null
            results = @(Import-Csv -LiteralPath $outCsv)
            budgets = @(Import-Csv -LiteralPath $outBudgets)
            json = Get-Content -LiteralPath $outJson -Raw |
                ConvertFrom-Json
        }
    } catch {
        return [pscustomobject]@{
            success = $false
            error = (
                $_.Exception.Message + [Environment]::NewLine +
                $_.ScriptStackTrace
            )
            results = @()
            budgets = @()
            json = $null
        }
    }
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'DesktopGrass-RuntimeQualificationTests-' +
    [Guid]::NewGuid().ToString('N')
)
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $outside = Join-Path ([IO.Path]::GetTempPath()) 'outside-results'
    Assert-ResultsRootOutsideRepo `
        -ResultsRoot $outside `
        -RepoRoot $repoRoot
    Assert-True -Condition $true -Name 'outside result root accepted'

    $insideRejected = $false
    try {
        Assert-ResultsRootOutsideRepo `
            -ResultsRoot (Join-Path $repoRoot 'raw') `
            -RepoRoot $repoRoot
    } catch {
        $insideRejected = $true
    }
    Assert-True -Condition $insideRejected -Name 'repo-local result root rejected'

    $registrySubKey = (
        'Software\DesktopGrass\QualificationTests\' +
        [Guid]::NewGuid().ToString('N')
    )
    $registryValueName = 'FixtureAutoStart'
    try {
        $missingRegistryValue = Get-AutoStartRegistryValue `
            -SubKey $registrySubKey `
            -ValueName $registryValueName
        $registryKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
            $registrySubKey
        )
        try {
            $registryKey.SetValue(
                $registryValueName,
                '%TEMP%\DesktopGrass.Native.exe',
                [Microsoft.Win32.RegistryValueKind]::ExpandString
            )
        } finally {
            $registryKey.Dispose()
        }
        $registryBackup = Get-AutoStartRegistryValue `
            -SubKey $registrySubKey `
            -ValueName $registryValueName
        Assert-Equal `
            -Expected '%TEMP%\DesktopGrass.Native.exe' `
            -Actual $registryBackup.value `
            -Name 'autostart backup does not expand environment variables'
        Assert-Equal `
            -Expected 'ExpandString' `
            -Actual $registryBackup.kind `
            -Name 'autostart backup records registry value kind'

        $registryKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
            $registrySubKey,
            $true
        )
        try {
            $registryKey.SetValue(
                $registryValueName,
                'changed',
                [Microsoft.Win32.RegistryValueKind]::String
            )
        } finally {
            $registryKey.Dispose()
        }
        Restore-AutoStartRegistryValue `
            -Backup $registryBackup `
            -SubKey $registrySubKey `
            -ValueName $registryValueName
        $registryRestored = Get-AutoStartRegistryValue `
            -SubKey $registrySubKey `
            -ValueName $registryValueName
        Assert-Equal `
            -Expected $registryBackup.value `
            -Actual $registryRestored.value `
            -Name 'autostart restoration restores the original value'
        Assert-Equal `
            -Expected $registryBackup.kind `
            -Actual $registryRestored.kind `
            -Name 'autostart restoration restores the original value kind'

        Restore-AutoStartRegistryValue `
            -Backup $missingRegistryValue `
            -SubKey $registrySubKey `
            -ValueName $registryValueName
        $registryRemoved = Get-AutoStartRegistryValue `
            -SubKey $registrySubKey `
            -ValueName $registryValueName
        Assert-True `
            -Condition (-not $registryRemoved.exists) `
            -Name 'autostart restoration removes a newly created value'
    } finally {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree(
            $registrySubKey,
            $false
        )
    }

    $orderA = Get-QualificationSceneOrder -Scenes @(0, 1, 2, 3, 4) -Seed 14
    $orderB = Get-QualificationSceneOrder -Scenes @(0, 1, 2, 3, 4) -Seed 14
    Assert-Equal `
        -Expected ($orderA -join ',') `
        -Actual ($orderB -join ',') `
        -Name 'scene shuffle is deterministic'

    $presentDir = Join-Path $tempRoot 'present-parser'
    New-Item -ItemType Directory -Path $presentDir | Out-Null
    $presentPath = Join-Path $presentDir 'present.csv'
    $presentStart = [DateTime]::UtcNow.AddMinutes(-1)
    Write-PresentCsv `
        -Path $presentPath `
        -ProcessId 77 `
        -StartUtc $presentStart
    $parsed = ConvertFrom-PresentMonCsv `
        -Path $presentPath `
        -TargetProcessId 77
    Assert-Equal -Expected 'available' -Actual $parsed.status `
        -Name 'absolute PresentMon CSV parses'
    Assert-Equal -Expected 4 -Actual @($parsed.rows).Count `
        -Name 'PresentMon parser keeps target PID rows'

    $nanoPath = Join-Path $presentDir 'nano.csv'
    @(
        'Application,ProcessID,SwapChainAddress,CPUStartDateTime,MsBetweenPresents'
        "app,77,0x1,$(
            $presentStart.ToString('yyyy-MM-ddTHH:mm:ss')
        ).123456789Z,40"
    ) | Out-File -LiteralPath $nanoPath -Encoding utf8
    $nanoParsed = ConvertFrom-PresentMonCsv `
        -Path $nanoPath `
        -TargetProcessId 77
    Assert-Equal -Expected 'available' -Actual $nanoParsed.status `
        -Name 'nanosecond PresentMon timestamp parses'

    $mismatchParsed = ConvertFrom-PresentMonCsv `
        -Path $presentPath `
        -TargetProcessId 78
    Assert-Equal -Expected 'error' -Actual $mismatchParsed.status `
        -Name 'PresentMon parser rejects a different PID'

    $legacyPath = Join-Path $presentDir 'legacy.csv'
    @(
        'Application,ProcessID,SwapChainAddress,TimeInSeconds'
        'app,77,0x1,1.0'
    ) | Out-File -LiteralPath $legacyPath -Encoding utf8
    $legacyParsed = ConvertFrom-PresentMonCsv `
        -Path $legacyPath `
        -TargetProcessId 77
    Assert-Equal -Expected 'error' -Actual $legacyParsed.status `
        -Name 'legacy relative PresentMon timestamps are rejected'

    $headerOnlyPath = Join-Path $presentDir 'zero.csv'
    @(
        'Application,ProcessID,SwapChainAddress,CPUStartDateTime,MsBetweenPresents'
    ) | Out-File -LiteralPath $headerOnlyPath -Encoding utf8
    $zeroParsed = ConvertFrom-PresentMonCsv `
        -Path $headerOnlyPath `
        -TargetProcessId 77
    Assert-Equal -Expected 'available' -Actual $zeroParsed.status `
        -Name 'header-only PresentMon capture is a measured zero'
    Assert-Equal -Expected 0 -Actual @($zeroParsed.rows).Count `
        -Name 'header-only PresentMon capture has zero rows'

    $selectedRows = Select-PresentMonRowsInWindow `
        -Rows $parsed.rows `
        -ProcessId 77 `
        -StartUtc $presentStart `
        -EndUtc $presentStart.AddSeconds(1)
    $cadence = Measure-SwapChainCadence `
        -Rows $selectedRows `
        -WindowStartUtc $presentStart `
        -WindowEndUtc $presentStart.AddSeconds(1)
    Assert-Equal -Expected 1 -Actual @($cadence).Count `
        -Name 'cadence remains per swap chain'
    Assert-Equal -Expected 4 -Actual $cadence[0].present_count `
        -Name 'cadence counts target rows'

    $coverage = Test-MetricCoverage -ExpectedSamples 10 -ValidSamples 9
    Assert-Equal -Expected 'available' -Actual $coverage.status `
        -Name '90 percent coverage is available'
    $lowCoverage = Test-MetricCoverage -ExpectedSamples 10 -ValidSamples 8
    Assert-Equal -Expected 'insufficient_coverage' -Actual $lowCoverage.status `
        -Name 'sub-90 percent coverage is unavailable'

    $missingCadence = Test-CadenceBudget `
        -MeasuredFps $null `
        -CapFps 12 `
        -VisibleAcFps 24 `
        -NonZeroPresentCount 0
    Assert-Equal -Expected 'not_evaluated' -Actual $missingCadence.status `
        -Name 'missing cadence never coerces to zero'

    $suppressionPass = Test-PresentSuppressionGate `
        -CaptureStatus 'available' `
        -SuppressedPresentCount 0 `
        -VisibleControlValid $true `
        -ResumeValid $true
    Assert-Equal -Expected 'pass' -Actual $suppressionPass.status `
        -Name 'zero presents with paired controls passes'
    $suppressionUnavailable = Test-PresentSuppressionGate `
        -CaptureStatus 'available' `
        -SuppressedPresentCount 0 `
        -VisibleControlValid $false `
        -ResumeValid $true
    Assert-Equal `
        -Expected 'not_evaluated' `
        -Actual $suppressionUnavailable.status `
        -Name 'zero presents without same-PID control cannot pass'

    $singleRoot = Join-Path $tempRoot 'single-noapp'
    $singleDir = Join-Path $singleRoot 'capture'
    New-Item -ItemType Directory -Path $singleDir -Force | Out-Null
    $singleId = 'single'
    $singleCell = New-FixtureCell `
        -Directory $singleDir `
        -CaptureId $singleId `
        -Scenario 'no-app-control'
    Write-FixtureManifest `
        -Directory $singleDir `
        -CaptureId $singleId `
        -Cells @($singleCell)
    $singleResult = Invoke-Aggregator $singleRoot
    Assert-True -Condition $singleResult.success `
        -Name (
            'single-scenario aggregation does not crash on empty suppression ' +
            "sets: $($singleResult.error)"
        )
    if ($singleResult.success) {
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $singleResult.json.overall_status `
            -Name 'partial matrix overall status is not evaluated'
    }
    $outputEscapeRejected = $false
    try {
        & $aggregatePath `
            -ResultsRoot $singleRoot `
            -OutCsv (Join-Path $tempRoot 'escaped-results.csv') |
            Out-Null
    } catch {
        $outputEscapeRejected = $_.Exception.Message -match (
            'escapes ResultsRoot'
        )
    }
    Assert-True `
        -Condition $outputEscapeRejected `
        -Name 'aggregator output paths cannot escape the evidence root'

    $coverageRoot = Join-Path $tempRoot 'coverage'
    $coverageDir = Join-Path $coverageRoot 'capture'
    New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null
    $coverageCell = New-FixtureCell `
        -Directory $coverageDir `
        -CaptureId 'coverage' `
        -ExpectedSamples 10 `
        -ActualSamples 5 `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $coverageDir `
        -CaptureId 'coverage' `
        -Cells @($coverageCell)
    $coverageResult = Invoke-Aggregator $coverageRoot
    Assert-True -Condition $coverageResult.success `
        -Name "truncated fixture aggregates: $($coverageResult.error)"
    if ($coverageResult.success) {
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $coverageResult.results[0].CpuStatus `
            -Name 'truncated process and power evidence cannot pass'
        Assert-Equal `
            -Expected '50' `
            -Actual $coverageResult.results[0].CpuCoveragePct `
            -Name 'truncated capture reports 50 percent CPU coverage'
    }

    $transientRoot = Join-Path $tempRoot 'transient-power'
    $transientDir = Join-Path $transientRoot 'capture'
    New-Item -ItemType Directory -Path $transientDir -Force | Out-Null
    $transientCell = New-FixtureCell `
        -Directory $transientDir `
        -CaptureId 'transient' `
        -TransientPower `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $transientDir `
        -CaptureId 'transient' `
        -Cells @($transientCell)
    $transientResult = Invoke-Aggregator $transientRoot
    Assert-Equal `
        -Expected 'not_evaluated' `
        -Actual $transientResult.results[0].PowerStatus `
        -Name 'transient power change invalidates the cell'

    $sessionRoot = Join-Path $tempRoot 'transient-session'
    $sessionDir = Join-Path $sessionRoot 'capture'
    New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null
    $sessionCell = New-FixtureCell `
        -Directory $sessionDir `
        -CaptureId 'transient-session' `
        -TransientSession `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $sessionDir `
        -CaptureId 'transient-session' `
        -Cells @($sessionCell)
    $sessionResult = Invoke-Aggregator $sessionRoot
    Assert-Equal `
        -Expected 'not_evaluated' `
        -Actual $sessionResult.results[0].SessionStatus `
        -Name 'transient session lock invalidates the cell'

    $duplicateRoot = Join-Path $tempRoot 'duplicate-sys'
    $duplicateDir = Join-Path $duplicateRoot 'capture'
    New-Item -ItemType Directory -Path $duplicateDir -Force | Out-Null
    $duplicateCell = New-FixtureCell `
        -Directory $duplicateDir `
        -CaptureId 'duplicate' `
        -DuplicateSys `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $duplicateDir `
        -CaptureId 'duplicate' `
        -Cells @($duplicateCell)
    $duplicateResult = Invoke-Aggregator $duplicateRoot
    Assert-Equal `
        -Expected 'not_evaluated' `
        -Actual $duplicateResult.results[0].EnergyStatus `
        -Name 'duplicate SYS Energy Meter rows cannot pass'

    $suppressedRoot = Join-Path $tempRoot 'suppressed'
    $suppressedDir = Join-Path $suppressedRoot 'capture'
    New-Item -ItemType Directory -Path $suppressedDir -Force | Out-Null
    $suppressedCell = New-FixtureCell `
        -Directory $suppressedDir `
        -CaptureId 'suppressed' `
        -Scenario 'fullscreen-suppression' `
        -Suppressed
    Assert-True `
        -Condition (
            $suppressedCell.VisibleControlBefore.start_utc -match 'Z$' -and
            $suppressedCell.VisibleControlBefore.start_utc.Substring(11, 2) -eq
                $suppressedCell.MeasurementStartUtc.Substring(11, 2)
        ) `
        -Name (
            'fixture control uses UTC: control=' +
            $suppressedCell.VisibleControlBefore.start_utc +
            ' measurement=' +
            $suppressedCell.MeasurementStartUtc
        )
    Write-FixtureManifest `
        -Directory $suppressedDir `
        -CaptureId 'suppressed' `
        -Cells @($suppressedCell)
    $suppressedResult = Invoke-Aggregator $suppressedRoot
    Assert-True `
        -Condition $suppressedResult.success `
        -Name "suppression fixture aggregates: $($suppressedResult.error)"
    if ($suppressedResult.success) {
        Assert-Equal `
            -Expected 'pass' `
            -Actual $suppressedResult.results[0].PresentSuppressionStatus `
            -Name (
                'same-PID visible, zero suppressed, and resumed presents pass: ' +
                $suppressedResult.results[0].PresentSuppressionReason +
                '; before=' +
                $suppressedResult.results[0].VisibleControlBeforeReason +
                ' raw=' +
                $suppressedResult.results[0].VisibleControlBeforeRawPresentCount +
                '; after=' +
                $suppressedResult.results[0].VisibleControlAfterReason +
                ' raw=' +
                $suppressedResult.results[0].VisibleControlAfterRawPresentCount
            )
    }

    $wrongPidRoot = Join-Path $tempRoot 'wrong-pid'
    $wrongPidDir = Join-Path $wrongPidRoot 'capture'
    New-Item -ItemType Directory -Path $wrongPidDir -Force | Out-Null
    $wrongPidCell = New-FixtureCell `
        -Directory $wrongPidDir `
        -CaptureId 'wrong-pid' `
        -Scenario 'fullscreen-suppression' `
        -Suppressed `
        -MismatchedControlPid
    Write-FixtureManifest `
        -Directory $wrongPidDir `
        -CaptureId 'wrong-pid' `
        -Cells @($wrongPidCell)
    $wrongPidResult = Invoke-Aggregator $wrongPidRoot
    Assert-True `
        -Condition $wrongPidResult.success `
        -Name "wrong-PID fixture aggregates: $($wrongPidResult.error)"
    if ($wrongPidResult.success) {
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $wrongPidResult.results[0].PresentSuppressionStatus `
            -Name 'visible control from another PID cannot prove suppression'
    }

    $resumeRoot = Join-Path $tempRoot 'resume'
    $resumeDir = Join-Path $resumeRoot 'capture'
    New-Item -ItemType Directory -Path $resumeDir -Force | Out-Null
    $resumeCell = New-FixtureCell `
        -Directory $resumeDir `
        -CaptureId 'resume' `
        -Scenario 'fullscreen-suppression' `
        -Suppressed `
        -ResumeHasNoPresents
    Write-FixtureManifest `
        -Directory $resumeDir `
        -CaptureId 'resume' `
        -Cells @($resumeCell)
    $resumeResult = Invoke-Aggregator $resumeRoot
    Assert-True `
        -Condition $resumeResult.success `
        -Name "resume fixture aggregates: $($resumeResult.error)"
    if ($resumeResult.success) {
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $resumeResult.results[0].PresentSuppressionStatus `
            -Name 'visible HWND without resumed presents cannot prove resume'
    }

    $presentMetadataRoot = Join-Path $tempRoot 'present-metadata'
    $presentMetadataDir = Join-Path $presentMetadataRoot 'capture'
    New-Item `
        -ItemType Directory `
        -Path $presentMetadataDir `
        -Force |
        Out-Null
    $presentMetadataCell = New-FixtureCell `
        -Directory $presentMetadataDir `
        -CaptureId 'present-metadata' `
        -Scenario 'fullscreen-suppression' `
        -Suppressed
    $presentMetadataCell.PresentCapture.arguments = @('--date_time')
    Write-FixtureManifest `
        -Directory $presentMetadataDir `
        -CaptureId 'present-metadata' `
        -Cells @($presentMetadataCell)
    $presentMetadataResult = Invoke-Aggregator $presentMetadataRoot
    Assert-True `
        -Condition $presentMetadataResult.success `
        -Name (
            'invalid PresentMon provenance aggregates without passing: ' +
            $presentMetadataResult.error
        )
    if ($presentMetadataResult.success) {
        Assert-Equal `
            -Expected 'error' `
            -Actual $presentMetadataResult.results[0].PresentStatus `
            -Name 'header-only capture requires a recorded PID filter'
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $presentMetadataResult.results[0].PresentSuppressionStatus `
            -Name 'invalid PresentMon provenance cannot prove suppression'
    }

    $duplicateSampleRoot = Join-Path $tempRoot 'duplicate-sample'
    $duplicateSampleDir = Join-Path $duplicateSampleRoot 'capture'
    New-Item `
        -ItemType Directory `
        -Path $duplicateSampleDir `
        -Force |
        Out-Null
    $duplicateSampleCell = New-FixtureCell `
        -Directory $duplicateSampleDir `
        -CaptureId 'duplicate-sample' `
        -DuplicateSample `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $duplicateSampleDir `
        -CaptureId 'duplicate-sample' `
        -Cells @($duplicateSampleCell)
    $duplicateSampleResult = Invoke-Aggregator $duplicateSampleRoot
    Assert-True `
        -Condition $duplicateSampleResult.success `
        -Name "duplicate sample fixture aggregates: $(
            $duplicateSampleResult.error
        )"
    if ($duplicateSampleResult.success) {
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $duplicateSampleResult.results[0].CellPrerequisiteStatus `
            -Name 'duplicate sample indices invalidate the cell'
    }

    $windowRoot = Join-Path $tempRoot 'out-of-window'
    $windowDir = Join-Path $windowRoot 'capture'
    New-Item -ItemType Directory -Path $windowDir -Force | Out-Null
    $windowCell = New-FixtureCell `
        -Directory $windowDir `
        -CaptureId 'out-of-window' `
        -NoPresentCapture
    $windowSamplePath = Join-Path $windowDir $windowCell.SampleCsv
    $windowRows = @(Import-Csv -LiteralPath $windowSamplePath)
    $windowRows[0].sample_utc = Format-FixtureUtc (
        ([DateTimeOffset]$windowCell.MeasurementStartUtc).AddSeconds(-1)
    )
    $windowRows |
        Export-Csv -LiteralPath $windowSamplePath -NoTypeInformation
    Write-FixtureManifest `
        -Directory $windowDir `
        -CaptureId 'out-of-window' `
        -Cells @($windowCell)
    $windowResult = Invoke-Aggregator $windowRoot
    Assert-True `
        -Condition $windowResult.success `
        -Name "out-of-window fixture aggregates: $($windowResult.error)"
    if ($windowResult.success) {
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $windowResult.results[0].CellPrerequisiteStatus `
            -Name 'out-of-window sample timestamps invalidate the cell'
    }

    $gpuStatusRoot = Join-Path $tempRoot 'gpu-status'
    $gpuStatusDir = Join-Path $gpuStatusRoot 'capture'
    New-Item -ItemType Directory -Path $gpuStatusDir -Force | Out-Null
    $gpuStatusCell = New-FixtureCell `
        -Directory $gpuStatusDir `
        -CaptureId 'gpu-status' `
        -UnavailableGpuStatus `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $gpuStatusDir `
        -CaptureId 'gpu-status' `
        -Cells @($gpuStatusCell)
    $gpuStatusResult = Invoke-Aggregator $gpuStatusRoot
    Assert-True `
        -Condition $gpuStatusResult.success `
        -Name "unavailable GPU fixture aggregates: $($gpuStatusResult.error)"
    if ($gpuStatusResult.success) {
        Assert-Equal `
            -Expected 'insufficient_coverage' `
            -Actual $gpuStatusResult.results[0].GpuStatus `
            -Name 'GPU values with unavailable status cannot pass coverage'
        Assert-Equal `
            -Expected '0' `
            -Actual $gpuStatusResult.results[0].GpuValidSamples `
            -Name 'unavailable GPU rows are excluded from valid samples'
    }

    $batteryCoverageRoot = Join-Path $tempRoot 'battery-energy-coverage'
    $batteryCoverageDir = Join-Path $batteryCoverageRoot 'capture'
    New-Item `
        -ItemType Directory `
        -Path $batteryCoverageDir `
        -Force |
        Out-Null
    $batteryCoverageCell = New-FixtureCell `
        -Directory $batteryCoverageDir `
        -CaptureId 'battery-energy-coverage' `
        -PowerState 'battery' `
        -ExpectedSamples 10 `
        -ActualSamples 10 `
        -SparseBatteryTelemetry `
        -NoSysEnergy `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $batteryCoverageDir `
        -CaptureId 'battery-energy-coverage' `
        -Cells @($batteryCoverageCell)
    $batteryCoverageResult = Invoke-Aggregator $batteryCoverageRoot
    Assert-True `
        -Condition $batteryCoverageResult.success `
        -Name (
            'sparse battery fixture aggregates without passing: ' +
            $batteryCoverageResult.error
        )
    if ($batteryCoverageResult.success) {
        Assert-Equal `
            -Expected 'insufficient_coverage' `
            -Actual $batteryCoverageResult.results[0].BatteryEnergyStatus `
            -Name 'battery capacity energy requires 90 percent coverage'
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $batteryCoverageResult.results[0].EnergyStatus `
            -Name 'two battery-capacity rows cannot become energy evidence'
        Assert-Equal `
            -Expected '20' `
            -Actual $batteryCoverageResult.results[0].EnergyCoveragePct `
            -Name 'battery energy reports capacity-row coverage'
    }

    $mixedThrottleRoot = Join-Path $tempRoot 'mixed-throttle'
    $mixedThrottleDir = Join-Path $mixedThrottleRoot 'capture'
    New-Item -ItemType Directory -Path $mixedThrottleDir -Force | Out-Null
    $mixedThrottleCell = New-FixtureCell `
        -Directory $mixedThrottleDir `
        -CaptureId 'mixed-throttle' `
        -MixedThrottleState `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $mixedThrottleDir `
        -CaptureId 'mixed-throttle' `
        -Cells @($mixedThrottleCell)
    $mixedThrottleResult = Invoke-Aggregator $mixedThrottleRoot
    Assert-True `
        -Condition $mixedThrottleResult.success `
        -Name "mixed throttling fixture aggregates: $(
            $mixedThrottleResult.error
        )"
    if ($mixedThrottleResult.success) {
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $mixedThrottleResult.results[0].ThrottleStatus `
            -Name 'transient OS process-throttling state cannot pass'
        Assert-Equal `
            -Expected 'mixed' `
            -Actual $mixedThrottleResult.results[0].ProcessPowerState `
            -Name 'actual mixed OS process-throttling state is reported'
    }

    $contaminatedRoot = Join-Path $tempRoot 'contaminated-noapp'
    $contaminatedDir = Join-Path $contaminatedRoot 'capture'
    New-Item -ItemType Directory -Path $contaminatedDir -Force | Out-Null
    $contaminatedCell = New-FixtureCell `
        -Directory $contaminatedDir `
        -CaptureId 'contaminated-noapp' `
        -Scenario 'no-app-control' `
        -ContaminatedNoApp
    Write-FixtureManifest `
        -Directory $contaminatedDir `
        -CaptureId 'contaminated-noapp' `
        -Cells @($contaminatedCell)
    $contaminatedResult = Invoke-Aggregator $contaminatedRoot
    Assert-True `
        -Condition $contaminatedResult.success `
        -Name (
            'contaminated no-app fixture aggregates without passing: ' +
            $contaminatedResult.error
        )
    if ($contaminatedResult.success) {
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $contaminatedResult.results[0].ProcessAbsenceStatus `
            -Name 'no-app control requires continuous process absence'
        Assert-Equal `
            -Expected 'not_evaluated' `
            -Actual $contaminatedResult.results[0].CellPrerequisiteStatus `
            -Name 'contaminated no-app control invalidates the cell'
    }

    $escapeRoot = Join-Path $tempRoot 'artifact-escape'
    $escapeDir = Join-Path $escapeRoot 'capture'
    New-Item -ItemType Directory -Path $escapeDir -Force | Out-Null
    $escapeCell = New-FixtureCell `
        -Directory $escapeDir `
        -CaptureId 'artifact-escape' `
        -NoPresentCapture
    $escapeCell.SampleCsv = '..\escaped.csv'
    Write-FixtureManifest `
        -Directory $escapeDir `
        -CaptureId 'artifact-escape' `
        -Cells @($escapeCell)
    $escapeResult = Invoke-Aggregator $escapeRoot
    Assert-True `
        -Condition (-not $escapeResult.success) `
        -Name 'artifact path escape is rejected'
    Assert-True `
        -Condition ($escapeResult.error -match 'escapes its capture directory') `
        -Name 'artifact path escape failure is explicit'

    $denominatorRoot = Join-Path $tempRoot 'declared-denominator'
    $denominatorDir = Join-Path $denominatorRoot 'capture'
    New-Item -ItemType Directory -Path $denominatorDir -Force | Out-Null
    $denominatorCell = New-FixtureCell `
        -Directory $denominatorDir `
        -CaptureId 'declared-denominator' `
        -NoPresentCapture
    $denominatorCell.ExpectedSampleCount = 5
    Write-FixtureManifest `
        -Directory $denominatorDir `
        -CaptureId 'declared-denominator' `
        -Cells @($denominatorCell)
    $denominatorResult = Invoke-Aggregator $denominatorRoot
    Assert-True `
        -Condition (-not $denominatorResult.success) `
        -Name 'declared sample denominator is recomputed'
    Assert-True `
        -Condition ($denominatorResult.error -match 'floor\(4 / 1\) is 4') `
        -Name 'declared denominator failure reports the theoretical count'

    $schemeRoot = Join-Path $tempRoot 'mixed-schemes'
    $schemeDirA = Join-Path $schemeRoot 'a'
    $schemeDirB = Join-Path $schemeRoot 'b'
    New-Item -ItemType Directory -Path $schemeDirA -Force | Out-Null
    New-Item -ItemType Directory -Path $schemeDirB -Force | Out-Null
    $schemeCellA = New-FixtureCell `
        -Directory $schemeDirA `
        -CaptureId 'scheme-a' `
        -PowerScheme 'scheme-a' `
        -NoPresentCapture
    $schemeCellB = New-FixtureCell `
        -Directory $schemeDirB `
        -CaptureId 'scheme-b' `
        -PowerState 'battery' `
        -PowerScheme 'scheme-b' `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $schemeDirA `
        -CaptureId 'scheme-a' `
        -Cells @($schemeCellA)
    Write-FixtureManifest `
        -Directory $schemeDirB `
        -CaptureId 'scheme-b' `
        -Cells @($schemeCellB)
    $schemeResult = Invoke-Aggregator $schemeRoot
    Assert-True `
        -Condition (-not $schemeResult.success) `
        -Name 'cross-state mixed power schemes are rejected'
    Assert-True `
        -Condition ($schemeResult.error -match 'across compared states') `
        -Name 'mixed-scheme failure is explicit'

    $visibleRoot = Join-Path $tempRoot 'visible-budget-definition'
    $visibleDir = Join-Path $visibleRoot 'capture'
    New-Item -ItemType Directory -Path $visibleDir -Force | Out-Null
    $visibleCells = @(
        New-FixtureCell `
            -Directory $visibleDir `
            -CaptureId 'visible-budget' `
            -Run 1
        New-FixtureCell `
            -Directory $visibleDir `
            -CaptureId 'visible-budget' `
            -Run 2
        New-FixtureCell `
            -Directory $visibleDir `
            -CaptureId 'visible-budget' `
            -Run 3
    )
    Write-FixtureManifest `
        -Directory $visibleDir `
        -CaptureId 'visible-budget' `
        -Cells $visibleCells
    $visibleResult = Invoke-Aggregator $visibleRoot
    Assert-True `
        -Condition $visibleResult.success `
        -Name "visible budget fixture aggregates: $($visibleResult.error)"
    if ($visibleResult.success) {
        $selfReferenceGates = @(
            $visibleResult.budgets |
                Where-Object { $_.Gate -eq 'visible-reference-envelope' }
        )
        $definitionGates = @(
            $visibleResult.budgets |
                Where-Object { $_.Gate -eq 'visible-budget-definition' }
        )
        Assert-Equal `
            -Expected 0 `
            -Actual $selfReferenceGates.Count `
            -Name 'visible cells are not tested against their own envelope'
        Assert-Equal `
            -Expected 5 `
            -Actual $definitionGates.Count `
            -Name 'visible baseline emits one definition per resource metric'
        Assert-True `
            -Condition (@(
                $definitionGates |
                    Where-Object { $_.Status -ne 'pass' }
            ).Count -eq 0) `
            -Name 'complete visible repetitions define provisional budgets'
    }

    $mixedRoot = Join-Path $tempRoot 'mixed-provenance'
    $mixedDirA = Join-Path $mixedRoot 'a'
    $mixedDirB = Join-Path $mixedRoot 'b'
    New-Item -ItemType Directory -Path $mixedDirA -Force | Out-Null
    New-Item -ItemType Directory -Path $mixedDirB -Force | Out-Null
    $mixedCellA = New-FixtureCell `
        -Directory $mixedDirA `
        -CaptureId 'mixed-a' `
        -NoPresentCapture
    $mixedCellB = New-FixtureCell `
        -Directory $mixedDirB `
        -CaptureId 'mixed-b' `
        -NoPresentCapture
    Write-FixtureManifest `
        -Directory $mixedDirA `
        -CaptureId 'mixed-a' `
        -Cells @($mixedCellA) `
        -ExeHash 'exe-a'
    Write-FixtureManifest `
        -Directory $mixedDirB `
        -CaptureId 'mixed-b' `
        -Cells @($mixedCellB) `
        -ExeHash 'exe-b'
    $mixedResult = Invoke-Aggregator $mixedRoot
    Assert-True -Condition (-not $mixedResult.success) `
        -Name 'mixed executable provenance is rejected'
    Assert-True `
        -Condition ($mixedResult.error -match 'mixed qualification provenance') `
        -Name 'mixed provenance failure is explicit'
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Runtime qualification tests: $script:passed passed, $script:failed failed"
if ($script:failed -gt 0) {
    exit 1
}
