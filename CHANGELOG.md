# Changelog

All notable changes to **DesktopGrass** — a "just for fun" topmost, click-through
ambient overlay for Windows.

This project is a small personal sandbox and does not follow semantic versioning;
entries are grouped by date instead.

---

## 2026-05-30 — Critters, color, weather, polish

### Added
- **Bunny** — third critter species (Grass scene only). Brown body with white tail
  puff, tall ears, signature hop-only locomotion (no walk cycle), nose twitch and
  ear wiggle when idle. Skittish: click within 90 DIP startles into a boosted
  hop-away. Crepuscular sleep bias.
- **Cat coat color variants** — 6 coats: gray tabby, orange, black (yellow eyes),
  white, brown tabby, cream. One PRNG draw per cat picks the variant.
- **Light rain** — Grass-scene weather. Thin blue-grey diagonal raindrops with a
  short motion-blur tail; Poisson spawn timing. Soft fade when switching scenes.
- **Butterflies (day) + fireflies (night)** — ambient Grass-scene flyers above
  the strip. Butterflies have a fluttering wing animation and 5 color variants
  (monarch, swallowtail, cabbage, morpho, pink). Fireflies have a warm yellow
  blink with a soft halo and asynchronous phase offsets so they never sync up.
  Crossfade during dawn/dusk for a "magic hour" overlap.
- **Day-night ambient tint** — subtle 8-phase color overlay keyed to the local
  hour (peak alpha 36/255). No tray toggle; always on.

### Changed
- **Pine trees beefed up** — `PINE_WIDTH_MIN/MAX` 16-28 → **28-48 DIP** so the
  conifer silhouette reads at ~2:1 height:width instead of ~3:1. Birches felt
  solid; pines no longer feel like skinny sticks next to them.

### Repository
- `.copilot-scratch/` is now `.gitignore`-d so future agent runs don't
  accidentally commit test scratch outputs.

---

## 2026-05-29 → 2026-05-30 — Pets, persistence, foundations

### Added
- **Sheep** — first critter species (Grass scene only). State machine with
  Walking / Grazing / Idle / Sleeping / Hopping / Greeting. Time-of-day biased
  sleep probability (rare in morning, common at night). Click within 64 DIP
  startles a sheep into a hop with a temporary speed boost. Cursor-curious idle
  sheep gently turn their heads toward the pointer. Sheep within 50 DIP greet
  each other (a brief frozen interaction with a head-bob and a `vx` flip on exit).
- **Cat** — second critter species. State machine with Walking / Idle / Sleeping
  / Pouncing. Click triggers a pounce *toward* the click (opposite of sheep's
  flee-away). Cats don't greet — they're aloof.
- **Per-pet names** — every critter draws a name from a species-specific pool
  (`SHEEP_NAME_POOL`, `CAT_NAME_POOL`, `BUNNY_NAME_POOL`). Names render on
  hover.
- **Pet count override** — tray-menu sub-menu lets you pin a fixed count
  (1-6) per species; default is random within the species range.
- **Persistence** — `%LOCALAPPDATA%\DesktopGrass\state.json` (atomic
  `.tmp` + rename). Round-trips scene selection, per-monitor cut state
  (stored as negative "seconds ago" to replay relative to load-time
  sim clock), pet counts, and the auto-start preference. Unmatched monitor
  keys are silently skipped.
- **Start with Windows toggle** — tray menu option; writes the appropriate
  `HKCU\...\Run` value. Native and Win2D use distinct value names so both
  builds can auto-start side-by-side.
- **Click-through smoke test** — end-to-end test launches each exe and
  asserts the topmost / click-through extended window styles.

---

## 2026-05-29 — Scenes, biomes, ambient gusts

### Added
- **Scene infrastructure** — tray menu lets you pick **Grass**, **Desert**, or
  **Winter**. Each scene swaps palette, content, and weather.
  - **Desert**: shorter blades, cacti (slot-bound, persistent across scene
    transitions), occasional rolling tumbleweeds.
  - **Winter**: -50% grass density, pines and birches, snowflake weather,
    no mushrooms.
- **Pine trees** — Winter-scene anchor. Stacked filled triangles with snow caps.
- **Birch tree variant** — second Winter tree style. Vertical white trunk with
  dark bark dashes, upward-angled branch fan, snow blobs at each branch tip
  and a soft snow cap on top.
- **Ambient gusts** — small, randomly scheduled wind puffs that propagate
  across the strip even when the cursor is nowhere near. Runs on its own
  PRNG stream so the existing cursor-gust path stays untouched.
- **Roaming-entity subsystem** — generalized scaffolding for non-grass entities
  (tumbleweeds, snowflakes, critters, raindrops, butterflies, fireflies).
- **Static CRT linking** — Native release exe is now `/MT` so it ships as a
  single ~210 KB binary with no VC++ redistributable dependency.

---

## 2026-05-28 → 2026-05-29 — Flowers, mushrooms, chord physics

### Added
- **Flowers** — small colored heads at occasional blade tips, on an independent
  PRNG stream.
- **Mushrooms** — short ivory stems with capped tops, on a fourth PRNG stream.
  Cut leaves a slightly taller stump stub (so the nub reads as distinct from
  cut grass stubs).
- **Chord-preserving blade bend** — blades now pivot from the root like a
  hinged stick. Leaning blades arc and drop their tip naturally instead of
  stretching.

### Changed
- Bumped passive sway amplitude by 10% (`BASE_AMPLITUDE` 3.0 → 3.3).
- Halved `GUST_TO_LEAN_FACTOR` (1.5 → 0.75) so cursor gusts feel less aggressive.

### Removed
- **WinUI 3 and WPF implementations** — the repo originally shipped four
  parallel impls (Native, Win2D, WinUI 3, WPF) for a head-to-head comparison.
  After A/B testing, WinUI 3 and WPF were 3-10× heavier on working set than
  Native and Win2D for the same overlay shape, with no behavioral advantage.
  See [`docs/comparison.md`](docs/comparison.md). The Native and Win2D builds
  are the supported implementations going forward.

---

## 2026-05-27 → 2026-05-28 — Initial implementation

### Added
- **Core grass simulation** — xorshift64 PRNG seeded via SplitMix64,
  procedural blade generation, sway physics with a 6-second base period,
  cursor-driven gust impulses propagating outward across the strip,
  click-to-cut with regrowth (30-90s wait + 2-4s animation).
- **Two reference implementations**:
  - **DesktopGrass.Native** — C++ / Win32 / Direct2D / DirectComposition.
  - **DesktopGrass.Win2D** — C# / .NET 10 / Vortice.Direct2D1 (the
    "Win2D" name is historical — it does not use `Microsoft.Graphics.Win2D`).
  - Both share the same `Sim` / `Constants` numerical core so blade
    geometry is bit-identical across renderers.
- **Multi-monitor support** — the overlay anchors to the bottom of each
  monitor's work area regardless of taskbar position.
- **Click-through topmost overlay** — `WS_EX_TRANSPARENT | WS_EX_LAYERED |
  WS_EX_TOPMOST` so input passes through to whatever is underneath.
- **Architecture spec** — `docs/architecture.md` captures the shared
  algorithm contract both impls implement.
- **UI / unit / smoke test scaffolding** — `winapp ui`-driven smoke
  harness plus per-impl unit tests for PRNG determinism, generation,
  sway, gusts, cuts, regrowth, and stroke geometry.
