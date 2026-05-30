# DesktopGrass — v1 Implementation Plan

## Problem & goals
A small, "just for fun" Windows app that draws procedurally generated grass along the bottom edge of every monitor, on top of all windows (including the taskbar). The grass:

- Is fully click-through — input always passes through to whatever is underneath.
- Sways gently on its own.
- Reacts to the cursor: when the mouse passes over (or just above) the grass strip, gusts of wind propagate outward.
- Reacts to clicks: clicks "cut" the blades near the click point (visual only — the click still hits whatever's underneath).
- Spans all monitors, anchored to the bottom of each screen regardless of taskbar position.

The same v1 feature set is implemented **three times for side-by-side comparison**:

1. **DesktopGrass.Native** — Win32 + Direct2D in C++ (no XAML).
2. **DesktopGrass.Win2D** — C# + Win2D (no XAML), managed mirror of #1.
3. **DesktopGrass.WinUI3** — packaged WinUI 3 app with a transparent click-through XAML window.

Future iterations (out of scope for v1): occasional trees that grow, regrowth of cut grass, weather (rain/snow), seasonal palettes, settings UI, auto-start.

## Confirmed decisions
- **Stacks**: three implementations — Win32+D2D (C++), C# + Win2D, WinUI 3.
- **Coverage**: all monitors, one window per monitor, grass anchored to the bottom of each screen (independent of taskbar position).
- **Input model**: all windows are click-through (`WS_EX_TRANSPARENT`). Mouse moves and clicks are **observed** via a global low-level mouse hook (`WH_MOUSE_LL`) — never consumed.
- **v1 features**: procedural grass + gentle sway + cursor-driven gusts + cut-on-click. No trees, no regrowth.
- **GPU**: all three are GPU-accelerated. Native and Win2D composite via DirectComposition so the layered window stays on the GPU.

## Architecture

### Repository layout
```
C:\Users\crutkas\source\DesktopGrass\
├── DesktopGrass.sln
├── src\
│   ├── DesktopGrass.Native\          # Win32 + Direct2D (C++)
│   ├── DesktopGrass.Win2D\           # C# + Win2D, no XAML
│   └── DesktopGrass.WinUI3\          # WinUI 3 packaged app
├── tests\
│   ├── DesktopGrass.Native.Tests\    # Catch2, pure-logic
│   ├── DesktopGrass.Win2D.Tests\     # xUnit, pure-logic
│   ├── DesktopGrass.WinUI3.Tests\    # xUnit, pure-logic
│   └── smoke\                        # winapp ui PowerShell smoke tests
│       ├── Run-SmokeTests.ps1
│       └── Smoke.Common.psm1
├── docs\
│   ├── architecture.md               # shared algorithm spec
│   ├── comparison.md                 # LoC / CPU / GPU / startup / size
│   └── manual-smoke.md               # manual smoke checklist (for releases)
└── README.md
```

Each implementation **independently re-implements** the shared algorithms in its native language. No interop layer between the three — deliberate, so each binary is self-contained and the comparison is honest.

### Window model (all three)
Per monitor, one window with:
- `WS_POPUP` (no chrome).
- Extended styles: `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`.
- Per-pixel alpha via DirectComposition (Native, Win2D) or XAML/Composition (WinUI 3).
- Spans the full width of its monitor; height = grass strip + headroom for sway/gust (~150 px). Bottom-aligned to the monitor.
- Topmost so it draws over the taskbar.
- Per-Monitor V2 DPI awareness.
- Recreated on `WM_DISPLAYCHANGE` / display-arrival.

### Input observation
- One `SetWindowsHookEx(WH_MOUSE_LL, …)` per process.
- Callback feeds two streams:
  - **Move** events near the grass strip → cursor delta → gust impulses.
  - **WM_LBUTTONDOWN** events inside the grass band → cut blades within a small radius.
- Always returns `CallNextHookEx(…)` — never consumes input. Callback does minimal work and queues events to the render thread.

### Procedural grass model
Per monitor:
- N blades, spaced ~4–8 px apart along the bottom edge (~400 blades per 1920 px monitor at v1 defaults).
- Each blade derives from a per-blade seed: base x, height (8–40 px), hue (greens with variance), thickness, sway phase offset, stiffness.
- Each blade renders as a quadratic Bezier: base anchor → control point (offset by current lean) → tip.

### Sway + gust physics
- Each blade tracks `swayPhase` and `gustVelocity`.
- Per frame: `swayPhase += baseSwaySpeed * dt`; `gustVelocity` decays exponentially; effective lean = `sin(swayPhase) * baseAmp + gustVelocity`.
- Cursor-move impulses inject `gustVelocity` into blades within a radius of the cursor's x position. The impulse propagates outward with a slight delay (wave).

### Cut state
- Each blade has a `cutHeight` (1.0 = uncut, 0.0 = fully cut).
- On click within the grass band: blades within radius transition `cutHeight` → 0 over ~200 ms.
- Cut blades render only the stump.
- Per-session only — no persistence in v1.

### Tray / lifecycle
- NotifyIcon tray icon with **Quit**.
- No auto-start in v1.
- No settings UI in v1 — defaults are constants in code.

## Testing

Three layers, deliberately light for a "fun" app.

### 1. Unit tests — pure logic, per impl
Each impl gets its own test project against pure functions (no GPU, no window, no hook). The algorithms are deterministic given a seed, so this is straightforward.

- **`DesktopGrass.Native.Tests`** (C++, Catch2 via vcpkg)
- **`DesktopGrass.Win2D.Tests`** (C#, xUnit)
- **`DesktopGrass.WinUI3.Tests`** (C#, xUnit)

What each test project covers:
- **Blade generation**: given seed `S` and monitor width `W`, the generated blade vector is bit-identical across runs; heights in `[8, 40]`, x positions monotonically increasing, hue in expected palette.
- **Sway**: `swayPhase` advances linearly with `dt`; effective lean stays bounded.
- **Gust**: impulse of magnitude `m` at cursor x decays exponentially; blades outside the gust radius receive zero impulse.
- **Cut**: blades within cut radius transition `cutHeight` 1.0 → 0.0 over ~200 ms with the expected easing; blades outside are untouched; idempotent on repeat clicks.

### 2. Smoke tests — winapp ui, screenshot-based
A single PowerShell script in `tests\smoke\Run-SmokeTests.ps1` launches each exe in turn and runs the same checks. Modeled on the MarkdownPreview pattern: **no UIA-property fallbacks**, because Direct2D/Composition content isn't in the UIA tree (same blind spot as WebView2 DOM). Pixel variance is the source of truth.

Per impl:
1. Launch the exe.
2. Wait up to 5 s for the expected window class to appear on the primary monitor (`FindWindowExW` via P/Invoke).
3. Assert ExStyles contain `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE` (via `GetWindowLongPtr`).
4. Screenshot the bottom 80 px strip of the primary monitor with `System.Drawing`.
5. Count unique pixel colors in the strip. **Fail if < 50** (means nothing drew, or only one solid color).
6. Send `WM_CLOSE` / exit tray; assert process exits cleanly within 2 s.

Multi-monitor smoke is deferred — single-monitor variant is the gate. Click-through verification (click passes through to a probe window beneath the grass) is also deferred to v2; v1 trusts the ExStyle assertion.

### 3. Manual smoke checklist
`docs\manual-smoke.md` lists the checks a human runs before a release:
- Grass appears on every monitor.
- Grass survives a display hot-plug.
- Cursor moving across the strip visibly triggers a gust that propagates.
- Left-click cuts blades near the click point.
- Click still hits the underlying window (taskbar button, desktop icon).
- Tray Quit exits cleanly with no lingering process.
- DPI change (move to a 4K monitor) re-renders correctly.

## Defaults for v1 (constants in code)
| Knob | Default |
| --- | --- |
| Grass strip height | 80 px above bottom of screen |
| Blades per 1920 px | ~400 |
| Sway period | ~3 s gentle baseline |
| Gust radius (cursor) | 150 px |
| Cut radius (click) | 30 px |
| Frame rate | 60 fps (vsync) |
| Palette | 6 greens, seeded per blade |

## Todos
Tracked in the SQL `todos` table — IDs kebab-case, titles in gerund form.

The dependency graph is shaped for parallel fleet execution: after `repo-bootstrap` and `shared-spec`, the three impl tracks (Native / Win2D / WinUI 3) are independent. Each track's window → render → hook → tray → tests can also progress in parallel within the track where possible. Smoke tests gate on each impl being runnable; `comparison-doc` gates on all three renders.

## Risks & notes
- **WinUI 3 click-through topmost**: not first-class. Likely needs `AppWindow` + interop to set `WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_TOPMOST|WS_EX_NOACTIVATE` via `SetWindowLongPtr` after window creation, plus `Microsoft.UI.Composition` for transparency. Expect more friction here than the other two.
- **Layered + GPU**: `UpdateLayeredWindow` is CPU-side. To keep GPU compositing for the Native and Win2D paths, use DirectComposition with a swap chain attached via `IDCompositionVisual` — the window stays layered/transparent at the desktop compositor level.
- **Mouse hook**: global LL hook must return quickly. Do minimal work in the callback; queue events to the render thread.
- **High DPI**: blade dimensions in DIPs, scaled per monitor; handle DPI changes.
- **Device lost**: handle D3D/D2D/Win2D device-lost on display hot-plug or GPU reset.
- **Taskbar position**: grass is anchored to the screen bottom, not the taskbar, so a side-docked or top-docked taskbar doesn't change anything.
- **Smoke-test blind spot**: Direct2D / Composition content is not in the UIA tree. Smoke tests must verify rendering via screenshot pixel-variance, not UIA properties — UIA "existence" fallbacks would be false positives (same trap as WebView2 DOM).

## Future iterations (parked)
- Occasional procedural trees that grow over time.
- Weather: rain droplets, snow accumulation on grass.
- Seasonal palettes (spring/summer/autumn/winter).
- Settings UI (density, palette, sway speed).
- Auto-start on login.
- Persistence of cut state across sessions.
