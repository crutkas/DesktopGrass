# Changelog

All notable changes to **DesktopGrass** — a "just for fun" topmost, click-through
ambient overlay for Windows.

This project is a small personal sandbox and does not follow semantic versioning;
entries are grouped by date instead.

---

---

---

## 2026-06-05 — Fix: Win2D grass goes stale / partial after a display change

### Fixed
- **Win2D (`d2d`) overlay no longer renders only part of the screen width after a
  resolution or DPI change.** Previously the Win2D app quit on `WM_DISPLAYCHANGE`
  and ignored `WM_DPICHANGED`, so after a live display change the window, swap
  chain and blade layout went stale and the grass covered only part of the
  monitor. The app now rebuilds its per-monitor windows in place on both
  messages (matching the native app's `OnDisplayChanged` behavior). A guard
  (`_rebuilding`) prevents the per-window `WM_DESTROY` fired while tearing down
  the old windows from being mistaken for an app-quit signal. Verified by
  toggling the primary monitor between 3270×2180 and 2560×1440: the app stays
  alive and the grass window tracks the new full width each time.

---

## 2026-06-05 — Performance: 30 fps cap + cheaper blade rendering

### Changed
- **Frame rate capped at 30 fps** (was 60). The scene is calm, time-based
  ambient motion, so halving the sample rate roughly halves per-frame CPU with
  no change to the animation itself. The native loop now paces authoritatively
  by QPC (subtracting render/present time from the frame budget) so the cadence
  holds regardless of how Present blocks; Win2D already paced via its QPC gate.
- **Native blades no longer build a path geometry per blade every frame.** The
  native renderer was allocating/opening/closing an `ID2D1PathGeometry` and
  stroking a quadratic Bezier for *every* grass/flower blade *every* frame
  (~700 COM allocations + curved-stroke tessellations per frame on a wide
  monitor). It now tessellates each blade into straight `DrawLine` segments with
  round caps — matching what the Win2D renderer already did.
- **Blade tessellation reduced from 6 to 4 segments** in both renderers. Draw
  calls are the dominant per-frame cost (CPU scales nearly linearly with segment
  count); 4 segments keeps short grass looking smooth. Measured steady-state on a
  single 2180-px monitor: ~38% → ~25% of one core. (Tunable: raise to 6 for
  smoother curves, lower to 2–3 for even less CPU.)



### Changed
- **Winter goes back to the snow-tipped grass look.** The sculpted snowbank /
  snowscape (and its wind-blown spindrift) looked off and was the dominant CPU
  cost — it redrew a per-2px column strip across the full monitor width every
  frame. The renderer no longer draws the snowbank; ordinary Winter ground cover
  renders as the original frosted, snow-capped grass blades again, with the pine
  and birch treeline (foreground/background depth retained) standing in it.
- **Trees sit back on the ground line** (the snow-accumulation base-burial offset
  is no longer applied at render time, since there's no bank to sink into).

### Kept
- **The click snow puff stays** — clicking the Winter ground still kicks up a
  burst of powder. The snow-accumulation / carve / drift simulation state is left
  intact (and still persisted) but is no longer drawn; only the puff is visible.



### Changed
- **The Winter snowbank is much lower and no longer buries the treeline.** Reduced
  the base depth (13→9), roll amplitude (7→5), cornice (11→5) and the snow
  accumulation cap (`SNOW_DEPTH_MAX` 30→6), so the bank reads as a calm drift along
  the bottom rather than a tall wall swallowing the background pines.
- **Click/drift snow puffs are now actually visible against the white bank.** Each
  puff draws a cool-toned rim (reusing the bank shadow color) behind a bright white
  core, and the grains are bigger (size 3.5–8 vs 2–4.5), loftier (burst speed
  70–150) and longer-lived (1.0–1.8 s), so a click reads as a real poof of powder.
- **Spindrift kick-up is a touch livelier** — slightly more, larger, faster grains
  (`SNOW_DRIFT_*` count 4–8, size scale 0.9, speed scale 0.85).

### Added
- **Wind-blown spindrift**: a few faint streaks of snow now skim horizontally just
  above the bank crest, scrolling with global time so the surface reads as
  wind-blown and alive. Render-only and fully deterministic from time + lane index
  (`SNOW_WIND_*`) — no new entities, sim logic, or PRNG draws.



### Added
- **Brushing the cursor low and fast across the Winter snowbank now kicks up a
  gentle wisp of powder** — the move-driven analogue of the autumn leaf-puff,
  giving Winter the same passing-cursor interaction as grass, desert, and fall
  (it previously only reacted to clicks). Reuses the snow-puff particle with
  fewer, smaller, slower grains (`SNOW_DRIFT_*`), gated by a low band near the
  ground, a minimum cursor speed (90 DIP/s), and a 0.12 s global cooldown so it
  stays calm. Uses an independent PRNG so it never perturbs the click-puff or
  snowflake streams.

### Fixed
- The cursor-move handler now rejects non-finite coordinates up front (matching
  the click handler), so a stray NaN can no longer poison the cursor baseline.

---

## 2026-06-04 — Winter treeline depth: foreground & background pines

### Added
- **Winter pines and birches are now split into foreground and background layers**
  so the treeline reads with real depth instead of a single flat row. About 45% of
  trees are pushed to the background: drawn smaller (`TREE_BG_SCALE` 0.62), hazier
  (`TREE_BG_OPACITY` 0.78), and *behind* the snowbank so the bank occludes their
  base. Foreground trees stay full-size and in front of the bank.
- Depth assignment is a single locked PRNG draw at generation (deterministic across
  scene re-entry); non-winter scenes clear the flag.

---

## 2026-06-04 — Snow footprints: carve-and-settle interaction

### Added
- **Clicking the winter snowbank now presses a soft footprint dent** that slowly
  settles back over ~6 seconds, mirroring the grass cut-and-regrow loop so winter
  has a tactile ground interaction like the other scenes (not just the snow puff).
  Clicks both kick up powder *and* press a dent at the cursor.
  - New transient carve heightfield `snowCarve` / `SnowCarve` (`SNOW_CARVE_BUCKETS`
    = 192 columns across the monitor). Never persisted; cleared on every scene
    change, sim init, and regenerate.
  - New constants `SNOW_CARVE_RADIUS_DIP` (24), `SNOW_CARVE_DEPTH_PER_CLICK` (7),
    `SNOW_CARVE_MAX_DEPTH` (11, the bank never inverts), `SNOW_CARVE_REFILL_RATE`
    (1.8 DIP/s).
  - New Sim API (both impls): `sim_apply_snow_carve`/`ApplySnowCarve` (raised-cosine
    falloff, clamped), `sim_decay_snow_carve`/`DecaySnowCarve` (settle-back, never
    negative), `snow_carve_depth_at`/`SnowCarveDepthAt` (interpolated query).
  - Carve decays *before* per-frame input is processed, so a same-frame click lands
    its full dent (pinned by an ordering test).
  - Renderer: the snowbank silhouette dips at carved columns (effective depth
    clamped to `SNOW_BANK_MIN_DEPTH`), with a cool recessed interior and a thin
    raised-rim highlight along steep carve gradients so the dent reads as a scooped
    trench rather than just a lower ridge.

### Notes
- Carve is **click-only** (no hover/Move carving) to keep the scene calm and make
  the interaction input-rate independent.

---

### Removed
- **Removed the Grass-scene light rain.** The raindrop emitter, renderer, brush,
  `EntityKind::Raindrop` discriminant (value `5`, now a retired gap), the
  `raindropPrng` / `nextRaindropSpawnTime` state, and all `RAINDROP_*` constants
  are gone from both the Native and Win2D implementations. Scene transitions now
  simply clear all roaming entities. Fireflies, butterflies, snow, and falling
  leaves are unaffected. Test counts drop accordingly (292 native / 300 Win2D).

## 2026-06-04 — Snow that actually looks like snow drifts

### Changed
- **Reworked the winter snow from repeated white half-circles into a single
  continuous, wind-sculpted snowbank.** Instead of one bump per blade, the ground
  is now a rolling drift with a multi-harmonic crest (big dunes + ripples + fine
  texture + occasional wind-piled cornices) and real tonal depth: a bright lit
  crest, a soft body, and a cool-blue shadow at the base, finished with a crisp
  highlighted crest edge and a cool sub-crest lip. Sparse sparkle still glints
  along the ridge, and accumulated snow still piles the bank higher over time.
  Purely a render change. The click snow puff is unchanged.

## 2026-06-04 — Bigger winter snow puff

### Changed
- **The click snow puff is now noticeably bigger** — larger powder motes, more of
  them per click, a wider and higher burst, and a bigger starting cloud, so a
  click on the winter snowbank kicks up a fuller poof. Still falls back and fades
  the same way; just more to see.

## 2026-06-04 — Trees sway with the wind & cursor

### Changed
- **Fall maples and winter pines/birches now sway ever so slightly** — like the
  grass, the trees already carried an ambient lean plus a nudge from the nearby
  cursor; the renderer now leans each tree about its trunk base by a small,
  clamped fraction of that lean. The result is a subtle drift of the canopy with
  the wind and the mouse, while the trunk stays rooted. Purely a render effect;
  the sway is damped well below the grass so it never reads as wobbling.

## 2026-06-04 — Winter snow pivot: drifts, click puffs & sparkle

### Changed
- **Winter now reads as snow, not grass** — the green winter grass blades are
  replaced by low rounded snow drifts (off-white base mound with a soft white
  highlight dab). Dense neighbors overlap into a continuous snowbank along the
  bottom of every monitor.

### Added
- **Snow puff on click** — clicking the winter snowbank kicks up a small burst of
  powder (6–10 white motes) that rises, slows, and falls back to the ground. The
  click still gently dents the drift underneath, so the bank reacts and refills.
- **Subtle drift sparkle** — a sparse, slow, deterministic twinkle plays across the
  drift tops so the snow catches the light without becoming busy.

## 2026-06-04 — Pine trees with depth

### Changed
- **Winter pines now read as rounded boughs instead of flat triangles** — each
  snow-capped tier gets a self-shadow dropped down-right and a softer lit face
  dabbed on the upper-left, giving the conifers the same sense of dimension the
  fall maples just gained. Color, size, and tier variation are unchanged; this is
  purely a shading upgrade.

## 2026-06-04 — Fuller fall trees & leaves that scatter on hover

### Changed
- **Redesigned the autumn maple canopy** — the flat single-oval crown is replaced
  by a fuller layered crown built from several overlapping autumn-shaded leaf
  clumps with a couple of lighter highlight dabs, so the fall trees read as soft,
  rounded foliage instead of a lollipop.

### Added
- **Leaf puff on hover** — moving the cursor over a leafy maple now sheds a small
  outward burst of 4–7 leaves, like a gust catching the foliage. The burst drifts
  outward and decays so the leaves settle naturally. Bare (already-shed) trees and
  trees on cooldown stay calm, and the effect uses its own deterministic PRNG
  stream so it never perturbs the ambient falling-leaf animation.

## 2026-06-04 — Shorter cactus arms & a finer mow

### Changed
- **Cactus arms are a bit shorter** — the arm's sideways reach drops from
  `width × 1.5` to `width × 1.2` and the upward tip from `h × 0.15` to `h × 0.10`,
  so arms read as stubbier and better proportioned.
- **Mowing swath halved** — `CUT_RADIUS` reduced 30 → 15 DIP, so a click/drag
  cuts a finer band of grass.

---

## 2026-06-04 — Bouncier, slower tumbleweeds

### Changed
- **Tumbleweeds roll a touch slower again** — `TUMBLEWEED_SPEED_MIN`/`MAX`
  trimmed 30–90 → 24–72 DIP/sec for an even calmer drift.

### Added
- **Subtle random tumbleweed bounce.** Each tumbleweed now hops gently as it
  rolls — a small parabolic arc (height is a fraction of its radius, capped so it
  never gets over the top) with a staggered, per-tumbleweed cadence so they don't
  hop in sync. The hop schedule/height are derived deterministically from the
  entity seed (no extra PRNG draws), so the spec-pinned spawn snapshots are
  unchanged. New tuning constants: `TUMBLEWEED_BOUNCE_GRAVITY`,
  `TUMBLEWEED_BOUNCE_HEIGHT_MIN/MAX_FRAC`, `TUMBLEWEED_BOUNCE_PERIOD_MIN/MAX`.

---

## 2026-06-04 — Arms only on tall cacti

### Changed
- **Only tall cacti grow arms now.** A new structural gate (`CACTUS_ARM_MIN_HEIGHT
  = 50.0`, in a 30–70 height range) forces short cacti to be armless, since an arm
  on a stubby cactus looked unbalanced. Tall cacti still get a left, right, or both
  arms via the existing probabilities. The gate is applied after the arm PRNG draws,
  so the generation stream stays deterministic.

---

## 2026-06-04 — Smoother cactus arms & cleaner cut cacti

### Changed
- **Cactus arms are now a single smooth curved stroke** instead of ~10 blocky
  tessellated segments. Both renderers build one path geometry (quadratic Bézier
  out to the elbow, then up to the tip) and stroke it with round caps/joins, so
  the arm curves like a grass blade. Native gained a shared round
  `ID2D1StrokeStyle`; Win2D reuses its existing round `_strokeStyle`.

### Fixed
- **Cut cacti no longer leave arms (or a green nub) on the ground.** A mowed
  cactus that settles at its cut floor now renders as a clean stump, and arms are
  only drawn while the cactus is near full height (`CACTUS_ARM_MIN_CUT_HEIGHT =
  0.85`), so a short/regrowing cactus doesn't show ground-hugging arms.

---

## 2026-06-04 — Slower tumbleweeds

### Changed
- **Tumbleweeds roll 25% slower** — `TUMBLEWEED_SPEED_MIN`/`MAX` lowered from
  40–120 to 30–90 DIP/sec across both implementations for a calmer drift across
  the desert strip. (Rotation speed is derived from `vx / size`, so the roll
  slows proportionally.)

---

## 2026-06-03 — Mowed grass leaves varied stubble

### Changed
- **Cut grass settles at a varied stubble height** — instead of every mowed
  blade collapsing to the same flat stump, each blade now keeps a small
  per-blade residual height (`cutFloor`, ~6–16% of full height) so the cut line
  reads with gentle, natural variation. The floor is drawn from an independent
  salted PRNG stream, so it does not perturb blade generation or any snapshot.
  Cut, regrowth, and persistence-restore all lerp to/from this floor. Mirrored
  in the Native and Win2D implementations. A blade already at its floor is no
  longer re-cuttable (the cut click is a no-op until it has regrown).

---

## 2026-06-03 — Denser grass, "None" critter means none, "All" option

### Added
- **"All" critter tray option** — the Critter submenu now offers **None /
  Sheep / Cat / All**. "All" spawns the curated mixed ground-critter set
  (sheep + cat + bunny + hedgehog) that was previously the hidden default.
  Added in both the Native and Win2D implementations; selection persists.

### Changed
- **Grass density +25%** — `DEFAULT_DENSITY` raised from 2.25 to 2.8125 across
  both the Native and Win2D implementations for a fuller, lusher field.
- **Thicker blades** — each blade is drawn ~1.5 px wider via a render-only
  `BLADE_THICKNESS_RENDER_BONUS` so the field reads denser on screen. Applied
  at the renderer (Native + Win2D) so the generation PRNG and blade snapshots
  are untouched.

### Fixed
- **"Critter → None" now spawns no animals** — previously `None` secretly
  generated the full mixed ground-critter set (sheep, cat, bunny, hedgehog),
  so selecting "None" still showed animals. `None` now produces zero ground
  critters; the gentle ambient flyers (butterflies, fireflies) remain. Sheep
  and Cat continue to spawn their single species. Fixed in both the Native
  and Win2D implementations.

---

## 2026-05-30 — Autumn, bird flybys, snow accumulation, hedgehog

### Added
- **Autumn scene** — fourth scene completes the four-seasons cycle (Grass /
  Desert / Winter / Autumn). Warm orange / red / yellow / gold blade palette.
  Falling leaves drift down with horizontal sway and rotation (six color
  variants matching the palette). Maple trees promote from slots like
  pines/birches do in Winter; each has a brown trunk and a soft filled canopy
  in one of four warm colors. 20% of maples are "bare" (late-autumn look,
  no canopy). Maples are cuttable. Autumn is intentionally the empty season —
  no critters, no rain, no snow, no birds, no butterflies/fireflies. Just
  leaves and warm trees.
- **Hedgehog** — fourth critter species (Grass scene only). Slow waddle,
  state machine with Walking / Snuffling / Idle / Sleeping / Curled.
  Nocturnal sleep bias (50% during day, 5% at night) — opposite of the
  other critters. Passive defense: click within 70 DIP triggers the curled
  state — the hedgehog tucks into a spiky ball and stays put for 3–5.5 sec,
  then resumes its original behavior. Does NOT flee. Count is probabilistic
  (~55% chance per session) so a hedgehog sighting feels earned.
- **Snow accumulation** — long Winter sessions slowly pile up a snow layer
  along the strip baseline at ~0.012 DIP/sec, capping at 30 DIP (so the
  layer fills out over ~40 minutes of continuous Winter). Renders between
  grass and pines/birches with a subtle vertical gradient and a gentle
  undulating top edge. Snowflakes that touch the snow top edge despawn
  visually "landing". Pines and birches subtly raise their base as the snow
  grows so they read as standing on the surface. Switching away from Winter
  melts the snow instantly; coming back resumes from the persisted depth.
  Persistence schema bumps v1 → v2 to round-trip the per-monitor `snowDepth`.
- **V-formation bird flybys** — Poisson-spawned daytime Grass-scene flocks of
  3–7 birds cross the strip far above critters and weather. Speed
  65–95 DIP/sec, altitude 78–96 DIP above grass top, mean spawn rate
  ~15/hour during the [07:00, 19:00) window. Flock formation is V or
  diagonal-line (50/50 per flock). Each bird has a slow wing flap with
  per-bird phase jitter so the flock never syncs, plus a gentle vertical
  drift bob. Alpha-fades in at spawn edge and out at despawn edge.

### Repository
- New `CHANGELOG.md`; `README.md` refreshed for the current feature set.

---

## 2026-05-30 — Critters, color, weather, polish

### Added
- **Bunny** — third critter species (Grass scene only). Brown body with white
  tail puff, tall ears, signature hop-only locomotion (no walk cycle), nose
  twitch and ear wiggle when idle. Skittish: click within 90 DIP startles
  into a boosted hop-away. Crepuscular sleep bias.
- **Cat coat color variants** — 6 coats: gray tabby, orange, black (yellow
  eyes), white, brown tabby, cream. One PRNG draw per cat picks the variant.
- **Light rain** — Grass-scene weather. Thin blue-grey diagonal raindrops
  with a short motion-blur tail; Poisson spawn timing. Soft fade when
  switching scenes.
- **Butterflies (day) + fireflies (night)** — ambient Grass-scene flyers
  above the strip. Butterflies have a fluttering wing animation and 5 color
  variants (monarch, swallowtail, cabbage, morpho, pink). Fireflies have
  a warm yellow blink with a soft halo and asynchronous phase offsets so
  they never sync up. Crossfade during dawn/dusk for a "magic hour" overlap.
- **Day-night ambient tint** — subtle 8-phase color overlay keyed to the
  local hour (peak alpha 36/255). No tray toggle; always on.

### Changed
- **Pine trees beefed up** — `PINE_WIDTH_MIN/MAX` 16–28 → **28–48 DIP** so the
  conifer silhouette reads at ~2:1 height:width instead of ~3:1. Birches felt
  solid; pines no longer feel like skinny sticks next to them.

### Repository
- `.copilot-scratch/` is now `.gitignore`-d so future agent runs don't
  accidentally commit test scratch outputs.

---

## 2026-05-29 → 2026-05-30 — Pets, persistence, foundations

### Added
- **Sheep** — first critter species (Grass scene only). State machine with
  Walking / Grazing / Idle / Sleeping / Hopping / Greeting. Time-of-day
  biased sleep probability (rare in morning, common at night). Click within
  64 DIP startles a sheep into a hop with a temporary speed boost.
  Cursor-curious idle sheep gently turn their heads toward the pointer.
  Sheep within 50 DIP greet each other (a brief frozen interaction with a
  head-bob and a `vx` flip on exit).
- **Cat** — second critter species. State machine with Walking / Idle /
  Sleeping / Pouncing. Click triggers a pounce *toward* the click (opposite
  of sheep's flee-away). Cats don't greet — they're aloof.
- **Per-pet names** — every critter draws a name from a species-specific pool
  (`SHEEP_NAME_POOL`, `CAT_NAME_POOL`, `BUNNY_NAME_POOL`, `HEDGEHOG_NAME_POOL`).
  Names render on hover.
- **Pet count override** — tray-menu sub-menu lets you pin a fixed count
  (1–6) per species; default is random within the species range.
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
- **Scene infrastructure** — tray menu lets you pick **Grass**, **Desert**,
  or **Winter**. Each scene swaps palette, content, and weather.
  - **Desert**: shorter blades, cacti (slot-bound, persistent across scene
    transitions), occasional rolling tumbleweeds.
  - **Winter**: -50% grass density, pines and birches, snowflake weather,
    no mushrooms.
- **Pine trees** — Winter-scene anchor. Stacked filled triangles with snow caps.
- **Birch tree variant** — second Winter tree style. Vertical white trunk
  with dark bark dashes, upward-angled branch fan, snow blobs at each branch
  tip and a soft snow cap on top.
- **Ambient gusts** — small, randomly scheduled wind puffs that propagate
  across the strip even when the cursor is nowhere near. Runs on its own
  PRNG stream so the existing cursor-gust path stays untouched.
- **Roaming-entity subsystem** — generalized scaffolding for non-grass
  entities (tumbleweeds, snowflakes, critters, raindrops, butterflies,
  fireflies, birds, leaves).
- **Static CRT linking** — Native release exe is now `/MT` so it ships as
  a single ~210 KB binary with no VC++ redistributable dependency.

---

## 2026-05-28 → 2026-05-29 — Flowers, mushrooms, chord physics

### Added
- **Flowers** — small colored heads at occasional blade tips, on an
  independent PRNG stream.
- **Mushrooms** — short ivory stems with capped tops, on a fourth PRNG
  stream. Cut leaves a slightly taller stump stub (so the nub reads as
  distinct from cut grass stubs).
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
