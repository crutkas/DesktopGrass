# Manual smoke checklist

## Purpose

Use this checklist for the release-time checks that the automated smoke harness
cannot prove for the supported Native build: real multi-monitor behavior,
actual click-through UX, cursor-driven gusts, cut-on-click visuals, DPI/display
changes, tray behavior, and resource hygiene. Run it before tagging a release.

## Setup

Run commands from the repository root.

- Build Native from scratch:
  ```powershell
  msbuild src\DesktopGrass.Native\DesktopGrass.Native.vcxproj /p:Configuration=Release /p:Platform=x64
  ```
- Release binary: `src\DesktopGrass.Native\out\x64\Release\DesktopGrass.Native.exe`
  (ARM64 builds land under `out\ARM64\Release\`).

The managed C#/Vortice project is a source-available comparison/reference, not a
release target. Its restore/build/test commands are retained as a commented CI
recipe for manual reproduction, not run as required CI coverage. Its optional
`Win2D` smoke target can help reproduce the historical comparison, but
managed-only platform behavior is not a Native release or PowerToys readiness
gate.

## Checklist — supported Native release

Spec refs for visual behavior: `docs/architecture.md` §2 for bottom alignment and DIPs, §8 for the gust band/radius, §9 for the cut band/radius/duration/session state, and the constants table for v1 values.

### Launch & basic rendering

- [ ] Grass appears along the bottom of the primary monitor within 2 seconds of launch.
- [ ] Grass appears on every connected monitor (test with at least 2 monitors).
- [ ] Grass is bottom-aligned to each monitor's work area (immediately above a bottom-docked taskbar; test bottom, top, and side taskbar positions).
- [ ] No window chrome, no title bar, no taskbar icon (`WS_EX_TOOLWINDOW`).

### Click-through behavior

- [ ] Move the mouse over the grass — cursor shape is whatever's underneath, NOT a wait/loading/arrow imposed by our window.
- [ ] Place an app window behind the strip and click a control through the grass — the underlying control activates.
- [ ] Click a desktop icon through the grass — the icon receives focus / opens on double-click.
- [ ] Drag-select on the desktop starting from a point covered by grass — the rubber band selection works as if the grass weren't there.

### Accessibility

The overlay is decorative. It must not become an input or accessibility target;
all user actions remain in the stock Windows notification-area menu.

#### Overlay focus, input, and UI Automation

1. Launch the Native app, then capture its PID:
   ```powershell
   $app = Get-Process DesktopGrass.Native
   ```
2. Put keyboard focus in another app. Move and click through every grass
   surface, then confirm the other app still owns focus.
3. Inspect the Native process:
   ```powershell
   winapp ui list-windows -a $app.Id --json
   winapp ui inspect -a $app.Id --interactive --json
   ```
4. Verify every `DesktopGrass.Native.Window` reports `isForeground: false` and
   `elementCount: 0`. The expected result is no actionable or misleading UIA
   descendants. The smoke harness separately requires `WS_EX_TRANSPARENT`,
   `WS_EX_NOACTIVATE`, and `WS_EX_TOOLWINDOW`; the window procedure also returns
   `HTTRANSPARENT`, `MA_NOACTIVATE`, and no provider for `WM_GETOBJECT`.

#### Tray keyboard access, names, and discoverability

1. Press `Win+B`.
2. If focus lands on **Show Hidden Icons**, press `Enter`.
3. Use the arrow keys to reach **Desktop Grass controls**. Narrator should
   announce that exact name and identify it as a button.
4. Press `Enter`. The menu must open next to the icon without moving focus to a
   grass surface.
5. Use Up/Down to visit **Scene**, **Critter**, **Start with Windows**, and
   **Quit DesktopGrass**. Use Right/Left to enter and leave both submenus.
   Narrator should announce each item, its checked state where applicable, and
   each submenu.
6. Press `Esc`. The menu closes and focus returns to **Desktop Grass controls**
   in the notification area, not to a grass surface.
7. Repeat by selecting and by right-clicking the tray icon; all three routes
   must open the same menu.

The implementation uses `NOTIFYICON_VERSION_4` and handles pointer selection,
context activation, `NIN_SELECT`, and `NIN_KEYSELECT`. Keyboard activation is
anchored to the notification icon rather than the current mouse position.

#### Contrast themes

The tray controls are a stock Windows `HMENU`, with no owner drawing or custom
colors. Validate the platform-rendered states rather than the decorative scene:

1. Open **Settings > Accessibility > Contrast themes** and apply each supported
   contrast theme available on the test machine.
2. Repeat the tray keyboard procedure above.
3. Verify menu text, separators, focus highlight, check marks, and submenu
   arrows remain visible; no item may rely on scene colors.
4. Restore the original theme.

The grass itself conveys no status or action. Its palette is not a substitute
for the accessible tray names and menu states.

#### Reduced motion

DesktopGrass follows **Settings > Accessibility > Visual effects > Animation
effects** (`SPI_GETCLIENTAREAANIMATION`). The explicit behavior is:

- **Off:** all decorative grass surfaces hide, rendering and mouse observation
  stop, and **Desktop Grass controls** remains available in the tray.
- **On:** the surfaces reappear and rendering resumes without restarting the
  app.

Toggle **Animation effects** off and on while the app is running and verify
those transitions occur promptly. The runtime-policy and settings-notification
paths have deterministic unit coverage; changing this global user preference is
intentionally a manual host check.

#### Accessibility validation record (2026-07-17)

Observed on Windows 11 Enterprise build 28000, ARM64, with the Native ARM64
Release build:

- A mixed-DPI topology produced two 3840-wide surfaces at 96 DPI (100% scale,
  110 physical pixels high) and one 3270-wide surface at 144 DPI (150% scale,
  165 physical pixels high). The Native smoke passed with 1934 unique rendered
  colors.
- `winapp ui inspect --interactive` reported zero interactive descendants for
  each decorative surface, and `list-windows` reported no surface as
  foreground.
- `Win+B` reached **Show Hidden Icons**; opening it focused **Desktop Grass
  controls**. Pressing `Enter` opened the Native menu, and UIA reported the four
  top-level names listed above. After `Esc`, focus returned to **Desktop Grass
  controls** and no grass surface was foreground. Invoking the icon's primary
  UIA action also opened the same menu.
- The host had a standard theme and Animation effects enabled during the
  observation. Contrast-theme visuals and live global Animation-effects
  transitions were not changed on this shared interactive host; use the exact
  procedures above on a release-test session.

### Cursor-driven gusts

- [ ] Slowly move the cursor across the strip; blades near the cursor visibly tilt away briefly.
- [ ] Rapidly fling the cursor across the strip; the gust wave is more dramatic.
- [ ] Move the cursor above the strip but still inside the gust band (`docs/architecture.md` §8: 110 DIP above ground); blades still react.
- [ ] Move the cursor far above the strip, outside the gust band; blades show only their baseline sway with no gust response.

### Cut-on-click

- [ ] Left-click in the grass strip; blades within the cut radius visibly drop to a stump within ~200 ms (`docs/architecture.md` §9: 30 DIP radius, 0.2 sec duration).
- [ ] Cut blades stay cut for the session.
- [ ] Repeat-clicking already-cut blades is a no-op (no visual glitch, no stutter).
- [ ] Clicking causes only a visual cut — the underlying app or desktop surface still receives the click.

### Display & DPI

- [ ] Run `pwsh tests\smoke\Run-SmokeTests.ps1 -Target Native`; it reports one correctly bounded, DPI-scaled grass HWND per active logical monitor.
- [ ] At 100%, 150%, and 200% scale, repeat the tray keyboard procedure; the
  stock menu text, check marks, focus indicator, and submenu arrows remain
  readable and unclipped.
- [ ] Hot-plug or disable one monitor; only its grass HWND disappears, no stale/duplicate/input-blocking HWND remains, and the other monitors keep animating without a reset.
- [ ] Reconnect that monitor; its grass returns with the same layout and valid cut state.
- [ ] Reorder monitors, including negative virtual-screen coordinates; grass follows each physical display and state does not move to another monitor.
- [ ] Change the primary monitor; every surface reaches its new physical-pixel position without a duplicate at the old origin.
- [ ] On a mixed-DPI setup, change one monitor's scale factor; only that surface is replaced, its DIP dimensions remain correct, and unaffected monitors keep their live scene state.
- [ ] Rotate one monitor landscape → portrait → landscape; its surface spans the new work-area width without DirectComposition artifacts.
- [ ] Change one monitor's resolution (for example 1920×1080 → 2560×1440); its surface extends to the new width and no old-size HWND remains.
- [ ] Dock the taskbar on the bottom, top, left, and right edges. For every edge, grass spans `rcWork`, ends at `rcWork.bottom`, and never covers the taskbar.
- [ ] Change taskbar thickness and auto-hide state; the surface moves or resizes within one second without resetting unaffected monitors.
- [ ] Restart Explorer; the tray icon returns and exactly one grass surface remains on every monitor.
- [ ] Perform several scale, resolution, taskbar, and hot-plug changes rapidly; the final topology converges within one second with no crash or orphan HWND.

Before each state-preservation transition, cut a visually recognizable patch and
select a non-default scene/critter. Afterward, verify those settings and the
monitor's cut pattern remain associated with the same physical display.

### Display hardware matrix

Record the exact GPU, connection/dock, Windows build, and result for every row
that is available. Mark unavailable rows explicitly rather than treating an x64
build or simulated unit fixture as physical coverage.

| Case | Minimum physical coverage |
| --- | --- |
| Architecture | Native x64 host and native ARM64 host |
| Monitor count | One monitor, two monitors, and three or more monitors |
| Scale | 100%, 150%, 200%, and at least one mixed-DPI pair |
| Position/primary | Left/right/above layouts, negative coordinates, reorder, and primary switch |
| Mode | Landscape, portrait, and a live resolution change |
| Taskbar | Bottom, top, left, right, thickness change, auto-hide, and Explorer restart |
| Connection | Direct cable, dock/DisplayLink if supported, disable/enable, unplug/reconnect |
| Virtual session | Remote Desktop and any supported virtual-display driver |
| Stress | Rapid chained changes and a 10-minute post-change idle run |

### Tray / lifecycle

- [ ] Tray icon appears in the system tray and its accessible name is **Desktop Grass controls**.
- [ ] `Win+B` keyboard activation and selecting or right-clicking the icon all open the same menu.
- [ ] Right-click tray icon → Quit closes the app within 2 seconds with no lingering process (`Get-Process DesktopGrass*` returns nothing).
- [ ] If the app crashes, no tray icon is left behind on tray-refresh.

### Resource hygiene

Run the app for 10 minutes idle.

- [ ] Memory usage stable (no leak visible in Task Manager > Details > Working Set).
- [ ] CPU usage idle when no cursor motion (~0–1%).
- [ ] GPU usage low (single-digit %) on a modern dGPU.

## Known limitations

- Auto-start is opt-in via tray → Start with Windows and writes the Native HKCU Run value.
- There is no settings UI. User-tunable animation values live in `config.json` and apply after restart.
- The Native automated smoke checks the current HWND count and work-area/DPI
  geometry and click-through styles on every active logical monitor, plus live
  rendering on one surface.
  Live topology transitions, state continuity, ARM64 hardware, remote/virtual
  displays, and long-run resource behavior remain hardware release gates.
- The custom-rendered scene is decorative and explicitly exposes no interactive
  UI Automation descendants. User controls are exposed through the stock
  Windows tray menu.

## Reporting bugs

File an issue at <https://github.com/crutkas/DesktopGrass/issues/new> with the
Native build/commit, Windows version, monitor configuration, and clear repro
steps.
