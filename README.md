# DesktopGrass

A small, "just for fun" Windows app that draws a procedurally generated patch of
the outside world along the bottom edge of every monitor, on top of all windows
(including the taskbar). Click-through — input passes through to whatever is
underneath. The design philosophy is **passive and calm**: ambient touches only,
no engagement loops, no toys.

![DesktopGrass in action](docs/assets/grass.gif)

## What's in it

**Grass scene** (default)
- Procedurally generated grass strip that sways gently with a 6-second base period.
- Cursor passing over the strip triggers gusts of wind that propagate outward.
- Clicking near grass cuts blades (the click itself still hits whatever's underneath).
  Cut blades regrow after 30-90 seconds, animating back over 2-4 seconds.
- Blades bend chord-preservingly — they pivot from the root like a hinged stick,
  so the tip arcs over and drops as they lean (no stretching artifacts).
- Occasional flowers and mushrooms procedurally appear on independent PRNG streams.
- Ambient gusts puff across the strip on their own schedule.
- **Critters** — sheep, cats, bunnies, and hedgehogs wander the strip. Each has
  a name shown on hover, a state machine (walking / grazing / idle / sleeping /
  etc.), time-of-day sleep biasing, and species-specific quirks: sheep greet
  each other, cats pounce toward clicks, bunnies skitter away from them,
  hedgehogs curl into a defensive spiky ball. Cats come in 6 coat variants.
  Hedgehog sightings are probabilistic (~55% per session). Pet count is
  configurable from the tray.
- **Butterflies and fireflies** — gentle ambient flyers drifting above the strip.
- **V-formation bird flybys** — occasional flocks of 3-7 birds cross overhead,
  far above the critters.

**Desert scene**
- Shorter grass; cacti dominate; rolling tumbleweeds.

**Winter scene**
- -50% grass density; pines and birches with snow caps; falling snowflakes.
- Pines and birches are split into a hazier background layer and a sharper
  foreground layer for a sense of depth.
- Clicking the strip kicks up a small two-tone snow puff at the cursor.
- Brushing the cursor low and fast across the strip kicks up a gentle wisp of
  spindrift.

**Autumn scene**
- Warm orange / red / yellow / gold blade palette. Falling leaves drift down
  with horizontal sway and rotation in six color variants. Maple trees with
  warm canopies; 20% are bare for a late-autumn feel. Intentionally the
  contemplative empty season — no critters, no other weather, just leaves
  and trees.

**Ocean scene**
- Teal / aqua seafloor palette so the grass reads as wisps of seagrass on a
  sandy bottom. Coral in three forms (fan, branching, brain) sprouts in five
  reef colors. Bubbles rise from the floor with a gentle horizontal wobble and
  pop at the surface, and a small school of fish swims back and forth across
  the strip.

**Always-on touches**
- App state (scene, cuts, pet counts, auto-start preference)
  persists across sessions in `%LOCALAPPDATA%\DesktopGrass\state.json`.
- Optional "Start with Windows" toggle in the tray menu.
- Spans all monitors, anchored to the bottom of each monitor's work area
  regardless of taskbar position.

See [`CHANGELOG.md`](CHANGELOG.md) for a chronological list of everything that
has shipped, and [`docs/architecture.md`](docs/architecture.md) for the shared
algorithm contract.

## Settings (`config.json`)

User-tunable knobs live in `%LOCALAPPDATA%\DesktopGrass\config.json`, separate
from the app-owned `state.json`. The file is created with annotated defaults on
first run and is **only ever read after that — never overwritten** — so your
edits stick. It accepts `//` and `/* */` comments and trailing commas (JSONC).
Edit it and restart the app to apply. Unknown keys are ignored; malformed files
fall back to defaults without being clobbered.

| Key            | Default  | Range     | Effect |
|----------------|----------|-----------|--------|
| `targetFps`    | `24`     | `5`–`144` | Animation frame rate. Lower = less CPU, choppier motion. |
| `bladeDensity` | `2.53125` | `0.2`–`5.0` | Grass blade density. Lower = fewer blades (less CPU). |
| `swaySpeed`    | `1.0`    | `0.0`–`3.0` | Grass sway speed multiplier. `0.0` = still, higher = faster. |
| `swayAmplitude`| `1.0`    | `0.0`–`3.0` | Grass sway amplitude (how far blades lean). `0.0` = upright. |

```jsonc
{
  "version": 1,
  "targetFps": 24,
  "bladeDensity": 2.53125,
  "swaySpeed": 1.0,
  "swayAmplitude": 1.0
}
```

## Two implementations

The same feature set is implemented two ways, both sharing the same `Sim` /
`Constants` numerical core so behavior stays bit-identical across renderers.

| Project | Stack | Renderer |
| --- | --- | --- |
| [`src/DesktopGrass.Native`](src/DesktopGrass.Native) | C++ / Win32 | Direct2D + DirectComposition |
| [`src/DesktopGrass.Win2D`](src/DesktopGrass.Win2D) | C# / .NET 10 | Vortice.Direct2D1 + DirectComposition (the "Win2D" name is historical — it uses Vortice, not `Microsoft.Graphics.Win2D`) |

> **History:** the repo originally shipped four parallel implementations to
> compare native, Direct2D-via-managed, packaged WinUI 3, and vanilla WPF for the
> same overlay shape. The WinUI 3 and WPF impls were dropped after a head-to-head
> A/B because they were 3-10× heavier on working set than the Native and Win2D
> builds while offering no behavioral advantage for a transparent, click-through,
> topmost overlay. See [`docs/comparison.md`](docs/comparison.md) for the full
> evaluation.

> Working on this with Copilot CLI on a different machine? See
> [`docs/agent-context/README.md`](docs/agent-context/README.md) — that folder
> is a portable snapshot of the agent's plan + per-milestone checkpoints,
> so a fresh session anywhere can pick up with full context.

## Run it

Pick an implementation and launch its release exe:

```powershell
# Native — C++/Direct2D (x64; use Platform=ARM64 for native ARM64)
msbuild src\DesktopGrass.Native\DesktopGrass.Native.vcxproj /p:Configuration=Release /p:Platform=x64
& "src\DesktopGrass.Native\out\x64\Release\DesktopGrass.Native.exe"

# Win2D — C#/Vortice
dotnet build src\DesktopGrass.Win2D -c Release -p:Platform=x64
& "src\DesktopGrass.Win2D\bin\x64\Release\net10.0-windows10.0.19041.0\DesktopGrass.Win2D.exe"
```

Right-click the tray icon for scene selection, pet count overrides, "Start with
Windows", and quit.

Both apps build for **x64** and **ARM64**. Build outputs are nested per platform
(`out\<Platform>\<Config>\` for Native, `bin\<Platform>\<Config>\<TFM>\` for
Win2D), so the two architectures can coexist side by side. Swap `x64` for `ARM64`
(Win2D RID `win-arm64`) to produce native ARM64 binaries.

The Native exe is built via MSBuild against `src\DesktopGrass.Native\DesktopGrass.Native.vcxproj`
(Release / x64 or ARM64). See [`docs/manual-smoke.md`](docs/manual-smoke.md) for the full
build-from-scratch checklist.

## Portability — running on another computer

| Build | What to copy | Size | Target requirements |
| --- | --- | --- | --- |
| **Native (Release)** | `src\DesktopGrass.Native\out\<Platform>\Release\DesktopGrass.Native.exe` (`<Platform>` = `x64` or `ARM64`) | ~210 KB | Windows 10 1809+, matching arch (x64 or ARM64). **Nothing else** — Release is statically linked against the CRT (`/MT`), so no VC++ redistributable is needed. |
| **Win2D (framework-dependent)** | `src\DesktopGrass.Win2D\bin\<Platform>\Release\net10.0-windows10.0.19041.0\` (whole folder, 15 files) | ~26 MB | Windows 10 1809+ (x64 or ARM64) **and** .NET 10 desktop runtime installed (`winget install Microsoft.DotNet.DesktopRuntime.10`). |
| **Win2D (self-contained, single file)** | `publish\win2d-selfcontained\DesktopGrass.Win2D.exe` after the publish command below | ~143 MB | Windows 10 1809+ (x64 or ARM64, matching the `-r` RID). **Nothing else** — .NET runtime + Vortice native DLLs are baked in. |

To produce the Win2D self-contained single-file build:

```powershell
dotnet publish src\DesktopGrass.Win2D -c Release -r win-x64 `
  --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\win2d-selfcontained
```

(Swap `-r win-x64` for `-r win-arm64` to publish a native ARM64 single-file build.)

Tip: For a drop-and-run experience on a friend's box, Native is the way — one
210 KB exe, no installer, no runtime. Win2D self-contained is the equivalent if
you want the C# build.

## Tests

- **Unit tests** — pure-logic suites for each impl in [`tests/`](tests). They
  cover PRNG determinism, blade generation, sway, gusts, cuts, regrowth, stroke
  geometry, flowers, mushrooms, scenes, weather (snow), critters (sheep, cat,
  bunny, hedgehog), ambient flyers (butterflies, fireflies, bird flybys),
  scene-specific entities (desert tumbleweeds, winter pines, autumn leaves &
  maples, ocean coral / fish / bubbles), `config.json` parsing, "Start with
  Windows" auto-start, persistence, and click-through window styles.
- **Cross-impl PRNG identity** — every species and weather entity is covered by
  a side-stream PRNG identity test that walks a parallel `Prng(seed XOR salt)`
  and asserts the bit-identical draw order. This is the cornerstone invariant
  that lets Native and Win2D stay in lockstep.
- **Smoke tests** — [`tests/smoke/Run-SmokeTests.ps1`](tests/smoke/Run-SmokeTests.ps1)
  launches each exe, asserts the click-through / topmost extended window styles,
  and verifies actual rendering via screenshot pixel-variance over the bottom strip.

Run everything:

```powershell
# Native unit tests (x64; build with Platform=ARM64 for the ARM64 suite)
& ".\tests\DesktopGrass.Native.Tests\out\x64\Release\DesktopGrass.Native.Tests.exe" --reporter compact

# Win2D unit tests
dotnet test tests\DesktopGrass.Win2D.Tests\DesktopGrass.Win2D.Tests.csproj -c Release

# Cross-impl smoke (2 targets)
pwsh tests\smoke\Run-SmokeTests.ps1 -Target All
```

## Conformance

Both implementations use:

- The same xorshift64 PRNG seeded via SplitMix64.
- The same canonical test seed (`0x6B6173746F`).
- The same per-feature PRNG salt for each independent stream — blades,
  regrowth, flowers, mushrooms, ambient gusts; ground critters (sheep, cat,
  bunny, hedgehog share one salt); ambient flyers (butterflies, fireflies,
  bird flybys); per-scene streams (desert cacti & tumbleweeds; winter
  snowflakes, pines, click-puff and cursor-drift puffs; autumn leaves, maples,
  leaf-puff; ocean coral, bubbles, fish).
- The same sway / gust / cut / regrowth / chord-bend / weather / critter math
  from [`docs/architecture.md`](docs/architecture.md).

The Native impl carries a canonical snapshot
(`tests/DesktopGrass.Native.Tests/snapshot_data.h`) that the Win2D impl's tests
cross-check against indirectly via the shared spec.

## Roadmap

Possible next directions, in no particular order:

- More critter species (deer, ducks crossing the strip).
- Auto-rotation of scenes by date (e.g. Autumn in October, Winter in December).
- Multi-monitor smoke tests in CI.
- A settings UI (currently held off — passive philosophy prefers tray-only
  controls; revisit if the tray menu starts feeling cluttered).

