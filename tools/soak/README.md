# Native soak and fault harness

`Run-NativeSoak.ps1` drives the normal `DesktopGrass.Native` production path
(never `--benchmark`) for issue #29. It reuses the benchmark sampler, runtime
qualification state guards, smoke HWND/topology probes, and the Native
device-recovery tests.

## Safety and prerequisites

- Run from an unlocked interactive Windows desktop with PowerShell 7.
- Build the Native app and Native tests in `Release` for the selected platform.
- Close every existing `DesktopGrass.Native` process.
- Put `-ResultsRoot` outside the repository. The harness refuses repo-local raw
  evidence and creates a ZIP artifact beside each result directory.
- The harness backs up `state.json`, verifies autostart launch safety, restores
  state byte-for-byte, and restores a changed autostart value in `finally`.
- Sleep and physical monitor transitions never run implicitly. They require
  `-AllowSystemTransitions`; sleep additionally requires
  `-EnableSleepResume`; monitor churn requires both a churn and restore script.
- Monitor scripts run in separate PowerShell processes and must exit non-zero on
  failure. The restore script runs after every churn and again during emergency
  cleanup if a churn fails. Validate those scripts on the lab machine first.
- The wakeable suspend path requests sleep (not hibernate), sets a wake timer,
  and fails if the session does not return active and unlocked. Confirm firmware,
  Windows wake-timer policy, power, and remote-access behavior before unattended
  use. Do not run on a machine where sleep could interrupt critical work.

The repository cannot provide a universal physical-monitor command: dock,
DisplayPort, USB display, VM, and lab switch hardware require different control
tools. The two scripts are the explicit, auditable hardware adapter. A churn
script must change the active topology; its paired restore script must restore
the exact baseline topology. The harness rejects a no-op churn and fingerprints
screen geometry, adapter modes, active EDID identities, and monitor PnP paths
before requiring the baseline fingerprint after every restore.

## Qualification run

A passing qualification is fixed at a minimum of four hours and requires scene
changes, at least one process lifecycle cycle, deterministic device-loss
coverage, sleep/resume, monitor churn, healthy windows, and all resource
budgets. Example:

```powershell
$evidence = Join-Path $env:USERPROFILE 'DesktopGrassSoakEvidence'

.\tools\soak\Run-NativeSoak.ps1 `
  -ResultsRoot $evidence `
  -DurationSec 14400 `
  -AllowSystemTransitions `
  -EnableSleepResume `
  -MonitorChurnScript C:\Lab\DesktopGrass-MonitorChurn.ps1 `
  -MonitorRestoreScript C:\Lab\DesktopGrass-MonitorRestore.ps1
```

Default limits are 64 MB working-set growth, 64 MB private-byte growth, 100
handles, 8 threads, 20 USER objects, 20 GDI objects, and 15% mean CPU of one
logical core. Override them with the corresponding `-Max*` parameters. Growth
is the worst of within-process-generation growth and baseline drift between
restarts, so lifecycle cycling cannot hide a rising launch baseline.
Qualification also requires at least 80% of the scheduled telemetry samples.
Transition adapters and device-recovery runs are terminated and failed when
they exceed `-OperationTimeoutSec`.

Device loss uses the existing `DeviceRecoveryTests` and
`RendererRecoveryIntegrationTests` via a VSTest filter. A skipped real renderer
integration is a failure, even when device-independent policy tests pass. The
real integration emits a per-run success marker only after exercising and
releasing its real graphics resource graph.
Deliberately causing a system TDR is not automated.

## Diagnostic run

Use diagnostic mode to validate scripts and artifact production without
claiming endurance evidence:

```powershell
.\tools\soak\Run-NativeSoak.ps1 `
  -ResultsRoot $evidence `
  -DurationSec 120 `
  -SampleIntervalSec 2 `
  -SceneIntervalSec 20 `
  -LifecycleIntervalSec 60 `
  -SkipDeviceLossTests `
  -DiagnosticRun
```

`summary.json` always labels a diagnostic or sub-four-hour result
`not_qualified` and states that it is harness validation only. Fast fixture
tests under `tests\soak` validate evaluation logic; they are not soak evidence.

## Evidence and failure detection

Each run writes:

| File | Contents |
|---|---|
| `manifest.json` | Exact executable hash, machine/display context, parameters, thresholds, and deterministic operation schedule |
| `samples.csv` | CPU, memory, handles, threads, USER/GDI objects, GPU/context-switch telemetry, HWND counts, topology, and hang probes |
| `events.ndjson` | Timestamped scene, lifecycle, device-loss, sleep, monitor, cleanup, and failure events |
| `device-loss\*.trx` and `*.log` | Filtered deterministic device-recovery results |
| `failures.log` | Crash, non-zero exit, hang, stale/missing HWND, transition, threshold, and cleanup failures |
| `windows-application-events.json` | Matching Windows Application log entries, or an explicit unavailable reason |
| `summary.json` | Coverage, resource checks, unmet criteria, and final `pass`, `fail`, or `not_qualified` status |
| `<run>.zip` | Retained artifact containing the full raw run directory |

Hang probes use `SendMessageTimeout`; stale windows fail the existing
one-surface-per-active-monitor topology assertion. Windows does not expose a
supported API to enumerate another process's low-level hooks. The harness
therefore treats hook lifetime as process-owned: every lifecycle operation must
exit the original PID, leave no DesktopGrass process or HWND, and launch a new
PID. Windows removes process-owned hooks on exit; a hung/stale owner fails the
run. USER objects, handles, and threads provide additional leak signals while
the process is alive.

Copy the ZIP to the issue, release evidence store, or CI artifact retention
system unchanged. A passing short run, fixtures, or benchmark sweep must never
be described as multi-hour soak evidence.
