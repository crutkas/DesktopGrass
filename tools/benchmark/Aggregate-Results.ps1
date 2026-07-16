# Aggregate version 1 or version 2 DesktopGrass benchmark result directories.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultDir,

    [string] $OutMarkdown = (Join-Path $ResultDir 'results.md'),
    [string] $OutCsv = (Join-Path $ResultDir 'results.csv'),
    [string] $OutEngineCsv = (Join-Path $ResultDir 'engine-results.csv'),
    [string] $OutEnergyCsv = (Join-Path $ResultDir 'energy-results.csv')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'Benchmark.Common.psm1') -Force

if (-not (Test-Path -LiteralPath $ResultDir)) {
    throw "ResultDir not found: $ResultDir"
}
$ResultDir = (Resolve-Path -LiteralPath $ResultDir).Path

$manifestPath = Join-Path $ResultDir 'manifest.json'
$machinePath = Join-Path $ResultDir 'machine.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "manifest.json missing in $ResultDir"
}

$manifest = @(
    Get-Content -LiteralPath $manifestPath -Raw |
        ConvertFrom-Json
)
$machine = if (Test-Path -LiteralPath $machinePath) {
    Get-Content -LiteralPath $machinePath -Raw | ConvertFrom-Json
} else {
    $null
}

function Get-PropertyValue {
    param(
        [AllowNull()] $InputObject,
        [Parameter(Mandatory)] [string] $Name,
        [AllowNull()] $DefaultValue = $null
    )

    if ($null -eq $InputObject) {
        return $DefaultValue
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $DefaultValue
    }
    return $property.Value
}

function ConvertTo-NullableDouble {
    param([AllowNull()] $Value)

    if ($null -eq $Value -or
        [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    return [double]$Value
}

function ConvertTo-NullableLong {
    param([AllowNull()] $Value)

    if ($null -eq $Value -or
        [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    return [long]$Value
}

function Get-NumericValues {
    param(
        [AllowNull()] [object[]] $Objects,
        [Parameter(Mandatory)] [string] $Property
    )

    return @(
        foreach ($item in @($Objects)) {
            $value = Get-PropertyValue $item $Property
            $number = ConvertTo-NullableDouble $value
            if ($null -ne $number) {
                $number
            }
        }
    )
}

function Test-MeasuredSample {
    param([Parameter(Mandatory)] $Sample)
    $value = Get-PropertyValue $Sample 'sample_measured'
    if ($null -eq $value) {
        return $true
    }
    return [string]$value -eq 'True'
}

function Get-TelemetryStatus {
    param(
        [Parameter(Mandatory)] $Entry,
        [Parameter(Mandatory)] [string] $Metric
    )

    $telemetry = Get-PropertyValue $Entry 'Telemetry'
    $statusObject = Get-PropertyValue $telemetry $Metric
    if ($null -eq $statusObject) {
        return [pscustomobject]@{
            status = 'legacy'
            source = 'schema v1'
            reason = $null
        }
    }
    return $statusObject
}

function Merge-MetricStatuses {
    param(
        [Parameter(Mandatory)] [object[]] $Statuses,
        [Parameter(Mandatory)] [string] $Source
    )

    $states = @(
        $Statuses |
            ForEach-Object { Get-PropertyValue $_ 'status' 'unknown' } |
            Select-Object -Unique
    )
    $reasons = @(
        $Statuses |
            ForEach-Object { Get-PropertyValue $_ 'reason' } |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
            Select-Object -Unique
    )
    $status = if ($states.Count -eq 1 -and $states[0] -eq 'available') {
        'available'
    } elseif ($states -contains 'partial' -or (
        $states -contains 'available' -and $states.Count -gt 1
    )) {
        'partial'
    } elseif ($states -contains 'error') {
        'error'
    } elseif ($states -contains 'legacy') {
        'legacy'
    } else {
        'unsupported'
    }

    return [pscustomobject]@{
        status = $status
        source = $Source
        reason = if ($reasons.Count -gt 0) {
            $reasons -join ' | '
        } else {
            $null
        }
    }
}

function Format-Metric {
    param(
        [AllowNull()] $Value,
        [int] $Decimals = 2
    )

    if ($null -eq $Value) {
        return 'n/a'
    }
    return ('{0:N' + $Decimals + '}') -f [double]$Value
}

function Escape-Markdown {
    param([AllowNull()] $Value)
    if ($null -eq $Value) {
        return ''
    }
    return ([string]$Value).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

$cellStats = [System.Collections.Generic.List[object]]::new()
$engineSamples = [System.Collections.Generic.List[object]]::new()
$energySamples = [System.Collections.Generic.List[object]]::new()

foreach ($entry in $manifest) {
    $schemaVersion = [int](Get-PropertyValue $entry 'SchemaVersion' 1)
    $workloadState = [string](Get-PropertyValue `
        $entry `
        'WorkloadState' `
        'visible')
    $powerState = [string](Get-PropertyValue `
        $entry `
        'PowerState' `
        'legacy-unknown')

    $framePath = Join-Path $ResultDir (
        [string](Get-PropertyValue $entry 'FrameCsv')
    )
    $samplePath = Join-Path $ResultDir (
        [string](Get-PropertyValue $entry 'SampleCsv')
    )
    $gpuFile = Get-PropertyValue $entry 'GpuCsv'
    $powerFile = Get-PropertyValue $entry 'PowerCsv'
    $energyFile = Get-PropertyValue $entry 'EnergyCsv'
    $gpuPath = if ($gpuFile) { Join-Path $ResultDir $gpuFile } else { $null }
    $powerPath = if ($powerFile) {
        Join-Path $ResultDir $powerFile
    } else {
        $null
    }
    $energyPath = if ($energyFile) {
        Join-Path $ResultDir $energyFile
    } else {
        $null
    }

    $renderMs = @()
    $dtMs = @()
    $frameCount = 0
    $targetFps = ConvertTo-NullableLong (
        Get-PropertyValue $entry 'TargetFps'
    )
    if (Test-Path -LiteralPath $framePath) {
        $frameLines = @(Get-Content -LiteralPath $framePath)
        $headerComment = $frameLines |
            Where-Object { $_ -match '^\s*#' } |
            Select-Object -First 1
        if ($null -eq $targetFps -and
            $headerComment -match '\btarget_fps=(\d+)\b') {
            $targetFps = [int]$Matches[1]
        }
        $csvLines = @(
            $frameLines | Where-Object { $_ -notmatch '^\s*#' }
        )
        $frameObjects = @()
        if ($csvLines.Count -ge 2) {
            $frameObjects = @($csvLines | ConvertFrom-Csv)
        }
        $renderMs = @(Get-NumericValues $frameObjects 'render_ms')
        $dtMs = @(Get-NumericValues $frameObjects 'dt_ms')
        $frameCount = $frameObjects.Count
    }

    $sampleObjects = if (Test-Path -LiteralPath $samplePath) {
        @(Import-Csv -LiteralPath $samplePath)
    } else {
        @()
    }
    $measuredSamples = @(
        $sampleObjects | Where-Object { Test-MeasuredSample $_ }
    )

    $cpuCorePct = @(
        foreach ($sample in $measuredSamples) {
            $newValue = ConvertTo-NullableDouble (
                Get-PropertyValue $sample 'cpu_core_pct'
            )
            if ($null -ne $newValue) {
                $newValue
                continue
            }
            $legacyValue = ConvertTo-NullableDouble (
                Get-PropertyValue $sample 'cpu_pct_normalized'
            )
            if ($null -ne $legacyValue) {
                $legacyValue
            }
        }
    )
    $workingSetMb = @(Get-NumericValues $measuredSamples 'working_set_mb')
    $privateMb = @(Get-NumericValues $measuredSamples 'private_mb')
    $gpuBusiestPct = @(
        Get-NumericValues $measuredSamples 'gpu_busiest_engine_pct'
    )
    $processContextSwitches = @(
        Get-NumericValues `
            $measuredSamples `
            'process_context_switches_per_sec'
    )
    $systemContextSwitches = @(
        Get-NumericValues `
            $measuredSamples `
            'system_context_switches_per_sec'
    )
    $systemInterrupts = @(
        Get-NumericValues `
            $measuredSamples `
            'system_interrupts_per_sec'
    )
    $systemDpcRate = @(
        Get-NumericValues $measuredSamples 'system_dpc_rate'
    )
    $processorQueueLength = @(
        Get-NumericValues $measuredSamples 'processor_queue_length'
    )

    if ($gpuPath -and (Test-Path -LiteralPath $gpuPath)) {
        foreach ($gpuSample in @(Import-Csv -LiteralPath $gpuPath)) {
            $utilization = ConvertTo-NullableDouble $gpuSample.utilization_pct
            if ($null -eq $utilization) {
                continue
            }
            $engineSamples.Add([pscustomobject]@{
                Scene = [int](Get-PropertyValue $entry 'Scene')
                SceneName = [string](Get-PropertyValue $entry 'SceneName')
                Variant = [string](Get-PropertyValue $entry 'Variant')
                WorkloadState = $workloadState
                PowerState = $powerState
                Run = [int](Get-PropertyValue $entry 'Run')
                AdapterLuid = [string]$gpuSample.adapter_luid
                PhysicalAdapter = ConvertTo-NullableLong `
                    $gpuSample.physical_adapter
                EngineIndex = ConvertTo-NullableLong $gpuSample.engine_index
                EngineType = [string]$gpuSample.engine_type
                UtilizationPct = $utilization
            })
        }
    }

    if ($energyPath -and (Test-Path -LiteralPath $energyPath)) {
        foreach ($energySample in @(Import-Csv -LiteralPath $energyPath)) {
            $powerMw = ConvertTo-NullableDouble $energySample.power_mw
            if ($null -eq $powerMw) {
                continue
            }
            $energySamples.Add([pscustomobject]@{
                Scene = [int](Get-PropertyValue $entry 'Scene')
                SceneName = [string](Get-PropertyValue $entry 'SceneName')
                Variant = [string](Get-PropertyValue $entry 'Variant')
                WorkloadState = $workloadState
                PowerState = $powerState
                Run = [int](Get-PropertyValue $entry 'Run')
                Meter = [string]$energySample.meter
                PowerMw = $powerMw
            })
        }
    }

    $powerObjects = if ($powerPath -and
        (Test-Path -LiteralPath $powerPath)) {
        @(Import-Csv -LiteralPath $powerPath)
    } else {
        @()
    }
    $measuredPower = @(
        $powerObjects | Where-Object { Test-MeasuredSample $_ }
    )
    $batteryCapacity = @(
        Get-NumericValues `
            $measuredPower `
            'battery_remaining_capacity_mwh'
    )
    $batteryDischargeRate = @(
        Get-NumericValues `
            $measuredPower `
            'battery_discharge_rate_mw'
    )
    $batteryEnergyDeltaMwh = $null
    $batteryAveragePowerW = $null
    $batteryEnergyStatus = 'unsupported'
    $batteryEnergyReason = 'No version 2 power samples were available.'
    $powerContextValid = Get-PropertyValue $entry 'PowerContextValid' $true
    if ($measuredPower.Count -gt 0) {
        $sampledPowerStates = @(
            $measuredPower.ac_line_status | Select-Object -Unique
        )
        if (-not [bool]$powerContextValid) {
            $batteryEnergyStatus = 'invalid'
            $batteryEnergyReason = 'Power context changed during the cell.'
        } elseif ($sampledPowerStates.Count -ne 1 -or
            $sampledPowerStates[0] -ne 'battery') {
            $batteryEnergyStatus = 'not_applicable'
            $batteryEnergyReason = 'Battery discharge is only derived on stable DC power.'
        } elseif ($batteryCapacity.Count -lt 2) {
            $batteryEnergyStatus = 'insufficient_data'
            $batteryEnergyReason =
                'At least two valid battery-capacity samples are required.'
        } else {
            $capacityDelta = [double]$batteryCapacity[0] -
                [double]$batteryCapacity[-1]
            $powerStartSec = ConvertTo-NullableDouble $measuredPower[0].t_sec
            $powerEndSec = ConvertTo-NullableDouble $measuredPower[-1].t_sec
            $powerDurationSec = if (
                $null -ne $powerStartSec -and $null -ne $powerEndSec
            ) {
                $powerEndSec - $powerStartSec
            } else {
                $null
            }
            if ($capacityDelta -le 0) {
                $batteryEnergyStatus = 'insufficient_resolution'
                $batteryEnergyReason =
                    'No positive battery-capacity delta was observed; zero is not used as a pass.'
            } elseif ($null -eq $powerDurationSec -or $powerDurationSec -le 0) {
                $batteryEnergyStatus = 'insufficient_data'
                $batteryEnergyReason = 'Battery sample duration is unavailable.'
            } else {
                $batteryEnergyStatus = 'available'
                $batteryEnergyReason = $null
                $batteryEnergyDeltaMwh = $capacityDelta
                $batteryAveragePowerW =
                    ($capacityDelta * 3600.0 / $powerDurationSec) / 1000.0
            }
        }
    }

    $benchmarkSec = 0.0
    $effectiveFps = 0.0
    $logPath = Join-Path $ResultDir (
        [string](Get-PropertyValue $entry 'LogFile')
    )
    if (Test-Path -LiteralPath $logPath) {
        $summary = Get-Content -LiteralPath $logPath -Raw
        if ($summary -match
            '\bduration_s=([0-9]+(?:\.[0-9]+)?)\s+fps=([0-9]+(?:\.[0-9]+)?)\b') {
            $benchmarkSec = [double]$Matches[1]
            $effectiveFps = [double]$Matches[2]
        }
    }
    if ($benchmarkSec -le 0) {
        $entryDuration = ConvertTo-NullableDouble (
            Get-PropertyValue $entry 'DurationSec'
        )
        if ($null -ne $entryDuration -and $entryDuration -gt 0) {
            $benchmarkSec = $entryDuration
        } else {
            $benchmarkSec = [double](Get-PropertyValue $entry 'WallSec' 0)
        }
        $effectiveFps = if ($benchmarkSec -gt 0) {
            $frameCount / $benchmarkSec
        } else {
            0.0
        }
    }

    $cellEnergy = @(
        $energySamples |
            Where-Object {
                $_.Scene -eq [int](Get-PropertyValue $entry 'Scene') -and
                $_.Variant -eq [string](Get-PropertyValue $entry 'Variant') -and
                $_.WorkloadState -eq $workloadState -and
                $_.PowerState -eq $powerState -and
                $_.Run -eq [int](Get-PropertyValue $entry 'Run') -and
                $_.Meter -ieq 'sys'
            }
    )
    $sysPowerMw = @($cellEnergy | ForEach-Object { $_.PowerMw })

    $sampledWallSec = ConvertTo-NullableDouble (
        Get-PropertyValue $entry 'SampledWallSec'
    )
    if ($null -eq $sampledWallSec -and $measuredSamples.Count -gt 1) {
        $firstSampleSec = ConvertTo-NullableDouble $measuredSamples[0].t_sec
        $lastSampleSec = ConvertTo-NullableDouble $measuredSamples[-1].t_sec
        if ($null -ne $firstSampleSec -and $null -ne $lastSampleSec) {
            $sampledWallSec = $lastSampleSec - $firstSampleSec
        }
    }

    $ioReadBytes = ConvertTo-NullableDouble (
        Get-PropertyValue $entry 'IoReadBytes'
    )
    $ioWriteBytes = ConvertTo-NullableDouble (
        Get-PropertyValue $entry 'IoWriteBytes'
    )
    $cpuCorePctMean = Get-Mean $cpuCorePct
    $cpuCorePctP95 = Get-Percentile `
        -Values $cpuCorePct `
        -Percentile 95

    $cellStats.Add([pscustomobject]@{
        SchemaVersion = $schemaVersion
        CellTag = [string](Get-PropertyValue $entry 'CellTag')
        Scene = [int](Get-PropertyValue $entry 'Scene')
        SceneName = [string](Get-PropertyValue $entry 'SceneName')
        Variant = [string](Get-PropertyValue $entry 'Variant')
        WorkloadState = $workloadState
        PowerState = $powerState
        Run = [int](Get-PropertyValue $entry 'Run')
        FrameCount = $frameCount
        BenchmarkSec = $benchmarkSec
        SampledWallSec = $sampledWallSec
        WallSec = [double](Get-PropertyValue $entry 'WallSec' 0)
        TargetFps = $targetFps
        EffectiveFps = $effectiveFps
        FrameP50Ms = Get-Percentile -Values $renderMs -Percentile 50
        FrameP95Ms = Get-Percentile -Values $renderMs -Percentile 95
        FrameP99Ms = Get-Percentile -Values $renderMs -Percentile 99
        DtP95Ms = Get-Percentile -Values $dtMs -Percentile 95
        CpuCorePctMean = $cpuCorePctMean
        CpuCorePctP95 = $cpuCorePctP95
        # Schema-v1 result aliases; values have always used one-core = 100%.
        CpuPctMean = $cpuCorePctMean
        CpuPctP95 = $cpuCorePctP95
        WorkingSetMbPeak = if ($workingSetMb.Count -gt 0) {
            [double](($workingSetMb | Measure-Object -Maximum).Maximum)
        } else {
            $null
        }
        PrivateMbPeak = if ($privateMb.Count -gt 0) {
            [double](($privateMb | Measure-Object -Maximum).Maximum)
        } else {
            $null
        }
        IoReadKb = if ($null -ne $ioReadBytes) {
            [math]::Round($ioReadBytes / 1KB, 1)
        } else {
            $null
        }
        IoWriteKb = if ($null -ne $ioWriteBytes) {
            [math]::Round($ioWriteBytes / 1KB, 1)
        } else {
            $null
        }
        GpuBusiestPctMean = Get-Mean $gpuBusiestPct
        GpuBusiestPctP95 = Get-Percentile `
            -Values $gpuBusiestPct `
            -Percentile 95
        ProcessContextSwitchesMean = Get-Mean $processContextSwitches
        ProcessContextSwitchesP95 = Get-Percentile `
            -Values $processContextSwitches `
            -Percentile 95
        SystemContextSwitchesMean = Get-Mean $systemContextSwitches
        SystemInterruptsMean = Get-Mean $systemInterrupts
        SystemDpcRateMean = Get-Mean $systemDpcRate
        ProcessorQueueLengthMean = Get-Mean $processorQueueLength
        EnergyMeterSysPowerMwMean = Get-Mean $sysPowerMw
        EnergyMeterSysPowerMwP95 = Get-Percentile `
            -Values $sysPowerMw `
            -Percentile 95
        BatteryEnergyDeltaMwh = $batteryEnergyDeltaMwh
        BatteryAveragePowerW = $batteryAveragePowerW
        BatteryDischargeRateMwMean = Get-Mean $batteryDischargeRate
        BatteryEnergyStatus = $batteryEnergyStatus
        BatteryEnergyReason = $batteryEnergyReason
        ProcessSampleCount = $measuredSamples.Count
        GpuSampleCount = $gpuBusiestPct.Count
        ContextSwitchSampleCount = $processContextSwitches.Count
        EnergyMeterSampleCount = $sysPowerMw.Count
        GpuCoveragePct = if ($measuredSamples.Count -gt 0) {
            100.0 * $gpuBusiestPct.Count / $measuredSamples.Count
        } else {
            $null
        }
        ContextSwitchCoveragePct = if ($measuredSamples.Count -gt 0) {
            100.0 * $processContextSwitches.Count / $measuredSamples.Count
        } else {
            $null
        }
        PowerContextValid = [bool]$powerContextValid
        IoStatus = Get-TelemetryStatus $entry 'io'
        GpuStatus = Get-TelemetryStatus $entry 'gpu_engine'
        ContextSwitchStatus = Get-TelemetryStatus `
            $entry `
            'process_context_switches'
        EnergyMeterStatus = Get-TelemetryStatus $entry 'energy_meter'
        BatteryStatus = Get-TelemetryStatus $entry 'battery'
        ExitCode = Get-PropertyValue $entry 'ExitCode'
        TimedOut = [bool](Get-PropertyValue $entry 'TimedOut' $false)
    })
}

$grouped = $cellStats |
    Group-Object Scene, Variant, WorkloadState, PowerState
$rows = foreach ($group in $grouped) {
    $first = $group.Group[0]
    $gpuStatus = Merge-MetricStatuses `
        -Statuses @($group.Group.GpuStatus) `
        -Source 'PDH GPU Engine'
    $contextStatus = Merge-MetricStatuses `
        -Statuses @($group.Group.ContextSwitchStatus) `
        -Source 'PDH Thread context switches'
    $energyStatus = Merge-MetricStatuses `
        -Statuses @($group.Group.EnergyMeterStatus) `
        -Source 'PDH Energy Meter'
    $batteryStatus = Merge-MetricStatuses `
        -Statuses @($group.Group.BatteryStatus) `
        -Source 'Windows battery provider'
    $batteryEnergyStates = @(
        $group.Group.BatteryEnergyStatus | Select-Object -Unique
    )
    $batteryEnergyReasons = @(
        $group.Group.BatteryEnergyReason |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_)
            } |
            Select-Object -Unique
    )
    $batteryEnergyStatus = if (
        $batteryEnergyStates.Count -eq 1
    ) {
        $batteryEnergyStates[0]
    } elseif ($batteryEnergyStates -contains 'available') {
        'partial'
    } else {
        'not_available'
    }
    $cpuCorePctMean = Get-Mean (
        Get-NumericValues $group.Group 'CpuCorePctMean'
    )
    $cpuCorePctP95 = Get-Mean (
        Get-NumericValues $group.Group 'CpuCorePctP95'
    )

    [pscustomobject]@{
        Scene = $first.Scene
        SceneName = $first.SceneName
        Variant = $first.Variant
        WorkloadState = $first.WorkloadState
        PowerState = $first.PowerState
        Runs = $group.Count
        EffectiveFpsMean = Get-Mean (
            Get-NumericValues $group.Group 'EffectiveFps'
        )
        EffectiveFpsStd = Get-StandardDeviation (
            Get-NumericValues $group.Group 'EffectiveFps'
        )
        FrameP50Ms = Get-Mean (
            Get-NumericValues $group.Group 'FrameP50Ms'
        )
        FrameP95Ms = Get-Mean (
            Get-NumericValues $group.Group 'FrameP95Ms'
        )
        FrameP99Ms = Get-Mean (
            Get-NumericValues $group.Group 'FrameP99Ms'
        )
        DtP95Ms = Get-Mean (
            Get-NumericValues $group.Group 'DtP95Ms'
        )
        CpuCorePctMean = $cpuCorePctMean
        CpuCorePctP95 = $cpuCorePctP95
        CpuPctMean = $cpuCorePctMean
        CpuPctP95 = $cpuCorePctP95
        WorkingSetMbPeak = Get-Mean (
            Get-NumericValues $group.Group 'WorkingSetMbPeak'
        )
        PrivateMbPeak = Get-Mean (
            Get-NumericValues $group.Group 'PrivateMbPeak'
        )
        IoReadKbMean = Get-Mean (
            Get-NumericValues $group.Group 'IoReadKb'
        )
        IoWriteKbMean = Get-Mean (
            Get-NumericValues $group.Group 'IoWriteKb'
        )
        GpuBusiestPctMean = Get-Mean (
            Get-NumericValues $group.Group 'GpuBusiestPctMean'
        )
        GpuBusiestPctP95 = Get-Mean (
            Get-NumericValues $group.Group 'GpuBusiestPctP95'
        )
        ProcessContextSwitchesMean = Get-Mean (
            Get-NumericValues $group.Group 'ProcessContextSwitchesMean'
        )
        ProcessContextSwitchesP95 = Get-Mean (
            Get-NumericValues $group.Group 'ProcessContextSwitchesP95'
        )
        EnergyMeterSysPowerMwMean = Get-Mean (
            Get-NumericValues $group.Group 'EnergyMeterSysPowerMwMean'
        )
        EnergyMeterSysPowerMwP95 = Get-Mean (
            Get-NumericValues $group.Group 'EnergyMeterSysPowerMwP95'
        )
        BatteryEnergyDeltaMwh = Get-Mean (
            Get-NumericValues $group.Group 'BatteryEnergyDeltaMwh'
        )
        BatteryAveragePowerW = Get-Mean (
            Get-NumericValues $group.Group 'BatteryAveragePowerW'
        )
        GpuCoveragePct = Get-Mean (
            Get-NumericValues $group.Group 'GpuCoveragePct'
        )
        ContextSwitchCoveragePct = Get-Mean (
            Get-NumericValues $group.Group 'ContextSwitchCoveragePct'
        )
        GpuMetricStatus = $gpuStatus.status
        GpuMetricReason = $gpuStatus.reason
        ContextSwitchMetricStatus = $contextStatus.status
        ContextSwitchMetricReason = $contextStatus.reason
        EnergyMeterMetricStatus = $energyStatus.status
        EnergyMeterMetricReason = $energyStatus.reason
        BatteryMetricStatus = $batteryStatus.status
        BatteryMetricReason = $batteryStatus.reason
        BatteryEnergyStatus = $batteryEnergyStatus
        BatteryEnergyReason = if ($batteryEnergyReasons.Count -gt 0) {
            $batteryEnergyReasons -join ' | '
        } else {
            $null
        }
        BudgetStatus = 'not_evaluated'
        BudgetReason = (
            'This report records a baseline; pass/fail requires an approved ' +
            'same-machine reference and all required metrics to be available.'
        )
    }
}

$rows = @(
    $rows | Sort-Object Scene, WorkloadState, PowerState, Variant
)
$rows | Export-Csv -LiteralPath $OutCsv -NoTypeInformation -Encoding utf8

$engineRows = @(
    $engineSamples |
        Group-Object `
            Scene,
            Variant,
            WorkloadState,
            PowerState,
            AdapterLuid,
            PhysicalAdapter,
            EngineType |
        ForEach-Object {
            $first = $_.Group[0]
            $values = @($_.Group | ForEach-Object { $_.UtilizationPct })
            [pscustomobject]@{
                Scene = $first.Scene
                SceneName = $first.SceneName
                Variant = $first.Variant
                WorkloadState = $first.WorkloadState
                PowerState = $first.PowerState
                AdapterLuid = $first.AdapterLuid
                PhysicalAdapter = $first.PhysicalAdapter
                EngineType = $first.EngineType
                Samples = $values.Count
                UtilizationPctMean = Get-Mean $values
                UtilizationPctP95 = Get-Percentile `
                    -Values $values `
                    -Percentile 95
                UtilizationPctMax = if ($values.Count -gt 0) {
                    [double](($values | Measure-Object -Maximum).Maximum)
                } else {
                    $null
                }
            }
        } |
        Sort-Object Scene, WorkloadState, PowerState, EngineType
)
Write-CsvWithHeader `
    -Path $OutEngineCsv `
    -Columns @(
        'Scene', 'SceneName', 'Variant', 'WorkloadState', 'PowerState',
        'AdapterLuid', 'PhysicalAdapter', 'EngineType', 'Samples',
        'UtilizationPctMean', 'UtilizationPctP95', 'UtilizationPctMax'
    ) `
    -Rows $engineRows

$energyRows = @(
    $energySamples |
        Group-Object Scene, Variant, WorkloadState, PowerState, Meter |
        ForEach-Object {
            $first = $_.Group[0]
            $values = @($_.Group | ForEach-Object { $_.PowerMw })
            [pscustomobject]@{
                Scene = $first.Scene
                SceneName = $first.SceneName
                Variant = $first.Variant
                WorkloadState = $first.WorkloadState
                PowerState = $first.PowerState
                Meter = $first.Meter
                Samples = $values.Count
                PowerMwMean = Get-Mean $values
                PowerMwP95 = Get-Percentile -Values $values -Percentile 95
                PowerMwMax = if ($values.Count -gt 0) {
                    [double](($values | Measure-Object -Maximum).Maximum)
                } else {
                    $null
                }
            }
        } |
        Sort-Object Scene, WorkloadState, PowerState, Meter
)
Write-CsvWithHeader `
    -Path $OutEnergyCsv `
    -Columns @(
        'Scene', 'SceneName', 'Variant', 'WorkloadState', 'PowerState',
        'Meter', 'Samples', 'PowerMwMean', 'PowerMwP95', 'PowerMwMax'
    ) `
    -Rows $energyRows

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine('# DesktopGrass benchmark results')
[void]$markdown.AppendLine('')
if ($machine) {
    [void]$markdown.AppendLine('## Machine and measurement context')
    [void]$markdown.AppendLine('')
    $hostName = Get-PropertyValue $machine 'host_name' (
        Get-PropertyValue $machine 'HostName' 'unknown'
    )
    $osObject = Get-PropertyValue $machine 'os'
    $osVersion = if ($osObject) {
        "$(Get-PropertyValue $osObject 'caption') $(Get-PropertyValue $osObject 'version') build $(Get-PropertyValue $osObject 'build_number')"
    } else {
        [string](Get-PropertyValue $machine 'OSVersion' 'unknown')
    }
    [void]$markdown.AppendLine("- Host: $(Escape-Markdown $hostName)")
    [void]$markdown.AppendLine("- OS: $(Escape-Markdown $osVersion)")
    [void]$markdown.AppendLine(
        "- Logical CPUs: $(Get-PropertyValue $machine 'logical_cpus' (Get-PropertyValue $machine 'LogicalCpus' 'unknown'))"
    )
    [void]$markdown.AppendLine(
        "- Platform: $(Get-PropertyValue $machine 'platform' 'legacy-unknown')"
    )
    [void]$markdown.AppendLine(
        "- Per-run requested render duration: $(Get-PropertyValue $machine 'DurationSec' 'unknown') s"
    )
    [void]$markdown.AppendLine(
        "- Counter sample interval: $(Get-PropertyValue $machine 'SampleIntervalSec' 'unknown') s"
    )
    [void]$markdown.AppendLine(
        "- Target FPS: $(Get-PropertyValue $machine 'TargetFps' 'unknown')"
    )
    [void]$markdown.AppendLine(
        "- Executable: ``$(Escape-Markdown (Get-PropertyValue $machine 'Exe' 'unknown'))``"
    )
    [void]$markdown.AppendLine(
        "- Sweep start: $(Get-PropertyValue $machine 'UtcStart' 'unknown')"
    )
    [void]$markdown.AppendLine('')

    $capabilities = Get-PropertyValue $machine 'counter_capabilities'
    if ($capabilities) {
        [void]$markdown.AppendLine('### Automated counter capabilities')
        [void]$markdown.AppendLine('')
        [void]$markdown.AppendLine('| Metric | Status | Source | Reason |')
        [void]$markdown.AppendLine('|---|---|---|---|')
        foreach ($property in $capabilities.PSObject.Properties) {
            $status = $property.Value
            [void]$markdown.AppendLine(
                "| $(Escape-Markdown $property.Name) | " +
                "$(Escape-Markdown (Get-PropertyValue $status 'status')) | " +
                "$(Escape-Markdown (Get-PropertyValue $status 'source')) | " +
                "$(Escape-Markdown (Get-PropertyValue $status 'reason')) |"
            )
        }
        [void]$markdown.AppendLine('')
    }
}

[void]$markdown.AppendLine('## Per-scene averages across runs')
[void]$markdown.AppendLine('')
[void]$markdown.AppendLine(
    '| Scene | State | Power | Variant | Runs | FPS | render p95 ms | CPU core % mean | GPU busiest % mean | GPU p95 | Context switches/s mean | Context p95 | WS peak MB | SYS power mW | Battery delta mWh | Budget |'
)
[void]$markdown.AppendLine(
    '|---|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|---|'
)
foreach ($row in $rows) {
    [void]$markdown.AppendLine(
        "| $(Escape-Markdown $row.SceneName) | " +
        "$(Escape-Markdown $row.WorkloadState) | " +
        "$(Escape-Markdown $row.PowerState) | " +
        "$(Escape-Markdown $row.Variant) | $($row.Runs) | " +
        "$(Format-Metric $row.EffectiveFpsMean 2) | " +
        "$(Format-Metric $row.FrameP95Ms 3) | " +
        "$(Format-Metric $row.CpuCorePctMean 2) | " +
        "$(Format-Metric $row.GpuBusiestPctMean 2) | " +
        "$(Format-Metric $row.GpuBusiestPctP95 2) | " +
        "$(Format-Metric $row.ProcessContextSwitchesMean 2) | " +
        "$(Format-Metric $row.ProcessContextSwitchesP95 2) | " +
        "$(Format-Metric $row.WorkingSetMbPeak 1) | " +
        "$(Format-Metric $row.EnergyMeterSysPowerMwMean 1) | " +
        "$(Format-Metric $row.BatteryEnergyDeltaMwh 1) | " +
        "$($row.BudgetStatus) |"
    )
}
[void]$markdown.AppendLine('')

[void]$markdown.AppendLine('## Per-run detail')
[void]$markdown.AppendLine('')
[void]$markdown.AppendLine(
    '| Cell | Frames | Render s | Sampled s | FPS | CPU core % | GPU busiest % | Context switches/s | SYS power mW | GPU coverage % | Power valid | Exit |'
)
[void]$markdown.AppendLine(
    '|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|---|--:|'
)
foreach ($cell in $cellStats) {
    [void]$markdown.AppendLine(
        "| $(Escape-Markdown $cell.CellTag) | $($cell.FrameCount) | " +
        "$(Format-Metric $cell.BenchmarkSec 2) | " +
        "$(Format-Metric $cell.SampledWallSec 2) | " +
        "$(Format-Metric $cell.EffectiveFps 2) | " +
        "$(Format-Metric $cell.CpuCorePctMean 2) | " +
        "$(Format-Metric $cell.GpuBusiestPctMean 2) | " +
        "$(Format-Metric $cell.ProcessContextSwitchesMean 2) | " +
        "$(Format-Metric $cell.EnergyMeterSysPowerMwMean 1) | " +
        "$(Format-Metric $cell.GpuCoveragePct 1) | " +
        "$($cell.PowerContextValid) | $(Escape-Markdown $cell.ExitCode) |"
    )
}
[void]$markdown.AppendLine('')

[void]$markdown.AppendLine('## Metric provenance and limitations')
[void]$markdown.AppendLine('')
[void]$markdown.AppendLine(
    '- **Render duration/FPS:** comes from the benchmark process QPC interval. Process startup and teardown are not labeled as rendering time.'
)
[void]$markdown.AppendLine(
    '- **CPU core %:** `Process.TotalProcessorTime` delta; 100% means one fully occupied logical core. `cpu_pct_normalized` remains only as a deprecated schema-v1-compatible alias.'
)
[void]$markdown.AppendLine(
    '- **GPU busiest %:** maximum valid per-PID `GPU Engine` utilization sample, matching Task Manager''s busiest-engine aggregation. Raw engines and types are in `engine-results.csv`.'
)
[void]$markdown.AppendLine(
    '- **Context switches/s:** sum of `Thread(*)\Context Switches/sec` instances whose `ID Process` matches the benchmark PID. It is a scheduling/timer-activity proxy, not a literal wakeup count.'
)
[void]$markdown.AppendLine(
    '- **SYS power:** hardware-specific Windows `Energy Meter(SYS)\Power` in milliwatts. It is system-wide, not process-attributed; all exposed meters are in `energy-results.csv`.'
)
[void]$markdown.AppendLine(
    '- **Battery energy:** system battery-capacity delta during a stable DC interval. A missing, zero-resolution, charging, or transitioned interval is `n/a`, never a passing zero.'
)
[void]$markdown.AppendLine(
    '- Empty cells mean unavailable or insufficient evidence. Consult metric status/reason columns in `results.csv` and source status in `manifest.json`.'
)
[void]$markdown.AppendLine('')

[void]$markdown.AppendLine('## Provisional same-machine budget')
[void]$markdown.AppendLine('')
[void]$markdown.AppendLine(
    'No row in this report is automatically passed. Establish a repeated visible reference on the same machine and power context first. Proposed visible regression limits are `max(reference mean + 3 stdev, reference mean x 1.20)` for CPU, GPU, context-switch, and system-power metrics, and `max(reference mean + 3 stdev, reference mean x 1.10)` for working set.'
)
[void]$markdown.AppendLine('')
[void]$markdown.AppendLine(
    'Once #22/#23 provide real suppressed states, the proposed gate is: zero rendered frames and each available CPU/GPU/context-switch/incremental-energy metric at or below 10% of the matching visible scene. Any required metric that is unavailable, partial without sufficient coverage, or collected across a power transition makes the budget `not_evaluated`, not `pass`.'
)

$markdown.ToString() |
    Out-File -LiteralPath $OutMarkdown -Encoding utf8

Write-Host "Wrote $OutMarkdown" -ForegroundColor Green
Write-Host "Wrote $OutCsv" -ForegroundColor Green
Write-Host "Wrote $OutEngineCsv" -ForegroundColor Green
Write-Host "Wrote $OutEnergyCsv" -ForegroundColor Green
