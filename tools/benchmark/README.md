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

The production runtime controls for paused/fullscreen/occluded and power-aware
states are not exercised by benchmark mode. The production-path qualification
below applies this proposed suppressed-state gate:

- zero rendered frames; and
- each available CPU, GPU, context-switch, and incremental-energy metric no
  more than 10% of the matching visible scene on the same machine/power state.

An unavailable metric, insufficient sample coverage, battery resolution
failure, or power transition produces `not_evaluated`, never `pass`. These
thresholds are a PowerToys review proposal, not a portable claim about other
hardware.

## Current scope

The benchmark driver only produces `WorkloadState=visible`. It does not
fabricate paused, fullscreen, occluded, or throttled labels. Use the separate
production-runtime workflow below for those states.

## Production-runtime qualification (issue #14)

Everything above this section measures `DesktopGrass.Native.exe --benchmark`.
`main.cpp` dispatches benchmark mode instead of constructing the production
`App`, so that path bypasses `RuntimePolicy`, runtime notifications,
multi-monitor visibility decisions, and all-paused waits. Benchmark results
must not be relabeled as production suppression or 12/5 FPS evidence.

`Run-RuntimeQualification.ps1` launches the unmodified executable without
`--benchmark`. It selects scenes through the production message-only window,
uses controlled external windows for visibility probes, samples schema-v2
telemetry, and optionally starts PresentMon for the same PID.

### Safety and state guards

- Raw output must be under a caller-supplied `-ResultsRoot` outside the
  repository. The driver and aggregator reject repo-local output.
- Another `DesktopGrass.Native` process causes a refusal. The exact bytes of
  `state.json` and the autostart registry value are backed up and restored.
  The production config/state paths cannot be overridden. The harness never
  rewrites `config.json`; it requires `targetFps` to match `-RequiredTargetFps`
  (24 by default).
- The Windows session must remain active and unlocked. WTS connection/lock
  state is checked before launch, before every cell, on every telemetry sample,
  and at cell end. A lock or disconnect aborts the sweep and makes the cell
  unavailable instead of allowing lock-paused work to be labeled visible or
  occluded.
- Power source, Battery Saver, and active power scheme are checked at every
  sample. Display topology, modes, and brightness are captured for provenance
  and checked at cell boundaries.
- The script never changes AC/DC state, a power plan, Battery Saver, display
  power, lock state, suspend, or hibernation. Battery, Saver, display-dim,
  lock, display-off, and suspend transitions require explicit manual action.
- Probe windows are owned by the harness and destroyed in `finally`.
  Fullscreen qualification requires its full-virtual-screen probe to become
  the foreground window. Occlusion qualification uses a non-activating,
  topmost opaque probe with the exact bounds of every grass strip.
- No-app controls prove that `DesktopGrass.Native` is absent before, throughout,
  and after sampling. A process that appears at any point contaminates the
  control and aborts the sweep.

### Repeatable command sequence

Use one clean evidence root, identical timing/configuration arguments, the same
executable/config/display hashes, a fixed seed, and at least three runs. Capture
a no-app control before and after each power-state block so Energy Meter cells
can be bracketed within `-MaxControlGapMinutes` (60 by default).

```pwsh
$qualEvidence = Join-Path $env:USERPROFILE 'DesktopGrassQualification'
$common = @{
    ResultsRoot = $qualEvidence
    Runs = 3
    DurationSec = 60
    SampleIntervalSec = 1
    WarmupSec = 5
    ProbeSettleSec = 2
    PresentControlDurationSec = 5
    Seed = 14
}

# AC, Saver off: controls plus every scene in all safe automated states.
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario no-app-control -ExpectedPowerSource ac `
    -ExpectedBatterySaver off
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario visible -ExpectedPowerSource ac -ExpectedBatterySaver off
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario fullscreen-suppression -ExpectedPowerSource ac `
    -ExpectedBatterySaver off
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario occlusion-suppression -ExpectedPowerSource ac `
    -ExpectedBatterySaver off
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario no-app-control -ExpectedPowerSource ac `
    -ExpectedBatterySaver off

# After the user physically establishes battery power with Saver off:
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario no-app-control -ExpectedPowerSource battery `
    -ExpectedBatterySaver off
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario visible -ExpectedPowerSource battery `
    -ExpectedBatterySaver off
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario no-app-control -ExpectedPowerSource battery `
    -ExpectedBatterySaver off

# After the user enables Battery Saver:
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario no-app-control -ExpectedPowerSource battery `
    -ExpectedBatterySaver on
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario visible -ExpectedPowerSource battery `
    -ExpectedBatterySaver on
.\tools\benchmark\Run-RuntimeQualification.ps1 @common `
    -Scenario no-app-control -ExpectedPowerSource battery `
    -ExpectedBatterySaver on

.\tools\benchmark\Aggregate-RuntimeQualification.ps1 `
    -ResultsRoot $qualEvidence
```

Pass an existing console PresentMon binary to every process-bearing invocation
with `-PresentMonExe`. The harness invokes it with `--process_id`,
`--output_file`, `--date_time`, `--no_console_stats`, `--session_name`,
`--timed`, and `--terminate_after_timed`. It records the path, version,
arguments, output, stderr, interval, exit code, and target PID. It never
downloads a binary or requests elevation. Measurement captures include a
recorded one-second lead-in and tail so the absolute PresentMon interval encloses
the telemetry interval; only rows inside the telemetry interval are evaluated.

### Present and suppression proof

PresentMon CSV must contain `ProcessID`, `SwapChainAddress`, and absolute
`CPUStartDateTime` timestamps. Rows for any other PID invalidate the capture.
A header-only CSV is accepted as a measured zero only when capture metadata
proves the target PID, `--process_id`, `--date_time`, successful exit, requested
duration, and an absolute capture interval enclosing the measured interval. A
missing file, failed tool, or legacy relative `TimeInSeconds` timestamp is
unavailable.

Cadence is calculated independently for each swap chain. A visible interval
must contain the expected swap-chain count and at least two rows per chain.
Each suppression cell captures visible-before, suppressed, and visible-after
intervals from the same production PID. Suppression is proved only by active
visible controls on both sides plus zero target-PID present rows in the middle.
Window visibility alone is not present proof.

Without PresentMon, or an equivalent separately reviewed WPR/DXGI capture, all
present-suppression and cadence gates remain `not_evaluated`.

### Metrics, coverage, and provenance

Each measured sample records:

- one-core process CPU percentage and working/private memory;
- raw per-process GPU engines and the busiest engine;
- per-process thread context switches/sec as a scheduling proxy;
- system context switches, interrupts, DPC rate, and queue length;
- exact `SYS` Energy Meter power when exposed;
- AC/DC, Battery Saver, active scheme, battery capacity/rate;
- WTS session connection/lock state; and
- OS `ProcessPowerThrottling` execution-speed state, separately from the
  application's 12/5 FPS policy.

Coverage uses the theoretical sample count
`floor(DurationSec / SampleIntervalSec)`, not the number of rows produced.
CPU, working set, GPU, context-switch, power, and session evidence require at
least 90% coverage by default. A disappearing GPU instance is unavailable, not
zero. Sample indices must be unique where one row per interval is expected;
every accepted row must have an in-range index and UTC timestamp inside the
measurement interval. GPU, context-switch, battery, and Energy Meter values are
counted only when their own status is `available`.

The process-power-state gate proves that OS throttling was observed at adequate
coverage and remained stable for the cell. Both consistently `throttled` and
consistently `unthrottled` are valid actual states; a transition is
`not_evaluated`, and the state is reported separately from the app's cadence
policy.

System energy uses only a uniquely identified meter named exactly `SYS`; it
never sums component rails or `_Total`. Battery energy requires a positive
capacity delta over positive elapsed time and at least 90% capacity-sample
coverage. Incremental `SYS` energy subtracts the mean of matching no-app
controls before and after the cell, with the same power source, Saver state,
and scheme. Unresolved meter/battery resolution or missing bracketing controls
cannot pass.

Aggregation recursively reads captures under the evidence root and rejects
mixed qualification-set, machine/display, executable/config, timing, target
FPS, platform, production-entry-point, seed, run-count, or power-scheme
provenance. Manifests must be inside the evidence root, each referenced artifact
must remain inside its capture directory, and every output must remain inside
the evidence root. All compared AC, battery, and Saver cells must use one active
power scheme.

### Provisional budgets

- Visible CPU, busiest GPU engine, context-switch proxy, and energy envelope:
  `max(reference mean + 3 * stdev, reference mean * 1.20)`.
- Visible working-set envelope:
  `max(reference mean + 3 * stdev, reference mean * 1.10)`.
- Those envelopes are baseline definitions. The defining visible repetitions
  are not tested against an envelope calculated from themselves; an independent
  future evidence set can use the recorded values as regression limits.
- Fullscreen and opaque-occlusion suppression: zero presents, and CPU, GPU,
  context-switch, and resolvable incremental energy no more than 10% of the
  matching AC visible reference. A denominator below instrument resolution is
  `not_evaluated`.
- Battery cadence: active per-swap-chain rate no more than `12 * 1.10` FPS.
  Battery Saver cadence: no more than `5 * 1.10` FPS. Matching AC cadence must
  be at least 1.5 times the cap to distinguish throttling.
- Throttled CPU/GPU/context uses the measured cadence ratio against AC visible;
  working set retains the 110% visible envelope.

All thresholds are provisional same-machine qualification budgets, not
portable hardware claims.

### Outputs and closure

Every invocation creates a timestamped capture directory with `machine.json`,
`manifest.json`, per-cell sample/GPU/power/energy/throttling CSVs, and optional
PresentMon CSV/logs. Aggregation writes:

- `runtime-qualification-results.csv`;
- `runtime-qualification-budgets.csv`;
- `runtime-qualification-results.json`; and
- `runtime-qualification-results.md`.

The strict matrix requires at least three qualifying runs for all five scenes
in AC visible, AC fullscreen suppression, AC opaque occlusion, battery visible,
and Battery Saver visible, plus no-app controls in all three power states.
Overall status is `pass` only when every generated gate passes. Missing matrix
cells, metrics, presents, controls, or provenance produce `not_evaluated`;
threshold violations produce `fail`.

Display-dim and lock/display-off/suspend/resume evidence is manual-only and is
not generated by this automated driver. Do not lock or suspend the machine
during a normal qualification sweep. Use a separately approved, user-initiated
PresentMon or elevated WPR protocol that spans the transition and proves both
no presents while paused and resumed presents afterward.

Issue #14 can close only after the strict automated matrix and every supported
manual scenario are complete. Unsupported hardware/tooling must be reported
with the raw artifact path; it is never converted to a pass.
