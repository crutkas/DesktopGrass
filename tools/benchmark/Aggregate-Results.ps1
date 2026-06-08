# tools/benchmark/Aggregate-Results.ps1
# Reads a benchmark sweep directory (produced by Run-Benchmark.ps1) and emits
# results.md plus results.csv with per-(scene, variant) statistics averaged
# across the runs in that cell.
#
# Stats per cell:
#   frame_p50_ms, frame_p95_ms, frame_p99_ms  (from per-frame render_ms)
#   dt_p95_ms                                 (from per-frame dt_ms; reveals stalls)
#   effective_fps                             (frame count / wall duration)
#   cpu_pct_mean, cpu_pct_p95                 (sampler, normalized by NLogical)
#   working_set_mb_peak                       (sampler)
#   private_mb_peak                           (sampler)
#   io_read_kb, io_write_kb                   (manifest)
#
# Each row in results.md/results.csv is the mean across $Runs runs; stdev is
# shown where meaningful.

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$ResultDir,

    [string]$OutMarkdown = (Join-Path $ResultDir 'results.md'),
    [string]$OutCsv      = (Join-Path $ResultDir 'results.csv')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path $ResultDir)) {
    throw "ResultDir not found: $ResultDir"
}

$manifestPath = Join-Path $ResultDir 'manifest.json'
$machinePath  = Join-Path $ResultDir 'machine.json'
if (-not (Test-Path $manifestPath)) {
    throw "manifest.json missing in $ResultDir"
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$machine  = if (Test-Path $machinePath) { Get-Content $machinePath -Raw | ConvertFrom-Json } else { $null }

function Percentile {
    param([double[]]$Values, [double]$P)
    if (-not $Values -or $Values.Count -eq 0) { return $null }
    $sorted = $Values | Sort-Object
    $idx = [math]::Min($sorted.Count - 1, [int][math]::Floor($P * ($sorted.Count - 1) / 100.0 + 0.5))
    return $sorted[$idx]
}

function Mean {
    param([double[]]$Values)
    if (-not $Values -or $Values.Count -eq 0) { return $null }
    return ($Values | Measure-Object -Average).Average
}

function Stdev {
    param([double[]]$Values)
    if (-not $Values -or $Values.Count -lt 2) { return 0.0 }
    $mean = Mean $Values
    $sumSq = 0.0
    foreach ($v in $Values) { $sumSq += ($v - $mean) * ($v - $mean) }
    return [math]::Sqrt($sumSq / ($Values.Count - 1))
}

function Fmt {
    param($Value, [int]$Decimals = 2)
    if ($null -eq $Value) { return '-' }
    return ('{0:N' + $Decimals + '}') -f [double]$Value
}

# Compute per-cell stats.
$cellStats = New-Object System.Collections.Generic.List[object]
foreach ($entry in $manifest) {
    $framePath  = Join-Path $ResultDir $entry.FrameCsv
    $samplePath = Join-Path $ResultDir $entry.SampleCsv

    $renderMs = @()
    $dtMs     = @()
    $frameCount = 0
    if (Test-Path $framePath) {
        # The frame CSV starts with a `# scene=...` comment line we need to skip.
        $framesRaw = Get-Content $framePath
        $framesCsv = $framesRaw | Where-Object { $_ -notmatch '^\s*#' }
        $framesObj = $framesCsv | ConvertFrom-Csv
        $renderMs  = @($framesObj | ForEach-Object { [double]$_.render_ms })
        $dtMs      = @($framesObj | ForEach-Object { [double]$_.dt_ms })
        $frameCount = $framesObj.Count
    }

    $cpuPct  = @()
    $wsMb    = @()
    $privMb  = @()
    if (Test-Path $samplePath) {
        $samplesObj = Import-Csv $samplePath
        $cpuPct = @($samplesObj | Where-Object { $_.cpu_pct_normalized -ne '' -and $_.cpu_pct_normalized -ne $null } |
                                  ForEach-Object { [double]$_.cpu_pct_normalized })
        $wsMb   = @($samplesObj | ForEach-Object { [double]$_.working_set_mb })
        $privMb = @($samplesObj | ForEach-Object { [double]$_.private_mb })
    }

    $effFps = if ($entry.WallSec -gt 0) { $frameCount / $entry.WallSec } else { 0 }

    $cellStats.Add([pscustomobject]@{
        Scene             = $entry.Scene
        SceneName         = $entry.SceneName
        Variant           = $entry.Variant
        Run               = $entry.Run
        FrameCount        = $frameCount
        WallSec           = [double]$entry.WallSec
        EffectiveFps      = $effFps
        FrameP50Ms        = Percentile -Values $renderMs -P 50
        FrameP95Ms        = Percentile -Values $renderMs -P 95
        FrameP99Ms        = Percentile -Values $renderMs -P 99
        DtP95Ms           = Percentile -Values $dtMs -P 95
        CpuPctMean        = Mean $cpuPct
        CpuPctP95         = Percentile -Values $cpuPct -P 95
        WorkingSetMbPeak  = if ($wsMb.Count) { ($wsMb | Measure-Object -Maximum).Maximum } else { $null }
        PrivateMbPeak     = if ($privMb.Count) { ($privMb | Measure-Object -Maximum).Maximum } else { $null }
        IoReadKb          = [math]::Round([double]$entry.IoReadBytes / 1KB, 1)
        IoWriteKb         = [math]::Round([double]$entry.IoWriteBytes / 1KB, 1)
        ExitCode          = $entry.ExitCode
    })
}

# Group per (Scene, Variant) and average.
$grouped = $cellStats | Group-Object Scene, Variant
$rows = foreach ($g in $grouped) {
    $first = $g.Group[0]
    $fp50 = @($g.Group | ForEach-Object { [double]$_.FrameP50Ms })
    $fp95 = @($g.Group | ForEach-Object { [double]$_.FrameP95Ms })
    $fp99 = @($g.Group | ForEach-Object { [double]$_.FrameP99Ms })
    $dtp95 = @($g.Group | ForEach-Object { [double]$_.DtP95Ms })
    $fps  = @($g.Group | ForEach-Object { [double]$_.EffectiveFps })
    $cpum = @($g.Group | Where-Object { $_.CpuPctMean -ne $null } | ForEach-Object { [double]$_.CpuPctMean })
    $cpup = @($g.Group | Where-Object { $_.CpuPctP95  -ne $null } | ForEach-Object { [double]$_.CpuPctP95 })
    $wspk = @($g.Group | Where-Object { $_.WorkingSetMbPeak -ne $null } | ForEach-Object { [double]$_.WorkingSetMbPeak })
    $prpk = @($g.Group | Where-Object { $_.PrivateMbPeak -ne $null } | ForEach-Object { [double]$_.PrivateMbPeak })
    $iorw = @($g.Group | ForEach-Object { [double]$_.IoReadKb })
    $ioww = @($g.Group | ForEach-Object { [double]$_.IoWriteKb })

    [pscustomobject]@{
        Scene            = $first.Scene
        SceneName        = $first.SceneName
        Variant          = $first.Variant
        Runs             = $g.Count
        EffectiveFpsMean = Mean $fps
        EffectiveFpsStd  = Stdev $fps
        FrameP50Ms       = Mean $fp50
        FrameP95Ms       = Mean $fp95
        FrameP99Ms       = Mean $fp99
        DtP95Ms          = Mean $dtp95
        CpuPctMean       = Mean $cpum
        CpuPctP95        = Mean $cpup
        WorkingSetMbPeak = Mean $wspk
        PrivateMbPeak    = Mean $prpk
        IoReadKbMean     = Mean $iorw
        IoWriteKbMean    = Mean $ioww
    }
}

$rows = $rows | Sort-Object Scene, Variant
$rows | Export-Csv -Path $OutCsv -NoTypeInformation -Encoding utf8

# Markdown report.
$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine('# DesktopGrass benchmark results')
[void]$md.AppendLine('')
if ($machine) {
    [void]$md.AppendLine('## Machine')
    [void]$md.AppendLine('')
    [void]$md.AppendLine("- Host: $($machine.HostName)")
    [void]$md.AppendLine("- OS: $($machine.OSVersion)")
    [void]$md.AppendLine("- Logical CPUs: $($machine.LogicalCpus)")
    [void]$md.AppendLine("- Per-run duration: $($machine.DurationSec)s, sample interval: $($machine.SampleIntervalSec)s")
    [void]$md.AppendLine("- Exe: ``$($machine.Exe)``")
    [void]$md.AppendLine("- Sweep start: $($machine.UtcStart)")
    [void]$md.AppendLine('')
}
[void]$md.AppendLine('## Per (scene, variant) averages across runs')
[void]$md.AppendLine('')
[void]$md.AppendLine('| Scene | Variant | Runs | FPS (mean) | render p50 ms | render p95 ms | render p99 ms | dt p95 ms | CPU% mean | CPU% p95 | WS peak MB | Private peak MB | IO read KB | IO write KB |')
[void]$md.AppendLine('|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|')
foreach ($r in $rows) {
    [void]$md.AppendLine(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} |' -f `
        $r.SceneName, $r.Variant, $r.Runs,
        (Fmt $r.EffectiveFpsMean 2),
        (Fmt $r.FrameP50Ms 3),
        (Fmt $r.FrameP95Ms 3),
        (Fmt $r.FrameP99Ms 3),
        (Fmt $r.DtP95Ms 2),
        (Fmt $r.CpuPctMean 2),
        (Fmt $r.CpuPctP95 2),
        (Fmt $r.WorkingSetMbPeak 1),
        (Fmt $r.PrivateMbPeak 1),
        (Fmt $r.IoReadKbMean 1),
        (Fmt $r.IoWriteKbMean 1)))
}
[void]$md.AppendLine('')
[void]$md.AppendLine('## Per-run detail')
[void]$md.AppendLine('')
[void]$md.AppendLine('| Cell | Scene | Variant | Run | Frames | Wall s | FPS | render p50 | render p95 | render p99 | dt p95 | CPU% mean | WS peak MB | Exit |')
[void]$md.AppendLine('|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|--:|')
foreach ($c in $cellStats) {
    [void]$md.AppendLine(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} |' -f `
        ('scene{0}-{1}-{2}-run{3}' -f $c.Scene, $c.SceneName.ToLowerInvariant(), $c.Variant, $c.Run),
        $c.SceneName, $c.Variant, $c.Run,
        $c.FrameCount,
        (Fmt $c.WallSec 2),
        (Fmt $c.EffectiveFps 2),
        (Fmt $c.FrameP50Ms 3),
        (Fmt $c.FrameP95Ms 3),
        (Fmt $c.FrameP99Ms 3),
        (Fmt $c.DtP95Ms 2),
        (Fmt $c.CpuPctMean 2),
        (Fmt $c.WorkingSetMbPeak 1),
        $c.ExitCode))
}
[void]$md.AppendLine('')
[void]$md.AppendLine('## Columns')
[void]$md.AppendLine('')
[void]$md.AppendLine('- **FPS**: frames written / wall-clock seconds. Target is 30; lower means the renderer ran behind pacing.')
[void]$md.AppendLine('- **render p50/p95/p99 ms**: time spent inside `Renderer::RenderFrame` per frame (QPC-bracketed in-process).')
[void]$md.AppendLine('- **dt p95 ms**: 95th percentile time between successive frames. Stalls / scheduler interference show up here, not in render_ms.')
[void]$md.AppendLine('- **CPU% mean / p95**: 100% = one full core. Renderer using one full core reads as 100, two cores as 200, etc. Sampled at 1Hz via `Process.TotalProcessorTime`.')
[void]$md.AppendLine('- **WS peak MB**: max Working Set seen during the run. Includes whatever D2D / DXGI / DComp keep resident.')
[void]$md.AppendLine('- **Private peak MB**: max private (committed) bytes. More stable than WS across runs.')
[void]$md.AppendLine('- **IO read/write KB**: cumulative process IO since launch. Persistence is disabled in benchmark mode, so steady-state writes should be near zero.')
[void]$md.AppendLine('')
[void]$md.AppendLine('GPU% is not captured here. See README.md for how to add PresentMon if needed.')

$md.ToString() | Out-File -FilePath $OutMarkdown -Encoding utf8

Write-Host "Wrote $OutMarkdown" -ForegroundColor Green
Write-Host "Wrote $OutCsv" -ForegroundColor Green
