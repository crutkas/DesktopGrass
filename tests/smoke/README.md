# DesktopGrass smoke tests

Screenshot-based smoke harness for the supported Native implementation and the
optional managed (`Win2D`) comparison/reference. Native smoke results are a
product gate. The managed target is retained to reproduce the historical
comparison; it does not imply release support, active platform hardening, or
future feature parity.

## Running

`Run-SmokeTests.ps1` launches existing build outputs; it does not build them.
The CI/lab entry point builds first. Expected executable locations are:

| Target | Support role | Build output |
| --- | --- | --- |
| `Native` | Required supported-product gate | `src\DesktopGrass.Native\out\<Platform>\<Configuration>\DesktopGrass.Native.exe` |
| `Win2D` | Optional managed-reference comparison | `src\DesktopGrass.Win2D\bin\<Platform>\<Configuration>\<TFM>\DesktopGrass.Win2D.exe` |

From the repository root:

```powershell
pwsh tests\smoke\Run-SmokeTests.ps1 -Target Native
pwsh tests\smoke\Run-SmokeTests.ps1 -Target Native -Configuration Debug

# Optional managed-reference comparison
pwsh tests\smoke\Run-SmokeTests.ps1 -Target Win2D -TimeoutSeconds 30
pwsh tests\smoke\Run-SmokeTests.ps1 -Target All -ContinueOnFailure
pwsh tests\smoke\Run-NativeRuntimeControlTests.ps1 -Configuration Release

# CI/lab entry point (build + strict interactive preflight + artifacts)
pwsh tests\smoke\Invoke-InteractiveCiSmoke.ps1 -Target Native
```

`-Target` accepts `Native`, `Win2D`, or `All`. The script exits
non-zero (`$LASTEXITCODE = 1`) if any target failed; without
`-ContinueOnFailure` it also `throw`s so CI noticed loudly.

Each per-target check performs, in order:

1. Launch the exe.
2. Poll for a top-level window with the expected class name owned by that
   process (via `FindWindowExW` + an `EnumWindows` cross-check).
3. Read `GWL_EXSTYLE` and assert
   `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`
   are all set. (Click-through gate.)
4. For Native, assert exactly one grass HWND per active logical monitor and
   verify each HWND spans that monitor's physical work-area width, ends at the
   work-area bottom, and has the expected per-monitor DPI-scaled height.
5. Poll physical-pixel screenshots of a grass HWND's bottom 80 px until the
   timeout, counting unique ARGB values sampled every 4th pixel. Fail if fewer
   than 50 colors appear.
6. `PostMessage(WM_CLOSE)`; wait up to 2 s; force-kill if it hangs.

The process is cleaned up in a `finally` block — even if any assertion
throws, no orphan `DesktopGrass.*.exe` is left running.

The reported `DurationMs` spans the complete smoke operation: optional
pre-launch setup, process launch, window discovery, assertions, screenshot
polling, and shutdown/cleanup. It is **not** startup duration or
launch-to-first-frame timing.

### Native runtime-control smoke

`Run-NativeRuntimeControlTests.ps1` requires an unlocked interactive desktop.
It creates controlled opaque Win32 probe windows and verifies that:

1. A foreground fullscreen probe hides only the grass surface on its monitor.
2. Removing the probe restores the same HWND without creating another surface.
3. A topmost opaque window covering the strip triggers reliable full-occlusion
   suppression and restores the same HWND when removed.
4. Suppressing every monitor enters the all-paused state and resumes the same
   HWND set.
5. `WM_CLOSE` exits with code zero within two seconds while every grass surface
   is suppressed.

The script does not synthesize power, lock, or suspend notifications. Validate
those paths on real hardware by observing the Native app while switching
AC/battery and Battery Saver, dimming/turning off the display, locking and
unlocking the session, and suspending/resuming. On multi-monitor hardware,
repeat with a fullscreen app on one monitor and confirm the other surface keeps
animating. Run the same checks on ARM64 hardware for an end-to-end architecture
pass.

## How `winapp ui` fits in

The [`Interactive smoke`](../../.github/workflows/interactive-smoke.yml)
workflow invokes `Invoke-InteractiveCiSmoke.ps1` on a dedicated self-hosted
Windows runner. The script fails closed unless it is running in an active,
unlocked, non-session-0 desktop and can capture that desktop. Native is the
required gate. The frozen managed reference runs as an advisory comparison and
retains its result without blocking Native work.

The direct `Run-SmokeTests.ps1` command remains the local inner loop. See the
[runner contract](../../docs/interactive-smoke-runner.md) for required labels,
tools, security boundaries, artifacts, cleanup, and the external provisioning
action.

## Why no UIA assertions

The supported Native binary and managed reference both render their content via
Direct2D / DirectComposition. **None of that content is in the UIA tree in any
meaningful way** — it's the same blind spot WebView2 has with its DOM.
A UIA-property assertion would either find nothing or, worse, succeed
against an empty placeholder window that never actually painted.

So the harness deliberately uses *pixel variance from a screenshot* as
the source of truth for "did it draw?". `GetWindowLongPtr` is used for
the click-through ExStyle gate because that *is* a real Win32 property —
but the rendering check has to come from the framebuffer, not from UIA.

## Files

| File                    | Purpose                                              |
| ----------------------- | ---------------------------------------------------- |
| `Smoke.Common.psm1`     | P/Invoke helpers + assertions + `Invoke-AppSmoke`.   |
| `Run-SmokeTests.ps1`    | Entry point; resolves exe paths and runs per target. |
| `Invoke-InteractiveCiSmoke.ps1` | CI build, session preflight, state isolation, logging, and cleanup. |
| `Run-NativeRuntimeControlTests.ps1` | Native fullscreen, occlusion, resource-reuse, and suppressed-shutdown checks. |
| `README.md`             | This file.                                           |

## Requirements

- PowerShell 7+ (`pwsh`). Windows PowerShell 5.1 is not supported.
- No admin elevation required.
- Runs on the interactive desktop session (it needs to take a real
  screenshot of the primary monitor).
- Building through `Invoke-InteractiveCiSmoke.ps1` additionally requires MSVC
  toolset `v145` plus a Windows SDK for Native and a .NET SDK accepted by
  `global.json` for the optional managed reference.
- The CI entry point additionally requires the tools and runner setup in
  [`docs/interactive-smoke-runner.md`](../../docs/interactive-smoke-runner.md).
