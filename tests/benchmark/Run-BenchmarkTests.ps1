[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$modulePath = Join-Path $repoRoot 'tools\benchmark\Benchmark.Common.psm1'
$aggregatePath = Join-Path $repoRoot 'tools\benchmark\Aggregate-Results.ps1'

Import-Module $modulePath -Force

$script:assertions = 0

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )
    $script:assertions++
    if (-not $Condition) {
        throw "Assertion failed: $Message"
    }
}

function Assert-Equal {
    param(
        [AllowNull()] $Actual,
        [AllowNull()] $Expected,
        [Parameter(Mandatory)] [string] $Message
    )
    $script:assertions++
    if ($Actual -ne $Expected) {
        throw (
            "Assertion failed: $Message. Expected '$Expected', actual '$Actual'."
        )
    }
}

function Assert-Near {
    param(
        [Parameter(Mandatory)] [double] $Actual,
        [Parameter(Mandatory)] [double] $Expected,
        [Parameter(Mandatory)] [double] $Tolerance,
        [Parameter(Mandatory)] [string] $Message
    )
    $script:assertions++
    if ([math]::Abs($Actual - $Expected) -gt $Tolerance) {
        throw (
            "Assertion failed: $Message. Expected $Expected +/- $Tolerance, " +
            "actual $Actual."
        )
    }
}

function Assert-NullOrEmpty {
    param(
        [AllowNull()] $Actual,
        [Parameter(Mandatory)] [string] $Message
    )
    $script:assertions++
    if ($null -ne $Actual -and
        -not [string]::IsNullOrWhiteSpace([string]$Actual)) {
        throw "Assertion failed: $Message. Actual '$Actual'."
    }
}

function Write-Json {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $Value
    )
    $Value |
        ConvertTo-Json -Depth 12 |
        Out-File -LiteralPath $Path -Encoding utf8
}

function Write-Lines {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $Lines
    )
    $Lines | Out-File -LiteralPath $Path -Encoding utf8
}

$validGpu = ConvertFrom-GpuEngineInstance `
    'pid_4242_luid_0x00000000_0x00012B1F_phys_0_eng_2_engtype_Copy'
Assert-True $validGpu.parsed 'GPU instance should parse'
Assert-Equal $validGpu.pid 4242 'GPU PID should parse'
Assert-Equal $validGpu.adapter_luid `
    '0x00000000_0x00012B1F' `
    'GPU LUID should be retained'
Assert-Equal $validGpu.engine_type 'Copy' 'GPU engine type should parse'

$invalidGpu = ConvertFrom-GpuEngineInstance 'not-a-gpu-engine'
Assert-True (-not $invalidGpu.parsed) 'Malformed GPU instance must be rejected'
Assert-NullOrEmpty $invalidGpu.pid 'Malformed GPU instance must not invent a PID'

Assert-NullOrEmpty (Get-Mean @($null, $null)) `
    'Mean of unavailable values must stay null'
Assert-Near (Get-Mean @(0.0, 2.0)) 1.0 0.0001 `
    'Measured zero must participate in a mean'
Assert-Near (
    Get-Percentile -Values @(0.0, 1.0, 3.0) -Percentile 95
) 3.0 0.0001 'Percentile should preserve measured zero and high values'
Assert-NullOrEmpty (Get-StandardDeviation @()) `
    'Standard deviation of no values must stay null'

$sampler = New-BenchmarkCounterSampler
try {
    Start-Sleep -Milliseconds 50
    $liveSample = Get-BenchmarkCounterSample `
        -Sampler $sampler `
        -ProcessId $PID
    Assert-True (
        $liveSample.status -in @('available', 'partial')
    ) 'Hardware-independent counter sampling should return a status'
    Assert-True (
        $sampler.capabilities.gpu_engine.status -in
            @('available', 'unsupported')
    ) 'GPU capability must be explicit'
    Assert-True (
        $liveSample.gpu_status -in
            @('available', 'no_process_instance', 'unsupported', 'error')
    ) 'GPU sample availability must be explicit'
} finally {
    $sampler.query.Dispose()
}

$power = Get-BenchmarkPowerSnapshot
Assert-True (
    $power.ac_line_status -in @('ac', 'battery', 'unknown')
) 'Power source must be normalized'
Assert-True (
    $power.battery_telemetry_status -in
        @('available', 'unsupported', 'no_instance', 'error')
) 'Battery telemetry capability must be explicit'

$tempRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "DesktopGrass-BenchmarkTests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $legacyDir = Join-Path $tempRoot 'legacy'
    New-Item -ItemType Directory -Path $legacyDir | Out-Null
    Write-Json -Path (Join-Path $legacyDir 'machine.json') -Value ([ordered]@{
        HostName = 'fixture'
        OSVersion = 'fixture-os'
        LogicalCpus = 8
        UtcStart = '2026-01-01T00:00:00Z'
        Exe = 'fixture.exe'
        DurationSec = 2
        TargetFps = 24
        SampleIntervalSec = 1
        Seed = 0
    })
    $legacyManifest = @(
        [pscustomobject]@{
            CellTag = 'scene0-grass-baseline-run1'
            Scene = 0
            SceneName = 'Grass'
            Variant = 'baseline'
            Run = 1
            DurationSec = 2
            TargetFps = 24
            FrameCsv = 'legacy.frames.csv'
            SampleCsv = 'legacy.samples.csv'
            LogFile = 'legacy.log.txt'
            ExitCode = 0
            WallSec = 2.2
            IoReadBytes = 1024
            IoWriteBytes = 2048
        }
    )
    Write-Json `
        -Path (Join-Path $legacyDir 'manifest.json') `
        -Value $legacyManifest
    Write-Lines -Path (Join-Path $legacyDir 'legacy.frames.csv') -Lines @(
        '# scene=0 duration_s=2 target_fps=24',
        'frame_index,t_seconds,dt_ms,render_ms',
        '0,0.0,41.6,1.0',
        '1,0.0416,41.6,3.0'
    )
    @(
        [pscustomobject]@{
            t_sec = 1
            cpu_pct_normalized = 10
            working_set_mb = 20
            private_mb = 12
            threads = 2
            handles = 20
            io_read_bytes = 1024
            io_write_bytes = 2048
            io_other_bytes = 0
        },
        [pscustomobject]@{
            t_sec = 2
            cpu_pct_normalized = 20
            working_set_mb = 22
            private_mb = 13
            threads = 2
            handles = 20
            io_read_bytes = 1024
            io_write_bytes = 2048
            io_other_bytes = 0
        }
    ) | Export-Csv `
        -LiteralPath (Join-Path $legacyDir 'legacy.samples.csv') `
        -NoTypeInformation `
        -Encoding utf8
    Write-Lines -Path (Join-Path $legacyDir 'legacy.log.txt') -Lines @(
        '[benchmark] scene=0 frames=2 duration_s=2.000 fps=1.00 exit=0'
    )

    & $aggregatePath -ResultDir $legacyDir | Out-Null
    $legacyResult = Import-Csv (
        Join-Path $legacyDir 'results.csv'
    ) | Select-Object -First 1
    Assert-Equal $legacyResult.WorkloadState 'visible' `
        'Legacy schema should default to visible'
    Assert-Equal $legacyResult.PowerState 'legacy-unknown' `
        'Legacy schema should preserve unknown power provenance'
    Assert-Near ([double]$legacyResult.CpuCorePctMean) 15.0 0.001 `
        'Legacy CPU alias should remain readable'
    Assert-Near ([double]$legacyResult.CpuPctMean) 15.0 0.001 `
        'Schema-v1 CPU result column should remain present'
    Assert-NullOrEmpty $legacyResult.GpuBusiestPctMean `
        'Missing legacy GPU data must not become zero'
    Assert-Equal $legacyResult.BudgetStatus 'not_evaluated' `
        'Legacy missing metrics must never pass a budget'

    $newDir = Join-Path $tempRoot 'new'
    New-Item -ItemType Directory -Path $newDir | Out-Null
    Write-Json -Path (Join-Path $newDir 'machine.json') -Value ([ordered]@{
        schema_version = 2
        host_name = 'fixture'
        os = [ordered]@{
            caption = 'fixture-os'
            version = '10.0'
            build_number = '1'
        }
        logical_cpus = 8
        platform = 'x64'
        HostName = 'fixture'
        OSVersion = '10.0'
        LogicalCpus = 8
        UtcStart = '2026-01-01T00:00:00Z'
        Exe = 'fixture.exe'
        DurationSec = 2
        TargetFps = 24
        SampleIntervalSec = 1
        counter_capabilities = [ordered]@{
            gpu_engine = [ordered]@{
                status = 'available'
                source = 'fixture'
                reason = $null
            }
        }
    })

    $available = [ordered]@{
        status = 'available'
        source = 'fixture'
        reason = $null
    }
    $unsupported = [ordered]@{
        status = 'unsupported'
        source = 'fixture'
        reason = 'fixture unavailable'
    }
    $newManifest = @(
        [pscustomobject]@{
            SchemaVersion = 2
            CellTag = 'scene0-grass-visible-baseline-run1'
            Scene = 0
            SceneName = 'Grass'
            Variant = 'baseline'
            WorkloadState = 'visible'
            PowerState = 'ac'
            Run = 1
            DurationSec = 2
            TargetFps = 24
            FrameCsv = 'visible.frames.csv'
            SampleCsv = 'visible.samples.csv'
            GpuCsv = 'visible.gpu.csv'
            PowerCsv = 'visible.power.csv'
            EnergyCsv = 'visible.energy.csv'
            LogFile = 'visible.log.txt'
            ExitCode = 0
            WallSec = 2.1
            SampledWallSec = 1
            IoReadBytes = 0
            IoWriteBytes = 0
            PowerContextValid = $true
            Telemetry = [ordered]@{
                io = $available
                gpu_engine = $available
                process_context_switches = $available
                energy_meter = $available
                battery = $unsupported
            }
        },
        [pscustomobject]@{
            SchemaVersion = 2
            CellTag = 'scene0-grass-paused-baseline-run1'
            Scene = 0
            SceneName = 'Grass'
            Variant = 'baseline'
            WorkloadState = 'paused'
            PowerState = 'ac'
            Run = 1
            DurationSec = 2
            TargetFps = 24
            FrameCsv = 'paused.frames.csv'
            SampleCsv = 'paused.samples.csv'
            GpuCsv = 'paused.gpu.csv'
            PowerCsv = 'paused.power.csv'
            EnergyCsv = 'paused.energy.csv'
            LogFile = 'paused.log.txt'
            ExitCode = 0
            WallSec = 2.1
            SampledWallSec = 1
            IoReadBytes = $null
            IoWriteBytes = $null
            PowerContextValid = $false
            Telemetry = [ordered]@{
                io = $unsupported
                gpu_engine = $unsupported
                process_context_switches = $available
                energy_meter = $unsupported
                battery = $unsupported
            }
        }
    )
    Write-Json `
        -Path (Join-Path $newDir 'manifest.json') `
        -Value $newManifest

    Write-Lines -Path (Join-Path $newDir 'visible.frames.csv') -Lines @(
        '# scene=0 duration_s=2 target_fps=24',
        'frame_index,t_seconds,dt_ms,render_ms',
        '0,0.0,41.6,1.0',
        '1,0.0416,41.6,2.0'
    )
    Write-Lines -Path (Join-Path $newDir 'paused.frames.csv') -Lines @(
        '# scene=0 duration_s=2 target_fps=24',
        'frame_index,t_seconds,dt_ms,render_ms'
    )

    @(
        [pscustomobject]@{
            sample_index = 0
            sample_measured = $true
            t_sec = 1
            cpu_core_pct = 5
            cpu_pct_normalized = 5
            working_set_mb = 20
            private_mb = 12
            gpu_busiest_engine_pct = 0
            process_context_switches_per_sec = 24
        },
        [pscustomobject]@{
            sample_index = 1
            sample_measured = $true
            t_sec = 2
            cpu_core_pct = 7
            cpu_pct_normalized = 7
            working_set_mb = 21
            private_mb = 13
            gpu_busiest_engine_pct = 2
            process_context_switches_per_sec = 30
        }
    ) | Export-Csv `
        -LiteralPath (Join-Path $newDir 'visible.samples.csv') `
        -NoTypeInformation `
        -Encoding utf8
    @(
        [pscustomobject]@{
            sample_index = 0
            sample_measured = $true
            t_sec = 1
            cpu_core_pct = 0
            cpu_pct_normalized = 0
            working_set_mb = 20
            private_mb = 12
            gpu_busiest_engine_pct = $null
            process_context_switches_per_sec = 1
        }
    ) | Export-Csv `
        -LiteralPath (Join-Path $newDir 'paused.samples.csv') `
        -NoTypeInformation `
        -Encoding utf8

    @(
        [pscustomobject]@{
            sample_index = 0
            t_sec = 1
            pid = 1
            instance_name =
                'pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D'
            adapter_luid = '0x0_0x1'
            physical_adapter = 0
            engine_index = 0
            engine_type = '3D'
            utilization_pct = 0
            counter_status = 0
        },
        [pscustomobject]@{
            sample_index = 1
            t_sec = 2
            pid = 1
            instance_name =
                'pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D'
            adapter_luid = '0x0_0x1'
            physical_adapter = 0
            engine_index = 0
            engine_type = '3D'
            utilization_pct = 2
            counter_status = 0
        }
    ) | Export-Csv `
        -LiteralPath (Join-Path $newDir 'visible.gpu.csv') `
        -NoTypeInformation `
        -Encoding utf8
    Write-Lines -Path (Join-Path $newDir 'paused.gpu.csv') -Lines @(
        'sample_index,t_sec,pid,instance_name,adapter_luid,physical_adapter,engine_index,engine_type,utilization_pct,counter_status'
    )

    foreach ($prefix in @('visible', 'paused')) {
        @(
            [pscustomobject]@{
                sample_index = 0
                sample_measured = $true
                t_sec = 1
                ac_line_status = 'ac'
                battery_present = $true
                battery_percent = 100
                battery_saver = $false
                active_power_scheme_guid = 'fixture'
                battery_telemetry_status = 'available'
                battery_remaining_capacity_mwh = 1000
                battery_discharge_rate_mw = 0
            },
            [pscustomobject]@{
                sample_index = 1
                sample_measured = $true
                t_sec = 2
                ac_line_status = 'ac'
                battery_present = $true
                battery_percent = 100
                battery_saver = $false
                active_power_scheme_guid = 'fixture'
                battery_telemetry_status = 'available'
                battery_remaining_capacity_mwh = 1000
                battery_discharge_rate_mw = 0
            }
        ) | Export-Csv `
            -LiteralPath (Join-Path $newDir "$prefix.power.csv") `
            -NoTypeInformation `
            -Encoding utf8
    }

    @(
        [pscustomobject]@{
            sample_index = 0
            t_sec = 1
            meter = 'sys'
            power_mw = 10000
            counter_status = 0
        },
        [pscustomobject]@{
            sample_index = 1
            t_sec = 2
            meter = 'sys'
            power_mw = 12000
            counter_status = 0
        }
    ) | Export-Csv `
        -LiteralPath (Join-Path $newDir 'visible.energy.csv') `
        -NoTypeInformation `
        -Encoding utf8
    Write-Lines -Path (Join-Path $newDir 'paused.energy.csv') -Lines @(
        'sample_index,t_sec,meter,power_mw,counter_status'
    )
    Write-Lines -Path (Join-Path $newDir 'visible.log.txt') -Lines @(
        '[benchmark] scene=0 frames=2 duration_s=2.000 fps=1.00 exit=0'
    )
    Write-Lines -Path (Join-Path $newDir 'paused.log.txt') -Lines @(
        '[benchmark] scene=0 frames=0 duration_s=2.000 fps=0.00 exit=0'
    )

    try {
        & $aggregatePath -ResultDir $newDir | Out-Null
    } catch {
        Write-Host $_.ScriptStackTrace
        throw
    }
    $newResults = @(Import-Csv (Join-Path $newDir 'results.csv'))
    Assert-Equal $newResults.Count 2 `
        'Visible and future paused states should aggregate separately'
    $visibleResult = $newResults |
        Where-Object { $_.WorkloadState -eq 'visible' }
    $pausedResult = $newResults |
        Where-Object { $_.WorkloadState -eq 'paused' }
    Assert-Near ([double]$visibleResult.GpuBusiestPctMean) 1.0 0.001 `
        'Measured GPU zero should remain valid'
    Assert-Near (
        [double]$visibleResult.EnergyMeterSysPowerMwMean
    ) 11000 0.001 'System energy meter should aggregate'
    Assert-Equal $visibleResult.BatteryEnergyStatus 'not_applicable' `
        'AC battery energy should be explicitly not applicable'
    Assert-NullOrEmpty $pausedResult.GpuBusiestPctMean `
        'Unavailable paused GPU must remain null'
    Assert-Equal $pausedResult.GpuMetricStatus 'unsupported' `
        'Unavailable paused GPU should retain provenance'
    Assert-Equal $pausedResult.BudgetStatus 'not_evaluated' `
        'Unavailable metrics must never pass a future budget'
    Assert-Near ([double]$pausedResult.EffectiveFpsMean) 0.0 0.001 `
        'A measured empty-frame paused run should retain valid zero FPS'

    $engineResults = @(Import-Csv (
        Join-Path $newDir 'engine-results.csv'
    ))
    Assert-Equal $engineResults.Count 1 `
        'Only measured GPU engine groups should be emitted'
    Assert-Near ([double]$engineResults[0].UtilizationPctMean) 1.0 0.001 `
        'Raw per-engine utilization should aggregate'
} finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force
}

Write-Host (
    "Benchmark script tests passed ($script:assertions assertions)."
) -ForegroundColor Green
