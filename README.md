# DesktopGrass

A small, "just for fun" Windows app that draws procedurally generated grass along the bottom edge of every monitor, on top of all windows (including the taskbar).

- Click-through — input passes through to whatever is underneath.
- Sways gently on its own with a 6-second period.
- Cursor passing over the strip triggers gusts of wind that propagate outward.
- Clicking near grass cuts blades (the click itself still hits whatever's underneath).
- Cut blades regrow after 30–90 seconds, animating back over 2–4 seconds.
- Blades bend chord-preservingly — they pivot from the root like a hinged stick, so the tip arcs over and drops as they lean (no stretching artifacts).
- Occasional flowers and mushrooms procedurally appear on independent PRNG streams.
- Spans all monitors, anchored to the bottom of each screen's work area regardless of taskbar position.

## Two implementations

The same feature set is implemented two ways, both sharing the same `Sim.cs` / `Constants.cs` numerical core so blade geometry stays bit-identical across renderers.

| Project | Stack | Renderer |
| --- | --- | --- |
| [`src/DesktopGrass.Native`](src/DesktopGrass.Native) | C++ / Win32 | Direct2D + DirectComposition |
| [`src/DesktopGrass.Win2D`](src/DesktopGrass.Win2D) | C# / .NET 10 | Vortice.Direct2D1 + DirectComposition (the "Win2D" name is historical — it uses Vortice, not `Microsoft.Graphics.Win2D`) |

> **History:** the repo originally shipped four parallel implementations to compare native, Direct2D-via-managed, packaged WinUI 3, and vanilla WPF for the same overlay shape. The WinUI 3 and WPF impls were dropped after a head-to-head A/B because they were 3–10× heavier on working set than the Native and Win2D builds while offering no behavioral advantage for a transparent, click-through, topmost overlay. See [`docs/comparison.md`](docs/comparison.md) for the full evaluation.

See [`docs/architecture.md`](docs/architecture.md) for the shared algorithm spec each impl implements.

> Working on this with Copilot CLI on a different machine? See
> [`docs/agent-context/README.md`](docs/agent-context/README.md) — that folder
> is a portable snapshot of the agent's plan + per-milestone checkpoints,
> so a fresh session anywhere can pick up with full context.

## Run it

Pick an implementation and launch its release exe:

```powershell
# Native — C++/Direct2D
& "src\DesktopGrass.Native\out\Release\DesktopGrass.Native.exe"

# Win2D — C#/Vortice
dotnet build src\DesktopGrass.Win2D -c Release
& "src\DesktopGrass.Win2D\bin\Release\net10.0-windows10.0.19041.0\DesktopGrass.Win2D.exe"
```

Right-click the tray icon to quit.

The Native exe is built via MSBuild against `src\DesktopGrass.Native\DesktopGrass.Native.vcxproj` (Release / x64). See [`docs/manual-smoke.md`](docs/manual-smoke.md) for the full build-from-scratch checklist.

## Portability — running on another computer

| Build | What to copy | Size | Target requirements |
| --- | --- | --- | --- |
| **Native (Release)** | `src\DesktopGrass.Native\out\Release\DesktopGrass.Native.exe` | ~210 KB | Windows 10 1809+ x64. **Nothing else** — Release is statically linked against the CRT (`/MT`), so no VC++ redistributable is needed. |
| **Win2D (framework-dependent)** | `src\DesktopGrass.Win2D\bin\Release\net10.0-windows10.0.19041.0\` (whole folder, 15 files) | ~26 MB | Windows 10 1809+ x64 **and** .NET 10 desktop runtime installed (`winget install Microsoft.DotNet.DesktopRuntime.10`). |
| **Win2D (self-contained, single file)** | `publish\win2d-selfcontained\DesktopGrass.Win2D.exe` after the publish command below | ~143 MB | Windows 10 1809+ x64. **Nothing else** — .NET runtime + Vortice native DLLs are baked in. |

To produce the Win2D self-contained single-file build:

```powershell
dotnet publish src\DesktopGrass.Win2D -c Release -r win-x64 `
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\win2d-selfcontained
```

Tip: For a drop-and-run experience on a friend's box, Native is the way — one 210 KB exe, no installer, no runtime. Win2D self-contained is the equivalent if you want the C# build.

## Tests

- **Unit tests** — pure-logic suites (PRNG determinism, blade generation, sway, gust, cut, regrowth, stroke geometry, flowers, mushrooms) for each impl in [`tests/`](tests). The two impls share a Sim/Constants core so they assert against the same numerical contract.
- **Smoke tests** — [`tests/smoke/Run-SmokeTests.ps1`](tests/smoke/Run-SmokeTests.ps1) launches each exe, asserts the click-through/topmost extended window styles, and verifies actual rendering via screenshot pixel-variance over the bottom strip.

Run everything:

```powershell
# Unit tests (Win2D)
dotnet test

# Cross-impl smoke (2 targets)
pwsh tests\smoke\Run-SmokeTests.ps1 -Target All
```

## Conformance

Both implementations use:

- The same xorshift64 PRNG seeded via SplitMix64
- The same canonical test seed (`0x6B6173746F`)
- The same blade-generation draw order (main / regrowth / flower / mushroom streams)
- The same sway / gust / cut / regrowth / chord-preserving-bend / flower / mushroom math from `docs\architecture.md`

The Native impl carries a canonical snapshot (`tests/DesktopGrass.Native.Tests/snapshot_data.h`) that the Win2D impl's tests cross-check against indirectly via the shared spec.

## Future iterations (not v1)

- More non-grass things — a small tree, occasional insects.
- Weather (rain, snow) and ambient wind gusts.
- Seasonal palettes / day-night color shift.
- Settings UI (density, palette, pause/resume).
- Auto-start on login.
- GitHub Actions CI for build + tests + smoke.
