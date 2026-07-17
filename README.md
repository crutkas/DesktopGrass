# DesktopGrass

A small, "just for fun" Windows app that draws a procedurally generated patch of
the outside world along the bottom edge of every monitor's work area, immediately
above its taskbar. The strip stays topmost and click-through, so input passes to
whatever is underneath. The design philosophy is **passive and calm**: ambient
touches only, no engagement loops, no toys.

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
- App state (scene, pet counts, auto-start preference, and monitor layout metadata)
  persists across sessions in `%LOCALAPPDATA%\DesktopGrass\state.json`.
- Optional "Start with Windows" toggle in the tray menu.
- Spans all monitors, anchored to the bottom of each monitor's work area
  regardless of taskbar position.

See [`CHANGELOG.md`](CHANGELOG.md) for a chronological list of everything that
has shipped, and [`docs/architecture.md`](docs/architecture.md) for the
simulation specification.

## Mouse input and privacy

DesktopGrass uses a Windows low-level mouse hook so movement can create gusts
and a left-button press can trigger a local visual effect. The supported Native
app enables the hook only while at least one grass strip is actively rendering.
It does not observe keyboard input, window titles, or window contents, and it
always passes mouse input through to the application underneath.

Each observed movement or left-button press is reduced to its event type,
timestamp, and screen position in a fixed-size in-memory queue. Events are
consumed by the simulation, dropped if the queue is full, and cleared when
observation stops. Hook ownership is process-scoped: normal shutdown and failed
startup both remove an installed hook. Cursor positions, clicks, and their
visual effects are not written to `state.json`, `config.json`, benchmark output,
or diagnostic messages.

The codebase has no telemetry client, network reporting, custom crash reporter,
or dump writer. DesktopGrass does not create or upload crash data; Windows may
still handle a process failure according to the operating system's own settings.

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

## Support policy

[`DesktopGrass.Native`](src/DesktopGrass.Native) is the supported standalone
implementation and the only PowerToys candidate. User-facing features, bug
fixes, platform hardening, release checks, and downloadable artifacts target
Native.

| Project | Role | Stack | Maintenance commitment |
| --- | --- | --- | --- |
| [`src/DesktopGrass.Native`](src/DesktopGrass.Native) | **Supported product and PowerToys candidate** | C++ / Win32 + Direct2D + DirectComposition | Active product development, platform hardening, tests, and release artifacts |
| [`src/DesktopGrass.Win2D`](src/DesktopGrass.Win2D) | **Source-available managed comparison/reference** | C# / .NET 10 + Vortice Direct2D + DirectComposition | Manual build/unit recipe retained for reproducibility; no active CI, feature-parity, platform-hardening, or release commitment |

The managed project may receive narrowly scoped build or test maintenance needed
to prevent source rot. Native changes do not need to be ported to it. The
default [`DesktopGrass.slnx`](DesktopGrass.slnx) contains only the supported
Native app and tests. Managed can be built and tested directly from its project
files; CI retains those steps as a commented manual recipe rather than a
required product gate.

> **History:** the repo originally shipped four parallel implementations to
> compare native, Direct2D-via-managed, packaged WinUI 3, and vanilla WPF for the
> same overlay shape. The WinUI 3 and WPF impls were dropped after a head-to-head
> A/B because they were 3-10× heavier on working set than the Native and Win2D
> builds while offering no behavioral advantage for a transparent, click-through,
> topmost overlay. See [`docs/comparison.md`](docs/comparison.md) for the full
> evaluation.

> [`docs/agent-context`](docs/agent-context/README.md) preserves the historical
> comparison-era development record. It is not the current roadmap; the support
> policy above and the Native migration backlog are authoritative.

## Run it

Build and launch the supported Native app:

```powershell
# x64; use Platform=ARM64 for native ARM64
msbuild src\DesktopGrass.Native\DesktopGrass.Native.vcxproj /p:Configuration=Release /p:Platform=x64
& "src\DesktopGrass.Native\out\x64\Release\DesktopGrass.Native.exe"
```

Select or right-click the **Desktop Grass controls** tray icon for scene
selection, pet count overrides, "Start with Windows", and quit. Keyboard users
can press `Win+B`, move to **Desktop Grass controls** (open **Show Hidden
Icons** first if needed), and press `Enter`.

Native builds for **x64** and **ARM64**. Outputs are nested under
`out\<Platform>\<Config>\`, so both architectures can coexist side by side.
Swap `x64` for `ARM64` to produce the ARM64 binary.

The Native exe is built via MSBuild against `src\DesktopGrass.Native\DesktopGrass.Native.vcxproj`
(Release / x64 or ARM64). See [`docs/manual-smoke.md`](docs/manual-smoke.md) for the full
build-from-scratch checklist.

## Running on another computer

| Build | What to copy | Size | Target requirements |
| --- | --- | --- | --- |
| **Native (Release)** | `src\DesktopGrass.Native\out\<Platform>\Release\DesktopGrass.Native.exe` (`<Platform>` = `x64` or `ARM64`) | ~210 KB | Windows 10 1809+, matching arch (x64 or ARM64). **Nothing else** — Release is statically linked against the CRT (`/MT`), so no VC++ redistributable is needed. |

The managed reference is intentionally not published or documented as a
portable/downloadable product build.

## Reproducing the managed reference

The C#/Vortice project remains source-available so the original comparison can
be reproduced on demand. These commands verify the reference manually; they do
not run in active CI or produce a supported release:

```powershell
dotnet build src\DesktopGrass.Win2D\DesktopGrass.Win2D.csproj -c Release -p:Platform=x64
dotnet test tests\DesktopGrass.Win2D.Tests\DesktopGrass.Win2D.Tests.csproj -c Release
```

## Tests

- **Unit tests** — the Native product suite plus the managed reference suite in
  [`tests/`](tests). They
  cover PRNG determinism, blade generation, sway, gusts, cuts, regrowth, stroke
  geometry, flowers, mushrooms, scenes, weather (snow), critters (sheep, cat,
  bunny, hedgehog), ambient flyers (butterflies, fireflies, bird flybys),
  scene-specific entities (desert tumbleweeds, winter pines, autumn leaves &
  maples, ocean coral / fish / bubbles), `config.json` parsing, "Start with
  Windows" auto-start, persistence, and click-through window styles. Native
  tests use the same Microsoft C++ Unit Test Framework as PowerToys and run
  through Visual Studio's `vstest.console`.
- **Managed baseline reproducibility** — existing side-stream PRNG identity and
  snapshot tests preserve the comparison state at the point Managed was frozen.
  They are evidence for the reference, not a forward feature-parity promise.
- **Smoke tests** — [`tests/smoke/Run-SmokeTests.ps1`](tests/smoke/Run-SmokeTests.ps1)
  gates the supported Native binary and can optionally exercise the managed
  reference. It asserts click-through / topmost window styles and verifies
  rendering via screenshot pixel variance over the bottom strip.

Run Native coverage and, when needed, reproduce the managed baseline:

```powershell
# Native unit tests (x64)
msbuild tests\DesktopGrass.Native.Tests\DesktopGrass.Native.Tests.vcxproj /p:Configuration=Release /p:Platform=x64
tests\DesktopGrass.Native.Tests\Run-Tests.ps1 -Configuration Release -Platform x64

# Native ARM64 hardware lab run with a retained TRX log
msbuild tests\DesktopGrass.Native.Tests\DesktopGrass.Native.Tests.vcxproj /p:Configuration=Release /p:Platform=ARM64
tests\DesktopGrass.Native.Tests\Run-Tests.ps1 -Configuration Release -Platform ARM64 -ResultsDirectory artifacts\arm64-tests

# Managed reference unit tests
dotnet test tests\DesktopGrass.Win2D.Tests\DesktopGrass.Win2D.Tests.csproj -c Release

# Optional comparison smoke (supported Native + managed reference)
pwsh tests\smoke\Run-SmokeTests.ps1 -Target All
```

The native test runner prints the OS, PowerShell process, VSTest PE, and test DLL
architectures before execution. ARM64 runs fail unless the OS, VSTest executable,
and test DLL are all ARM64, so x64 emulation cannot be recorded as native ARM64
coverage. Preserve the generated TRX file with the lab result.

## Simulation specification and reference baseline

Native is governed by [`docs/architecture.md`](docs/architecture.md). At the
managed freeze point, Native and Managed used:

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

The Native implementation carries a canonical snapshot
(`tests/DesktopGrass.Native.Tests/snapshot_data.h`) that the Win2D impl's tests
cross-check against indirectly. Future Native behavior may intentionally move
beyond that managed baseline without being backported.

## PowerToys migration

The Native implementation is the selected PowerToys candidate. The ordered
work items, dependencies, and acceptance gates are tracked in
[`docs/powertoys-migration.md`](docs/powertoys-migration.md).

## Roadmap

All product roadmap and platform-hardening work applies to Native only. Managed
has no feature-parity or product-hardening roadmap.

Possible Native directions, in no particular order:

- More critter species (deer, ducks crossing the strip).
- Auto-rotation of scenes by date (e.g. Autumn in October, Winter in December).
- Multi-monitor smoke tests in CI.
- A settings UI (currently held off — passive philosophy prefers tray-only
  controls; revisit if the tray menu starts feeling cluttered).

## License

DesktopGrass is licensed under the [MIT License](LICENSE).
