# DesktopGrass smoke tests

Screenshot-based smoke harness for the three DesktopGrass implementations
(`Native`, `Win2D`, `WinUI3`). Designed to be the post-build sanity check
that says "yes, this build actually painted something click-through on top
of the desktop".

## Running

From the repo root:

```powershell
pwsh tests\smoke\Run-SmokeTests.ps1 -Target All
pwsh tests\smoke\Run-SmokeTests.ps1 -Target Native -Configuration Debug
pwsh tests\smoke\Run-SmokeTests.ps1 -Target All -ContinueOnFailure
```

`-Target` accepts `Native`, `Win2D`, `WinUI3`, or `All`. The script exits
non-zero (`$LASTEXITCODE = 1`) if any target failed; without
`-ContinueOnFailure` it also `throw`s so CI noticed loudly.

Each per-target check performs, in order:

1. Launch the exe.
2. Poll for a top-level window with the expected class name owned by that
   process (via `FindWindowExW` + an `EnumWindows` cross-check).
3. Read `GWL_EXSTYLE` and assert
   `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`
   are all set. (Click-through gate.)
4. Sleep 1500 ms so DirectComposition / XAML Composition can produce a real
   first frame.
5. Screenshot the bottom 80 px strip of the primary monitor and count
   unique ARGB values sampled every 4th pixel. Fail if fewer than 50.
6. `PostMessage(WM_CLOSE)`; wait up to 2 s; force-kill if it hangs.

The process is cleaned up in a `finally` block — even if any assertion
throws, no orphan `DesktopGrass.*.exe` is left running.

## How `winapp ui` fits in

The harness is shaped to slot into GitHub's `winapp ui` CLI (same pattern
the [microsoft/calculator](https://github.com/microsoft/calculator) team
uses): the implementation's CI workflow calls `winapp run <build-dir>` to
launch the app under test, and a `winapp ui ...` verification step then
invokes `Run-SmokeTests.ps1` against the resulting build output.

For v1 the harness is also runnable directly against the built exe (no
`winapp ui` required) — that's the inner loop developers actually use.
`winapp ui` is the deployment vehicle for CI; the assertions are the same.

## Why no UIA assertions

All three implementations render their content via Direct2D /
DirectComposition (Native, Win2D) or XAML Composition (WinUI 3). **None
of that content is in the UIA tree in any meaningful way** — it's the
same blind spot WebView2 has with its DOM. A UIA-property assertion would
either find nothing or, worse, succeed against an empty placeholder
window that never actually painted.

So the harness deliberately uses *pixel variance from a screenshot* as
the source of truth for "did it draw?". `GetWindowLongPtr` is used for
the click-through ExStyle gate because that *is* a real Win32 property —
but the rendering check has to come from the framebuffer, not from UIA.

## Files

| File                    | Purpose                                              |
| ----------------------- | ---------------------------------------------------- |
| `Smoke.Common.psm1`     | P/Invoke helpers + assertions + `Invoke-AppSmoke`.   |
| `Run-SmokeTests.ps1`    | Entry point; resolves exe paths and runs per target. |
| `README.md`             | This file.                                           |

## Requirements

- PowerShell 7+ (`pwsh`). Windows PowerShell 5.1 is not supported.
- No admin elevation required.
- Runs on the interactive desktop session (it needs to take a real
  screenshot of the primary monitor).
