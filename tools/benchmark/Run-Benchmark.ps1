# Drive DesktopGrass.Native --benchmark across scenes and collect versioned,
# provenance-bearing process, GPU, scheduling, power, and battery evidence.

[CmdletBinding()]
param(
    [string] $ResultsRoot = (Join-Path $PSScriptRoot 'results'),

    # 0=Grass 1=Desert 2=Winter 3=Autumn 4=Ocean.
    [int[]] $Scenes = @(0, 1, 2, 3, 4),

    [string[]] $Variants = @('baseline'),

    # This benchmark-mode driver measures only the visible state. Production
    # suppression states use Run-RuntimeQualification.ps1.
    [ValidateSet('visible')]
    [string] $WorkloadState = 'visible',

    [ValidateRange(1, 100)]
    [int] $Runs = 3,

    [ValidateRange(1, 86400)]
    [int] $DurationSec = 60,

    [ValidateRange(1, 240)]
    [int] $TargetFps = 24,

    [uint64] $Seed = 0,

    [ValidateRange(0.25, 60.0)]
    [double] $SampleIntervalSec = 1.0,

    [ValidateSet('any', 'ac', 'battery')]
    [string] $ExpectedPowerSource = 'any',

    [ValidateSet('any', 'on', 'off')]
    [string] $ExpectedBatterySaver = 'any',

    [switch] $RequireGpuTelemetry,
    [switch] $RequireWakeTelemetry,
    [switch] $RequireEnergyMeter,
    [switch] $RequireBatteryTelemetry,

    [switch] $SkipBuild,

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

    [string] $Vcxproj = (
        Join-Path $PSScriptRoot `
            '..\..\src\DesktopGrass.Native\DesktopGrass.Native.vcxproj'
    ),

    [string] $VcvarsBat =
        'C:\Program Files\Microsoft Visual Studio\18\Enterprise\VC\Auxiliary\Build\vcvars64.bat',

    [switch] $HideWindow
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'Benchmark.Common.psm1') -Force

if (-not $PSBoundParameters.ContainsKey('Exe')) {
    $Exe = Join-Path $PSScriptRoot (
        "..\..\src\DesktopGrass.Native\out\$Platform\Release\DesktopGrass.Native.exe"
    )
}

if (-not ('DesktopGrass.Bench.Io' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace DesktopGrass.Bench
{
    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    public static class Io
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessIoCounters(
            IntPtr process,
            out IoCounters counters);
    }
}
'@
}

function Get-ProcIoCounters {
    param([Parameter(Mandatory)] [System.Diagnostics.Process] $Process)

    $counters = [DesktopGrass.Bench.IoCounters]::new()
    try {
        $handle = $Process.Handle
    } catch {
        return [pscustomobject]@{
            status = 'error'
            reason = $_.Exception.Message
            counters = $null
        }
    }

    if (-not [DesktopGrass.Bench.Io]::GetProcessIoCounters(
        $handle,
        [ref]$counters
    )) {
        $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        return [pscustomobject]@{
            status = 'error'
            reason = "GetProcessIoCounters failed with Win32 error $errorCode."
            counters = $null
        }
    }

    return [pscustomobject]@{
        status = 'available'
        reason = $null
        counters = $counters
    }
}

function Get-PowerStateKey {
    param([Parameter(Mandatory)] $Snapshot)

    if ($Snapshot.ac_line_status -eq 'ac') {
        return 'ac'
    }
    if ($Snapshot.ac_line_status -eq 'battery' -and
        $Snapshot.battery_saver -eq $true) {
        return 'battery-saver'
    }
    if ($Snapshot.ac_line_status -eq 'battery') {
        return 'battery'
    }
    return 'unknown'
}

function Assert-ExpectedPowerState {
    param(
        [Parameter(Mandatory)] $Snapshot,
        [Parameter(Mandatory)] [string] $When
    )

    if ($ExpectedPowerSource -ne 'any' -and
        $Snapshot.ac_line_status -ne $ExpectedPowerSource) {
        throw (
            "Expected power source '$ExpectedPowerSource' $When, but Windows " +
            "reported '$($Snapshot.ac_line_status)'."
        )
    }

    if ($ExpectedBatterySaver -ne 'any') {
        $expectedSaver = $ExpectedBatterySaver -eq 'on'
        if ($null -eq $Snapshot.battery_saver -or
            $Snapshot.battery_saver -ne $expectedSaver) {
            throw (
                "Expected battery saver '$ExpectedBatterySaver' $When, but " +
                "Windows reported '$($Snapshot.battery_saver)'."
            )
        }
    }
}

function Assert-RequiredTelemetry {
    param(
        [Parameter(Mandatory)] $CounterSampler,
        [Parameter(Mandatory)] $PowerSnapshot
    )

    if ($RequireGpuTelemetry -and
        $CounterSampler.capabilities.gpu_engine.status -ne 'available') {
        throw (
            'GPU telemetry was required but is unavailable: ' +
            $CounterSampler.capabilities.gpu_engine.reason
        )
    }
    if ($RequireWakeTelemetry -and (
        $CounterSampler.capabilities.thread_pid.status -ne 'available' -or
        $CounterSampler.capabilities.thread_context_switches.status -ne
            'available'
    )) {
        throw 'Thread context-switch telemetry was required but is unavailable.'
    }
    if ($RequireEnergyMeter -and
        $CounterSampler.capabilities.energy_meter_power.status -ne 'available') {
        throw (
            'Windows Energy Meter telemetry was required but is unavailable: ' +
            $CounterSampler.capabilities.energy_meter_power.reason
        )
    }
    if ($RequireBatteryTelemetry -and
        $PowerSnapshot.battery_telemetry_status -ne 'available') {
        throw (
            'Battery telemetry was required but is unavailable: ' +
            $PowerSnapshot.error
        )
    }
}

function Get-ObservedMetricStatus {
    param(
        [Parameter(Mandatory)] $Capability,
        [Parameter(Mandatory)] [int] $ValidSamples,
        [Parameter(Mandatory)] [int] $ErrorSamples,
        [Parameter(Mandatory)] [string] $Source,
        [string] $NoSampleReason = 'No valid samples were observed.'
    )

    if ($Capability.status -ne 'available') {
        return $Capability
    }
    if ($ValidSamples -eq 0) {
        return New-MetricStatus `
            -Status $(if ($ErrorSamples -gt 0) { 'error' } else { 'unsupported' }) `
            -Source $Source `
            -Reason $NoSampleReason
    }
    if ($ErrorSamples -gt 0) {
        return New-MetricStatus `
            -Status partial `
            -Source $Source `
            -Reason "$ErrorSamples sample(s) failed."
    }
    return New-MetricStatus -Status available -Source $Source
}

$sceneNames = @('Grass', 'Desert', 'Winter', 'Autumn', 'Ocean')
$variantsNormalized = @(
    foreach ($variant in $Variants) {
        if ([string]::IsNullOrWhiteSpace($variant) -or
            $variant -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
            throw "Invalid variant '$variant'. Use letters, digits, dot, dash, or underscore."
        }
        $variant
    }
)

foreach ($scene in $Scenes) {
    if ($scene -lt 0 -or $scene -ge $sceneNames.Count) {
        throw "Scene index $scene out of range (0..$($sceneNames.Count - 1))."
    }
}

if (-not $SkipBuild) {
    Write-Host (
        "== Building DesktopGrass.Native (Release|$Platform) =="
    ) -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $VcvarsBat)) {
        throw "vcvars64.bat not found at $VcvarsBat; pass -VcvarsBat or -SkipBuild."
    }
    $projectPath = (Resolve-Path -LiteralPath $Vcxproj).Path
    $buildCommand = (
        "call `"$VcvarsBat`" >nul && " +
        "msbuild `"$projectPath`" /p:Configuration=Release " +
        "/p:Platform=$Platform /m /nologo /v:m"
    )
    & $env:ComSpec /c $buildCommand
    if ($LASTEXITCODE -ne 0) {
        throw "msbuild failed with exit $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $Exe)) {
    throw "Benchmark exe not found at $Exe. Build it first or pass -Exe."
}
$Exe = (Resolve-Path -LiteralPath $Exe).Path

$counterSampler = New-BenchmarkCounterSampler
try {
    $initialPower = Get-BenchmarkPowerSnapshot
    Assert-RequiredTelemetry `
        -CounterSampler $counterSampler `
        -PowerSnapshot $initialPower
    Assert-ExpectedPowerState -Snapshot $initialPower -When 'before the sweep'

    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH-mm-ssZ')
    $resultDir = Join-Path $ResultsRoot $stamp
    New-Item -ItemType Directory -Force -Path $resultDir | Out-Null
    Write-Host "== Results dir: $resultDir ==" -ForegroundColor Cyan

    $sweepParameters = [ordered]@{
        duration_sec = $DurationSec
        target_fps = $TargetFps
        sample_interval_sec = $SampleIntervalSec
        seed = $Seed
        scenes = $Scenes
        variants = $variantsNormalized
        workload_state = $WorkloadState
        expected_power_source = $ExpectedPowerSource
        expected_battery_saver = $ExpectedBatterySaver
    }
    $machine = Get-BenchmarkMachineContext `
        -Exe $Exe `
        -Platform $Platform `
        -CounterSampler $counterSampler `
        -SweepParameters $sweepParameters

    # Preserve the schema-v1 top-level fields for existing consumers.
    $machine['HostName'] = $machine.host_name
    $machine['OSVersion'] = $machine.os.version
    $machine['LogicalCpus'] = $machine.logical_cpus
    $machine['UtcStart'] = $machine.utc_start
    $machine['Exe'] = $machine.executable.path
    $machine['DurationSec'] = $DurationSec
    $machine['TargetFps'] = $TargetFps
    $machine['SampleIntervalSec'] = $SampleIntervalSec
    $machine['Seed'] = $Seed

    $machine |
        ConvertTo-Json -Depth 12 |
        Out-File `
            -LiteralPath (Join-Path $resultDir 'machine.json') `
            -Encoding utf8

    $manifest = [System.Collections.Generic.List[object]]::new()
    $cellNumber = 0
    $totalCells = $Scenes.Count * $variantsNormalized.Count * $Runs

    foreach ($scene in $Scenes) {
        $sceneName = $sceneNames[$scene]
        foreach ($variant in $variantsNormalized) {
            for ($run = 1; $run -le $Runs; $run++) {
                $cellNumber++
                $powerBefore = Get-BenchmarkPowerSnapshot
                Assert-ExpectedPowerState `
                    -Snapshot $powerBefore `
                    -When "before scene $sceneName run $run"
                $powerState = Get-PowerStateKey $powerBefore

                $cellTag = (
                    'scene{0}-{1}-{2}-{3}-run{4}' -f
                        $scene,
                        $sceneName.ToLowerInvariant(),
                        $WorkloadState,
                        $variant,
                        $run
                )
                $frameCsv = Join-Path $resultDir "$cellTag.frames.csv"
                $sampleCsv = Join-Path $resultDir "$cellTag.samples.csv"
                $gpuCsv = Join-Path $resultDir "$cellTag.gpu.csv"
                $powerCsv = Join-Path $resultDir "$cellTag.power.csv"
                $energyCsv = Join-Path $resultDir "$cellTag.energy.csv"
                $logFile = Join-Path $resultDir "$cellTag.log.txt"

                Write-Host (
                    "[$cellNumber/$totalCells] $cellTag ($powerState)"
                ) -ForegroundColor Yellow

                $exeArgs = @(
                    '--benchmark',
                    "--scene=$scene",
                    "--duration=$DurationSec",
                    "--fps=$TargetFps",
                    "--out=$frameCsv"
                )
                if ($Seed -ne 0) {
                    $exeArgs += '--seed=0x' + $Seed.ToString('X')
                }
                if ($HideWindow.IsPresent) {
                    $exeArgs += '--hidden=1'
                }

                $startTime = Get-Date
                $process = Start-Process `
                    -FilePath $Exe `
                    -ArgumentList $exeArgs `
                    -PassThru `
                    -RedirectStandardOutput $logFile

                $samples = [System.Collections.Generic.List[object]]::new()
                $gpuSamples = [System.Collections.Generic.List[object]]::new()
                $powerSamples = [System.Collections.Generic.List[object]]::new()
                $energySamples = [System.Collections.Generic.List[object]]::new()
                $sampleErrors = [System.Collections.Generic.List[string]]::new()
                $deadline = $startTime.AddSeconds(
                    [math]::Max(15, $DurationSec * 2)
                )
                $previousCpu = [TimeSpan]::Zero
                $previousTime = $null
                $firstSample = $true
                $sampleIndex = 0
                $gpuValidSamples = 0
                $gpuErrorSamples = 0
                $contextValidSamples = 0
                $contextErrorSamples = 0
                $energyValidSamples = 0
                $energyErrorSamples = 0
                $batteryValidSamples = 0
                $batteryCapacityValidSamples = 0
                $batteryErrorSamples = 0
                $ioValidSamples = 0
                $ioErrorSamples = 0

                while (-not $process.HasExited -and (Get-Date) -lt $deadline) {
                    Start-Sleep -Milliseconds (
                        [int]($SampleIntervalSec * 1000)
                    )
                    try {
                        $liveProcess = Get-Process `
                            -Id $process.Id `
                            -ErrorAction Stop
                    } catch {
                        break
                    }

                    $now = Get-Date
                    $elapsedSec = ($now - $startTime).TotalSeconds
                    try {
                        $cpu = $liveProcess.TotalProcessorTime
                        $workingSetMb = [math]::Round(
                            $liveProcess.WorkingSet64 / 1MB,
                            3
                        )
                        $privateMb = [math]::Round(
                            $liveProcess.PrivateMemorySize64 / 1MB,
                            3
                        )
                        $processThreads = $liveProcess.Threads
                        $threadCount = if ($null -ne $processThreads) {
                            $processThreads.Count
                        } else {
                            $null
                        }
                        $handleCount = $liveProcess.HandleCount
                    } catch {
                        if ($process.HasExited) {
                            break
                        }
                        throw
                    }
                    $cpuCorePct = $null
                    if (-not $firstSample -and $null -ne $previousTime) {
                        $intervalSec = ($now - $previousTime).TotalSeconds
                        if ($intervalSec -gt 0) {
                            $cpuSec = ($cpu - $previousCpu).TotalSeconds
                            # One fully occupied logical core is 100%.
                            $cpuCorePct = ($cpuSec / $intervalSec) * 100.0
                        }
                    }

                    $io = Get-ProcIoCounters -Process $liveProcess
                    if ($io.status -eq 'available') {
                        $ioValidSamples++
                    } else {
                        $ioErrorSamples++
                        $sampleErrors.Add("io sample $sampleIndex`: $($io.reason)")
                    }

                    $counterSample = Get-BenchmarkCounterSample `
                        -Sampler $counterSampler `
                        -ProcessId $process.Id
                    $powerSample = Get-BenchmarkPowerSnapshot
                    if ($powerSample.error) {
                        $sampleErrors.Add(
                            "power sample $sampleIndex`: $($powerSample.error)"
                        )
                    }

                    # The first PDH interval spans process startup and the gap
                    # since the previous cell, so keep it only as a priming row.
                    $sampleIsMeasured = -not $firstSample
                    if ($sampleIsMeasured) {
                        if ($counterSample.gpu_status -eq 'available') {
                            $gpuValidSamples++
                        } elseif ($counterSample.gpu_status -eq 'error') {
                            $gpuErrorSamples++
                        }
                        if ($counterSample.context_switch_status -eq 'available') {
                            $contextValidSamples++
                        } elseif ($counterSample.context_switch_status -eq 'error') {
                            $contextErrorSamples++
                        }
                        if ($counterSample.energy_meter_status -eq 'available') {
                            $energyValidSamples++
                        } elseif ($counterSample.energy_meter_status -eq 'error') {
                            $energyErrorSamples++
                        }
                        if ($powerSample.battery_telemetry_status -eq 'available') {
                            $batteryValidSamples++
                            if ($null -ne
                                $powerSample.battery_remaining_capacity_mwh) {
                                $batteryCapacityValidSamples++
                            }
                        } elseif (
                            $powerSample.battery_telemetry_status -eq 'error'
                        ) {
                            $batteryErrorSamples++
                        }
                    }
                    if ($counterSample.error) {
                        $sampleErrors.Add(
                            "counter sample $sampleIndex`: $($counterSample.error)"
                        )
                    }

                    $samples.Add([pscustomobject]@{
                        sample_index = $sampleIndex
                        sample_measured = $sampleIsMeasured
                        t_sec = $elapsedSec
                        sample_utc = $now.ToUniversalTime().ToString('o')
                        cpu_core_pct = $cpuCorePct
                        # Deprecated compatibility alias; same one-core scale.
                        cpu_pct_normalized = $cpuCorePct
                        working_set_mb = $workingSetMb
                        private_mb = $privateMb
                        threads = $threadCount
                        handles = $handleCount
                        io_read_bytes = if ($io.counters) {
                            [long]$io.counters.ReadTransferCount
                        } else {
                            $null
                        }
                        io_write_bytes = if ($io.counters) {
                            [long]$io.counters.WriteTransferCount
                        } else {
                            $null
                        }
                        io_other_bytes = if ($io.counters) {
                            [long]$io.counters.OtherTransferCount
                        } else {
                            $null
                        }
                        gpu_busiest_engine_pct = if ($sampleIsMeasured) {
                            $counterSample.gpu_busiest_engine_pct
                        } else {
                            $null
                        }
                        process_context_switches_per_sec = if ($sampleIsMeasured) {
                            $counterSample.process_context_switches_per_sec
                        } else {
                            $null
                        }
                        system_context_switches_per_sec = if ($sampleIsMeasured) {
                            $counterSample.system_context_switches_per_sec
                        } else {
                            $null
                        }
                        system_interrupts_per_sec = if ($sampleIsMeasured) {
                            $counterSample.system_interrupts_per_sec
                        } else {
                            $null
                        }
                        system_dpc_rate = if ($sampleIsMeasured) {
                            $counterSample.system_dpc_rate
                        } else {
                            $null
                        }
                        processor_queue_length = if ($sampleIsMeasured) {
                            $counterSample.processor_queue_length
                        } else {
                            $null
                        }
                        io_status = $io.status
                        gpu_status = if ($sampleIsMeasured) {
                            $counterSample.gpu_status
                        } else {
                            'priming'
                        }
                        context_switch_status = if ($sampleIsMeasured) {
                            $counterSample.context_switch_status
                        } else {
                            'priming'
                        }
                        counter_status = if ($sampleIsMeasured) {
                            $counterSample.status
                        } else {
                            'priming'
                        }
                    })

                    if ($sampleIsMeasured) {
                        foreach ($gpuRow in $counterSample.gpu_engines) {
                            $gpuSamples.Add([pscustomobject]@{
                                sample_index = $sampleIndex
                                t_sec = $elapsedSec
                                sample_utc =
                                    $now.ToUniversalTime().ToString('o')
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
                                t_sec = $elapsedSec
                                sample_utc =
                                    $now.ToUniversalTime().ToString('o')
                                meter = $energyRow.meter
                                power_mw = $energyRow.power_mw
                                counter_status = $energyRow.counter_status
                            })
                        }
                    }

                    $powerSamples.Add([pscustomobject]@{
                        sample_index = $sampleIndex
                        sample_measured = $sampleIsMeasured
                        t_sec = $elapsedSec
                        sample_utc = $now.ToUniversalTime().ToString('o')
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
                        battery_charge_rate_mw =
                            $powerSample.battery_charge_rate_mw
                        battery_discharge_rate_mw =
                            $powerSample.battery_discharge_rate_mw
                        power_status = $powerSample.status
                    })

                    $previousCpu = $cpu
                    $previousTime = $now
                    $firstSample = $false
                    $sampleIndex++
                }

                $timedOut = -not $process.HasExited
                if ($timedOut) {
                    $process.Kill($true)
                }
                $process.WaitForExit()
                $endTime = Get-Date
                $powerAfter = Get-BenchmarkPowerSnapshot

                $sampleColumns = @(
                    'sample_index', 'sample_measured', 't_sec', 'sample_utc',
                    'cpu_core_pct', 'cpu_pct_normalized', 'working_set_mb',
                    'private_mb', 'threads', 'handles', 'io_read_bytes',
                    'io_write_bytes', 'io_other_bytes',
                    'gpu_busiest_engine_pct',
                    'process_context_switches_per_sec',
                    'system_context_switches_per_sec',
                    'system_interrupts_per_sec', 'system_dpc_rate',
                    'processor_queue_length', 'io_status', 'gpu_status',
                    'context_switch_status', 'counter_status'
                )
                $gpuColumns = @(
                    'sample_index', 't_sec', 'sample_utc', 'pid',
                    'instance_name', 'adapter_luid', 'physical_adapter',
                    'engine_index', 'engine_type', 'utilization_pct',
                    'counter_status'
                )
                $powerColumns = @(
                    'sample_index', 'sample_measured', 't_sec', 'sample_utc',
                    'ac_line_status', 'battery_present', 'battery_percent',
                    'battery_saver', 'active_power_scheme_guid',
                    'battery_telemetry_status',
                    'battery_remaining_capacity_mwh',
                    'battery_charge_rate_mw',
                    'battery_discharge_rate_mw', 'power_status'
                )
                $energyColumns = @(
                    'sample_index', 't_sec', 'sample_utc', 'meter',
                    'power_mw', 'counter_status'
                )
                Write-CsvWithHeader `
                    -Path $sampleCsv `
                    -Columns $sampleColumns `
                    -Rows $samples
                Write-CsvWithHeader `
                    -Path $gpuCsv `
                    -Columns $gpuColumns `
                    -Rows $gpuSamples
                Write-CsvWithHeader `
                    -Path $powerCsv `
                    -Columns $powerColumns `
                    -Rows $powerSamples
                Write-CsvWithHeader `
                    -Path $energyCsv `
                    -Columns $energyColumns `
                    -Rows $energySamples

                $lastIoSample = @(
                    $samples |
                        Where-Object { $_.io_status -eq 'available' }
                ) | Select-Object -Last 1
                $measuredSamples = @(
                    $samples | Where-Object { $_.sample_measured }
                )
                $measuredPowerSamples = @(
                    $powerSamples | Where-Object { $_.sample_measured }
                )
                $measurementStartUtc = if ($measuredSamples.Count -gt 0) {
                    $measuredSamples[0].sample_utc
                } else {
                    $null
                }
                $measurementEndUtc = if ($measuredSamples.Count -gt 0) {
                    $measuredSamples[-1].sample_utc
                } else {
                    $null
                }
                $sampledWallSec = if ($measuredSamples.Count -gt 1) {
                    [double]$measuredSamples[-1].t_sec -
                        [double]$measuredSamples[0].t_sec
                } else {
                    $null
                }

                $powerTransitionReasons =
                    [System.Collections.Generic.List[string]]::new()
                if ($powerBefore.ac_line_status -ne $powerAfter.ac_line_status) {
                    $powerTransitionReasons.Add('AC/DC source changed.')
                }
                if ($powerBefore.battery_saver -ne $powerAfter.battery_saver) {
                    $powerTransitionReasons.Add('Battery saver state changed.')
                }
                if ($powerBefore.active_power_scheme_guid -ne
                    $powerAfter.active_power_scheme_guid) {
                    $powerTransitionReasons.Add('Active power scheme changed.')
                }
                if (@(
                    $measuredPowerSamples.ac_line_status |
                        Select-Object -Unique
                ).Count -gt 1) {
                    $powerTransitionReasons.Add(
                        'Sampled AC/DC state changed during measurement.'
                    )
                }

                $telemetry = [ordered]@{
                    io = Get-ObservedMetricStatus `
                        -Capability (New-MetricStatus `
                            -Status available `
                            -Source 'GetProcessIoCounters') `
                        -ValidSamples $ioValidSamples `
                        -ErrorSamples $ioErrorSamples `
                        -Source 'GetProcessIoCounters'
                    gpu_engine = Get-ObservedMetricStatus `
                        -Capability $counterSampler.capabilities.gpu_engine `
                        -ValidSamples $gpuValidSamples `
                        -ErrorSamples $gpuErrorSamples `
                        -Source (
                            'PDH \GPU Engine(*)\Utilization Percentage'
                        ) `
                        -NoSampleReason (
                            'No valid GPU engine instance was attributed to ' +
                            'the benchmark PID.'
                        )
                    process_context_switches = Get-ObservedMetricStatus `
                        -Capability (
                            $counterSampler.capabilities.thread_context_switches
                        ) `
                        -ValidSamples $contextValidSamples `
                        -ErrorSamples $contextErrorSamples `
                        -Source (
                            'PDH \Thread(*)\Context Switches/sec + ID Process'
                        )
                    energy_meter = Get-ObservedMetricStatus `
                        -Capability (
                            $counterSampler.capabilities.energy_meter_power
                        ) `
                        -ValidSamples $energyValidSamples `
                        -ErrorSamples $energyErrorSamples `
                        -Source 'PDH \Energy Meter(*)\Power'
                    battery = Get-ObservedMetricStatus `
                        -Capability (New-MetricStatus `
                            -Status $(if (
                                $powerBefore.battery_telemetry_status -eq
                                    'available'
                            ) {
                                'available'
                            } elseif (
                                $powerBefore.battery_telemetry_status -eq
                                    'error'
                            ) {
                                'error'
                            } else {
                                'unsupported'
                            }) `
                            -Source 'root\wmi BatteryStatus' `
                            -Reason $powerBefore.error) `
                        -ValidSamples $batteryValidSamples `
                        -ErrorSamples $batteryErrorSamples `
                        -Source 'root\wmi BatteryStatus'
                }

                $entry = [pscustomobject]@{
                    SchemaVersion = Get-BenchmarkSchemaVersion
                    CellTag = $cellTag
                    Scene = $scene
                    SceneName = $sceneName
                    Variant = $variant
                    WorkloadState = $WorkloadState
                    PowerState = $powerState
                    Run = $run
                    DurationSec = $DurationSec
                    TargetFps = $TargetFps
                    FrameCsv = Split-Path $frameCsv -Leaf
                    SampleCsv = Split-Path $sampleCsv -Leaf
                    GpuCsv = Split-Path $gpuCsv -Leaf
                    PowerCsv = Split-Path $powerCsv -Leaf
                    EnergyCsv = Split-Path $energyCsv -Leaf
                    LogFile = Split-Path $logFile -Leaf
                    ExitCode = if ($timedOut) { $null } else { $process.ExitCode }
                    TimedOut = $timedOut
                    WallSec = ($endTime - $startTime).TotalSeconds
                    StartUtc = $startTime.ToUniversalTime().ToString('o')
                    EndUtc = $endTime.ToUniversalTime().ToString('o')
                    MeasurementStartUtc = $measurementStartUtc
                    MeasurementEndUtc = $measurementEndUtc
                    SampledWallSec = $sampledWallSec
                    IoReadBytes = if ($lastIoSample) {
                        [long]$lastIoSample.io_read_bytes
                    } else {
                        $null
                    }
                    IoWriteBytes = if ($lastIoSample) {
                        [long]$lastIoSample.io_write_bytes
                    } else {
                        $null
                    }
                    IoOtherBytes = if ($lastIoSample) {
                        [long]$lastIoSample.io_other_bytes
                    } else {
                        $null
                    }
                    PowerContextStart = $powerBefore
                    PowerContextEnd = $powerAfter
                    PowerContextValid =
                        $powerTransitionReasons.Count -eq 0
                    PowerContextInvalidReasons = @($powerTransitionReasons)
                    Telemetry = $telemetry
                    SampleErrors = @($sampleErrors | Select-Object -Unique)
                }
                $manifest.Add($entry)
                $manifest |
                    ConvertTo-Json -Depth 12 |
                    Out-File `
                        -LiteralPath (Join-Path $resultDir 'manifest.json') `
                        -Encoding utf8

                $strictFailures = [System.Collections.Generic.List[string]]::new()
                if ($RequireGpuTelemetry -and $gpuValidSamples -eq 0) {
                    $strictFailures.Add(
                        'GPU telemetry was required, but the cell had no valid per-PID samples.'
                    )
                }
                if ($RequireWakeTelemetry -and $contextValidSamples -eq 0) {
                    $strictFailures.Add(
                        'Wake-proxy telemetry was required, but the cell had no valid context-switch samples.'
                    )
                }
                if ($RequireEnergyMeter -and $energyValidSamples -eq 0) {
                    $strictFailures.Add(
                        'Energy Meter telemetry was required, but the cell had no valid power samples.'
                    )
                }
                if ($RequireBatteryTelemetry -and
                    $batteryCapacityValidSamples -eq 0) {
                    $strictFailures.Add(
                        'Battery telemetry was required, but the cell had no valid capacity samples.'
                    )
                }
                if (($ExpectedPowerSource -ne 'any' -or
                    $ExpectedBatterySaver -ne 'any') -and
                    $powerTransitionReasons.Count -gt 0) {
                    $strictFailures.Add(
                        'The required power context changed during the cell.'
                    )
                }
                if ($strictFailures.Count -gt 0) {
                    throw "$cellTag failed strict telemetry requirements: $($strictFailures -join ' ')"
                }
                if ($timedOut) {
                    throw "Benchmark timed out for $cellTag."
                }
                if ($process.ExitCode -ne 0) {
                    throw (
                        "Benchmark failed for $cellTag with exit " +
                        "$($process.ExitCode)."
                    )
                }
            }
        }
    }

    Write-Host ''
    Write-Host "== Sweep complete: $cellNumber cells ==" -ForegroundColor Green
    Write-Host (
        "Run aggregate: tools\benchmark\Aggregate-Results.ps1 " +
        "-ResultDir `"$resultDir`""
    ) -ForegroundColor Cyan
} finally {
    if ($counterSampler -and $counterSampler.query) {
        $counterSampler.query.Dispose()
    }
}
