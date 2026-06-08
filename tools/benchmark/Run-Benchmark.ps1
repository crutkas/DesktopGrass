# tools/benchmark/Run-Benchmark.ps1
# Drive the DesktopGrass.Native --benchmark sweep across scenes (and, in future,
# optimization variants). For each (scene, variant, run) cell the script:
#
#   1. Starts DesktopGrass.Native.exe in --benchmark mode with a per-cell CSV
#      output path. The exe runs for $DurationSec and writes one row per frame.
#   2. Concurrently polls Get-Process -Id <pid> at 1Hz to capture CPU time,
#      working set, private bytes, IO bytes, threads, handles.
#   3. Waits for the exe to exit, stops the sampler, and writes the per-cell
#      sample CSV next to the frame CSV.
#
# After the sweep the script emits a manifest.json describing every cell so the
# companion Aggregate-Results.ps1 script can produce a human-readable summary.
#
# GPU% is not measured directly here. Per-process GPU attribution on Windows
# requires PresentMon (ETW) which needs an admin elevation and a separate
# install. Frame time (render_ms, dt_ms) from the in-process CSV already shows
# GPU+CPU end-to-end frame cost; PresentMon can be layered in later.

[CmdletBinding()]
param(
    # Where to drop all benchmark artifacts. A timestamped subdirectory is
    # created here on every invocation.
    [string]$ResultsRoot = (Join-Path $PSScriptRoot 'results'),

    # Scenes to run. 0=Grass 1=Desert 2=Winter 3=Autumn 4=Ocean. Default = all.
    [int[]]$Scenes = @(0,1,2,3,4),

    # Variant tags to attach to each run. Today only 'baseline' is implemented;
    # this list exists so a future PR adding A/B opts can extend the sweep.
    [string[]]$Variants = @('baseline'),

    # Number of runs per (scene, variant) cell.
    [int]$Runs = 3,

    # Per-run duration in seconds.
    [int]$DurationSec = 60,

    # Optional: override scene seed (default = use binary's built-in seed).
    [uint64]$Seed = 0,

    # Counter sample interval in seconds. 1Hz keeps the sampler quiet enough
    # not to perturb the measurement.
    [double]$SampleIntervalSec = 1.0,

    # Skip msbuild and use the existing exe if it's already built.
    [switch]$SkipBuild,

    # Path to the exe. Defaults to the standard Release|x64 output location.
    [string]$Exe = (Join-Path $PSScriptRoot '..\..\src\DesktopGrass.Native\out\x64\Release\DesktopGrass.Native.exe'),

    # Path to the vcxproj to build when -SkipBuild is not set.
    [string]$Vcxproj = (Join-Path $PSScriptRoot '..\..\src\DesktopGrass.Native\DesktopGrass.Native.vcxproj'),

    # Path to vcvars64.bat. The default mirrors crutkas's local VS 2026 install;
    # CI builds the binary separately and passes -SkipBuild + -Exe.
    [string]$VcvarsBat = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\VC\Auxiliary\Build\vcvars64.bat',

    # Hide the visible window during measurement. Defaults off because the
    # production code path always presents a visible swap chain — hiding it
    # would change what's measured.
    [switch]$HideWindow
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Per-process IO counters aren't on System.Diagnostics.Process directly.
# Add a tiny P/Invoke shim for GetProcessIoCounters once at script start.
if (-not ('DesktopGrass.Bench.Io' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

namespace DesktopGrass.Bench {
    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }
    public static class Io {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetProcessIoCounters(IntPtr hProcess, out IoCounters lpIoCounters);
    }
}
"@
}

function Get-ProcIoCounters {
    param([System.Diagnostics.Process]$Proc)
    $c = New-Object DesktopGrass.Bench.IoCounters
    try {
        $handle = $Proc.Handle
    } catch {
        return $null
    }
    if (-not [DesktopGrass.Bench.Io]::GetProcessIoCounters($handle, [ref]$c)) {
        return $null
    }
    return $c
}

$sceneNames   = @('Grass','Desert','Winter','Autumn','Ocean')

if (-not $SkipBuild) {
    Write-Host '== Building DesktopGrass.Native (Release|x64) ==' -ForegroundColor Cyan
    if (-not (Test-Path $VcvarsBat)) {
        throw "vcvars64.bat not found at $VcvarsBat — pass -VcvarsBat or -SkipBuild."
    }
    $proj = (Resolve-Path $Vcxproj).Path
    $cmd  = "call `"$VcvarsBat`" >nul && msbuild `"$proj`" /p:Configuration=Release /p:Platform=x64 /m /nologo /v:m"
    & $env:ComSpec /c $cmd
    if ($LASTEXITCODE -ne 0) {
        throw "msbuild failed with exit $LASTEXITCODE"
    }
}

if (-not (Test-Path $Exe)) {
    throw "Benchmark exe not found at $Exe. Build it first or pass -Exe."
}
$Exe = (Resolve-Path $Exe).Path

# Per-invocation result directory: results/2026-06-07T22-30-00Z/
$stamp     = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH-mm-ssZ')
$resultDir = Join-Path $ResultsRoot $stamp
New-Item -ItemType Directory -Force -Path $resultDir | Out-Null
Write-Host "== Results dir: $resultDir ==" -ForegroundColor Cyan

$machine = [ordered]@{
    HostName          = $env:COMPUTERNAME
    OSVersion         = [System.Environment]::OSVersion.VersionString
    LogicalCpus       = [System.Environment]::ProcessorCount
    UtcStart          = (Get-Date).ToUniversalTime().ToString('o')
    Exe               = $Exe
    DurationSec       = $DurationSec
    SampleIntervalSec = $SampleIntervalSec
    Seed              = $Seed
}
$machine | ConvertTo-Json -Depth 4 |
    Out-File -FilePath (Join-Path $resultDir 'machine.json') -Encoding utf8

$manifest = New-Object System.Collections.Generic.List[object]
$cellNumber = 0
$totalCells = $Scenes.Count * $Variants.Count * $Runs

foreach ($scene in $Scenes) {
    if ($scene -lt 0 -or $scene -ge $sceneNames.Count) {
        throw "Scene index $scene out of range (0..$($sceneNames.Count-1))"
    }
    $sceneName = $sceneNames[$scene]
    foreach ($variant in $Variants) {
        for ($run = 1; $run -le $Runs; ++$run) {
            $cellNumber++
            $cellTag   = "scene{0}-{1}-{2}-run{3}" -f $scene, $sceneName.ToLowerInvariant(), $variant, $run
            $frameCsv  = Join-Path $resultDir ($cellTag + '.frames.csv')
            $sampleCsv = Join-Path $resultDir ($cellTag + '.samples.csv')
            $logFile   = Join-Path $resultDir ($cellTag + '.log.txt')

            Write-Host ("[{0}/{1}] {2}" -f $cellNumber, $totalCells, $cellTag) -ForegroundColor Yellow

            $exeArgs = @(
                '--benchmark',
                "--scene=$scene",
                "--duration=$DurationSec",
                "--out=$frameCsv"
            )
            if ($Seed -ne 0) {
                $exeArgs += ('--seed=0x' + $Seed.ToString('X'))
            }
            if ($HideWindow.IsPresent) {
                $exeArgs += '--hidden'
            }

            $startTime = Get-Date
            $proc = Start-Process -FilePath $Exe -ArgumentList $exeArgs -PassThru `
                                   -RedirectStandardOutput $logFile

            $samples = New-Object System.Collections.Generic.List[object]
            $deadline = $startTime.AddSeconds([math]::Max(15, $DurationSec * 2))
            $prevCpu = [TimeSpan]::Zero
            $prevTime = $null
            $firstSample = $true
            while (-not $proc.HasExited -and ((Get-Date) -lt $deadline)) {
                Start-Sleep -Milliseconds ([int]($SampleIntervalSec * 1000))
                try {
                    $p = Get-Process -Id $proc.Id -ErrorAction Stop
                } catch {
                    break
                }
                $now = Get-Date
                $cpu = $p.TotalProcessorTime
                $cpuPct = $null
                if (-not $firstSample -and $prevTime) {
                    $dtSec = ($now - $prevTime).TotalSeconds
                    if ($dtSec -gt 0) {
                        $cpuSec = ($cpu - $prevCpu).TotalSeconds
                        # 100% = one full core. Not divided by NumLogicalProcessors:
                        # a renderer using one full core reads as 100, two cores as 200, etc.
                        $cpuPct = ($cpuSec / $dtSec) * 100.0
                    }
                }
                $io = Get-ProcIoCounters -Proc $p
                $samples.Add([pscustomobject]@{
                    t_sec              = ($now - $startTime).TotalSeconds
                    cpu_pct_normalized = $cpuPct
                    working_set_mb     = [math]::Round($p.WorkingSet64 / 1MB, 3)
                    private_mb         = [math]::Round($p.PrivateMemorySize64 / 1MB, 3)
                    threads            = $p.Threads.Count
                    handles            = $p.HandleCount
                    io_read_bytes      = if ($io) { [long]$io.ReadTransferCount } else { 0 }
                    io_write_bytes     = if ($io) { [long]$io.WriteTransferCount } else { 0 }
                    io_other_bytes     = if ($io) { [long]$io.OtherTransferCount } else { 0 }
                })
                $prevCpu = $cpu
                $prevTime = $now
                $firstSample = $false
            }
            if (-not $proc.HasExited) {
                Write-Warning "Benchmark exceeded deadline; waiting for natural exit (pid=$($proc.Id))."
            }
            $proc.WaitForExit() | Out-Null
            $endTime = Get-Date

            # Pull final IO totals from the LAST live sample (process is gone
            # after WaitForExit, so Get-Process won't see it).
            $ioRead = 0L; $ioWrite = 0L; $ioOther = 0L
            if ($samples.Count -gt 0) {
                $last = $samples[$samples.Count - 1]
                $ioRead  = [long]$last.io_read_bytes
                $ioWrite = [long]$last.io_write_bytes
                $ioOther = [long]$last.io_other_bytes
            }

            $samples | Export-Csv -Path $sampleCsv -NoTypeInformation -Encoding utf8

            $manifest.Add([pscustomobject]@{
                CellTag      = $cellTag
                Scene        = $scene
                SceneName    = $sceneName
                Variant      = $variant
                Run          = $run
                DurationSec  = $DurationSec
                FrameCsv     = (Split-Path $frameCsv -Leaf)
                SampleCsv    = (Split-Path $sampleCsv -Leaf)
                LogFile      = (Split-Path $logFile -Leaf)
                ExitCode     = $proc.ExitCode
                WallSec      = ($endTime - $startTime).TotalSeconds
                StartUtc     = $startTime.ToUniversalTime().ToString('o')
                EndUtc       = $endTime.ToUniversalTime().ToString('o')
                IoReadBytes  = $ioRead
                IoWriteBytes = $ioWrite
                IoOtherBytes = $ioOther
            })
        }
    }
}

$manifest | ConvertTo-Json -Depth 4 |
    Out-File -FilePath (Join-Path $resultDir 'manifest.json') -Encoding utf8

Write-Host ''
Write-Host "== Sweep complete: $cellNumber cells ==" -ForegroundColor Green
Write-Host ("Run aggregate: tools\benchmark\Aggregate-Results.ps1 -ResultDir `"$resultDir`"") -ForegroundColor Cyan
