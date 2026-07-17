# RuntimeQualification.Common.psm1
#
# Shared helpers for production-runtime qualification of DesktopGrass.Native
# (issue #14). These functions support Run-RuntimeQualification.ps1 and
# Aggregate-RuntimeQualification.ps1. They deliberately reuse
# Benchmark.Common.psm1's schema-v2 sampler/statistics helpers and
# tests\smoke\Smoke.Common.psm1's window/probe helpers instead of duplicating
# them.
#
# This module never launches the app with --benchmark. It only supports
# driving the normal production App path: message-window scene commands,
# safe visible-foreground/occlusion probes, state/autostart backup and
# restore, and strict budget/coverage math for that evidence.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# -Global is required here: without it, PowerShell scopes a module-nested
# Import-Module to this module only, which then hides Benchmark.Common's
# exported functions (Get-Mean, Get-BenchmarkPowerSnapshot, etc.) from any
# caller script that separately imports both modules -- even though the
# caller's own top-level Import-Module of Benchmark.Common.psm1 already ran.
Import-Module (Join-Path $PSScriptRoot 'Benchmark.Common.psm1') -Force -Global

function ConvertTo-NullableDouble {
    param([AllowNull()] $Value)

    if ($null -eq $Value -or
        [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    return [double]$Value
}

function ConvertTo-NullableInt {
    param([AllowNull()] $Value)

    if ($null -eq $Value -or
        [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    return [int]$Value
}

function Get-StringSha256 {
    param([Parameter(Mandatory)] [string] $Value)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
        return ([Convert]::ToHexString($sha.ComputeHash($bytes))).ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Get-RuntimeDisplayContext {
    <#
    .SYNOPSIS
        Captures a stable, hashable display-context snapshot for provenance.
    #>
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        $screens = @(
            [Windows.Forms.Screen]::AllScreens |
                Sort-Object DeviceName |
                ForEach-Object {
                    [ordered]@{
                        device_name = $_.DeviceName
                        primary = $_.Primary
                        bounds_x = $_.Bounds.X
                        bounds_y = $_.Bounds.Y
                        bounds_width = $_.Bounds.Width
                        bounds_height = $_.Bounds.Height
                        working_x = $_.WorkingArea.X
                        working_y = $_.WorkingArea.Y
                        working_width = $_.WorkingArea.Width
                        working_height = $_.WorkingArea.Height
                        bits_per_pixel = $_.BitsPerPixel
                    }
                }
        )
        $videoModes = @(
            Get-CimInstance Win32_VideoController -ErrorAction Stop |
                Sort-Object Name |
                ForEach-Object {
                    [ordered]@{
                        name = $_.Name
                        width = $_.CurrentHorizontalResolution
                        height = $_.CurrentVerticalResolution
                        refresh_hz = $_.CurrentRefreshRate
                    }
                }
        )
        $brightness = try {
            @(
                Get-CimInstance `
                    -Namespace root/WMI `
                    -ClassName WmiMonitorBrightness `
                    -ErrorAction Stop |
                    Sort-Object InstanceName |
                    ForEach-Object {
                        [ordered]@{
                            instance = $_.InstanceName
                            active = $_.Active
                            percent = $_.CurrentBrightness
                        }
                    }
            )
        } catch {
            @()
        }
        $details = [ordered]@{
            screens = $screens
            video_modes = $videoModes
            brightness = $brightness
            brightness_status = if ($brightness.Count -gt 0) {
                'available'
            } else {
                'unsupported'
            }
        }
        $json = $details | ConvertTo-Json -Depth 8 -Compress
        return [pscustomobject]@{
            status = 'available'
            reason = $null
            hash = Get-StringSha256 $json
            details = $details
        }
    } catch {
        return [pscustomobject]@{
            status = 'error'
            reason = $_.Exception.Message
            hash = $null
            details = $null
        }
    }
}

function Get-RuntimeMachineFingerprint {
    <#
    .SYNOPSIS
        Produces a non-portable machine fingerprint for same-machine guards.
    #>
    try {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $system = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
        $product = Get-CimInstance Win32_ComputerSystemProduct -ErrorAction Stop
        $details = [ordered]@{
            computer_name = $env:COMPUTERNAME
            os_caption = $os.Caption
            os_version = $os.Version
            os_build = $os.BuildNumber
            os_architecture = $os.OSArchitecture
            system_manufacturer = $system.Manufacturer
            system_model = $system.Model
            logical_processors = $system.NumberOfLogicalProcessors
            system_uuid = $product.UUID
            process_architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        }
        $json = $details | ConvertTo-Json -Depth 4 -Compress
        return [pscustomobject]@{
            status = 'available'
            reason = $null
            hash = Get-StringSha256 $json
            details = $details
        }
    } catch {
        return [pscustomobject]@{
            status = 'error'
            reason = $_.Exception.Message
            hash = $null
            details = $null
        }
    }
}

# ---------------------------------------------------------------------------
# Repository/output safety
# ---------------------------------------------------------------------------

function Assert-ResultsRootOutsideRepo {
    <#
    .SYNOPSIS
        Refuses a -ResultsRoot that resolves inside the repository. Raw
        qualification evidence (executables hashes, CSVs, logs, machine
        snapshots) must never be committed.
    #>
    param(
        [Parameter(Mandatory)] [string] $ResultsRoot,
        [Parameter(Mandatory)] [string] $RepoRoot
    )

    $resolvedResults = [IO.Path]::GetFullPath($ResultsRoot).TrimEnd('\') + '\'
    $resolvedRepo = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\') + '\'
    if ($resolvedResults.StartsWith(
        $resolvedRepo,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw (
            "ResultsRoot '$ResultsRoot' resolves inside the repository " +
            "('$RepoRoot'). Raw qualification evidence must be stored " +
            "outside the repo; pass a -ResultsRoot elsewhere."
        )
    }
}

function Assert-NoOtherNativeProcess {
    <#
    .SYNOPSIS
        Refuses to run if a DesktopGrass.Native process already exists, so
        the qualification harness never attaches to, competes with, or
        mislabels evidence from an unrelated instance.
    #>
    param([string] $ProcessName = 'DesktopGrass.Native')

    $existing = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($existing.Count -gt 0) {
        $ids = ($existing | ForEach-Object { $_.Id }) -join ', '
        throw (
            "Refusing to start qualification: $($existing.Count) existing " +
            "$ProcessName process(es) already running (PID(s): $ids). " +
            'Close them first.'
        )
    }
}

# ---------------------------------------------------------------------------
# User state/config/autostart guards
# ---------------------------------------------------------------------------

function Backup-QualificationStateFile {
    <#
    .SYNOPSIS
        Captures the exact bytes of a user file (state.json) before the
        harness sends any scene command, so it can be restored byte-for-byte.
    #>
    param([Parameter(Mandatory)] [string] $Path)

    if (Test-Path -LiteralPath $Path) {
        return [pscustomobject]@{
            path = $Path
            existed = $true
            bytes = [IO.File]::ReadAllBytes($Path)
        }
    }
    return [pscustomobject]@{
        path = $Path
        existed = $false
        bytes = $null
    }
}

function Restore-QualificationStateFile {
    <#
    .SYNOPSIS
        Restores a user file to its exact pre-run bytes, or removes it if
        the run itself created it (it did not exist beforehand).
    #>
    param(
        [Parameter(Mandatory)] [pscustomobject] $Backup
    )

    if ($Backup.existed) {
        [IO.File]::WriteAllBytes($Backup.path, $Backup.bytes)
    } elseif (Test-Path -LiteralPath $Backup.path) {
        Remove-Item -LiteralPath $Backup.path -Force
    }
}

function Get-AutoStartRegistryValue {
    <#
    .SYNOPSIS
        Reads the current HKCU Run-key autostart entry so it can be
        compared/restored after the qualification run exits.
    #>
    param(
        [string] $SubKey = 'Software\Microsoft\Windows\CurrentVersion\Run',
        [string] $ValueName = 'DesktopGrass.Native'
    )

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey, $false)
    if ($null -eq $key) {
        return [pscustomobject]@{
            exists = $false
            value = $null
            kind = $null
        }
    }
    try {
        if ($key.GetValueNames() -notcontains $ValueName) {
            return [pscustomobject]@{
                exists = $false
                value = $null
                kind = $null
            }
        }
        $kind = $key.GetValueKind($ValueName)
        $value = $key.GetValue(
            $ValueName,
            $null,
            [Microsoft.Win32.RegistryValueOptions]::
                DoNotExpandEnvironmentNames
        )
        return [pscustomobject]@{
            exists = $true
            value = $value
            kind = $kind.ToString()
        }
    } finally {
        $key.Dispose()
    }
}

function Test-AutoStartChanged {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Before,
        [Parameter(Mandatory)] [pscustomobject] $After
    )

    $beforeValue = $Before.value | ConvertTo-Json -Compress
    $afterValue = $After.value | ConvertTo-Json -Compress
    if ($Before.exists -ne $After.exists -or
        $Before.kind -ne $After.kind -or
        $beforeValue -ne $afterValue) {
        return [pscustomobject]@{
            changed = $true
            reason = (
                'The user Run-key autostart entry changed while the ' +
                'qualification build ran.'
            )
        }
    }
    return [pscustomobject]@{ changed = $false; reason = $null }
}

function Restore-AutoStartRegistryValue {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Backup,
        [string] $SubKey = 'Software\Microsoft\Windows\CurrentVersion\Run',
        [string] $ValueName = 'DesktopGrass.Native'
    )

    if ($Backup.exists) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($SubKey)
        if ($null -eq $key) {
            throw "Unable to open HKCU\$SubKey for autostart restoration."
        }
        try {
            $kind = [Microsoft.Win32.RegistryValueKind](
                [Enum]::Parse(
                    [Microsoft.Win32.RegistryValueKind],
                    [string]$Backup.kind,
                    $true
                )
            )
            $key.SetValue($ValueName, $Backup.value, $kind)
        } finally {
            $key.Dispose()
        }
    } else {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($SubKey, $true)
        if ($null -ne $key) {
            try {
                $key.DeleteValue($ValueName, $false)
            } finally {
                $key.Dispose()
            }
        }
    }
}

function Assert-AutoStartLaunchSafe {
    <#
    .SYNOPSIS
        Refuses a production launch that would reconcile the HKCU Run value.
    #>
    param(
        [Parameter(Mandatory)] [string] $StateFilePath,
        [Parameter(Mandatory)] [string] $ExePath,
        [Parameter(Mandatory)] [pscustomobject] $RegistryValue
    )

    $stateEnabled = $false
    if (Test-Path -LiteralPath $StateFilePath) {
        $state = Get-Content -LiteralPath $StateFilePath -Raw |
            ConvertFrom-Json
        if ($state.PSObject.Properties['autoStart']) {
            $stateEnabled = [bool]$state.autoStart
        }
    }
    $expectedValue = '"' + [IO.Path]::GetFullPath($ExePath) + '"'
    $registryMatchesExe = (
        $RegistryValue.exists -and
        [string]::Equals(
            [string]$RegistryValue.value,
            $expectedValue,
            [StringComparison]::OrdinalIgnoreCase)
    )
    $launchWouldChange = if ($stateEnabled) {
        -not $registryMatchesExe
    } else {
        $RegistryValue.exists
    }
    if ($launchWouldChange) {
        throw (
            'Refusing to launch the qualification build because production ' +
            'autostart reconciliation would modify the current HKCU Run ' +
            'entry. Align or disable Start with Windows manually first.'
        )
    }
    return [pscustomobject]@{
        state_enabled = $stateEnabled
        registry_matches_qualification_exe = $registryMatchesExe
    }
}

function Assert-ProductionTargetFps {
    <#
    .SYNOPSIS
        Requires the production config.json to already resolve to the
        qualification target FPS, rather than rewriting the user's config.
    #>
    param(
        [Parameter(Mandatory)] [string] $ConfigPath,
        [Parameter(Mandatory)] [int] $ExpectedTargetFps
    )

    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        throw (
            "Production config not found at $ConfigPath. Refusing to " +
            'qualify against defaults that may not match the shipped ' +
            'config; create it by running the app once or pass ' +
            '-ConfigFilePath.'
        )
    }
    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $actual = [int]$config.targetFps
    if ($actual -ne $ExpectedTargetFps) {
        throw (
            "Production config targetFps=$actual does not match the " +
            "required qualification targetFps=$ExpectedTargetFps. Fix the " +
            'config or pass -RequiredTargetFps to match it; the harness ' +
            'does not rewrite user config.'
        )
    }
    return $actual
}

function Wait-ForPersistedScene {
    <#
    .SYNOPSIS
        Polls state.json until its persisted scene matches -ExpectedScene,
        so a cell is only labeled once the production state actually
        reflects the requested scene (App.SetScene saves synchronously).
    #>
    param(
        [Parameter(Mandatory)] [string] $StateFilePath,
        [Parameter(Mandatory)] [string] $ExpectedScene,
        [int] $TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $StateFilePath) {
            try {
                $state = Get-Content -LiteralPath $StateFilePath -Raw |
                    ConvertFrom-Json
                if ([string]$state.scene -eq $ExpectedScene) {
                    return $true
                }
            } catch {
                # state.json can be mid-write (rename from .tmp); retry.
            }
        }
        Start-Sleep -Milliseconds 100
    }
    throw (
        "Timed out after ${TimeoutSeconds}s waiting for persisted " +
        "state.json scene to become '$ExpectedScene'."
    )
}

# ---------------------------------------------------------------------------
# Deterministic scene order
# ---------------------------------------------------------------------------

function Get-QualificationSceneOrder {
    <#
    .SYNOPSIS
        Returns a deterministic, seed-reproducible Fisher-Yates shuffle of
        -Scenes so the randomized order can be re-derived and documented
        from the seed alone.
    #>
    param(
        [Parameter(Mandatory)] [int[]] $Scenes,
        [Parameter(Mandatory)] [uint64] $Seed
    )

    $rng = [System.Random]::new([int]($Seed -band 0x7FFFFFFF))
    $order = [System.Collections.Generic.List[int]]::new([int[]]$Scenes)
    for ($i = $order.Count - 1; $i -gt 0; $i--) {
        $j = $rng.Next(0, $i + 1)
        $tmp = $order[$i]
        $order[$i] = $order[$j]
        $order[$j] = $tmp
    }
    return @($order)
}

# ---------------------------------------------------------------------------
# PresentMon CSV parsing
# ---------------------------------------------------------------------------

function ConvertFrom-PresentMonDateTime {
    param([Parameter(Mandatory)] [string] $Value)

    # PresentMon emits nanosecond precision, while DateTimeOffset accepts at
    # most seven fractional digits. Truncation retains 100 ns precision.
    $normalized = [regex]::Replace($Value.Trim(), '(\.\d{7})\d+', '$1')
    $parsed = [DateTimeOffset]::MinValue
    $styles = [Globalization.DateTimeStyles]::AllowWhiteSpaces -bor
        [Globalization.DateTimeStyles]::AssumeLocal
    if (-not [DateTimeOffset]::TryParse(
        $normalized,
        [Globalization.CultureInfo]::InvariantCulture,
        $styles,
        [ref]$parsed)) {
        throw "PresentMon CPUStartDateTime '$Value' is not parseable."
    }
    return $parsed.UtcDateTime
}

function ConvertFrom-PresentMonCsv {
    <#
    .SYNOPSIS
        Parses a PID-filtered PresentMon CSV into normalized absolute UTC rows.

    .DESCRIPTION
        The harness invokes current PresentMon with --date_time and therefore
        requires CPUStartDateTime. It deliberately rejects the legacy relative
        TimeInSeconds shape because process startup and ETW-session latency
        make reconstructed workload labels ambiguous.
        A header-only CSV (PresentMon ran but captured zero presents) is
        'available' with zero rows, which is a materially different, valid
        status from a missing/failed capture.
    #>
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [int] $TargetProcessId
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{
            status = 'unavailable'
            reason = "PresentMon CSV not found: $Path"
            rows = @()
        }
    }

    $lines = @(
        Get-Content -LiteralPath $Path |
            Where-Object { $_.Trim().Length -gt 0 -and $_ -notmatch '^\s*#' }
    )
    if ($lines.Count -eq 0) {
        return [pscustomobject]@{
            status = 'error'
            reason = 'PresentMon CSV has no header row.'
            rows = @()
        }
    }

    $requiredColumns = @('ProcessID', 'SwapChainAddress', 'CPUStartDateTime')
    $header = @(
        ($lines[0] -split ',') |
            ForEach-Object { $_.Trim().Trim('"') }
    )
    foreach ($required in $requiredColumns) {
        if ($header -notcontains $required) {
            return [pscustomobject]@{
                status = 'error'
                reason = (
                    "PresentMon CSV is missing required column " +
                    "'$required'. Present columns: $($header -join ', ')."
                )
                rows = @()
            }
        }
    }

    if ($lines.Count -eq 1) {
        # Header only: PresentMon ran and produced zero present rows. That is
        # a valid measured zero, not a capture failure.
        return [pscustomobject]@{ status = 'available'; reason = $null; rows = @() }
    }

    $records = @($lines | ConvertFrom-Csv)
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($record in $records) {
        $processId = ConvertTo-NullableInt $record.ProcessID
        if ($null -eq $processId) {
            return [pscustomobject]@{
                status = 'error'
                reason = 'A PresentMon row has no numeric ProcessID.'
                rows = @()
            }
        }
        if ($processId -ne $TargetProcessId) {
            return [pscustomobject]@{
                status = 'error'
                reason = (
                    "PresentMon row PID $processId does not match target PID " +
                    "$TargetProcessId."
                )
                rows = @()
            }
        }
        $swapChain = [string]$record.SwapChainAddress
        if ([string]::IsNullOrWhiteSpace($swapChain)) {
            return [pscustomobject]@{
                status = 'error'
                reason = 'A PresentMon row has no SwapChainAddress.'
                rows = @()
            }
        }
        try {
            $sampleUtc = ConvertFrom-PresentMonDateTime `
                -Value ([string]$record.CPUStartDateTime)
        } catch {
            return [pscustomobject]@{
                status = 'error'
                reason = $_.Exception.Message
                rows = @()
            }
        }
        $rows.Add([pscustomobject]@{
            process_id = $processId
            swap_chain = $swapChain
            sample_utc = $sampleUtc.ToString('o')
            dropped = if ($record.PSObject.Properties['Dropped']) {
                ConvertTo-NullableInt $record.Dropped
            } else {
                $null
            }
            ms_between_presents = if (
                $record.PSObject.Properties['MsBetweenPresents']
            ) {
                ConvertTo-NullableDouble $record.MsBetweenPresents
            } else {
                $null
            }
        })
    }
    return [pscustomobject]@{ status = 'available'; reason = $null; rows = $rows }
}

function Select-PresentMonRowsInWindow {
    <#
    .SYNOPSIS
        Filters normalized PresentMon rows (from ConvertFrom-PresentMonCsv)
        to a single PID and an inclusive [StartUtc, EndUtc] measurement
        window, so setup/teardown presents never count toward a cell.
    #>
    param(
        [AllowNull()] [object[]] $Rows,
        [Parameter(Mandatory)] [int] $ProcessId,
        [Parameter(Mandatory)] [datetime] $StartUtc,
        [Parameter(Mandatory)] [datetime] $EndUtc
    )

    $startUtc = $StartUtc.ToUniversalTime()
    $endUtc = $EndUtc.ToUniversalTime()
    return @(
        foreach ($row in @($Rows)) {
            if ($row.process_id -ne $ProcessId) {
                continue
            }
            $sampleUtc = [datetime]::Parse(
                $row.sample_utc,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind
            ).ToUniversalTime()
            if ($sampleUtc -ge $startUtc -and $sampleUtc -le $endUtc) {
                $row
            }
        }
    )
}

function Measure-SwapChainCadence {
    <#
    .SYNOPSIS
        Computes an effective present rate per swap chain over a
        measurement window, so multi-monitor cadence is never multiplied by
        monitor count.
    #>
    param(
        [AllowNull()] [object[]] $Rows,
        [Parameter(Mandatory)] [datetime] $WindowStartUtc,
        [Parameter(Mandatory)] [datetime] $WindowEndUtc
    )

    $windowSec = ($WindowEndUtc - $WindowStartUtc).TotalSeconds
    if ($windowSec -le 0) {
        throw 'Measurement window end must be after start.'
    }

    $grouped = @($Rows) | Group-Object -Property swap_chain
    return @(
        foreach ($group in $grouped) {
            $presentCount = $group.Count
            [pscustomobject]@{
                swap_chain = $group.Name
                present_count = $presentCount
                window_sec = $windowSec
                effective_fps = $presentCount / $windowSec
            }
        }
    )
}

# ---------------------------------------------------------------------------
# Coverage gate
# ---------------------------------------------------------------------------

function Test-MetricCoverage {
    <#
    .SYNOPSIS
        Requires at least -MinRatio (default 90%) of the expected measured
        samples to be valid. Below that, the metric can never pass a budget.
    #>
    param(
        [Parameter(Mandatory)] [int] $ExpectedSamples,
        [Parameter(Mandatory)] [int] $ValidSamples,
        [double] $MinRatio = 0.90
    )

    if ($ExpectedSamples -le 0) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            ratio = $null
            reason = 'No measured samples were expected for this cell.'
        }
    }
    $ratio = $ValidSamples / [double]$ExpectedSamples
    if ($ratio -ge $MinRatio) {
        return [pscustomobject]@{ status = 'available'; ratio = $ratio; reason = $null }
    }
    return [pscustomobject]@{
        status = 'insufficient_coverage'
        ratio = $ratio
        reason = (
            "Coverage $([math]::Round($ratio * 100, 1))% is below the " +
            "required $($MinRatio * 100)%."
        )
    }
}

# ---------------------------------------------------------------------------
# Visible reference envelope and budget gates
# ---------------------------------------------------------------------------

function Get-VisibleReferenceEnvelope {
    <#
    .SYNOPSIS
        max(mean + 3*stdev, mean*RatioMultiplier) -- the same-machine visible
        baseline definition used as an input to later comparisons and the
        throttle model.
    #>
    param(
        [AllowNull()] [Nullable[double]] $Mean,
        [AllowNull()] [Nullable[double]] $StdDev,
        [Parameter(Mandatory)] [double] $RatioMultiplier
    )

    if ($null -eq $Mean) {
        return $null
    }
    $stdev = if ($null -eq $StdDev) { 0.0 } else { $StdDev }
    return [math]::Max($Mean + 3 * $stdev, $Mean * $RatioMultiplier)
}

function Test-VisibleRegressionBudget {
    <#
    .SYNOPSIS
        Compares a measured value against its Get-VisibleReferenceEnvelope
        bound. Any unavailable input is 'not_evaluated', never 'pass'.
    #>
    param(
        [AllowNull()] [Nullable[double]] $Value,
        [Parameter(Mandatory)] [string] $MetricStatus,
        [AllowNull()] [Nullable[double]] $Envelope
    )

    if ($MetricStatus -ne 'available' -or $null -eq $Value -or $null -eq $Envelope) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            reason = 'The measured value or its reference envelope is unavailable.'
        }
    }
    if ($Value -le $Envelope) {
        return [pscustomobject]@{ status = 'pass'; reason = $null }
    }
    return [pscustomobject]@{
        status = 'fail'
        reason = "Measured $Value exceeds the reference envelope $Envelope."
    }
}

function Test-SuppressionMetricRatio {
    <#
    .SYNOPSIS
        Requires a suppressed-state metric mean to be <= MaxRatio (default
        10%) of the matching visible-state mean. An unavailable status on
        either side, or a visible mean at/below zero (below instrument
        resolution), is 'not_evaluated'.
    #>
    param(
        [AllowNull()] [Nullable[double]] $VisibleMean,
        [AllowNull()] [Nullable[double]] $SuppressedMean,
        [Parameter(Mandatory)] [string] $VisibleStatus,
        [Parameter(Mandatory)] [string] $SuppressedStatus,
        [double] $MaxRatio = 0.10
    )

    if ($VisibleStatus -ne 'available' -or $SuppressedStatus -ne 'available' -or
        $null -eq $VisibleMean -or $null -eq $SuppressedMean) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            ratio = $null
            reason = 'The visible or suppressed metric is unavailable.'
        }
    }
    if ($VisibleMean -le 0) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            ratio = $null
            reason = (
                'The visible denominator is at or below instrument resolution.'
            )
        }
    }
    $ratio = $SuppressedMean / $VisibleMean
    if ($ratio -le $MaxRatio) {
        return [pscustomobject]@{ status = 'pass'; ratio = $ratio; reason = $null }
    }
    return [pscustomobject]@{
        status = 'fail'
        ratio = $ratio
        reason = (
            "Suppressed/visible ratio $([math]::Round($ratio, 4)) exceeds " +
            "the $MaxRatio budget."
        )
    }
}

function Test-PresentSuppressionGate {
    <#
    .SYNOPSIS
        The present-suppression proof: capture must be available, paired
        with a valid same-PID visible control and a resume check, and the
        suppressed window must show zero present rows. A measured nonzero
        count fails; any missing prerequisite is 'not_evaluated'.
    #>
    param(
        [Parameter(Mandatory)] [string] $CaptureStatus,
        [AllowNull()] [Nullable[int]] $SuppressedPresentCount,
        [Parameter(Mandatory)] [bool] $VisibleControlValid,
        [Parameter(Mandatory)] [bool] $ResumeValid
    )

    if ($CaptureStatus -ne 'available') {
        return [pscustomobject]@{
            status = 'not_evaluated'
            reason = "PresentMon capture status is '$CaptureStatus'."
        }
    }
    if (-not $VisibleControlValid) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            reason = (
                'No valid same-PID visible control capture is paired with ' +
                'this suppressed interval.'
            )
        }
    }
    if (-not $ResumeValid) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            reason = 'Post-suppression resume verification did not pass.'
        }
    }
    if ($null -eq $SuppressedPresentCount) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            reason = 'The suppressed present-row count is unavailable.'
        }
    }
    if ($SuppressedPresentCount -eq 0) {
        return [pscustomobject]@{ status = 'pass'; reason = $null }
    }
    return [pscustomobject]@{
        status = 'fail'
        reason = (
            "$SuppressedPresentCount present row(s) were captured during " +
            'the suppressed measurement window.'
        )
    }
}

function Test-CadenceBudget {
    <#
    .SYNOPSIS
        The 12/5 FPS policy cadence gate: requires a matching AC visible
        rate high enough to distinguish the cap, then requires the measured
        per-swap-chain rate to stay at or below cap*ToleranceFactor with
        enough nonzero presents to prove the surface stayed active.
    #>
    param(
        [AllowNull()] [Nullable[double]] $MeasuredFps,
        [Parameter(Mandatory)] [double] $CapFps,
        [AllowNull()] [Nullable[double]] $VisibleAcFps,
        [Parameter(Mandatory)] [int] $NonZeroPresentCount,
        [double] $ToleranceFactor = 1.10,
        [double] $MinDistinguishRatio = 1.5
    )

    if ($null -eq $VisibleAcFps -or $VisibleAcFps -lt ($CapFps * $MinDistinguishRatio)) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            reason = (
                'The matching AC visible rate is not high enough to ' +
                'distinguish the cap.'
            )
        }
    }
    if ($null -eq $MeasuredFps -or $NonZeroPresentCount -le 0) {
        return [pscustomobject]@{
            status = 'not_evaluated'
            reason = (
                'No measured nonzero per-swap-chain cadence is available.'
            )
        }
    }
    $limit = $CapFps * $ToleranceFactor
    if ($MeasuredFps -le $limit) {
        return [pscustomobject]@{ status = 'pass'; reason = $null }
    }
    return [pscustomobject]@{
        status = 'fail'
        reason = (
            "Measured cadence $([math]::Round($MeasuredFps, 3)) fps " +
            "exceeds cap*$ToleranceFactor ($limit fps)."
        )
    }
}

function Get-ThrottleExpectedBudget {
    <#
    .SYNOPSIS
        Models the expected throttled resource value from the measured
        paused/no-app floor and the AC visible value:
        floor + (visible - floor) * (throttledFps / visibleFps), then
        returns max(model + 3*stdev, model*RatioMultiplier).
    #>
    param(
        [Parameter(Mandatory)] [double] $FloorValue,
        [Parameter(Mandatory)] [double] $VisibleAcValue,
        [Parameter(Mandatory)] [double] $ThrottledFps,
        [Parameter(Mandatory)] [double] $VisibleAcFps,
        [AllowNull()] [Nullable[double]] $StdDevOfRepeatedRuns,
        [double] $RatioMultiplier = 1.20
    )

    if ($VisibleAcFps -le 0) {
        return $null
    }
    $model = $FloorValue +
        ($VisibleAcValue - $FloorValue) * ($ThrottledFps / $VisibleAcFps)
    $stdev = if ($null -eq $StdDevOfRepeatedRuns) { 0.0 } else { $StdDevOfRepeatedRuns }
    return [math]::Max($model + 3 * $stdev, $model * $RatioMultiplier)
}

Export-ModuleMember -Function @(
    'Assert-AutoStartLaunchSafe',
    'Assert-NoOtherNativeProcess',
    'Assert-ProductionTargetFps',
    'Assert-ResultsRootOutsideRepo',
    'Backup-QualificationStateFile',
    'ConvertFrom-PresentMonCsv',
    'ConvertTo-NullableDouble',
    'ConvertTo-NullableInt',
    'Get-AutoStartRegistryValue',
    'Get-QualificationSceneOrder',
    'Get-RuntimeDisplayContext',
    'Get-RuntimeMachineFingerprint',
    'Get-StringSha256',
    'Get-ThrottleExpectedBudget',
    'Get-VisibleReferenceEnvelope',
    'Measure-SwapChainCadence',
    'Restore-AutoStartRegistryValue',
    'Restore-QualificationStateFile',
    'Select-PresentMonRowsInWindow',
    'Test-AutoStartChanged',
    'Test-CadenceBudget',
    'Test-MetricCoverage',
    'Test-PresentSuppressionGate',
    'Test-SuppressionMetricRatio',
    'Test-VisibleRegressionBudget',
    'Wait-ForPersistedScene'
)
