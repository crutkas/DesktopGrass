# DesktopGrass

A small, "just for fun" Windows app that draws procedurally generated grass along the bottom edge of every monitor, on top of all windows (including the taskbar).

- Click-through — input passes through to whatever is underneath.
- Sways gently on its own with a 6-second period.
- Cursor passing over the strip triggers gusts of wind that propagate outward.
- Clicking near grass cuts blades (the click itself still hits whatever's underneath).
- Cut blades regrow after 30–90 seconds, animating back over 2–4 seconds.
- Blades bend chord-preservingly — they pivot from the root like a hinged stick, so the tip arcs over and drops as they lean (no stretching artifacts).
- Spans all monitors, anchored to the bottom of each screen's work area regardless of taskbar position.

## Four implementations

The same v1 feature set is implemented four ways, all sharing the same `Sim.cs` / `Constants.cs` numerical core so blade geometry stays bit-identical across renderers.

| Project | Stack | Renderer |
| --- | --- | --- |
| [`src/DesktopGrass.Native`](src/DesktopGrass.Native) | C++ / Win32 | Direct2D + DirectComposition |
| [`src/DesktopGrass.Win2D`](src/DesktopGrass.Win2D) | C# / .NET 10 | Vortice.Direct2D1 + DirectComposition (the "Win2D" name is historical — it uses Vortice, not `Microsoft.Graphics.Win2D`) |
| [`src/DesktopGrass.WinUI3`](src/DesktopGrass.WinUI3) | C# / .NET 10 | Windows App SDK + WinUI 3 + `Microsoft.Graphics.Win2D` canvas (unpackaged, self-contained) |
| [`src/DesktopGrass.WPF`](src/DesktopGrass.WPF) | C# / .NET 10 | Vanilla WPF `DrawingContext` — no Direct2D, no WinUI |

See [`docs/architecture.md`](docs/architecture.md) for the shared algorithm spec each impl implements, and [`docs/comparison.md`](docs/comparison.md) for a side-by-side comparison.

## Run it

Pick an implementation and launch its release exe:

```powershell
# Native — C++/Direct2D
& "src\DesktopGrass.Native\out\Release\DesktopGrass.Native.exe"

# Win2D — C#/Vortice
dotnet build src\DesktopGrass.Win2D -c Release
& "src\DesktopGrass.Win2D\bin\Release\net10.0-windows10.0.19041.0\DesktopGrass.Win2D.exe"

# WinUI3 — C#/WindowsAppSDK
dotnet build src\DesktopGrass.WinUI3 -c Release
& "src\DesktopGrass.WinUI3\bin\Release\net10.0-windows10.0.19041.0\win-x64\DesktopGrass.WinUI3.exe"

# WPF — C#/.NET 10 WPF
dotnet build src\DesktopGrass.WPF -c Release
& "src\DesktopGrass.WPF\bin\Release\net10.0-windows10.0.19041.0\DesktopGrass.WPF.exe"
```

Right-click the tray icon to quit.

The Native exe is built via MSBuild against `src\DesktopGrass.Native\DesktopGrass.Native.vcxproj` (Release / x64). See [`docs/manual-smoke.md`](docs/manual-smoke.md) for the full build-from-scratch checklist.

## Tests

- **Unit tests** — pure-logic suites (PRNG determinism, blade generation, sway, gust, cut, regrowth, stroke geometry) for each impl in [`tests/`](tests). The four impls share a Sim/Constants core so they all assert against the same numerical contract.
- **Smoke tests** — [`tests/smoke/Run-SmokeTests.ps1`](tests/smoke/Run-SmokeTests.ps1) launches each exe, asserts the click-through/topmost extended window styles, and verifies actual rendering via screenshot pixel-variance over the bottom strip.

Run everything:

```powershell
# Unit tests (all C# impls)
dotnet test

# Cross-impl smoke (4 targets)
pwsh tests\smoke\Run-SmokeTests.ps1 -Target All
```

## Conformance

All four implementations use:

- The same xorshift64 PRNG seeded via SplitMix64
- The same canonical test seed (`0x6B6173746F`)
- The same blade-generation draw order
- The same sway / gust / cut / regrowth / chord-preserving-bend math from `docs\architecture.md`

The Native impl carries a canonical snapshot (`tests/DesktopGrass.Native.Tests/snapshot_data.h`) that the other three impls' tests cross-check against indirectly via the shared spec.

## Future iterations (not v1)

- Occasional non-grass things — flowers, mushrooms, a small tree.
- Weather (rain, snow) and ambient wind gusts.
- Seasonal palettes / day-night color shift.
- Settings UI (density, palette, pause/resume).
- Auto-start on login.
- GitHub Actions CI for build + tests + smoke.
