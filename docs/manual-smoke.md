# Manual smoke checklist

## Purpose

Use this checklist for the release-time checks that the automated smoke harness cannot prove: real multi-monitor behavior, actual click-through UX, cursor-driven gusts, cut-on-click visuals, DPI/display changes, tray behavior, and resource hygiene. The developer shipping a release build runs it before tagging a release.

## Setup

Run commands from the repository root.

- **Native — Win32 + Direct2D (C++)**
  - Build from scratch:
    ```powershell
    msbuild src\DesktopGrass.Native\DesktopGrass.Native.vcxproj /p:Configuration=Release /p:Platform=x64
    ```
  - Release binary: `src\DesktopGrass.Native\out\x64\Release\DesktopGrass.Native.exe` (ARM64 builds land under `out\ARM64\Release\`)
- **Win2D — C# + Direct2D/Vortice track**
  - Build from scratch:
    ```powershell
    dotnet build src\DesktopGrass.Win2D -c Release -p:Platform=x64
    ```
  - Release binary: `src\DesktopGrass.Win2D\bin\x64\Release\net10.0-windows10.0.19041.0\DesktopGrass.Win2D.exe` (ARM64 builds land under `bin\ARM64\Release\...`)

The expected output paths above match the project files and the current smoke harness resolution rules.

## Checklist — Native

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

- [ ] Hot-plug a monitor (unplug or disable in display settings); grass disappears from that monitor without crashing the app.
- [ ] Plug it back in; grass reappears on it.
- [ ] Change a monitor's scale factor (Settings → System → Display → Scale); grass remains visible (or returns within one second) and the blade dimensions look correct (no doubled-thickness, no half-height).
- [ ] Move a monitor's resolution (e.g., 1920x1080 → 2560x1440); grass extends to the new width without artifacts.

### Tray / lifecycle

- [ ] Tray icon appears in the system tray and tooltip shows the impl name.
- [ ] Right-click tray icon → Quit closes the app within 2 seconds with no lingering process (`Get-Process DesktopGrass*` returns nothing).
- [ ] If the app crashes, no tray icon is left behind on tray-refresh.

### Resource hygiene

Run the app for 10 minutes idle.

- [ ] Memory usage stable (no leak visible in Task Manager > Details > Working Set).
- [ ] CPU usage idle when no cursor motion (~0–1%).
- [ ] GPU usage low (single-digit %) on a modern dGPU.

## Checklist — Win2D

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

- [ ] Hot-plug a monitor (unplug or disable in display settings); grass disappears from that monitor without crashing the app.
- [ ] Plug it back in; grass reappears on it.
- [ ] Change a monitor's scale factor (Settings → System → Display → Scale); grass remains visible (or returns within one second) and the blade dimensions look correct (no doubled-thickness, no half-height).
- [ ] Move a monitor's resolution (e.g., 1920x1080 → 2560x1440); grass extends to the new width without artifacts.

### Tray / lifecycle

- [ ] Tray icon appears in the system tray and tooltip shows the impl name.
- [ ] Right-click tray icon → Quit closes the app within 2 seconds with no lingering process (`Get-Process DesktopGrass*` returns nothing).
- [ ] If the app crashes, no tray icon is left behind on tray-refresh.

### Resource hygiene

Run the app for 10 minutes idle.

- [ ] Memory usage stable (no leak visible in Task Manager > Details > Working Set).
- [ ] CPU usage idle when no cursor motion (~0–1%).
- [ ] GPU usage low (single-digit %) on a modern dGPU.

## Known limitations

- Auto-start is opt-in via tray → Start with Windows and writes the per-implementation HKCU Run value.
- There is no settings UI. User-tunable animation values live in `config.json` and apply after restart.
- The automated smoke harness checks the primary monitor only; multi-monitor, DPI, display-change, and long-run resource behavior remain manual release gates.
- The custom-rendered scene is decorative and has no meaningful UI Automation tree. User controls are exposed through the tray menu.

## Reporting bugs

File an issue at <https://github.com/crutkas/DesktopGrass/issues/new> with: implementation name, Windows version, monitor configuration, and clear repro steps.
