# DesktopGrass

A small, "just for fun" Windows app that draws procedurally generated grass along the bottom edge of every monitor, on top of all windows (including the taskbar).

- Click-through — input passes through to whatever is underneath.
- Sways gently on its own.
- Cursor passing over the strip triggers gusts of wind that propagate outward.
- Clicking near grass cuts blades visually (the click still hits whatever's underneath).
- Spans all monitors, anchored to the bottom of each screen regardless of taskbar position.

## Three implementations
For comparison, the same v1 feature set is implemented three ways:

| Project | Stack | Notes |
| --- | --- | --- |
| [`src/DesktopGrass.Native`](src/DesktopGrass.Native) | Win32 + Direct2D (C++) | No XAML. DirectComposition for GPU-composited layered window. |
| [`src/DesktopGrass.Win2D`](src/DesktopGrass.Win2D) | C# + Win2D | No XAML. Managed mirror of the Native impl. |
| [`src/DesktopGrass.WinUI3`](src/DesktopGrass.WinUI3) | WinUI 3 (packaged) | Transparent click-through XAML window. |

See [`docs/architecture.md`](docs/architecture.md) for the shared algorithm spec each impl implements, and [`docs/comparison.md`](docs/comparison.md) for a side-by-side comparison.

## Tests

- **Unit tests** — pure-logic (blade generation, sway, gust, cut) per impl in [`tests/`](tests).
- **Smoke tests** — [`tests/smoke/Run-SmokeTests.ps1`](tests/smoke/Run-SmokeTests.ps1) launches each exe via `winapp ui`, asserts ExStyles, and verifies rendering via screenshot pixel variance.
- **Manual checklist** — [`docs/manual-smoke.md`](docs/manual-smoke.md) for release-time human checks.

## Future iterations (not v1)

- Occasional trees that grow over time.
- Regrowth of cut grass.
- Weather (rain, snow).
- Seasonal palettes.
- Settings UI.
- Auto-start on login.
