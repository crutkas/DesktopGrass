# Aggregate-RuntimeQualification.ps1
#
# Strict aggregation for production-path runtime qualification evidence.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ResultsRoot,

    [string] $OutCsv,

    [string] $OutBudgetsCsv,

    [string] $OutJson,

    [string] $OutMarkdown,

    [ValidateRange(0.5, 1.0)]
    [double] $MinCoverageRatio = 0.90,

    [ValidateRange(3, 100)]
    [int] $MinimumRuns = 3,

    [ValidateRange(0.0, 1.0)]
    [double] $SuppressionMaxRatio = 0.10,

    [ValidateRange(1.0, 2.0)]
    [double] $VisibleRatioMultiplier = 1.20,

    [ValidateRange(1.0, 2.0)]
    [double] $WorkingSetRatioMultiplier = 1.10,

    [ValidateRange(1.0, 2.0)]
    [double] $ThrottleToleranceFactor = 1.10,

    [ValidateRange(1.0, 4.0)]
    [double] $MinThrottleDistinguishRatio = 1.5,

    [ValidateRange(1, 1440)]
    [int] $MaxControlGapMinutes = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'Benchmark.Common.psm1') -Force
Import-Module (
    Join-Path $PSScriptRoot 'RuntimeQualification.Common.psm1'
) -Force

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ResultsRoot = [IO.Path]::GetFullPath($ResultsRoot)
Assert-ResultsRootOutsideRepo -ResultsRoot $ResultsRoot -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $ResultsRoot -PathType Container)) {
    throw "ResultsRoot is not a directory: $ResultsRoot"
}
$ResultsRoot = (Resolve-Path -LiteralPath $ResultsRoot).Path

function Resolve-OutputPath {
    param(
        [AllowNull()] [string] $Path,
        [Parameter(Mandatory)] [string] $DefaultFileName
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($Path)) {
        Join-Path $ResultsRoot $DefaultFileName
    } elseif ([IO.Path]::IsPathRooted($Path)) {
        $Path
    } else {
        Join-Path $ResultsRoot $Path
    }
    $resolved = [IO.Path]::GetFullPath($candidate)
    $boundary = $ResultsRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith(
        $boundary,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "Output path '$Path' escapes ResultsRoot '$ResultsRoot'."
    }
    return $resolved
}
$OutCsv = Resolve-OutputPath `
    -Path $OutCsv `
    -DefaultFileName 'runtime-qualification-results.csv'
$OutBudgetsCsv = Resolve-OutputPath `
    -Path $OutBudgetsCsv `
    -DefaultFileName 'runtime-qualification-budgets.csv'
$OutJson = Resolve-OutputPath `
    -Path $OutJson `
    -DefaultFileName 'runtime-qualification-results.json'
$OutMarkdown = Resolve-OutputPath `
    -Path $OutMarkdown `
    -DefaultFileName 'runtime-qualification-results.md'

function Get-PropertyValue {
    param(
        [AllowNull()] [psobject] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [AllowNull()] $Default = $null
    )

    if ($null -eq $Object) {
        return $Default
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $Default
    }
    return $property.Value
}

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory)] [psobject] $Object,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Context
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value -or
        [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "$Context is missing required property '$Name'."
    }
    return $property.Value
}

function ConvertTo-Bool {
    param([AllowNull()] $Value)

    if ($null -eq $Value -or
        [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    if ($Value -is [bool]) {
        return [bool]$Value
    }
    switch ([string]$Value) {
        'True' { return $true }
        'False' { return $false }
        'true' { return $true }
        'false' { return $false }
        '1' { return $true }
        '0' { return $false }
        default { return $null }
    }
}

function Test-MeasuredSample {
    param([Parameter(Mandatory)] [psobject] $Sample)

    return (ConvertTo-Bool (
        Get-PropertyValue $Sample 'sample_measured' $false
    )) -eq $true
}

function Get-ValidatedArtifactSeries {
    param(
        [AllowEmptyCollection()] [object[]] $Rows,
        [Parameter(Mandatory)] [int] $ExpectedSamples,
        [Parameter(Mandatory)] [DateTime] $WindowStartUtc,
        [Parameter(Mandatory)] [DateTime] $WindowEndUtc,
        [Parameter(Mandatory)] [string] $Label,
        [switch] $RequireMeasuredFlag,
        [switch] $AllowDuplicateIndices
    )

    $validRows = [Collections.Generic.List[object]]::new()
    $reasons = [Collections.Generic.List[string]]::new()
    $indices = [Collections.Generic.HashSet[int]]::new()
    foreach ($row in @($Rows)) {
        if ($null -eq $row) {
            continue
        }
        if ($RequireMeasuredFlag -and -not (Test-MeasuredSample $row)) {
            continue
        }
        $sampleIndex = ConvertTo-NullableInt (
            Get-PropertyValue $row 'sample_index'
        )
        if ($null -eq $sampleIndex -or
            $sampleIndex -lt 0 -or
            $sampleIndex -ge $ExpectedSamples) {
            $reasons.Add("$Label contains an out-of-range sample index.")
            continue
        }
        if (-not $AllowDuplicateIndices -and -not $indices.Add($sampleIndex)) {
            $reasons.Add("$Label contains duplicate sample index $sampleIndex.")
            continue
        }
        if ($AllowDuplicateIndices) {
            [void]$indices.Add($sampleIndex)
        }

        $sampleUtcValue = Get-PropertyValue $row 'sample_utc'
        try {
            $sampleUtc = ConvertTo-Utc $sampleUtcValue
        } catch {
            $reasons.Add("$Label contains an invalid sample_utc value.")
            continue
        }
        if ($sampleUtc -lt $WindowStartUtc -or
            $sampleUtc -gt $WindowEndUtc) {
            $reasons.Add("$Label contains an out-of-window sample.")
            continue
        }
        $validRows.Add($row)
    }
    return [pscustomobject]@{
        Valid = $reasons.Count -eq 0
        Reason = @($reasons | Select-Object -Unique) -join ' '
        Rows = @($validRows)
    }
}

function ConvertTo-Utc {
    param([Parameter(Mandatory)] $Value)

    if ($Value -is [DateTimeOffset]) {
        return $Value.UtcDateTime
    }
    if ($Value -is [DateTime]) {
        return $Value.ToUniversalTime()
    }
    return [DateTime]::Parse(
        [string]$Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind
    ).ToUniversalTime()
}

function ConvertTo-UtcString {
    param([AllowNull()] $Value)

    if ($null -eq $Value -or
        [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    return (ConvertTo-Utc $Value).ToString('o')
}

function Resolve-ArtifactPath {
    param(
        [AllowNull()] [string] $Path,
        [Parameter(Mandatory)] [string] $ManifestDirectory
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    $resolved = if ([IO.Path]::IsPathRooted($Path)) {
        [IO.Path]::GetFullPath($Path)
    } else {
        [IO.Path]::GetFullPath((Join-Path $ManifestDirectory $Path))
    }
    $boundary = [IO.Path]::GetFullPath($ManifestDirectory).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith(
        $boundary,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw (
            "Artifact path '$Path' escapes its capture directory " +
            "'$ManifestDirectory'."
        )
    }
    return $resolved
}

function Import-CsvArtifact {
    param([AllowNull()] [string] $Path)

    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) {
        return @()
    }
    return @(Import-Csv -LiteralPath $Path)
}

function Get-NumericValues {
    param(
        [AllowEmptyCollection()] [object[]] $Rows,
        [Parameter(Mandatory)] [string] $Property
    )

    return @(
        foreach ($row in @($Rows)) {
            $propertyValue = $row.PSObject.Properties[$Property]
            if ($null -eq $propertyValue) {
                continue
            }
            $value = ConvertTo-NullableDouble $propertyValue.Value
            if ($null -ne $value) {
                $value
            }
        }
    )
}

function Get-MetricSummary {
    param(
        [AllowEmptyCollection()] [double[]] $Values,
        [Parameter(Mandatory)] [int] $ExpectedSamples,
        [Parameter(Mandatory)] [bool] $PrerequisiteValid,
        [AllowNull()] [string] $PrerequisiteReason
    )

    $measuredValues = @(
        $Values | Where-Object { $null -ne $_ }
    )
    $coverage = Test-MetricCoverage `
        -ExpectedSamples $ExpectedSamples `
        -ValidSamples $measuredValues.Count `
        -MinRatio $MinCoverageRatio
    $status = if (-not $PrerequisiteValid) {
        'not_evaluated'
    } elseif ($coverage.status -eq 'available') {
        'available'
    } else {
        $coverage.status
    }
    $reason = if (-not $PrerequisiteValid) {
        $PrerequisiteReason
    } else {
        $coverage.reason
    }
    return [pscustomobject]@{
        status = $status
        reason = $reason
        mean = if ($measuredValues.Count -gt 0) {
            Get-Mean $measuredValues
        } else {
            $null
        }
        stdev = if ($measuredValues.Count -gt 0) {
            Get-StandardDeviation $measuredValues
        } else {
            $null
        }
        valid_samples = $measuredValues.Count
        expected_samples = $ExpectedSamples
        coverage_pct = if ($null -ne $coverage.ratio) {
            [Math]::Min(100.0, $coverage.ratio * 100.0)
        } else {
            $null
        }
    }
}

function New-CaptureEvidenceFailure {
    param(
        [Parameter(Mandatory)] [string] $Status,
        [Parameter(Mandatory)] [string] $Reason
    )

    return [pscustomobject]@{
        Status = $Status
        Reason = $Reason
        RawPresentCount = $null
        PresentCount = $null
        SwapChainCount = $null
        MinSwapChainFps = $null
        MeanSwapChainFps = $null
        MaxSwapChainFps = $null
        ActiveValid = $false
        ActiveReason = $Reason
        Rows = @()
    }
}

function Get-CaptureEvidence {
    param(
        [Parameter(Mandatory)] [psobject] $Capture,
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [int] $ExpectedSwapChains,
        [Parameter(Mandatory)] [string] $ManifestDirectory,
        [AllowNull()] $WindowStartUtc,
        [AllowNull()] $WindowEndUtc
    )

    $captureStatus = [string](
        Get-PropertyValue $Capture 'status' 'unavailable'
    )
    if ($captureStatus -ne 'available') {
        return New-CaptureEvidenceFailure `
            -Status $captureStatus `
            -Reason ([string](Get-PropertyValue $Capture 'reason' (
                "capture status is '$captureStatus'"
            )))
    }

    $metadataReasons = [Collections.Generic.List[string]]::new()
    $captureTargetPid = ConvertTo-NullableInt (
        Get-PropertyValue $Capture 'target_process_id'
    )
    if ($captureTargetPid -ne $ProcessId) {
        $metadataReasons.Add(
            "capture target PID '$captureTargetPid' does not match $ProcessId"
        )
    }
    $exitCode = ConvertTo-NullableInt (
        Get-PropertyValue $Capture 'exit_code'
    )
    if ($exitCode -ne 0) {
        $metadataReasons.Add("capture exit code is '$exitCode'")
    }
    $arguments = @(
        Get-PropertyValue $Capture 'arguments' @()
    )
    $pidArgumentIndex = [Array]::IndexOf(
        [object[]]$arguments,
        '--process_id'
    )
    if ($pidArgumentIndex -lt 0 -or
        $pidArgumentIndex + 1 -ge $arguments.Count -or
        (ConvertTo-NullableInt $arguments[$pidArgumentIndex + 1]) -ne
            $ProcessId) {
        $metadataReasons.Add('capture arguments do not target the cell PID')
    }
    if ($arguments -notcontains '--date_time') {
        $metadataReasons.Add('capture arguments do not request absolute timestamps')
    }
    $captureDuration = ConvertTo-NullableDouble (
        Get-PropertyValue $Capture 'duration_sec'
    )
    if ($null -eq $captureDuration -or $captureDuration -le 0) {
        $metadataReasons.Add('capture duration is missing or invalid')
    }
    $captureStart = $null
    $captureEnd = $null
    try {
        $captureStart = ConvertTo-Utc (
            Get-RequiredProperty `
                -Object $Capture `
                -Name 'start_utc' `
                -Context 'PresentMon capture'
        )
        $captureEnd = ConvertTo-Utc (
            Get-RequiredProperty `
                -Object $Capture `
                -Name 'end_utc' `
                -Context 'PresentMon capture'
        )
    } catch {
        $metadataReasons.Add($_.Exception.Message)
    }
    if ($null -ne $captureStart -and $null -ne $captureEnd) {
        if ($captureEnd -le $captureStart) {
            $metadataReasons.Add('capture interval is empty or reversed')
        } elseif ($null -ne $captureDuration -and
            ($captureEnd - $captureStart).TotalSeconds + 0.25 -lt
                $captureDuration) {
            $metadataReasons.Add('capture interval is shorter than requested')
        }
        if ($WindowStartUtc -and
            $captureStart -gt (ConvertTo-Utc $WindowStartUtc)) {
            $metadataReasons.Add(
                'capture starts after the measurement interval'
            )
        }
        if ($WindowEndUtc -and
            $captureEnd -lt (ConvertTo-Utc $WindowEndUtc)) {
            $metadataReasons.Add(
                'capture ends before the measurement interval'
            )
        }
    }
    if ($metadataReasons.Count -gt 0) {
        return New-CaptureEvidenceFailure `
            -Status 'error' `
            -Reason ($metadataReasons -join '; ')
    }

    $path = Resolve-ArtifactPath `
        -Path ([string](Get-PropertyValue $Capture 'output_path')) `
        -ManifestDirectory $ManifestDirectory
    $parsed = ConvertFrom-PresentMonCsv `
        -Path $path `
        -TargetProcessId $ProcessId
    if ($parsed.status -ne 'available') {
        return New-CaptureEvidenceFailure `
            -Status $parsed.status `
            -Reason $parsed.reason
    }

    $startValue = if ($WindowStartUtc) {
        $WindowStartUtc
    } else {
        Get-PropertyValue $Capture 'start_utc'
    }
    $endValue = if ($WindowEndUtc) {
        $WindowEndUtc
    } else {
        Get-PropertyValue $Capture 'end_utc'
    }
    if ([string]::IsNullOrWhiteSpace($startValue) -or
        [string]::IsNullOrWhiteSpace($endValue)) {
        return [pscustomobject]@{
            Status = 'error'
            Reason = 'Capture has no absolute UTC window.'
            RawPresentCount = @($parsed.rows).Count
            PresentCount = $null
            SwapChainCount = $null
            MinSwapChainFps = $null
            MeanSwapChainFps = $null
            MaxSwapChainFps = $null
            ActiveValid = $false
            ActiveReason = 'Capture has no absolute UTC window.'
            Rows = @()
        }
    }
    try {
        $startUtc = ConvertTo-Utc $startValue
        $endUtc = ConvertTo-Utc $endValue
        $rows = @(
            Select-PresentMonRowsInWindow `
                -Rows $parsed.rows `
                -ProcessId $ProcessId `
                -StartUtc $startUtc `
                -EndUtc $endUtc
        )
        $cadence = @(
            Measure-SwapChainCadence `
                -Rows $rows `
                -WindowStartUtc $startUtc `
                -WindowEndUtc $endUtc
        )
    } catch {
        return [pscustomobject]@{
            Status = 'error'
            Reason = $_.Exception.Message
            RawPresentCount = @($parsed.rows).Count
            PresentCount = $null
            SwapChainCount = $null
            MinSwapChainFps = $null
            MeanSwapChainFps = $null
            MaxSwapChainFps = $null
            ActiveValid = $false
            ActiveReason = $_.Exception.Message
            Rows = @()
        }
    }

    $rates = @($cadence | ForEach-Object { $_.effective_fps })
    $minimumPresents = if ($cadence.Count -gt 0) {
        ($cadence.present_count | Measure-Object -Minimum).Minimum
    } else {
        0
    }
    $activeValid = (
        $rows.Count -gt 0 -and
        $ExpectedSwapChains -gt 0 -and
        $cadence.Count -eq $ExpectedSwapChains -and
        $minimumPresents -ge 2
    )
    $rawRange = if (@($parsed.rows).Count -gt 0) {
        '{0}..{1}' -f
            $parsed.rows[0].sample_utc,
            $parsed.rows[-1].sample_utc
    } else {
        '<empty>'
    }
    $activeReason = if ($activeValid) {
        $null
    } elseif ($rows.Count -eq 0) {
        (
            'No target-PID present rows were inside capture window ' +
            "$($startUtc.ToString('o'))..$($endUtc.ToString('o')); raw range " +
            "$rawRange."
        )
    } elseif ($cadence.Count -ne $ExpectedSwapChains) {
        (
            "Captured $($cadence.Count) swap chain(s), expected exactly " +
            "$ExpectedSwapChains."
        )
    } else {
        'At least one swap chain has fewer than two present rows.'
    }

    return [pscustomobject]@{
        Status = 'available'
        Reason = $null
        RawPresentCount = @($parsed.rows).Count
        PresentCount = $rows.Count
        SwapChainCount = $cadence.Count
        MinSwapChainFps = if ($rates.Count -gt 0) {
            ($rates | Measure-Object -Minimum).Minimum
        } else {
            $null
        }
        MeanSwapChainFps = if ($rates.Count -gt 0) {
            Get-Mean $rates
        } else {
            $null
        }
        MaxSwapChainFps = if ($rates.Count -gt 0) {
            ($rates | Measure-Object -Maximum).Maximum
        } else {
            $null
        }
        ActiveValid = $activeValid
        ActiveReason = $activeReason
        Rows = $rows
    }
}

$manifestPaths = @(
    Get-ChildItem `
        -LiteralPath $ResultsRoot `
        -Filter 'manifest.json' `
        -File `
        -Recurse
)
if ($manifestPaths.Count -eq 0) {
    throw "No manifest.json files found under $ResultsRoot."
}

$manifestRecords = [Collections.Generic.List[object]]::new()
$captureIds = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)
foreach ($manifestFile in $manifestPaths) {
    $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw |
        ConvertFrom-Json
    $context = $manifestFile.FullName
    $schema = [int](Get-RequiredProperty `
        -Object $manifest `
        -Name 'QualificationSchemaVersion' `
        -Context $context)
    if ($schema -ne 2) {
        throw "$context has unsupported qualification schema $schema."
    }
    $captureId = [string](Get-RequiredProperty `
        -Object $manifest `
        -Name 'CaptureId' `
        -Context $context)
    if (-not $captureIds.Add($captureId)) {
        throw "Duplicate CaptureId '$captureId' in $context."
    }
    $machineFingerprint = Get-RequiredProperty `
        -Object $manifest `
        -Name 'MachineFingerprint' `
        -Context $context
    $displayContext = Get-RequiredProperty `
        -Object $manifest `
        -Name 'DisplayContext' `
        -Context $context
    if ([string](Get-RequiredProperty `
        -Object $machineFingerprint `
        -Name 'status' `
        -Context "$context MachineFingerprint") -ne 'available') {
        throw "$context has unavailable machine provenance."
    }
    if ([string](Get-RequiredProperty `
        -Object $displayContext `
        -Name 'status' `
        -Context "$context DisplayContext") -ne 'available') {
        throw "$context has unavailable display provenance."
    }

    $signatureObject = [ordered]@{
        qualification_set_id = [string](Get-RequiredProperty `
            -Object $manifest `
            -Name 'QualificationSetId' `
            -Context $context)
        machine_hash = [string](Get-RequiredProperty `
            -Object $machineFingerprint `
            -Name 'hash' `
            -Context "$context MachineFingerprint")
        display_hash = [string](Get-RequiredProperty `
            -Object $displayContext `
            -Name 'hash' `
            -Context "$context DisplayContext")
        executable_sha256 = [string](Get-RequiredProperty `
            -Object $manifest `
            -Name 'ExecutableSha256' `
            -Context $context)
        config_sha256 = [string](Get-RequiredProperty `
            -Object $manifest `
            -Name 'ConfigSha256' `
            -Context $context)
        target_fps = [int](Get-RequiredProperty `
            -Object $manifest `
            -Name 'RequiredTargetFps' `
            -Context $context)
        platform = [string](Get-RequiredProperty `
            -Object $manifest `
            -Name 'Platform' `
            -Context $context)
        production_entry_point = [string](Get-RequiredProperty `
            -Object $manifest `
            -Name 'ProductionEntryPoint' `
            -Context $context)
        sample_interval_sec = [double](Get-RequiredProperty `
            -Object $manifest `
            -Name 'SampleIntervalSec' `
            -Context $context)
        duration_sec = [int](Get-RequiredProperty `
            -Object $manifest `
            -Name 'DurationSec' `
            -Context $context)
        warmup_sec = [int](Get-RequiredProperty `
            -Object $manifest `
            -Name 'WarmupSec' `
            -Context $context)
        probe_settle_sec = [int](Get-RequiredProperty `
            -Object $manifest `
            -Name 'ProbeSettleSec' `
            -Context $context)
        present_control_duration_sec = [int](Get-RequiredProperty `
            -Object $manifest `
            -Name 'PresentControlDurationSec' `
            -Context $context)
        present_capture_lead_in_sec = [int](Get-PropertyValue `
            $manifest `
            'PresentCaptureLeadInSec' `
            0)
        present_capture_tail_sec = [int](Get-PropertyValue `
            $manifest `
            'PresentCaptureTailSec' `
            0)
        seed = [uint64](Get-RequiredProperty `
            -Object $manifest `
            -Name 'Seed' `
            -Context $context)
        runs = [int](Get-RequiredProperty `
            -Object $manifest `
            -Name 'Runs' `
            -Context $context)
    }
    $signatureJson = $signatureObject | ConvertTo-Json -Compress
    $manifestRecords.Add([pscustomobject]@{
        Path = $manifestFile.FullName
        Directory = $manifestFile.DirectoryName
        Manifest = $manifest
        CaptureId = $captureId
        Signature = $signatureJson
        SignatureObject = $signatureObject
        Valid = (ConvertTo-Bool (
            Get-PropertyValue $manifest 'Aborted' $false
        )) -eq $false
        InvalidReason = if (
            (ConvertTo-Bool (
                Get-PropertyValue $manifest 'Aborted' $false
            )) -ne $false
        ) {
            [string](Get-PropertyValue $manifest 'AbortReason' 'aborted')
        } else {
            $null
        }
    })
}

$signatureGroups = @($manifestRecords | Group-Object Signature)
if ($signatureGroups.Count -ne 1) {
    $details = @(
        $signatureGroups | ForEach-Object {
            "$($_.Count)x $($_.Name)"
        }
    ) -join [Environment]::NewLine
    throw (
        'Refusing to combine mixed qualification provenance. Found ' +
        "$($signatureGroups.Count) signatures:`n$details"
    )
}
$provenance = $manifestRecords[0].SignatureObject

$cellRows = [Collections.Generic.List[object]]::new()
foreach ($manifestRecord in $manifestRecords) {
    $manifest = $manifestRecord.Manifest
    $cells = @(Get-PropertyValue $manifest 'Cells' @())
    foreach ($entry in $cells) {
        $cellContext = "$($manifestRecord.Path):$(
            Get-PropertyValue $entry 'CellTag' '<untagged>'
        )"
        if ([int](Get-RequiredProperty `
            -Object $entry `
            -Name 'SchemaVersion' `
            -Context $cellContext) -ne 2) {
            throw "$cellContext is not benchmark schema v2."
        }
        if ([string](Get-RequiredProperty `
            -Object $entry `
            -Name 'CaptureId' `
            -Context $cellContext) -ne $manifestRecord.CaptureId) {
            throw "$cellContext CaptureId does not match its manifest."
        }

        $expectedSamples = [int](Get-RequiredProperty `
            -Object $entry `
            -Name 'ExpectedSampleCount' `
            -Context $cellContext)
        $durationSec = [double](Get-RequiredProperty `
            -Object $entry `
            -Name 'DurationSec' `
            -Context $cellContext)
        $sampleIntervalSec = [double](Get-RequiredProperty `
            -Object $entry `
            -Name 'SampleIntervalSec' `
            -Context $cellContext)
        if ($durationSec -ne [double]$manifestRecord.SignatureObject.duration_sec -or
            $sampleIntervalSec -ne
                [double]$manifestRecord.SignatureObject.sample_interval_sec) {
            throw "$cellContext timing does not match its sweep manifest."
        }
        $theoreticalSamples = [int][Math]::Floor(
            $durationSec / $sampleIntervalSec
        )
        if ($expectedSamples -ne $theoreticalSamples -or
            $theoreticalSamples -lt 1) {
            throw (
                "$cellContext declares $expectedSamples expected samples, but " +
                "floor($durationSec / $sampleIntervalSec) is " +
                "$theoreticalSamples."
            )
        }
        $measurementStartUtc = ConvertTo-Utc (Get-RequiredProperty `
            -Object $entry `
            -Name 'MeasurementStartUtc' `
            -Context $cellContext)
        $measurementEndUtc = ConvertTo-Utc (Get-RequiredProperty `
            -Object $entry `
            -Name 'MeasurementEndUtc' `
            -Context $cellContext)
        if ($measurementEndUtc -le $measurementStartUtc) {
            throw "$cellContext has an empty or reversed measurement interval."
        }
        $scenario = [string](Get-RequiredProperty `
            -Object $entry `
            -Name 'Scenario' `
            -Context $cellContext)
        if ($scenario -ne [string](Get-RequiredProperty `
            -Object $manifest `
            -Name 'Scenario' `
            -Context $manifestRecord.Path)) {
            throw "$cellContext scenario does not match its sweep manifest."
        }
        if ([int](Get-RequiredProperty `
            -Object $entry `
            -Name 'RequiredTargetFps' `
            -Context $cellContext) -ne
            [int]$manifestRecord.SignatureObject.target_fps) {
            throw "$cellContext target FPS does not match its sweep manifest."
        }
        $runNumber = [int](Get-RequiredProperty `
            -Object $entry `
            -Name 'Run' `
            -Context $cellContext)
        if ($runNumber -lt 1 -or
            $runNumber -gt [int]$manifestRecord.SignatureObject.runs) {
            throw "$cellContext run index is outside its sweep manifest."
        }
        $powerState = [string](Get-RequiredProperty `
            -Object $entry `
            -Name 'PowerState' `
            -Context $cellContext)
        $processId = ConvertTo-NullableInt (
            Get-PropertyValue $entry 'ProcessId'
        )
        $samplePath = Resolve-ArtifactPath `
            -Path ([string](Get-PropertyValue $entry 'SampleCsv')) `
            -ManifestDirectory $manifestRecord.Directory
        $gpuPath = Resolve-ArtifactPath `
            -Path ([string](Get-PropertyValue $entry 'GpuCsv')) `
            -ManifestDirectory $manifestRecord.Directory
        $powerPath = Resolve-ArtifactPath `
            -Path ([string](Get-PropertyValue $entry 'PowerCsv')) `
            -ManifestDirectory $manifestRecord.Directory
        $energyPath = Resolve-ArtifactPath `
            -Path ([string](Get-PropertyValue $entry 'EnergyCsv')) `
            -ManifestDirectory $manifestRecord.Directory
        $throttlePath = Resolve-ArtifactPath `
            -Path ([string](Get-PropertyValue $entry 'ThrottleCsv')) `
            -ManifestDirectory $manifestRecord.Directory

        $sampleSeries = Get-ValidatedArtifactSeries `
            -Rows (Import-CsvArtifact $samplePath) `
            -ExpectedSamples $expectedSamples `
            -WindowStartUtc $measurementStartUtc `
            -WindowEndUtc $measurementEndUtc `
            -Label 'sample CSV' `
            -RequireMeasuredFlag
        $measuredSamples = @($sampleSeries.Rows)
        $gpuSeries = Get-ValidatedArtifactSeries `
            -Rows (Import-CsvArtifact $gpuPath) `
            -ExpectedSamples $expectedSamples `
            -WindowStartUtc $measurementStartUtc `
            -WindowEndUtc $measurementEndUtc `
            -Label 'GPU CSV' `
            -AllowDuplicateIndices
        $gpuObjects = @($gpuSeries.Rows)
        $powerSeries = Get-ValidatedArtifactSeries `
            -Rows (Import-CsvArtifact $powerPath) `
            -ExpectedSamples $expectedSamples `
            -WindowStartUtc $measurementStartUtc `
            -WindowEndUtc $measurementEndUtc `
            -Label 'power CSV' `
            -RequireMeasuredFlag
        $measuredPower = @($powerSeries.Rows)
        $energySeries = Get-ValidatedArtifactSeries `
            -Rows (Import-CsvArtifact $energyPath) `
            -ExpectedSamples $expectedSamples `
            -WindowStartUtc $measurementStartUtc `
            -WindowEndUtc $measurementEndUtc `
            -Label 'energy CSV' `
            -AllowDuplicateIndices
        $energyObjects = @($energySeries.Rows)
        $throttleSeries = Get-ValidatedArtifactSeries `
            -Rows (Import-CsvArtifact $throttlePath) `
            -ExpectedSamples $expectedSamples `
            -WindowStartUtc $measurementStartUtc `
            -WindowEndUtc $measurementEndUtc `
            -Label 'throttling CSV' `
            -RequireMeasuredFlag
        $measuredThrottle = @($throttleSeries.Rows)

        $powerCoverage = Test-MetricCoverage `
            -ExpectedSamples $expectedSamples `
            -ValidSamples $measuredPower.Count `
            -MinRatio $MinCoverageRatio
        $powerReasons = [Collections.Generic.List[string]]::new()
        if (-not $powerSeries.Valid) {
            $powerReasons.Add($powerSeries.Reason)
        }
        if ($powerCoverage.status -ne 'available') {
            $powerReasons.Add($powerCoverage.reason)
        }
        if (-not $manifestRecord.Valid) {
            $powerReasons.Add(
                "manifest aborted: $($manifestRecord.InvalidReason)"
            )
        }
        if ((ConvertTo-Bool (Get-PropertyValue `
            $entry `
            'PowerContextValid' `
            $false)) -ne $true) {
            $powerReasons.Add(
                [string](Get-PropertyValue `
                    $entry `
                    'PowerContextReason' `
                    'cell power context is invalid')
            )
        }
        $powerSignatures = @(
            foreach ($powerRow in $measuredPower) {
                $saver = ConvertTo-Bool $powerRow.battery_saver
                $matches = ConvertTo-Bool $powerRow.context_matches
                if ($powerRow.power_status -ne 'available') {
                    $powerReasons.Add(
                        "power sample status '$($powerRow.power_status)'"
                    )
                }
                if ($matches -ne $true) {
                    $powerReasons.Add(
                        'at least one power sample does not match its baseline'
                    )
                }
                if ([string]::IsNullOrWhiteSpace(
                    [string]$powerRow.active_power_scheme_guid)) {
                    $powerReasons.Add(
                        'at least one active power scheme is unavailable'
                    )
                }
                '{0}|{1}|{2}' -f
                    $powerRow.ac_line_status,
                    $saver,
                    $powerRow.active_power_scheme_guid
            }
        )
        $powerSignatures = @($powerSignatures | Select-Object -Unique)
        if ($powerSignatures.Count -ne 1) {
            $powerReasons.Add(
                "measured power context has $($powerSignatures.Count) states"
            )
        }
        $expectedSource = if ($powerState -eq 'ac') {
            'ac'
        } else {
            'battery'
        }
        $expectedSaver = $powerState -eq 'battery-saver'
        foreach ($powerRow in $measuredPower) {
            if ($powerRow.ac_line_status -ne $expectedSource -or
                (ConvertTo-Bool $powerRow.battery_saver) -ne
                    $expectedSaver) {
                $powerReasons.Add(
                    "sample does not match labeled power state '$powerState'"
                )
                break
            }
        }
        $powerStatus = if ($powerReasons.Count -eq 0) {
            'available'
        } else {
            'not_evaluated'
        }

        $sessionReasons = [Collections.Generic.List[string]]::new()
        if (-not $powerSeries.Valid) {
            $sessionReasons.Add($powerSeries.Reason)
        }
        if (-not $manifestRecord.Valid) {
            $sessionReasons.Add(
                "manifest aborted: $($manifestRecord.InvalidReason)"
            )
        }
        if ((ConvertTo-Bool (Get-PropertyValue `
            $entry `
            'SessionContextValid' `
            $false)) -ne $true) {
            $sessionReasons.Add(
                [string](Get-PropertyValue `
                    $entry `
                    'SessionContextReason' `
                    'cell session context is missing or invalid')
            )
        }
        $sessionSignatures = @(
            foreach ($powerRow in $measuredPower) {
                $sessionStatusValue = [string](Get-PropertyValue `
                    $powerRow `
                    'session_status' `
                    'missing')
                $sessionId = [string](Get-PropertyValue `
                    $powerRow `
                    'session_id' `
                    '')
                $connectState = [string](Get-PropertyValue `
                    $powerRow `
                    'session_connect_state' `
                    'missing')
                $lockState = [string](Get-PropertyValue `
                    $powerRow `
                    'session_lock_state' `
                    'missing')
                $sessionMatches = ConvertTo-Bool (Get-PropertyValue `
                    $powerRow `
                    'session_context_matches')
                if ($sessionStatusValue -ne 'available') {
                    $sessionReasons.Add(
                        "session sample status '$sessionStatusValue'"
                    )
                }
                if ($sessionMatches -ne $true) {
                    $sessionReasons.Add(
                        'at least one session sample does not match its baseline'
                    )
                }
                if ([string]::IsNullOrWhiteSpace($sessionId)) {
                    $sessionReasons.Add(
                        'at least one session ID is unavailable'
                    )
                }
                if ($connectState -ne 'active') {
                    $sessionReasons.Add(
                        "session connect state is '$connectState'"
                    )
                }
                if ($lockState -ne 'unlocked') {
                    $sessionReasons.Add(
                        "session lock state is '$lockState'"
                    )
                }
                '{0}|{1}|{2}' -f $sessionId, $connectState, $lockState
            }
        )
        $sessionSignatures = @($sessionSignatures | Select-Object -Unique)
        if ($sessionSignatures.Count -ne 1) {
            $sessionReasons.Add(
                "measured session context has $($sessionSignatures.Count) states"
            )
        }
        $sessionStatus = if ($sessionReasons.Count -eq 0) {
            'available'
        } else {
            'not_evaluated'
        }

        $processAbsenceReasons = [Collections.Generic.List[string]]::new()
        $processAbsenceStatus = 'not_applicable'
        if ($scenario -eq 'no-app-control') {
            if ((ConvertTo-Bool (Get-PropertyValue `
                $entry `
                'NoAppProcessAbsent' `
                $false)) -ne $true) {
                $processAbsenceReasons.Add(
                    [string](Get-PropertyValue `
                        $entry `
                        'NoAppProcessAbsenceReason' `
                        'target-process absence was not proven')
                )
            }
            foreach ($powerRow in $measuredPower) {
                if ((ConvertTo-Bool (Get-PropertyValue `
                    $powerRow `
                    'target_process_absent')) -ne $true) {
                    $processAbsenceReasons.Add(
                        'at least one sample did not prove target-process absence'
                    )
                    break
                }
            }
            $processAbsenceStatus = if (
                $processAbsenceReasons.Count -eq 0
            ) {
                'available'
            } else {
                'not_evaluated'
            }
        }

        $displayValid = (
            $manifestRecord.Valid -and
            (ConvertTo-Bool (
                Get-PropertyValue $entry 'DisplayContextValid' $false
            )) -eq $true
        )
        $cellPrerequisiteValid = (
            $manifestRecord.Valid -and
            $powerStatus -eq 'available' -and
            $sessionStatus -eq 'available' -and
            $sampleSeries.Valid -and
            (
                $scenario -ne 'no-app-control' -or
                $processAbsenceStatus -eq 'available'
            ) -and
            $displayValid
        )
        $cellPrerequisiteReason = @(
            if (-not $manifestRecord.Valid) {
                "manifest aborted: $($manifestRecord.InvalidReason)"
            }
            if ($powerStatus -ne 'available') {
                $powerReasons -join ' | '
            }
            if ($sessionStatus -ne 'available') {
                $sessionReasons -join ' | '
            }
            if (-not $sampleSeries.Valid) {
                $sampleSeries.Reason
            }
            if ($scenario -eq 'no-app-control' -and
                $processAbsenceStatus -ne 'available') {
                $processAbsenceReasons -join ' | '
            }
            if (-not $displayValid) {
                'display context was unavailable or changed'
            }
        ) | Where-Object { $_ }
        $cellPrerequisiteReason = $cellPrerequisiteReason -join ' | '

        $cpuSummary = Get-MetricSummary `
            -Values (Get-NumericValues $measuredSamples 'cpu_core_pct') `
            -ExpectedSamples $expectedSamples `
            -PrerequisiteValid ($cellPrerequisiteValid -and $null -ne $processId) `
            -PrerequisiteReason $cellPrerequisiteReason
        $workingSummary = Get-MetricSummary `
            -Values (Get-NumericValues $measuredSamples 'working_set_mb') `
            -ExpectedSamples $expectedSamples `
            -PrerequisiteValid ($cellPrerequisiteValid -and $null -ne $processId) `
            -PrerequisiteReason $cellPrerequisiteReason
        $gpuValues = Get-NumericValues `
            @($measuredSamples | Where-Object {
                $_.gpu_status -eq 'available'
            }) `
            'gpu_busiest_engine_pct'
        $gpuPidValid = (
            $null -eq $processId -or
            @(
                $gpuObjects | Where-Object {
                    (ConvertTo-NullableInt $_.pid) -ne $processId
                }
            ).Count -eq 0
        )
        $gpuSummary = Get-MetricSummary `
            -Values $gpuValues `
            -ExpectedSamples $expectedSamples `
            -PrerequisiteValid (
                $cellPrerequisiteValid -and
                $null -ne $processId -and
                $gpuPidValid -and
                $gpuSeries.Valid
            ) `
            -PrerequisiteReason $(if (-not $gpuPidValid) {
                'GPU detail rows contain a different PID.'
            } elseif (-not $gpuSeries.Valid) {
                $gpuSeries.Reason
            } else {
                $cellPrerequisiteReason
            })
        $contextSummary = Get-MetricSummary `
            -Values (Get-NumericValues `
                @($measuredSamples | Where-Object {
                    $_.context_switch_status -eq 'available'
                }) `
                'process_context_switches_per_sec') `
            -ExpectedSamples $expectedSamples `
            -PrerequisiteValid ($cellPrerequisiteValid -and $null -ne $processId) `
            -PrerequisiteReason $cellPrerequisiteReason

        $sysRows = @(
            $energyObjects | Where-Object {
                [string]$_.meter -ieq 'SYS' -and
                [string]$_.counter_status -eq 'available' -and
                -not [string]::IsNullOrWhiteSpace([string]$_.power_mw)
            }
        )
        $duplicateSys = @(
            $sysRows |
                Group-Object sample_index |
                Where-Object { $_.Count -ne 1 }
        ).Count -gt 0
        $sysValues = if ($duplicateSys) {
            @()
        } else {
            Get-NumericValues $sysRows 'power_mw'
        }
        $sysSummary = Get-MetricSummary `
            -Values $sysValues `
            -ExpectedSamples $expectedSamples `
            -PrerequisiteValid (
                $cellPrerequisiteValid -and
                -not $duplicateSys -and
                $energySeries.Valid
            ) `
            -PrerequisiteReason $(if ($duplicateSys) {
                'SYS Energy Meter has duplicate rows for a sample.'
            } elseif (-not $energySeries.Valid) {
                $energySeries.Reason
            } else {
                $cellPrerequisiteReason
            })

        $capacityRows = @(
            foreach ($powerRow in $measuredPower) {
                if ($powerRow.battery_telemetry_status -ne 'available') {
                    continue
                }
                $capacity = ConvertTo-NullableDouble (
                    $powerRow.battery_remaining_capacity_mwh
                )
                if ($null -ne $capacity) {
                    [pscustomobject]@{
                        CapacityMwh = $capacity
                        SampleUtc = ConvertTo-Utc $powerRow.sample_utc
                    }
                }
            }
        ) | Sort-Object SampleUtc
        $batteryCapacityCoverage = Test-MetricCoverage `
            -ExpectedSamples $expectedSamples `
            -ValidSamples $capacityRows.Count `
            -MinRatio $MinCoverageRatio
        $batteryEnergyDeltaMwh = $null
        $batteryAveragePowerMw = $null
        $batteryEnergyStatus = if ($powerState -eq 'ac') {
            'not_applicable'
        } elseif (-not $cellPrerequisiteValid) {
            'not_evaluated'
        } elseif ($batteryCapacityCoverage.status -ne 'available') {
            $batteryCapacityCoverage.status
        } else {
            'insufficient_resolution'
        }
        $batteryEnergyReason = if ($powerState -eq 'ac') {
            'Battery discharge is not applicable on AC.'
        } elseif (-not $cellPrerequisiteValid) {
            $cellPrerequisiteReason
        } elseif ($batteryCapacityCoverage.status -ne 'available') {
            $batteryCapacityCoverage.reason
        } else {
            'A positive battery-capacity delta was not resolved.'
        }
        if ($batteryEnergyStatus -eq 'insufficient_resolution' -and
            $capacityRows.Count -ge 2) {
            $delta = $capacityRows[0].CapacityMwh -
                $capacityRows[-1].CapacityMwh
            $elapsedSec = (
                $capacityRows[-1].SampleUtc -
                $capacityRows[0].SampleUtc
            ).TotalSeconds
            if ($delta -gt 0 -and $elapsedSec -gt 0) {
                $batteryEnergyDeltaMwh = $delta
                $batteryAveragePowerMw = $delta / ($elapsedSec / 3600.0)
                $batteryEnergyStatus = 'available'
                $batteryEnergyReason = $null
            }
        }
        $batteryRateValues = @(
            Get-NumericValues `
                @($measuredPower | Where-Object {
                    $_.battery_telemetry_status -eq 'available'
                }) `
                'battery_discharge_rate_mw'
        )

        $energySource = if ($sysSummary.status -eq 'available') {
            'energy_meter_sys'
        } elseif ($batteryEnergyStatus -eq 'available' -and
            $cellPrerequisiteValid) {
            'battery_capacity'
        } else {
            'unavailable'
        }
        $energyStatus = if ($energySource -ne 'unavailable') {
            'available'
        } else {
            'not_evaluated'
        }
        $energyReason = if ($energyStatus -eq 'available') {
            $null
        } else {
            @(
                $sysSummary.reason
                $batteryEnergyReason
            ) | Where-Object { $_ } | Select-Object -Unique
        }
        $energyReason = $energyReason -join ' | '
        $energyMean = if ($energySource -eq 'energy_meter_sys') {
            $sysSummary.mean
        } elseif ($energySource -eq 'battery_capacity') {
            $batteryAveragePowerMw
        } else {
            $null
        }

        $throttleAvailable = @(
            $measuredThrottle | Where-Object { $_.status -eq 'available' }
        )
        $throttleCoverage = Test-MetricCoverage `
            -ExpectedSamples $expectedSamples `
            -ValidSamples $throttleAvailable.Count `
            -MinRatio $MinCoverageRatio
        $throttleStatus = if ($null -eq $processId) {
            'not_applicable'
        } elseif (-not $throttleSeries.Valid) {
            'not_evaluated'
        } elseif (-not $cellPrerequisiteValid) {
            'not_evaluated'
        } elseif ($throttleCoverage.status -eq 'available') {
            'available'
        } else {
            $throttleCoverage.status
        }
        $throttleValues = @(
            foreach ($throttleRow in $throttleAvailable) {
                ConvertTo-Bool $throttleRow.execution_speed_throttled
            }
        )
        $invalidThrottleValues = @(
            $throttleValues | Where-Object { $null -eq $_ }
        ).Count
        $throttledCount = @(
            $throttleValues | Where-Object { $_ -eq $true }
        ).Count
        $processPowerState = if ($throttleStatus -ne 'available') {
            'unavailable'
        } elseif ($invalidThrottleValues -gt 0) {
            $throttleStatus = 'not_evaluated'
            'unavailable'
        } elseif ($throttledCount -eq 0) {
            'unthrottled'
        } elseif ($throttledCount -eq $throttleValues.Count) {
            'throttled'
        } else {
            $throttleStatus = 'not_evaluated'
            'mixed'
        }
        $throttleReason = if ($throttleStatus -eq 'available' -or
            $throttleStatus -eq 'not_applicable') {
            $null
        } elseif (-not $throttleSeries.Valid) {
            $throttleSeries.Reason
        } elseif (-not $cellPrerequisiteValid) {
            $cellPrerequisiteReason
        } elseif ($invalidThrottleValues -gt 0) {
            'At least one available throttling row has no Boolean state.'
        } elseif ($processPowerState -eq 'mixed') {
            'OS process throttling state changed during the cell.'
        } else {
            $throttleCoverage.reason
        }

        $presentEvidence = $null
        $beforeEvidence = $null
        $afterEvidence = $null
        $presentActiveStatus = 'not_applicable'
        $presentActiveReason = 'no-app-control has no target process.'
        $presentSuppressionStatus = 'not_applicable'
        $presentSuppressionReason = (
            'Visible/no-app cells do not use a suppression proof.'
        )
        if ($null -ne $processId) {
            $expectedSwapChains = [int](Get-PropertyValue `
                $entry `
                'ExpectedVisibleWindowCount' `
                0)
            $presentEvidence = Get-CaptureEvidence `
                -Capture (Get-RequiredProperty `
                    -Object $entry `
                    -Name 'PresentCapture' `
                    -Context $cellContext) `
                -ProcessId $processId `
                -ExpectedSwapChains $expectedSwapChains `
                -ManifestDirectory $manifestRecord.Directory `
                -WindowStartUtc (Get-PropertyValue `
                    $entry `
                    'MeasurementStartUtc') `
                -WindowEndUtc (Get-PropertyValue `
                    $entry `
                    'MeasurementEndUtc')
            if ($scenario -eq 'visible') {
                $presentActiveStatus = if (
                    -not $cellPrerequisiteValid
                ) {
                    'not_evaluated'
                } elseif (
                    $presentEvidence.Status -eq 'available' -and
                    $presentEvidence.ActiveValid
                ) {
                    'pass'
                } elseif ($presentEvidence.Status -eq 'available') {
                    'fail'
                } else {
                    'not_evaluated'
                }
                $presentActiveReason = if (-not $cellPrerequisiteValid) {
                    $cellPrerequisiteReason
                } elseif ($presentActiveStatus -eq 'pass') {
                    $null
                } else {
                    $presentEvidence.ActiveReason
                }
            } else {
                $beforeEvidence = Get-CaptureEvidence `
                    -Capture (Get-RequiredProperty `
                        -Object $entry `
                        -Name 'VisibleControlBefore' `
                        -Context $cellContext) `
                    -ProcessId $processId `
                    -ExpectedSwapChains $expectedSwapChains `
                    -ManifestDirectory $manifestRecord.Directory
                $afterEvidence = Get-CaptureEvidence `
                    -Capture (Get-RequiredProperty `
                        -Object $entry `
                        -Name 'VisibleControlAfter' `
                        -Context $cellContext) `
                    -ProcessId $processId `
                    -ExpectedSwapChains $expectedSwapChains `
                    -ManifestDirectory $manifestRecord.Directory
                $visibilityBefore = Get-PropertyValue `
                    $entry `
                    'VisibilityBefore'
                $visibilityDuring = Get-PropertyValue `
                    $entry `
                    'VisibilityDuring'
                $visibilityValid = (
                    $null -ne $visibilityBefore -and
                    $null -ne $visibilityDuring -and
                    [int](Get-PropertyValue $visibilityBefore 'visible' 0) -eq
                        $expectedSwapChains -and
                    [int](Get-PropertyValue $visibilityDuring 'hidden' 0) -eq
                        $expectedSwapChains
                )
                $visibleControlValid = (
                    $beforeEvidence.Status -eq 'available' -and
                    $beforeEvidence.ActiveValid -and
                    $visibilityValid
                )
                $resumeValid = (
                    (ConvertTo-Bool (Get-PropertyValue `
                        $entry `
                        'ResumeWindowsVisible' `
                        $false)) -eq $true -and
                    $afterEvidence.Status -eq 'available' -and
                    $afterEvidence.ActiveValid
                )
                $suppressionResult = Test-PresentSuppressionGate `
                    -CaptureStatus $presentEvidence.Status `
                    -SuppressedPresentCount $presentEvidence.PresentCount `
                    -VisibleControlValid $visibleControlValid `
                    -ResumeValid $resumeValid
                if (-not $cellPrerequisiteValid) {
                    $suppressionResult = [pscustomobject]@{
                        status = 'not_evaluated'
                        reason = $cellPrerequisiteReason
                    }
                }
                $presentSuppressionStatus = $suppressionResult.status
                $presentSuppressionReason = $suppressionResult.reason
            }
        }

        $cellRows.Add([pscustomobject]@{
            CaptureId = $manifestRecord.CaptureId
            RunId = [string](Get-RequiredProperty `
                -Object $entry `
                -Name 'RunId' `
                -Context $cellContext)
            CellTag = [string](Get-RequiredProperty `
                -Object $entry `
                -Name 'CellTag' `
                -Context $cellContext)
            ManifestPath = $manifestRecord.Path
            ManifestDirectory = $manifestRecord.Directory
            ManifestValid = $manifestRecord.Valid
            Scenario = $scenario
            SceneName = [string](Get-PropertyValue $entry 'SceneName' 'none')
            PowerState = $powerState
            PowerScheme = if ($powerSignatures.Count -eq 1) {
                ($powerSignatures[0] -split '\|', 3)[2]
            } else {
                $null
            }
            Run = $runNumber
            DurationSec = [double](Get-PropertyValue $entry 'DurationSec' 0)
            ProcessId = $processId
            ExpectedSamples = $expectedSamples
            ActualSamples = $measuredSamples.Count
            MeasurementStartUtc = ConvertTo-UtcString (Get-PropertyValue `
                $entry `
                'MeasurementStartUtc')
            MeasurementEndUtc = ConvertTo-UtcString (Get-PropertyValue `
                $entry `
                'MeasurementEndUtc')
            CellPrerequisiteStatus = if ($cellPrerequisiteValid) {
                'available'
            } else {
                'not_evaluated'
            }
            CellPrerequisiteReason = $cellPrerequisiteReason
            PowerStatus = $powerStatus
            PowerReason = $powerReasons -join ' | '
            PowerCoveragePct = if ($null -ne $powerCoverage.ratio) {
                [Math]::Min(100.0, $powerCoverage.ratio * 100.0)
            } else {
                $null
            }
            SessionStatus = $sessionStatus
            SessionReason = $sessionReasons -join ' | '
            SessionCoveragePct = if ($null -ne $powerCoverage.ratio) {
                [Math]::Min(100.0, $powerCoverage.ratio * 100.0)
            } else {
                $null
            }
            ProcessAbsenceStatus = $processAbsenceStatus
            ProcessAbsenceReason = $processAbsenceReasons -join ' | '
            DisplayStatus = if ($displayValid) {
                'available'
            } else {
                'not_evaluated'
            }
            CpuStatus = $cpuSummary.status
            CpuReason = $cpuSummary.reason
            CpuMean = $cpuSummary.mean
            CpuStdev = $cpuSummary.stdev
            CpuValidSamples = $cpuSummary.valid_samples
            CpuCoveragePct = $cpuSummary.coverage_pct
            WorkingSetStatus = $workingSummary.status
            WorkingSetReason = $workingSummary.reason
            WorkingSetMean = $workingSummary.mean
            WorkingSetStdev = $workingSummary.stdev
            WorkingSetValidSamples = $workingSummary.valid_samples
            WorkingSetCoveragePct = $workingSummary.coverage_pct
            GpuStatus = $gpuSummary.status
            GpuReason = $gpuSummary.reason
            GpuMean = $gpuSummary.mean
            GpuStdev = $gpuSummary.stdev
            GpuValidSamples = $gpuSummary.valid_samples
            GpuCoveragePct = $gpuSummary.coverage_pct
            ContextStatus = $contextSummary.status
            ContextReason = $contextSummary.reason
            ContextMean = $contextSummary.mean
            ContextStdev = $contextSummary.stdev
            ContextValidSamples = $contextSummary.valid_samples
            ContextCoveragePct = $contextSummary.coverage_pct
            EnergySource = $energySource
            EnergyStatus = $energyStatus
            EnergyReason = $energyReason
            EnergyMean = $energyMean
            EnergyStdev = if ($energySource -eq 'energy_meter_sys') {
                $sysSummary.stdev
            } else {
                $null
            }
            EnergyValidSamples = if (
                $energySource -eq 'energy_meter_sys'
            ) {
                $sysSummary.valid_samples
            } else {
                $capacityRows.Count
            }
            EnergyCoveragePct = if (
                $energySource -eq 'energy_meter_sys'
            ) {
                $sysSummary.coverage_pct
            } else {
                if ($null -ne $batteryCapacityCoverage.ratio) {
                    [Math]::Min(
                        100.0,
                        $batteryCapacityCoverage.ratio * 100.0
                    )
                } else {
                    $null
                }
            }
            BatteryEnergyStatus = $batteryEnergyStatus
            BatteryEnergyReason = $batteryEnergyReason
            BatteryEnergyDeltaMwh = $batteryEnergyDeltaMwh
            BatteryAveragePowerMw = $batteryAveragePowerMw
            BatteryDischargeRateMwMean = if (
                $batteryRateValues.Count -gt 0
            ) {
                Get-Mean $batteryRateValues
            } else {
                $null
            }
            ThrottleStatus = $throttleStatus
            ThrottleReason = $throttleReason
            ThrottleCoveragePct = if (
                $null -ne $throttleCoverage.ratio
            ) {
                [Math]::Min(100.0, $throttleCoverage.ratio * 100.0)
            } else {
                $null
            }
            ThrottledSampleCount = $throttledCount
            ProcessPowerState = $processPowerState
            PresentStatus = if ($null -ne $presentEvidence) {
                $presentEvidence.Status
            } else {
                'not_applicable'
            }
            PresentReason = if ($null -ne $presentEvidence) {
                $presentEvidence.Reason
            } else {
                'no-app-control'
            }
            PresentCount = if ($null -ne $presentEvidence) {
                $presentEvidence.PresentCount
            } else {
                $null
            }
            SwapChainCount = if ($null -ne $presentEvidence) {
                $presentEvidence.SwapChainCount
            } else {
                $null
            }
            MinSwapChainFps = if ($null -ne $presentEvidence) {
                $presentEvidence.MinSwapChainFps
            } else {
                $null
            }
            MeanSwapChainFps = if ($null -ne $presentEvidence) {
                $presentEvidence.MeanSwapChainFps
            } else {
                $null
            }
            MaxSwapChainFps = if ($null -ne $presentEvidence) {
                $presentEvidence.MaxSwapChainFps
            } else {
                $null
            }
            PresentActiveStatus = $presentActiveStatus
            PresentActiveReason = $presentActiveReason
            PresentSuppressionStatus = $presentSuppressionStatus
            PresentSuppressionReason = $presentSuppressionReason
            VisibleControlBeforeStatus = if ($null -ne $beforeEvidence) {
                $beforeEvidence.Status
            } else {
                'not_applicable'
            }
            VisibleControlBeforeActive = if ($null -ne $beforeEvidence) {
                $beforeEvidence.ActiveValid
            } else {
                $false
            }
            VisibleControlBeforeRawPresentCount = if (
                $null -ne $beforeEvidence
            ) {
                $beforeEvidence.RawPresentCount
            } else {
                $null
            }
            VisibleControlBeforeReason = if ($null -ne $beforeEvidence) {
                $beforeEvidence.ActiveReason
            } else {
                'not_applicable'
            }
            VisibleControlAfterStatus = if ($null -ne $afterEvidence) {
                $afterEvidence.Status
            } else {
                'not_applicable'
            }
            VisibleControlAfterActive = if ($null -ne $afterEvidence) {
                $afterEvidence.ActiveValid
            } else {
                $false
            }
            VisibleControlAfterRawPresentCount = if (
                $null -ne $afterEvidence
            ) {
                $afterEvidence.RawPresentCount
            } else {
                $null
            }
            VisibleControlAfterReason = if ($null -ne $afterEvidence) {
                $afterEvidence.ActiveReason
            } else {
                'not_applicable'
            }
            IncrementalEnergyStatus = 'not_evaluated'
            IncrementalEnergyReason = 'No bracketing no-app controls selected.'
            IncrementalEnergyMean = $null
            EnergyFloorMean = $null
            EnergyFloorBeforeTag = $null
            EnergyFloorAfterTag = $null
        })
    }
}

if ($cellRows.Count -eq 0) {
    throw 'Qualification manifests contain no cells.'
}

$duplicateCells = @(
    $cellRows |
        Group-Object Scenario, SceneName, PowerState, RunId |
        Where-Object { $_.Count -gt 1 }
)
if ($duplicateCells.Count -gt 0) {
    throw "Duplicate qualification run cells found: $($duplicateCells[0].Name)"
}

$schemes = @(
    $cellRows |
        Where-Object { $_.PowerStatus -eq 'available' } |
        ForEach-Object { $_.PowerScheme } |
        Where-Object { $_ } |
        Select-Object -Unique
)
if ($schemes.Count -gt 1) {
    throw (
        "Qualification evidence contains $($schemes.Count) active power " +
        'schemes across compared states; refusing cross-scheme references.'
    )
}

$noAppRows = @(
    $cellRows | Where-Object { $_.Scenario -eq 'no-app-control' }
)
foreach ($cell in (
    $cellRows | Where-Object { $_.Scenario -ne 'no-app-control' }
)) {
    if ($cell.EnergyStatus -ne 'available' -or
        [string]::IsNullOrWhiteSpace($cell.MeasurementStartUtc) -or
        [string]::IsNullOrWhiteSpace($cell.MeasurementEndUtc)) {
        continue
    }
    $cellStart = ConvertTo-Utc $cell.MeasurementStartUtc
    $cellEnd = ConvertTo-Utc $cell.MeasurementEndUtc
    $eligible = @(
        $noAppRows | Where-Object {
            $_.PowerState -eq $cell.PowerState -and
            $_.PowerScheme -eq $cell.PowerScheme -and
            $_.EnergyStatus -eq 'available' -and
            $_.EnergySource -eq $cell.EnergySource
        }
    )
    $before = @(
        $eligible | Where-Object {
            (ConvertTo-Utc $_.MeasurementEndUtc) -le $cellStart
        } | Sort-Object {
            ConvertTo-Utc $_.MeasurementEndUtc
        } -Descending
    ) | Select-Object -First 1
    $after = @(
        $eligible | Where-Object {
            (ConvertTo-Utc $_.MeasurementStartUtc) -ge $cellEnd
        } | Sort-Object {
            ConvertTo-Utc $_.MeasurementStartUtc
        }
    ) | Select-Object -First 1
    if ($null -eq $before -or $null -eq $after) {
        $cell.IncrementalEnergyReason = (
            'Both before and after no-app controls are required.'
        )
        continue
    }
    $beforeGap = $cellStart - (
        ConvertTo-Utc $before.MeasurementEndUtc
    )
    $afterGap = (
        ConvertTo-Utc $after.MeasurementStartUtc
    ) - $cellEnd
    if ($beforeGap.TotalMinutes -gt $MaxControlGapMinutes -or
        $afterGap.TotalMinutes -gt $MaxControlGapMinutes) {
        $cell.IncrementalEnergyReason = (
            "A bracketing control is more than $MaxControlGapMinutes minutes " +
            'from the measured cell.'
        )
        continue
    }
    $floor = Get-Mean @(
        [double]$before.EnergyMean,
        [double]$after.EnergyMean
    )
    $cell.IncrementalEnergyStatus = 'available'
    $cell.IncrementalEnergyReason = $null
    $cell.EnergyFloorMean = $floor
    $cell.IncrementalEnergyMean = [double]$cell.EnergyMean - $floor
    $cell.EnergyFloorBeforeTag = $before.CellTag
    $cell.EnergyFloorAfterTag = $after.CellTag
}

function Get-GroupKey {
    param([Parameter(Mandatory)] $Row)
    return "$($Row.SceneName)|$($Row.PowerState)"
}

function New-ReferenceGroup {
    param(
        [AllowEmptyCollection()] [object[]] $Rows,
        [Parameter(Mandatory)] [string] $ValueProperty,
        [Parameter(Mandatory)] [string] $StatusProperty,
        [string] $AvailableStatus = 'available'
    )

    $eligibleRows = @(
        foreach ($row in @($Rows)) {
            if ($null -eq $row) {
                continue
            }
            $status = Get-PropertyValue $row $StatusProperty
            $value = Get-PropertyValue $row $ValueProperty
            $prerequisite = Get-PropertyValue `
                $row `
                'CellPrerequisiteStatus'
            if ($status -eq $AvailableStatus -and
                $null -ne $value -and
                $prerequisite -eq 'available') {
                $row
            }
        }
    )
    $runCount = @(
        $eligibleRows |
            ForEach-Object { $_.RunId } |
            Select-Object -Unique
    ).Count
    $values = @($eligibleRows | ForEach-Object {
        [double]$_.$ValueProperty
    })
    if ($runCount -lt $MinimumRuns -or $values.Count -lt $MinimumRuns) {
        return [pscustomobject]@{
            status = 'unavailable'
            reason = (
                "Only $runCount qualifying repeated run(s); " +
                "$MinimumRuns required."
            )
            mean = $null
            stdev = $null
            run_count = $runCount
        }
    }
    return [pscustomobject]@{
        status = 'available'
        reason = $null
        mean = Get-Mean $values
        stdev = Get-StandardDeviation $values
        run_count = $runCount
    }
}

$budgetRows = [Collections.Generic.List[object]]::new()
function Add-BudgetRow {
    param(
        [Parameter(Mandatory)] [string] $Group,
        [Parameter(Mandatory)] [string] $Gate,
        [Parameter(Mandatory)] [string] $Metric,
        [Parameter(Mandatory)] [string] $Status,
        [AllowNull()] [string] $Reason,
        [AllowNull()] $Detail = $null
    )

    $budgetRows.Add([pscustomobject]@{
        Group = $Group
        Gate = $Gate
        Metric = $Metric
        Status = $Status
        Reason = $Reason
        Detail = $Detail
    })
}

$expectedScenes = @('Grass', 'Desert', 'Winter', 'Autumn', 'Ocean')
$matrixRequirements = [Collections.Generic.List[object]]::new()
foreach ($scene in $expectedScenes) {
    $matrixRequirements.Add([pscustomobject]@{
        Scenario = 'visible'
        Scene = $scene
        Power = 'ac'
    })
    $matrixRequirements.Add([pscustomobject]@{
        Scenario = 'fullscreen-suppression'
        Scene = $scene
        Power = 'ac'
    })
    $matrixRequirements.Add([pscustomobject]@{
        Scenario = 'occlusion-suppression'
        Scene = $scene
        Power = 'ac'
    })
    $matrixRequirements.Add([pscustomobject]@{
        Scenario = 'visible'
        Scene = $scene
        Power = 'battery'
    })
    $matrixRequirements.Add([pscustomobject]@{
        Scenario = 'visible'
        Scene = $scene
        Power = 'battery-saver'
    })
}
foreach ($power in @('ac', 'battery', 'battery-saver')) {
    $matrixRequirements.Add([pscustomobject]@{
        Scenario = 'no-app-control'
        Scene = 'none'
        Power = $power
    })
}
foreach ($requirement in $matrixRequirements) {
    $matching = @(
        $cellRows | Where-Object {
            $_.Scenario -eq $requirement.Scenario -and
            $_.SceneName -eq $requirement.Scene -and
            $_.PowerState -eq $requirement.Power -and
            $_.CellPrerequisiteStatus -eq 'available'
        }
    )
    $runCount = @(
        $matching |
            ForEach-Object { $_.RunId } |
            Select-Object -Unique
    ).Count
    Add-BudgetRow `
        -Group (
            "$($requirement.Scenario):$($requirement.Scene)|" +
            $requirement.Power
        ) `
        -Gate 'matrix-coverage' `
        -Metric 'RepeatedRuns' `
        -Status $(if ($runCount -ge $MinimumRuns) {
            'pass'
        } else {
            'not_evaluated'
        }) `
        -Reason $(if ($runCount -ge $MinimumRuns) {
            $null
        } else {
            "Only $runCount qualifying run(s); $MinimumRuns required."
        }) `
        -Detail @{ run_count = $runCount; minimum_runs = $MinimumRuns }
}

$metricDefs = @(
    [pscustomobject]@{
        Name = 'CPU'
        Mean = 'CpuMean'
        Status = 'CpuStatus'
        Ratio = $VisibleRatioMultiplier
    }
    [pscustomobject]@{
        Name = 'GPU'
        Mean = 'GpuMean'
        Status = 'GpuStatus'
        Ratio = $VisibleRatioMultiplier
    }
    [pscustomobject]@{
        Name = 'ContextSwitches'
        Mean = 'ContextMean'
        Status = 'ContextStatus'
        Ratio = $VisibleRatioMultiplier
    }
    [pscustomobject]@{
        Name = 'WorkingSet'
        Mean = 'WorkingSetMean'
        Status = 'WorkingSetStatus'
        Ratio = $WorkingSetRatioMultiplier
    }
    [pscustomobject]@{
        Name = 'Energy'
        Mean = 'EnergyMean'
        Status = 'EnergyStatus'
        Ratio = $VisibleRatioMultiplier
    }
)

$visibleRows = @(
    $cellRows | Where-Object { $_.Scenario -eq 'visible' }
)
$visibleGroups = @{}
foreach ($group in ($visibleRows | Group-Object { Get-GroupKey $_ })) {
    $visibleGroups[$group.Name] = @($group.Group)
}

foreach ($key in $visibleGroups.Keys) {
    $rows = $visibleGroups[$key]
    foreach ($row in $rows) {
        Add-BudgetRow `
            -Group "visible:$key" `
            -Gate 'visible-present' `
            -Metric "Presents:$($row.CellTag)" `
            -Status $row.PresentActiveStatus `
            -Reason $row.PresentActiveReason `
            -Detail @{
                present_count = $row.PresentCount
                swap_chain_count = $row.SwapChainCount
                max_fps = $row.MaxSwapChainFps
            }
        Add-BudgetRow `
            -Group "visible:$key" `
            -Gate 'process-power-state-observed' `
            -Metric "PowerThrottling:$($row.CellTag)" `
            -Status $(if ($row.ThrottleStatus -eq 'available') {
                'pass'
            } else {
                'not_evaluated'
            }) `
            -Reason $row.ThrottleReason `
            -Detail @{
                coverage_pct = $row.ThrottleCoveragePct
                throttled_samples = $row.ThrottledSampleCount
                process_power_state = $row.ProcessPowerState
            }
    }
    foreach ($metricDef in $metricDefs) {
        $reference = New-ReferenceGroup `
            -Rows $rows `
            -ValueProperty $metricDef.Mean `
            -StatusProperty $metricDef.Status
        $envelope = Get-VisibleReferenceEnvelope `
            -Mean $reference.mean `
            -StdDev $reference.stdev `
            -RatioMultiplier $metricDef.Ratio
        Add-BudgetRow `
            -Group "visible:$key" `
            -Gate 'visible-budget-definition' `
            -Metric $metricDef.Name `
            -Status $(if ($reference.status -eq 'available') {
                'pass'
            } else {
                'not_evaluated'
            }) `
            -Reason $reference.reason `
            -Detail @{
                envelope = $envelope
                reference_mean = $reference.mean
                reference_stdev = $reference.stdev
                run_count = $reference.run_count
            }
    }
}

function Add-SuppressionBudgets {
    param(
        [Parameter(Mandatory)] [string] $ScenarioLabel,
        [AllowEmptyCollection()] [object[]] $SuppressedRows
    )

    foreach ($group in (@($SuppressedRows) | Group-Object {
        Get-GroupKey $_
    })) {
        $rows = @($group.Group)
        $runCount = @(
            $rows |
                Where-Object {
                    $_.CellPrerequisiteStatus -eq 'available'
                } |
                ForEach-Object { $_.RunId } |
                Select-Object -Unique
        ).Count
        Add-BudgetRow `
            -Group "${ScenarioLabel}:$($group.Name)" `
            -Gate 'repeated-runs' `
            -Metric 'RunCount' `
            -Status $(if ($runCount -ge $MinimumRuns) {
                'pass'
            } else {
                'not_evaluated'
            }) `
            -Reason $(if ($runCount -ge $MinimumRuns) {
                $null
            } else {
                "Only $runCount qualifying run(s); $MinimumRuns required."
            }) `
            -Detail @{ run_count = $runCount }

        $visible = if ($visibleGroups.ContainsKey($group.Name)) {
            $visibleGroups[$group.Name]
        } else {
            @()
        }
        foreach ($suppressed in $rows) {
            Add-BudgetRow `
                -Group "${ScenarioLabel}:$($group.Name)" `
                -Gate 'present-suppression' `
                -Metric "Presents:$($suppressed.CellTag)" `
                -Status $suppressed.PresentSuppressionStatus `
                -Reason $suppressed.PresentSuppressionReason `
                -Detail @{
                    present_count = $suppressed.PresentCount
                    before_control_active =
                        $suppressed.VisibleControlBeforeActive
                    after_control_active =
                        $suppressed.VisibleControlAfterActive
                    process_id = $suppressed.ProcessId
                }
            Add-BudgetRow `
                -Group "${ScenarioLabel}:$($group.Name)" `
                -Gate 'process-power-state-observed' `
                -Metric "PowerThrottling:$($suppressed.CellTag)" `
                -Status $(if ($suppressed.ThrottleStatus -eq 'available') {
                    'pass'
                } else {
                    'not_evaluated'
                }) `
                -Reason $suppressed.ThrottleReason `
                -Detail @{
                    coverage_pct = $suppressed.ThrottleCoveragePct
                    throttled_samples =
                        $suppressed.ThrottledSampleCount
                    process_power_state =
                        $suppressed.ProcessPowerState
                }
            foreach ($metricDef in ($metricDefs | Where-Object {
                $_.Name -notin @('WorkingSet', 'Energy')
            })) {
                $reference = New-ReferenceGroup `
                    -Rows $visible `
                    -ValueProperty $metricDef.Mean `
                    -StatusProperty $metricDef.Status
                $ratioResult = Test-SuppressionMetricRatio `
                    -VisibleMean $reference.mean `
                    -SuppressedMean $suppressed.($metricDef.Mean) `
                    -VisibleStatus $reference.status `
                    -SuppressedStatus $suppressed.($metricDef.Status) `
                    -MaxRatio $SuppressionMaxRatio
                Add-BudgetRow `
                    -Group "${ScenarioLabel}:$($group.Name)" `
                    -Gate 'suppression-ratio' `
                    -Metric "$($metricDef.Name):$($suppressed.CellTag)" `
                    -Status $ratioResult.status `
                    -Reason $ratioResult.reason `
                    -Detail @{
                        visible_mean = $reference.mean
                        suppressed_mean =
                            $suppressed.($metricDef.Mean)
                        ratio = $ratioResult.ratio
                        reference_runs = $reference.run_count
                    }
            }

            $visibleEnergy = New-ReferenceGroup `
                -Rows $visible `
                -ValueProperty 'IncrementalEnergyMean' `
                -StatusProperty 'IncrementalEnergyStatus'
            $energyResult = Test-SuppressionMetricRatio `
                -VisibleMean $visibleEnergy.mean `
                -SuppressedMean $suppressed.IncrementalEnergyMean `
                -VisibleStatus $visibleEnergy.status `
                -SuppressedStatus $suppressed.IncrementalEnergyStatus `
                -MaxRatio $SuppressionMaxRatio
            Add-BudgetRow `
                -Group "${ScenarioLabel}:$($group.Name)" `
                -Gate 'suppression-incremental-energy' `
                -Metric "Energy:$($suppressed.CellTag)" `
                -Status $energyResult.status `
                -Reason $energyResult.reason `
                -Detail @{
                    visible_incremental_mean = $visibleEnergy.mean
                    suppressed_incremental =
                        $suppressed.IncrementalEnergyMean
                    suppressed_floor = $suppressed.EnergyFloorMean
                    ratio = $energyResult.ratio
                    before_control =
                        $suppressed.EnergyFloorBeforeTag
                    after_control =
                        $suppressed.EnergyFloorAfterTag
                }
        }
    }
}

Add-SuppressionBudgets `
    -ScenarioLabel 'fullscreen-suppression' `
    -SuppressedRows @(
        $cellRows | Where-Object {
            $_.Scenario -eq 'fullscreen-suppression'
        }
    )
Add-SuppressionBudgets `
    -ScenarioLabel 'occlusion-suppression' `
    -SuppressedRows @(
        $cellRows | Where-Object {
            $_.Scenario -eq 'occlusion-suppression'
        }
    )

foreach ($throttleDefinition in @(
    [pscustomobject]@{ PowerState = 'battery'; Cap = 12.0 }
    [pscustomobject]@{ PowerState = 'battery-saver'; Cap = 5.0 }
)) {
    $throttleRows = @(
        $visibleRows | Where-Object {
            $_.PowerState -eq $throttleDefinition.PowerState
        }
    )
    foreach ($group in ($throttleRows | Group-Object SceneName)) {
        $rows = @($group.Group)
        $acKey = "$($group.Name)|ac"
        $acRows = if ($visibleGroups.ContainsKey($acKey)) {
            $visibleGroups[$acKey]
        } else {
            @()
        }
        $runCount = @(
            $rows |
                Where-Object {
                    $_.CellPrerequisiteStatus -eq 'available'
                } |
                ForEach-Object { $_.RunId } |
                Select-Object -Unique
        ).Count
        Add-BudgetRow `
            -Group (
                "throttle:$($group.Name)|" +
                $throttleDefinition.PowerState
            ) `
            -Gate 'repeated-runs' `
            -Metric 'RunCount' `
            -Status $(if ($runCount -ge $MinimumRuns) {
                'pass'
            } else {
                'not_evaluated'
            }) `
            -Reason $(if ($runCount -ge $MinimumRuns) {
                $null
            } else {
                "Only $runCount qualifying run(s); $MinimumRuns required."
            }) `
            -Detail @{ run_count = $runCount }

        $acCadence = New-ReferenceGroup `
            -Rows $acRows `
            -ValueProperty 'MaxSwapChainFps' `
            -StatusProperty 'PresentActiveStatus' `
            -AvailableStatus 'pass'
        foreach ($throttled in $rows) {
            $cadenceResult = Test-CadenceBudget `
                -MeasuredFps $throttled.MaxSwapChainFps `
                -CapFps $throttleDefinition.Cap `
                -VisibleAcFps $acCadence.mean `
                -NonZeroPresentCount $(if (
                    $null -ne $throttled.PresentCount
                ) {
                    [int]$throttled.PresentCount
                } else {
                    0
                }) `
                -ToleranceFactor $ThrottleToleranceFactor `
                -MinDistinguishRatio $MinThrottleDistinguishRatio
            Add-BudgetRow `
                -Group (
                    "throttle:$($group.Name)|" +
                    $throttleDefinition.PowerState
                ) `
                -Gate 'cadence' `
                -Metric "Cadence:$($throttled.CellTag)" `
                -Status $cadenceResult.status `
                -Reason $cadenceResult.reason `
                -Detail @{
                    measured_fps = $throttled.MaxSwapChainFps
                    cap_fps = $throttleDefinition.Cap
                    visible_ac_fps = $acCadence.mean
                    ac_reference_runs = $acCadence.run_count
                }
            Add-BudgetRow `
                -Group (
                    "throttle:$($group.Name)|" +
                    $throttleDefinition.PowerState
                ) `
                -Gate 'process-power-state-observed' `
                -Metric "PowerThrottling:$($throttled.CellTag)" `
                -Status $(if ($throttled.ThrottleStatus -eq 'available') {
                    'pass'
                } else {
                    'not_evaluated'
                }) `
                -Reason $throttled.ThrottleReason `
                -Detail @{
                    coverage_pct = $throttled.ThrottleCoveragePct
                    throttled_samples =
                        $throttled.ThrottledSampleCount
                    process_power_state =
                        $throttled.ProcessPowerState
                }

            foreach ($metricDef in ($metricDefs | Where-Object {
                $_.Name -notin @('WorkingSet', 'Energy')
            })) {
                $acMetric = New-ReferenceGroup `
                    -Rows $acRows `
                    -ValueProperty $metricDef.Mean `
                    -StatusProperty $metricDef.Status
                if ($cadenceResult.status -ne 'pass' -or
                    $acMetric.status -ne 'available' -or
                    $throttled.($metricDef.Status) -ne 'available') {
                    Add-BudgetRow `
                        -Group (
                            "throttle:$($group.Name)|" +
                            $throttleDefinition.PowerState
                        ) `
                        -Gate 'throttle-model' `
                        -Metric "$($metricDef.Name):$(
                            $throttled.CellTag
                        )" `
                        -Status 'not_evaluated' `
                        -Reason (
                            'Cadence, AC reference, or throttled metric is ' +
                            'unavailable.'
                        )
                    continue
                }
                $expectedBudget = Get-ThrottleExpectedBudget `
                    -FloorValue 0.0 `
                    -VisibleAcValue $acMetric.mean `
                    -ThrottledFps $throttled.MaxSwapChainFps `
                    -VisibleAcFps $acCadence.mean `
                    -StdDevOfRepeatedRuns $acMetric.stdev `
                    -RatioMultiplier $VisibleRatioMultiplier
                $modelResult = Test-VisibleRegressionBudget `
                    -Value $throttled.($metricDef.Mean) `
                    -MetricStatus $throttled.($metricDef.Status) `
                    -Envelope $expectedBudget
                Add-BudgetRow `
                    -Group (
                        "throttle:$($group.Name)|" +
                        $throttleDefinition.PowerState
                    ) `
                    -Gate 'throttle-model' `
                    -Metric "$($metricDef.Name):$(
                        $throttled.CellTag
                    )" `
                    -Status $modelResult.status `
                    -Reason $modelResult.reason `
                    -Detail @{
                        measured = $throttled.($metricDef.Mean)
                        expected_budget = $expectedBudget
                        visible_ac_mean = $acMetric.mean
                    }
            }

            $workingReference = New-ReferenceGroup `
                -Rows $acRows `
                -ValueProperty 'WorkingSetMean' `
                -StatusProperty 'WorkingSetStatus'
            $workingEnvelope = Get-VisibleReferenceEnvelope `
                -Mean $workingReference.mean `
                -StdDev $workingReference.stdev `
                -RatioMultiplier $WorkingSetRatioMultiplier
            $workingResult = Test-VisibleRegressionBudget `
                -Value $throttled.WorkingSetMean `
                -MetricStatus $throttled.WorkingSetStatus `
                -Envelope $workingEnvelope
            if ($workingReference.status -ne 'available') {
                $workingResult = [pscustomobject]@{
                    status = 'not_evaluated'
                    reason = $workingReference.reason
                }
            }
            Add-BudgetRow `
                -Group (
                    "throttle:$($group.Name)|" +
                    $throttleDefinition.PowerState
                ) `
                -Gate 'working-set-envelope' `
                -Metric "WorkingSet:$($throttled.CellTag)" `
                -Status $workingResult.status `
                -Reason $workingResult.reason `
                -Detail @{
                    measured = $throttled.WorkingSetMean
                    envelope = $workingEnvelope
                }

            $acEnergy = New-ReferenceGroup `
                -Rows $acRows `
                -ValueProperty 'IncrementalEnergyMean' `
                -StatusProperty 'IncrementalEnergyStatus'
            if ($cadenceResult.status -ne 'pass' -or
                $acEnergy.status -ne 'available' -or
                $throttled.IncrementalEnergyStatus -ne 'available' -or
                $acEnergy.mean -le 0) {
                Add-BudgetRow `
                    -Group (
                        "throttle:$($group.Name)|" +
                        $throttleDefinition.PowerState
                    ) `
                    -Gate 'throttle-incremental-energy' `
                    -Metric "Energy:$($throttled.CellTag)" `
                    -Status 'not_evaluated' `
                    -Reason (
                        'Cadence or bracketing incremental-energy evidence ' +
                        'is unavailable or below resolution.'
                    )
            } else {
                $energyBudget = Get-ThrottleExpectedBudget `
                    -FloorValue 0.0 `
                    -VisibleAcValue $acEnergy.mean `
                    -ThrottledFps $throttled.MaxSwapChainFps `
                    -VisibleAcFps $acCadence.mean `
                    -StdDevOfRepeatedRuns $acEnergy.stdev `
                    -RatioMultiplier $VisibleRatioMultiplier
                $energyResult = Test-VisibleRegressionBudget `
                    -Value $throttled.IncrementalEnergyMean `
                    -MetricStatus $throttled.IncrementalEnergyStatus `
                    -Envelope $energyBudget
                Add-BudgetRow `
                    -Group (
                        "throttle:$($group.Name)|" +
                        $throttleDefinition.PowerState
                    ) `
                    -Gate 'throttle-incremental-energy' `
                    -Metric "Energy:$($throttled.CellTag)" `
                    -Status $energyResult.status `
                    -Reason $energyResult.reason `
                    -Detail @{
                        measured_incremental =
                            $throttled.IncrementalEnergyMean
                        expected_budget = $energyBudget
                        floor = $throttled.EnergyFloorMean
                    }
            }
        }
    }
}

foreach ($controlGroup in ($noAppRows | Group-Object PowerState)) {
    $validControls = @(
        $controlGroup.Group | Where-Object {
            $_.EnergyStatus -eq 'available' -and
            $_.CellPrerequisiteStatus -eq 'available'
        }
    )
    $runCount = @(
        $validControls |
            ForEach-Object { $_.RunId } |
            Select-Object -Unique
    ).Count
    Add-BudgetRow `
        -Group "no-app:$($controlGroup.Name)" `
        -Gate 'control-coverage' `
        -Metric 'EnergyControls' `
        -Status $(if ($runCount -ge $MinimumRuns) {
            'pass'
        } else {
            'not_evaluated'
        }) `
        -Reason $(if ($runCount -ge $MinimumRuns) {
            $null
        } else {
            "Only $runCount valid energy control(s); $MinimumRuns required."
        }) `
        -Detail @{ run_count = $runCount }
}

$budgetStatuses = @($budgetRows | ForEach-Object { $_.Status })
$overallStatus = if ($budgetStatuses.Count -eq 0) {
    'not_evaluated'
} elseif ($budgetStatuses -contains 'fail') {
    'fail'
} elseif (@($budgetStatuses | Where-Object { $_ -ne 'pass' }).Count -gt 0) {
    'not_evaluated'
} else {
    'pass'
}

$resultColumns = @(
    'CaptureId', 'RunId', 'CellTag', 'Scenario', 'SceneName', 'PowerState',
    'PowerScheme', 'Run', 'DurationSec', 'ProcessId', 'ExpectedSamples',
    'ActualSamples', 'MeasurementStartUtc', 'MeasurementEndUtc',
    'CellPrerequisiteStatus', 'CellPrerequisiteReason', 'PowerStatus',
    'PowerReason', 'PowerCoveragePct', 'SessionStatus', 'SessionReason',
    'SessionCoveragePct', 'ProcessAbsenceStatus', 'ProcessAbsenceReason',
    'DisplayStatus', 'CpuStatus', 'CpuReason',
    'CpuMean', 'CpuStdev', 'CpuValidSamples', 'CpuCoveragePct',
    'WorkingSetStatus', 'WorkingSetReason', 'WorkingSetMean',
    'WorkingSetStdev', 'WorkingSetValidSamples', 'WorkingSetCoveragePct',
    'GpuStatus', 'GpuReason', 'GpuMean', 'GpuStdev', 'GpuValidSamples',
    'GpuCoveragePct', 'ContextStatus', 'ContextReason', 'ContextMean',
    'ContextStdev', 'ContextValidSamples', 'ContextCoveragePct',
    'EnergySource', 'EnergyStatus', 'EnergyReason', 'EnergyMean',
    'EnergyStdev', 'EnergyValidSamples', 'EnergyCoveragePct',
    'BatteryEnergyStatus', 'BatteryEnergyReason', 'BatteryEnergyDeltaMwh',
    'BatteryAveragePowerMw', 'BatteryDischargeRateMwMean', 'ThrottleStatus',
    'ThrottleReason', 'ThrottleCoveragePct', 'ThrottledSampleCount',
    'ProcessPowerState', 'PresentStatus', 'PresentReason', 'PresentCount',
    'SwapChainCount',
    'MinSwapChainFps', 'MeanSwapChainFps', 'MaxSwapChainFps',
    'PresentActiveStatus', 'PresentActiveReason',
    'PresentSuppressionStatus', 'PresentSuppressionReason',
    'VisibleControlBeforeStatus', 'VisibleControlBeforeActive',
    'VisibleControlBeforeRawPresentCount', 'VisibleControlBeforeReason',
    'VisibleControlAfterStatus',
    'VisibleControlAfterActive', 'VisibleControlAfterReason',
    'VisibleControlAfterRawPresentCount',
    'IncrementalEnergyStatus', 'IncrementalEnergyReason',
    'IncrementalEnergyMean', 'EnergyFloorMean', 'EnergyFloorBeforeTag',
    'EnergyFloorAfterTag'
)
Write-CsvWithHeader `
    -Path $OutCsv `
    -Columns $resultColumns `
    -Rows $cellRows
Write-CsvWithHeader `
    -Path $OutBudgetsCsv `
    -Columns @('Group', 'Gate', 'Metric', 'Status', 'Reason') `
    -Rows $budgetRows

$jsonResult = [ordered]@{
    schema_version = 2
    generated_utc = [DateTime]::UtcNow.ToString('o')
    results_root = $ResultsRoot
    provenance = $provenance
    manifest_count = $manifestRecords.Count
    cell_count = $cellRows.Count
    minimum_runs = $MinimumRuns
    minimum_coverage_ratio = $MinCoverageRatio
    overall_status = $overallStatus
    cells = $cellRows
    budgets = $budgetRows
}
$jsonResult |
    ConvertTo-Json -Depth 12 |
    Out-File -LiteralPath $OutJson -Encoding utf8

$budgetCounts = @{}
foreach ($status in @('pass', 'fail', 'not_evaluated')) {
    $budgetCounts[$status] = @(
        $budgetRows | Where-Object { $_.Status -eq $status }
    ).Count
}
$notEvaluatedReasons = @(
    $budgetRows |
        Where-Object { $_.Status -eq 'not_evaluated' } |
        Group-Object Reason |
        Sort-Object Count -Descending |
        Select-Object -First 20
)
$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add('# DesktopGrass production-runtime qualification')
$markdown.Add('')
$markdown.Add("- Overall budget status: **$overallStatus**")
$markdown.Add("- Manifests: $($manifestRecords.Count)")
$markdown.Add("- Cells: $($cellRows.Count)")
$markdown.Add("- Passing gates: $($budgetCounts.pass)")
$markdown.Add("- Failing gates: $($budgetCounts.fail)")
$markdown.Add("- Not evaluated gates: $($budgetCounts.not_evaluated)")
$markdown.Add("- Minimum repeated runs: $MinimumRuns")
$markdown.Add(
    "- Minimum metric coverage: $([Math]::Round(
        $MinCoverageRatio * 100,
        1
    ))%"
)
$markdown.Add('')
$markdown.Add('## Provenance')
$markdown.Add('')
$markdown.Add((
    '- Qualification set: `{0}`' -f
        $provenance.qualification_set_id
))
$markdown.Add(('- Machine: `{0}`' -f $provenance.machine_hash))
$markdown.Add(('- Display context: `{0}`' -f $provenance.display_hash))
$markdown.Add(('- Executable: `{0}`' -f $provenance.executable_sha256))
$markdown.Add(('- Config: `{0}`' -f $provenance.config_sha256))
$markdown.Add("- Target FPS: $($provenance.target_fps)")
$markdown.Add('')
$markdown.Add('## Not evaluated')
$markdown.Add('')
if ($notEvaluatedReasons.Count -eq 0) {
    $markdown.Add('None.')
} else {
    foreach ($reasonGroup in $notEvaluatedReasons) {
        $reason = if ([string]::IsNullOrWhiteSpace($reasonGroup.Name)) {
            'No reason supplied.'
        } else {
            $reasonGroup.Name
        }
        $markdown.Add("- $($reasonGroup.Count)x $reason")
    }
}
$markdown.Add('')
$markdown.Add(
    'A passing automated budget set is not by itself authorization to close ' +
    'issue #14; manual display/session/suspend evidence and hardware support ' +
    'must still be assessed separately.'
)
$markdown |
    Out-File -LiteralPath $OutMarkdown -Encoding utf8

Write-Host "Aggregated $($cellRows.Count) cells from $(
    $manifestRecords.Count
) manifest(s)." -ForegroundColor Green
Write-Host "Overall status: $overallStatus" -ForegroundColor Cyan
Write-Host "Results: $OutCsv"
Write-Host "Budgets: $OutBudgetsCsv"
Write-Host "JSON: $OutJson"
Write-Host "Report: $OutMarkdown"
