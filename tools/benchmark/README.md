# DesktopGrass benchmark harness

A small Windows-only harness for measuring `DesktopGrass.Native.exe` cost
across scenes (and, in future, optimization variants).

## What it measures

- **Per-frame CPU+GPU end-to-end time** — captured in-process by bracketing
  `Renderer::RenderFrame` with `QueryPerformanceCounter`. Written one row per
  frame to a CSV. Yields p50 / p95 / p99 of `render_ms` plus `dt_ms` (time
  between frames, which surfaces pacing/scheduler stalls that don't show up in
  render time itself).
- **Per-process CPU%, working set, private bytes, IO** — sampled at 1 Hz by
  polling `Get-Process -Id <pid>` from the driver script. CPU% is normalised
  to logical-core count, so `100%` = one full core.
- **Effective FPS** — `frame_count / wall_seconds`. Useful for spotting cells
  where the renderer is falling behind the 30 fps pacing target.

Things explicitly **not** measured today:

- **GPU%** and per-engine utilisation. Per-process GPU attribution on Windows
  requires PresentMon (ETW) with elevation. End-to-end frame cost is already
  captured in `render_ms`; if a future PR needs per-engine breakdowns we'll
  layer PresentMon in beside the in-process sampler.

## Quick start

```pwsh
# Build + run a full sweep (5 scenes x 3 runs x 60 s = ~15 min).
.\tools\benchmark\Run-Benchmark.ps1

# Aggregate the most recent sweep into results.md / results.csv.
$latest = (Get-ChildItem .\tools\benchmark\results | Sort-Object Name -Descending | Select-Object -First 1).FullName
.\tools\benchmark\Aggregate-Results.ps1 -ResultDir $latest
```

A shorter smoke run for a single scene:

```pwsh
.\tools\benchmark\Run-Benchmark.ps1 -Scenes 0 -Runs 1 -DurationSec 10 -SkipBuild
```

`-SkipBuild` reuses the existing
`src\DesktopGrass.Native\out\x64\Release\DesktopGrass.Native.exe`. Without it,
the harness rebuilds Release|x64 with the local VS 2026 vcvars (override with
`-VcvarsBat <path>` or just pre-build and use `-SkipBuild`).

## Runtime contract of `--benchmark` mode

`DesktopGrass.Native.exe --benchmark` bypasses the production lifecycle:

- No tray icon, no `MouseHook`, no persistence read/write.
- No multi-monitor enumeration. One `GrassWindow` is created on the primary
  monitor's bottom strip (same DComp/D2D path users see).
- Scene and critter override the persisted settings via CLI flags.
- Window exits cleanly after `--duration=<sec>` seconds.

Supported flags (`--key=value` or `--key value`):

| Flag | Default | Meaning |
|---|---|---|
| `--benchmark` | — | Mode switch. Required. |
| `--scene=N` | 0 | 0=Grass 1=Desert 2=Winter 3=Autumn 4=Ocean (names also accepted). |
| `--critter=N` | 0 | 0=None 1=Sheep 2=Cat 3=Bunny. Names also accepted. |
| `--critter-count=N` | 0 | 0 = random count; positive caps at 6. |
| `--seed=0xHEX` | built-in app seed | Override per-monitor seed for content stability across binaries. |
| `--duration=SEC` | 60 | Run length. |
| `--width=PX` | primary work-area width | Render strip width. |
| `--height=PX` | STRIP_HEIGHT+HEADROOM | Logged in the CSV header; actual HWND height is fixed by spec. |
| `--fps=N` | 30 | Pacing target. |
| `--out=PATH` | none | Per-frame CSV path. Omit to skip the dump. |
| `--hidden` | off | `SW_HIDE` instead of `SW_SHOWNOACTIVATE`. Off by default so the production code path is exercised. |

Frame CSV header:

```
# scene=2 critter=0 critter_count=0 seed=0xD3C7C0F30070D511 duration_s=60 ...
frame_index,t_seconds,dt_ms,render_ms
```

## What the driver writes

`Run-Benchmark.ps1` writes a timestamped directory under
`tools\benchmark\results\YYYY-MM-DDThh-mm-ssZ\` containing, per cell:

- `<cellTag>.frames.csv` — per-frame timings (above).
- `<cellTag>.samples.csv` — 1Hz process samples.
- `<cellTag>.log.txt` — stdout from the exe (the one-line `[benchmark]` summary).

Plus `manifest.json` (one entry per cell) and `machine.json` (host/CPU info,
sweep parameters). The aggregator reads both to produce `results.md` and
`results.csv`.

## Caveats

- The benchmark window is **visible and topmost** by default because that's
  the user-facing code path. Don't move the mouse over the strip during
  measurement — `MouseHook` is disabled inside benchmark mode so cuts are
  impossible, but cursor presence in the strip still affects the renderer's
  cursor-position read.
- Make sure the production `DesktopGrass.Native.exe` from the tray is not
  running before starting the sweep — two layered topmost strips on the same
  monitor will fight for compositor order and skew the numbers.
- Numbers are not portable across machines. Always compare results from the
  same `machine.json`.
