# DesktopGrass Native benchmark and energy evidence

This Windows-only workflow measures the Native renderer across all scenes and
records enough machine and power context to make same-machine comparisons
repeatable. It deliberately separates:

- automated, machine-readable metrics available without elevation;
- optional metrics that depend on the display driver, battery provider, or
  hardware energy meters; and
- manual Windows Performance Toolkit or `powercfg` evidence.

An unavailable metric is recorded as unavailable with its source and reason. It
is never emitted as a synthetic zero and can never pass a budget.

## Requirements

- Windows 10 or Windows 11.
- PowerShell 7+ (`pwsh`).
- A Release build of `DesktopGrass.Native.exe`, or the Visual Studio C++ tools
  needed for the script to build it.
- No administrator elevation for the automated harness.

The harness does not install PresentMon, drivers, providers, packages, or
tracing tools.

## Quick start

Store raw evidence outside the repository:

```pwsh
$evidence = Join-Path $env:USERPROFILE 'DesktopGrassEvidence'

# All scenes, three visible runs each, 60 seconds per run.
.\tools\benchmark\Run-Benchmark.ps1 -ResultsRoot $evidence

# Aggregate the latest sweep.
$latest = Get-ChildItem $evidence -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
.\tools\benchmark\Aggregate-Results.ps1 -ResultDir $latest.FullName
```

A short all-scene smoke sweep:

```pwsh
.\tools\benchmark\Run-Benchmark.ps1 `
    -ResultsRoot $evidence `
    -Scenes 0,1,2,3,4 `
    -Runs 1 `
    -DurationSec 5 `
    -SkipBuild
```

The default platform follows the OS architecture (`ARM64` on ARM64, otherwise
`x64`). Override it with `-Platform x64` or `-Platform ARM64`.

## Automated data sources

| Metric | Automated source | Scope | Permission and availability |
|---|---|---|---|
| Frame time and FPS | In-process `QueryPerformanceCounter` around `Renderer::RenderFrame` | Benchmark process | Always available when the benchmark runs |
| CPU | `Process.TotalProcessorTime` delta | Benchmark process | Unelevated |
| Working set/private bytes | `System.Diagnostics.Process` | Benchmark process | Unelevated |
| IO | `GetProcessIoCounters` | Benchmark process | Unelevated; failures are null, not zero |
| GPU utilization and engines | PDH `\GPU Engine(*)\Utilization Percentage`, filtered by PID | Benchmark process and individual GPU engines | Unelevated; requires a WDDM/driver counter provider |
| Scheduling/timer proxy | PDH `\Thread(*)\Context Switches/sec` joined to `\Thread(*)\ID Process` | Sum of benchmark-process threads | Unelevated; this is not a literal wakeup count |
| System scheduling context | PDH system context switches, interrupts, DPC rate, and processor queue length | Entire system | Unelevated; diagnostic context only |
| Hardware power | PDH `\Energy Meter(*)\Power` | Hardware-specific system rails/meters | Unelevated when the platform exposes it; not process-attributed |
| AC/DC and battery saver | `GetSystemPowerStatus` | Entire system | Unelevated |
| Active power scheme | `PowerGetActiveScheme` | Current user/system | Unelevated |
| Battery capacity/rate | `root\wmi` battery provider | Entire system battery | Optional; hardware/driver dependent |

The PDH helper uses `PdhAddEnglishCounterW`, so counter paths do not depend on
the Windows display language.

### GPU aggregation

Each valid GPU counter instance is retained with its PID, adapter LUID, physical
adapter, engine index, and engine type. The summary GPU value is the **busiest
engine** (maximum engine utilization for the process at that sample), matching
Windows Task Manager's documented approach. Engines are not summed because
separate logical engines can share physical cores and a sum can double-count
capacity.

Per-process GPU attribution therefore does **not** inherently require
PresentMon or elevation. It does require a compatible WDDM driver. PresentMon
remains useful as an optional ETW-based cross-check.

### Context switches are a proxy, not wakeups

Windows does not expose a stable, universally available unelevated
per-process "timer wakeups per second" counter. The harness sums context-switch
rates for threads owned by the benchmark PID. That captures scheduling activity
caused by timer waits, rendering, IO, preemption, and other reasons. Reports
name it `process_context_switches_per_sec`; they never relabel it as an exact
wakeup count.

### Power and energy scope

`Energy Meter` values are system/hardware-rail power in milliwatts. A `SYS`
meter is summarized when present, while every exposed meter is retained in
`energy-results.csv`. Meter names and coverage are platform specific; `SYS`,
`GPU_COMPUTE`, or `_Total` do not have portable meanings across machines.

Battery capacity and charge/discharge rates are system-wide. A positive
capacity delta is only derived while the run stays on DC with a stable saver
state and power scheme. Charging, AC operation, a power transition, too few
samples, or capacity resolution too coarse to show a positive delta produces
`n/a` with a reason. A zero-resolution battery interval is never treated as
zero energy.

None of these values is direct process energy. Attribute impact by comparing
the same machine and power context against an interleaved idle control.

## Availability and strict runs

Each source is preflighted and recorded as `available`, `unsupported`, `error`,
or `partial`. Per-sample states also distinguish priming, no process instance,
and a measured value.

By default, optional-source failures warn through metadata while the remaining
metrics are collected. A lab run can fail before launch if required evidence is
missing:

```pwsh
.\tools\benchmark\Run-Benchmark.ps1 `
    -ResultsRoot $evidence `
    -RequireGpuTelemetry `
    -RequireWakeTelemetry `
    -RequireEnergyMeter
```

Battery runs can additionally use `-RequireBatteryTelemetry`.

## Verified power scenarios

The script derives the power-state key from Windows; it does not trust a
free-form label. Expected-state switches prevent accidental mislabeled runs:

```pwsh
# AC, battery saver off.
.\tools\benchmark\Run-Benchmark.ps1 `
    -ResultsRoot $evidence `
    -ExpectedPowerSource ac `
    -ExpectedBatterySaver off

# Battery, saver off.
.\tools\benchmark\Run-Benchmark.ps1 `
    -ResultsRoot $evidence `
    -ExpectedPowerSource battery `
    -ExpectedBatterySaver off `
    -RequireBatteryTelemetry

# Battery saver.
.\tools\benchmark\Run-Benchmark.ps1 `
    -ResultsRoot $evidence `
    -ExpectedPowerSource battery `
    -ExpectedBatterySaver on `
    -RequireBatteryTelemetry
```

The source, saver state, and active scheme are recorded at cell start, every
sample, and cell end. A transition marks the cell invalid for direct power
comparison.

## Output schema

Every invocation creates
`<ResultsRoot>\YYYY-MM-DDThh-mm-ssZ\`.

### Sweep-level files

| File | Contents |
|---|---|
| `machine.json` | Schema version, OS/build/architecture, CPU, GPU/driver/display, elevation, executable hash, tool presence, initial power/battery context, counter capabilities, and sweep parameters |
| `manifest.json` | One entry per cell, including scene/variant/state/power dimensions, exact render and sampled intervals, file references, source statuses, power validity, errors, and exit state |
| `results.csv` | Per-scene/state/power/variant aggregates plus metric status/reason and `BudgetStatus` |
| `results.md` | Human-readable context, summaries, provenance, limitations, and provisional budget |
| `engine-results.csv` | Per-engine-type GPU aggregates |
| `energy-results.csv` | Per-hardware-meter power aggregates |

`machine.json` and each manifest entry use schema version 2. The original PR
#13 top-level machine and manifest fields remain present, and the aggregator
continues to read schema version 1 directories.

### Per-cell files

| File | Contents |
|---|---|
| `<cell>.frames.csv` | In-process frame index, render-relative time, `dt_ms`, and `render_ms` |
| `<cell>.samples.csv` | Process CPU/memory/IO, busiest GPU engine, context-switch proxy, and system scheduling context |
| `<cell>.gpu.csv` | Raw per-PID GPU-engine samples |
| `<cell>.power.csv` | AC/DC, battery saver, scheme, battery percentage/capacity/rate |
| `<cell>.energy.csv` | Raw hardware Energy Meter power samples |
| `<cell>.log.txt` | Native benchmark summary |

CSV nulls are empty fields. Header-only optional files mean the source produced
no valid rows; consult the manifest status for why.

### Timing boundaries

- Native frame timestamps start after window/renderer setup and stop before
  teardown.
- The first external counter interval is retained only as `priming` because it
  spans process startup and the gap from the previous cell.
- `sample_measured=True` rows define the external measurement interval.
- The last sample is taken while the process is live; teardown is excluded.
- `WallSec` is diagnostic process wall time and is not labeled render duration.

## Runtime contract of `--benchmark`

Benchmark mode bypasses the production lifecycle:

- no tray icon, `MouseHook`, or persistence read/write;
- one `GrassWindow` on the primary monitor's bottom strip;
- the same visible DComp/D2D path as production unless `-HideWindow` is used;
- deterministic scene/critter/seed overrides; and
- clean exit after the requested render duration.

Supported Native flags (`--key=value` or `--key value`):

| Flag | Default | Meaning |
|---|---|---|
| `--benchmark` | - | Required mode switch |
| `--scene=N` | 0 | 0=Grass, 1=Desert, 2=Winter, 3=Autumn, 4=Ocean |
| `--critter=N` | 0 | 0=None, 1=Sheep, 2=Cat, 3=Bunny |
| `--critter-count=N` | 0 | 0=random count; positive capped at 6 |
| `--seed=0xHEX` | Built-in seed | Stable content override |
| `--duration=SEC` | 60 | In-process render interval |
| `--width=PX` | Primary work-area width | Render strip width |
| `--height=PX` | Strip default | Logged requested height |
| `--fps=N` | 24 | Frame pacing target |
| `--out=PATH` | None | Per-frame CSV path |
| `--hidden` | Off | Hide instead of showing the benchmark window |

## Repeatable evidence protocol

Use the same physical machine for a comparison. Record separate AC, battery,
and battery-saver sweeps.

1. Close the production DesktopGrass process and unrelated foreground
   workloads.
2. Fix display brightness, refresh rate, resolution, monitor topology, power
   scheme, and thermal/fan environment.
3. Keep the benchmark visible and unobstructed for the visible baseline. Do not
   move the pointer over the strip.
4. Warm the machine with a non-recorded sweep.
5. Use every scene, a fixed seed, and at least three recorded runs. Vary scene
   order between repeated sweeps to reduce thermal/order bias and retain the
   order in the manifest.
6. For battery attribution, unplug AC, wait for charge/discharge state to
   stabilize, choose a duration long enough for the battery provider's capacity
   resolution, and interleave an equivalent idle-control interval.
7. Do not combine AC, battery, or saver transitions in one result group.
8. Preserve the raw directory with the report. Do not compare only copied
   summary rows.

Battery percentage alone is too coarse for short energy claims. If capacity or
hardware power is unavailable, keep the result as unavailable and use the
manual lab workflow instead.

## Manual Windows evidence

These tools complement the harness but are not automated dependencies.

### WPR/WPA

For a deeper timer/context-switch/GPU/power trace, use an elevated terminal:

```pwsh
wpr -start GeneralProfile -filemode
# Run one controlled benchmark scenario.
wpr -stop DesktopGrass.etl 'DesktopGrass controlled benchmark'
```

Open the ETL in WPA and inspect CPU Usage (Precise), context switches/waits,
DPC/ISR, GPU Utilization, and Power tables that the hardware exposes. Align the
trace with manifest UTC timestamps. Record the WPR profile, Windows/ADK
version, elevation, dropped events, and exported WPA table configuration.

WPR/WPA data is system-wide and can perturb a low-cost workload. Use it as a
separate confirmation run, not silently mixed with the light PDH baseline.

### `powercfg`

Useful machine-readable or diagnostic exports include:

```pwsh
powercfg /batteryreport /output battery-report.xml /xml
powercfg /srumutil /output srum.csv /csv
```

Battery Report describes battery hardware/history; SRUM is historical Energy
Estimation data with coarse intervals and access/elevation constraints. Neither
is a precise short-cell process energy meter.

`powercfg /energy /xml` is elevated system diagnostics and Microsoft recommends
running it while the machine is idle. Use it to find configuration/timer
problems, not concurrently as proof of DesktopGrass render energy.

SleepStudy and System Power Report describe Modern Standby and power
transitions on supported devices. They are not active visible-render
benchmarks.

## Provisional same-machine budget

The report intentionally emits `BudgetStatus=not_evaluated` until an approved
reference exists and every required metric is available.

Proposed visible regression limits, calculated independently per scene and
power state:

- CPU core %, busiest GPU %, context-switch proxy, and system power:
  `max(reference mean + 3 * stdev, reference mean * 1.20)`.
- Working-set peak:
  `max(reference mean + 3 * stdev, reference mean * 1.10)`.

After issues #22 and #23 implement genuine paused/fullscreen/occluded and
power-aware states, the proposed suppressed-state gate is:

- zero rendered frames; and
- each available CPU, GPU, context-switch, and incremental-energy metric no
  more than 10% of the matching visible scene on the same machine/power state.

An unavailable metric, insufficient sample coverage, battery resolution
failure, or power transition produces `not_evaluated`, never `pass`. These
thresholds are a PowerToys review proposal, not a portable claim about other
hardware.

## Current scope

The current driver only produces `WorkloadState=visible`. The schema and
aggregator already group arbitrary future states and correctly handle an empty
frame CSV, but this branch does not fabricate paused, fullscreen, occluded, or
throttled behavior. Final visible-versus-suppressed evidence remains open until
#22/#23 land.
