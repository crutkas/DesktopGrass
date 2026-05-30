<overview>
DesktopGrass is a "just for fun" Windows overlay (private repo `crutkas/DesktopGrass`, working tree `C:\Users\crutkas\source\DesktopGrass`, branch `main`) that paints procedural grass / flowers / mushrooms across the bottom of every monitor, click-through, on top of the taskbar. Two parallel impls — Native (C++/Direct2D) and Win2D (C#/Vortice) — share a single locked spec in `docs/architecture.md`. The user queued three asks: (1) random small ambient gusts ✅ shipped (`75c27bd`); (2) extend the tray to switch scenes ✅ shipped (`af3cae6` Native + `5f8c18b` Win2D); (3) fleet out a Desert scene (cacti + tumbleweeds) and a Winter scene (snowflakes + snow-tipped blades) ← Phase 3 in progress. Phase 3 plan: lock spec → ship cross-impl entity subsystem skeleton → fleet two parallel agents (Desert + Winter content) → validate + commit.
</overview>

<history>
1. **Phase 1 — Ambient gusts (§8.1)** — already shipped before this segment.

2. **Phase 2 — Scene infrastructure (§13)** — already shipped Native portion before compaction; this segment finished the Native renderer + tray refactor and the test, then fleeted the Win2D backport:
   - Edited `Renderer.h`/`Renderer.cpp` to make `brushes_[SCENE_COUNT][PALETTE_SIZE]`, pre-create all 18 grass brushes at device-init, and read `brushes_[sceneIdx][b.hue]` in `DrawGrass`.
   - Refactored `App.h`/`App.cpp` to add menu IDs `kMenuSceneGrass/Desert/Winter = 1010..1012`, build a Scene popup submenu via `CreatePopupMenu` + `AppendMenuW(MF_POPUP, sceneSubmenu_, L"Scene")`, dispatch `WM_COMMAND`, and add `SetScene` / `UpdateSceneMenuCheck` that broadcasts `sim_set_scene` to every window + calls `CheckMenuRadioItem`.
   - Created Native `scene_tests.cpp` with 8 cases (enum discriminants, default scene, state-only behavior, round-trip, palette ARGB alpha, Grass == PALETTE, Desert/Winter literal values). Registered in `.vcxproj`.
   - Built Native + tests: 65/65 pass, 69,474 assertions. Smoke PASS 3205 unique colors. Committed as `af3cae6`, pushed.
   - Fleeted a background `general-purpose` agent (id `win2d-scene-backport`) with a detailed prompt anchored on `d0d7fc4` + `af3cae6`, including exact code skeletons for `Constants.cs` (Scene enum + 2D `SCENE_PALETTES`), `Sim.cs` (CurrentScene field + SetScene + ComputeBladeStroke gets a `Scene` parameter), `GrassWindow.cs` (`_brushes[,]` 2D + DrawEntities forwarder + Dispose), `App.cs` (`Win32App.RequestSceneChange/ConsumePendingSceneChange` with `Interlocked.Exchange`, polled in the message loop), `TrayIcon.cs` (Scene ▸ submenu with sibling-uncheck radio behavior).
   - While agent worked, drafted Phase 3 design in session files (`phase3-design.md`).
   - Agent completed cleanly in 510s. Reviewed full diff. Re-ran build (clean), tests (65/65), and smoke (Native 3101 / Win2D 3126 PASS). Committed Win2D backport as `5f8c18b`, pushed.

3. **Phase 3 — Spec + entity subsystem skeleton:**
   - Wrote the spec patch in `docs/architecture.md`: §13.1 amendment (sim_set_scene becomes generator-aware), §13.2 entity subsystem (`EntityKind` enum + `Entity` struct + `sim.entities` vector + generic tick), §14 Desert (cacti slot-bound + tumbleweed roamers), §15 Winter (snowflakes continuous-emit + snow-tipped blade caps). Extended §11 constants table with 28 new entries (cactus dims/probs/salt/color, tumbleweed dims/count/speed/color/salt, snowflake rates/sway/lifetime/salt, snow-tip render constants). Committed as `b309eb6`, pushed.
   - Implemented the entity subsystem **skeleton** in both impls (no actual entity kinds — just plumbing): Native `Constants.h` got `EntityKind` enum + `MAX_ENTITIES_PER_MONITOR=64`; `Sim.h` got `Entity` struct + `sim.entities` vector + `sim.entitySeed` field + `sim_tick_entities` decl; `Sim.cpp` implements `sim_tick_entities` (generic position/rotation/age integration, safe on empty), amends `sim_set_scene` to call `entities.clear()`, reserves capacity in `sim_init`/`sim_regenerate`, calls `sim_tick_entities` from `sim_tick`. `Renderer.h/.cpp` add a `DrawEntities()` no-op called after `DrawGrass`.
   - Win2D mirrors all of the above: `Constants.cs` gets `MAX_ENTITIES_PER_MONITOR`; `Sim.cs` gets `EntityKind` enum + `Entity` struct + `Entities` list + `EntitySeed` + `SetScene` clearing entities + `ResetEntities` + `TickEntities`; `GrassWindow.cs` gets `DrawEntities(groundY)` no-op called after the blade draw loop.
   - Created `entity_skeleton_tests.cpp` (Native, 6 cases) and `EntitySkeletonTests.cs` (Win2D, 6 cases) and registered both in their project files.
   - Built Native (clean) + tests (71/71, was 65). Built Win2D (clean) + tests (71/71, was 65). Smoke PASS both impls (Native 3186 / Win2D 3108). Committed as `c7b836c`, pushed.

4. **Currently:** Phase 3 spec + skeleton both pushed. Ready to fleet two parallel agents (Desert §14 + Winter §15) — work has NOT started on either.
</history>

<work_done>
**Current `main` HEAD:** `c7b836c` "Entity subsystem skeleton (§13.2) — both impls" (pushed).

**Commits this segment (chronological):**
- `af3cae6` — Scene infrastructure (§13) — Native (pushed)
- `5f8c18b` — Scene infrastructure (§13) — Win2D backport (pushed; fleeted agent)
- `b309eb6` — Spec: §13.1 amendment + §13.2 entity subsystem + §14 Desert + §15 Winter (pushed; 285 line spec patch)
- `c7b836c` — Entity subsystem skeleton (§13.2) — both impls (pushed; +418 lines / -3)

**Files modified in this segment (all committed and pushed):**
- `docs/architecture.md` — +285 lines: §13.1 amendment, §13.2 entity subsystem, §14 Desert (cacti + tumbleweeds), §15 Winter (snowflakes + snow-tipped blades); §11 table got 28 new entries.
- `src/DesktopGrass.Native/src/{Constants.h, Sim.h, Sim.cpp, Renderer.h, Renderer.cpp, App.h, App.cpp}` — Phase 2 scene infra (Native) + Phase 3 entity skeleton.
- `src/DesktopGrass.Win2D/{Constants.cs, Sim.cs, GrassWindow.cs, App.cs, TrayIcon.cs}` — Phase 2 backport + Phase 3 entity skeleton.
- `tests/DesktopGrass.Native.Tests/src/{scene_tests.cpp, entity_skeleton_tests.cpp}` — added + registered in vcxproj.
- `tests/DesktopGrass.Win2D.Tests/SimTests/{SceneTests.cs, EntitySkeletonTests.cs}` — added + registered in csproj.
- `tests/DesktopGrass.Win2D.Tests/SimTests/CutTests.cs` — updated 2 calls to `ComputeBladeStroke(b, 110.0, Scene.Grass)` for the new signature.
- `tests/{Native,Win2D}.Tests/...` project files — registered new test files.

**Session artifact (not committed, in session-state/files/):**
- `phase3-design.md` — draft of entity subsystem architecture written before phase3 spec was locked. Now superseded by the committed `architecture.md` §13.1/§13.2/§14/§15 — but kept for reference.

**Tests:** Native 71/71 (69,496 assertions), Win2D 71/71. Smoke PASS both impls.

**SQL todos state:**
- `passive-gusts`, `scene-spec`, `scene-native`, `scene-win2d`, `phase3-spec` → done
- `phase3-entity-skeleton` → in_progress (should be marked done now — skeleton is shipped)
- `desert-fleet`, `winter-fleet` → pending (depend on `phase3-entity-skeleton` via `todo_deps`)

**What works:** All 142 tests across both impls pass; scene tray switches between Grass/Desert/Winter; entity subsystem plumbing in place but always empty until §14/§15 generators land.
</work_done>

<technical_details>

## Phase 2 Win2D backport agent — lessons + diff highlights
- Agent promoted `Scene` enum to top-level (namespace `DesktopGrass.Win2D`) instead of nesting in Constants — explicitly allowed in my prompt. All references use bare `Scene.Grass` etc.
- `Win32App.RequestSceneChange/ConsumePendingSceneChange` uses `Interlocked.Exchange` on a single `int` (-1 = no pending, 0..2 = pending Scene). Polled in `App.RunMessageLoop` between PeekMessage drain and render tick.
- TrayIcon ctor now takes initial `Scene`; menu has Scene ▸ submenu with sibling-uncheck radio behavior via shared `SelectScene(Scene)` helper that loops `sceneItems`. WinForms doesn't have first-class radio, so we use `Checked = true` on click + uncheck siblings.
- `_brushes` is now `ID2D1SolidColorBrush[,]` 2D; all 18 brushes pre-created in `CreateGraphics` and disposed in nested loop.
- **Sim.cs Stroke method signature changed:** `Sim.ComputeBladeStroke(in Blade b, double groundY, Scene scene)` — caller in `GrassWindow.cs:259` and 2 callers in `CutTests.cs` updated. ARGB lookup: `Constants.SCENE_PALETTES[(int)scene, b.Hue]`.

## Phase 3 entity subsystem design (committed in §13.2)
- **`EntityKind` discriminants spec-locked:** `None=0, Tumbleweed=1, Snowflake=2`. Cross-impl integer-comparable.
- **`Entity` struct fields (uniform across kinds):** `kind`, `x`, `y`, `vx`, `vy`, `size` (radius DIP), `rotation`, `rotationSpeed`, `age`, `lifetime` (≤0 = infinite respawn), `seed` (uint32 per-entity render seed).
- **Storage:** Native `std::vector<Entity>`; Win2D `List<Entity>`; both **pre-reserve to `MAX_ENTITIES_PER_MONITOR = 64`** at construction (Native: `entities.reserve()` in `sim_init`/`sim_regenerate`; Win2D: `new List<Entity>(64)` field initializer + `Capacity >= 64` after `ResetEntities`). Cap prevents snowflake emitter from unbounded growth.
- **§13.1 amendment:** `sim_set_scene` / `SetScene` is no longer state-only. It now calls `entities.clear()` and (when §14/§15 generators exist) dispatches per-scene generators. The §12 first-blade snapshot is still preserved because blade generation is scene-agnostic.
- **`sim.entitySeed` field** — plumbed in skeleton for use by §14/§15 generator PRNGs salted as `entitySeed XOR {CACTUS,TUMBLEWEED,SNOWFLAKE}_PRNG_SALT`. Set in `sim_init` to the original `seed` parameter.
- **`sim_tick_entities(sim, dt)` / `TickEntities(dt)`** — generic forward pass: `x += vx*dt; y += vy*dt; rotation += rotationSpeed*dt; age += dt`. Called from `sim_tick` after blade physics. Per-kind branches (tumbleweed respawn, snowflake sway/cull) deferred to §14/§15.
- **`Renderer::DrawEntities()` / `GrassWindow.DrawEntities(groundY)`** — no-op stubs called after `DrawGrass`. Per-kind rendering deferred.

## Phase 3 §14 Desert spec (committed, not implemented)
- **Cacti:** slot-bound blade variant alongside `isFlower`/`isMushroom`. Promoted with `CACTUS_PROBABILITY = 0.005` from `CACTUS_PRNG_SALT = 0xCAC75CAC75CAC75Cull` stream. 3 silhouette types (column / one-arm / saguaro). Cuttable + regrowable using existing grass cut/regrow model. Color `CACTUS_COLOR = 0xFF2D7A2D`.
- **Tumbleweeds:** roaming entities, `floor(monitorWidth/1920 * TUMBLEWEED_COUNT_PER_1920DIP=4)` per monitor. Generated by `tumbleweedPrng = Prng(entitySeed XOR TUMBLEWEED_PRNG_SALT = 0x7B0117CA7B0117CA)`. Per-fire draw order: `size, x, y_offset, speed, signDir, rotation, seed` (7 draws). Respawn at opposite edge on off-screen. Color `0xFF8A6A3D`. Render: 5 concentric arc strokes at rotation offsets.
- **Critical note for §14 agent:** switching scenes back to Grass must **restore** the original `isFlower`/`isMushroom` slot tags. Spec suggests two options: parallel `originalVariants[]` array OR re-run flower+mushroom variant streams (deterministic from seed). Latter is cleaner since streams are pure.

## Phase 3 §15 Winter spec (committed, not implemented)
- **Snowflakes:** continuous emission with Poisson-distributed inter-arrival times. Rate `SNOWFLAKE_EMIT_RATE_PER_1920DIP = 8.0` flakes/sec scaled by monitor width. `nextSnowflakeSpawnTime` advances by `-ln(1 - u) / λ` per fire. Per-fire draw order (7 draws): `size, x, fallSpeed, rotation, rotationSpeed, seed, intervalDraw`. PRNG salt `SNOWFLAKE_PRNG_SALT = 0xC0FFEE1CECAFEBAB`. Capped at `MAX_ENTITIES_PER_MONITOR` (missed spawns are "lost"). Lifetime = `(groundY + size) / fallSpeed + 2.0` sec padding.
- **Snowflake sway:** `vx = SWAY_AMPLITUDE * SWAY_FREQUENCY * 2π * cos(age * 2π * SWAY_FREQUENCY + seed/UINT_MAX * 2π)` → ~37.7 DIP/sec peak.
- **Snow-tipped blades:** render-only branch in DrawGrass — when `currentScene == Winter && !isMushroom && !isCactus && cutHeight >= CUT_STUMP_THRESHOLD`, draw a filled circle of radius `thickness * SNOW_TIP_RADIUS_FACTOR (1.25)` in white at the tip. Scales with thickness, disappears on cut.

## Build / test / smoke commands (all verified this segment)
```pwsh
# Native — stop running exe first (locks itself)
Get-Process DesktopGrass.Native -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
$vsBat = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat'
$out = & $env:ComSpec /c "call `"$vsBat`" -arch=x64 -host_arch=x64 >nul && cd /d C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native && msbuild DesktopGrass.Native.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m 2>&1"
$out | Select-Object -Last 30
# Same pattern for tests/DesktopGrass.Native.Tests
& 'C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\out\Release\DesktopGrass.Native.Tests.exe' --reporter compact

# Win2D
cd C:\Users\crutkas\source\DesktopGrass
Get-Process DesktopGrass.Win2D -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
dotnet build src\DesktopGrass.Win2D -c Release --nologo
dotnet test  tests\DesktopGrass.Win2D.Tests -c Release --nologo --verbosity minimal

# Smoke
pwsh -NoProfile -File 'tests\smoke\Run-SmokeTests.ps1' -Target All -Configuration Release
```

## Persistent quirks (carry forward)
- **PS gotcha:** `& $env:ComSpec /c "... | Select-Object ..."` fails — Select-Object is PowerShell. Capture to `$out` then pipe in PS.
- **Native exe self-locks** on rebuild — ALWAYS `Stop-Process -Id` first. `Stop-Process` is restricted to `-Id <pid>` only (no `-Name`).
- **Win2D test csproj has `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>`** — every new test file MUST be added with explicit `<Compile Include="...">`. Pre-existing `FlowerTests.cs` is NOT registered (pre-existing bug, out of scope).
- **Win2D tests file-link `Constants.cs` / `Sim.cs` from the app project**, so tests compile WITH the source — `internal` visibility is naturally shared. New Sim members exposed via `public` (e.g., `CurrentScene`, `Entities`, `EntitySeed`, `TickEntities`, `ResetEntities`).
- **Co-authored-by trailer required on every commit:** `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`
- `Sim.AmbientPrng.State` is a `ulong` accessible from tests (made public in Phase 1). New `Sim.Entities` and `Sim.EntitySeed` are also public.

## Fleet pattern (proven 4× now: regrowth, flowers, mushrooms, ambient, scene-infra)
1. Lock spec patch in `docs/architecture.md` FIRST, commit + push standalone (anchor SHA).
2. Implement Native (reference) yourself, commit + push (another anchor SHA).
3. Fleet ONE general-purpose agent (background mode is fine) for Win2D backport with: anchor SHAs, full design, naming reminders (PascalCase Win2D, SCREAMING_SNAKE constants, public where tests need it), files to edit, exact code skeletons, test additions, build + test commands, validation gates. **DO NOT commit/push** — orchestrator handles single commit.
4. Validate + commit + push.

## Open questions (not blocking)
- The §14 spec note about restoring original flower/mushroom tags on Scene→Grass switch suggests two impls — agent should pick "re-run variant streams" for cleanliness.
- Ambient-gust-affects-roamer behavior is documented as "optional Phase 3.1" — agents may skip it.
- The phase3-design.md session artifact mentions `0xC0FFEE1CECAFEBABE` as snowflake salt but the committed spec uses `0xC0FFEE1CECAFEBAB` (16 hex chars exactly). Use the committed value.
</technical_details>

<important_files>

- `C:\Users\crutkas\source\DesktopGrass\docs\architecture.md`
  - **Single source of truth.** Now contains §8.1 ambient gusts, §13 scene infra, §13.1 amendment, §13.2 entity subsystem, §14 Desert, §15 Winter. §11 constants table has 28 new Phase 3 entries.
  - Phase 3 agents will reference §13.1/§13.2/§14 (Desert) or §13.1/§13.2/§15 (Winter).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Constants.h`
  - All Native constants. Phase 3 added `EntityKind` enum + `MAX_ENTITIES_PER_MONITOR = 64` right after `SCENE_PALETTES`. Phase 3 agents will add `CACTUS_*`, `TUMBLEWEED_*`, `SNOWFLAKE_*`, `SNOW_TIP_*` constants + the 3 PRNG salts here.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.h`
  - Sim struct + free functions. Phase 3 added `Entity` struct, `std::vector<Entity> entities`, `uint64_t entitySeed`, `void sim_tick_entities(Sim&, double dt)`. Phase 3 agents will add `void generate_tumbleweeds(Sim&)` / `void respawn_tumbleweed(...)` (Desert) or `void emit_snowflakes(Sim&, double dt)` (Winter).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.cpp`
  - Phase 3: `sim_set_scene` now calls `sim.entities.clear()` and has comment-blocked switch stubs for per-kind generator dispatch. `sim_tick_entities` implements generic integration loop. `sim_init`/`sim_regenerate` reserve capacity + set `entitySeed`. `sim_tick` calls `sim_tick_entities` after blade physics.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Renderer.h` + `Renderer.cpp`
  - Phase 2: `brushes_[SCENE_COUNT][PALETTE_SIZE]` + scene-aware DrawGrass. Phase 3: added `void DrawEntities()` no-op called from `RenderFrame` after `DrawGrass()`. Phase 3 agents will fill in per-kind render branches here.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\App.h` + `App.cpp`
  - Phase 2: Scene submenu with `kMenuSceneGrass/Desert/Winter = 1010..1012`, `SetScene` broadcasts to all windows, `UpdateSceneMenuCheck` uses `CheckMenuRadioItem`.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Constants.cs`
  - Win2D constants. Phase 2: `Scene` enum top-level, `SCENE_COUNT`, `DESERT_PALETTE`, `WINTER_PALETTE`, `SCENE_PALETTES[,]`. Phase 3: `MAX_ENTITIES_PER_MONITOR = 64`. Phase 3 agents will add Desert/Winter content constants here.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Sim.cs`
  - Phase 2: `CurrentScene`, `SetScene`, `ComputeBladeStroke(...Scene scene)`. Phase 3: `EntityKind` enum, `Entity` struct, `List<Entity> Entities` (pre-reserved capacity 64), `ulong EntitySeed`, `SetScene` clears entities, `ResetEntities(seed)`, `TickEntities(dt)`. `Tick` calls `TickEntities` after blade loop.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\GrassWindow.cs`
  - Phase 2: `_brushes[,]` 2D + scene-aware draw + SetScene forwarder. Phase 3: `private void DrawEntities(float groundY)` no-op called after blade loop.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\App.cs` + `TrayIcon.cs`
  - Phase 2: `Win32App.RequestSceneChange/ConsumePendingSceneChange` (Interlocked), App polls between PeekMessage and render. Tray has Scene ▸ submenu with sibling-uncheck radio.

- `tests/DesktopGrass.Native.Tests/src/entity_skeleton_tests.cpp` + `tests/DesktopGrass.Win2D.Tests/SimTests/EntitySkeletonTests.cs`
  - **NEW this segment**, 6 tests each. Templates the Phase 3 agents should extend (init seeding via `sim_init` / `BuildSim()`, populated-entity assertions, tick wiring check).

- `tests/DesktopGrass.Native.Tests/DesktopGrass.Native.Tests.vcxproj`
  - Tests registered: `prng_tests, blade_gen_tests, sway_tests, gust_tests, cut_tests, regrowth_tests, flower_tests, mushroom_tests, ambient_gust_tests, scene_tests, entity_skeleton_tests`. Phase 3 agents must register `desert_tests.cpp` and `winter_tests.cpp`.

- `tests/DesktopGrass.Win2D.Tests/DesktopGrass.Win2D.Tests.csproj`
  - Has `EnableDefaultCompileItems=false`. Registered tests: BladeGen, PRNG, Sway, Gust, Cut, Regrowth, AmbientGust, Scene, EntitySkeleton, InternalsVisible. Phase 3 agents must register `DesertTests.cs` and `WinterTests.cs`.

- `tests/DesktopGrass.Native.Tests/src/snapshot_data.h` — `desktopgrass::test::CANONICAL_BLADE_COUNT = 321`. Used by tests for stream independence checks.

- `C:/Users/crutkas/.copilot/session-state/e286b6d3-8e11-4aa2-b2d7-87ceb1f5de22/files/phase3-design.md`
  - Session-only draft (not committed). Pre-spec brainstorm — now superseded by committed §13.1/§13.2/§14/§15. Kept for reference; ignore where it conflicts with the committed spec.

</important_files>

<next_steps>

**Immediate next steps when work resumes (Phase 3 content fleet):**

1. **Mark `phase3-entity-skeleton` done** in the SQL todos (it's currently still in_progress; the work shipped as `c7b836c`):
   ```sql
   UPDATE todos SET status = 'done', updated_at = CURRENT_TIMESTAMP WHERE id = 'phase3-entity-skeleton';
   UPDATE todos SET status = 'in_progress', updated_at = CURRENT_TIMESTAMP WHERE id IN ('desert-fleet', 'winter-fleet');
   ```

2. **Fleet TWO parallel `general-purpose` agents in background mode** (independent — no shared file conflicts beyond the spec they both reference). Each agent owns one scene end-to-end in BOTH impls.

3. **Agent #A — Desert (§14)** prompt should include:
   - Anchor SHAs: spec `b309eb6`, entity skeleton `c7b836c`. Read `docs/architecture.md` §13.1, §13.2, §14, §11 (Desert rows).
   - Scope: cacti (slot-bound blade variant, isCactus tag, 3 types, cuttable+regrowable) + tumbleweeds (roaming entities, respawn on offscreen edge).
   - **Native files:** `Constants.h` (add CACTUS_* + TUMBLEWEED_* constants from §11), `Sim.h` (add `bool isCactus`, `uint8_t cactusType`, `double cactusHeight/cactusWidth`, `int8_t cactusArmSide` to `Blade`; add `generate_cacti` + `generate_tumbleweeds` + `respawn_tumbleweed` decls), `Sim.cpp` (implement generators + extend `sim_set_scene` Desert branch + extend `sim_tick_entities` Tumbleweed branch + **handle Scene→Grass restoration of flower/mushroom tags by re-running variant streams**), `Renderer.cpp` (DrawGrass cactus branch + DrawEntities Tumbleweed branch + new cactus brush).
   - **Win2D files:** mirror all of the above in `Constants.cs`, `Sim.cs` (Blade gets IsCactus/CactusType/CactusHeight/CactusWidth/CactusArmSide), `GrassWindow.cs` (cactus rendering + tumbleweed rendering + cactus brush).
   - **Tests:** `desert_tests.cpp` / `DesertTests.cs` with first-tumbleweed snapshot (spec-derived via side `Prng(CANONICAL_TEST_SEED XOR TUMBLEWEED_PRNG_SALT)`), cactus count for canonical seed + 1920 width, scene round-trip preserves blades, off-screen respawn behavior.
   - Validation: tests pass at 75+/75+, build clean, smoke PASS, register new test files in vcxproj/csproj.
   - **DO NOT commit/push** — orchestrator handles single feature commit.

4. **Agent #B — Winter (§15)** prompt should include:
   - Same anchors: `b309eb6` + `c7b836c`. Read §13.1, §13.2, §15, §11 (Winter rows).
   - Scope: snowflakes (continuous-emit roaming entities, Poisson inter-arrival, capped at MAX_ENTITIES_PER_MONITOR, sin-wave sway, lifetime-based culling) + snow-tipped blades (pure render branch).
   - **Native files:** `Constants.h` (SNOWFLAKE_* + SNOW_TIP_* constants), `Sim.h` (declare `reset_snowflake_emitter` + `emit_snowflakes`, add `double nextSnowflakeSpawnTime` + `Prng snowflakePrng` to Sim), `Sim.cpp` (extend `sim_set_scene` Winter branch + extend `sim_tick_entities` Snowflake branch for sway + culling + emit + helpers), `Renderer.cpp` (DrawEntities Snowflake branch + DrawGrass snow-tip cap branch when `currentScene == Winter`).
   - **Win2D files:** mirror in `Constants.cs`, `Sim.cs` (NextSnowflakeSpawnTime + SnowflakePrng fields), `GrassWindow.cs` (snowflake rendering + snow-tip cap in DrawBlade).
   - **Tests:** `winter_tests.cpp` / `WinterTests.cs` with first-snowflake snapshot after 1 tick at dt=0.5s (spec-derived via side Prng), MAX_ENTITIES cap stress test (60s sim time), Scene→Grass clears entities.
   - **Caveat:** Snow-tip render branch needs `currentScene == Winter` check inside DrawBlade (Native) / DrawBlade (Win2D).
   - Validation gates and "do not commit" same as Agent #A.

5. **While both agents run** (background mode), do not poll — completion notifications will fire. Independent prep work: nothing actionable since both agents own the same source files; just wait.

6. **On agent return for each:** validate (build clean, tests pass, smoke PASS), single feature commit + push (`Desert scene content (§14) — cacti + tumbleweeds, both impls` / `Winter scene content (§15) — snowflakes + snow-tipped blades, both impls`).

7. **After both ship:** relaunch native so user can see all 3 scenes live. Optional Phase 3.1: ambient gust → roamer impulse (deferred per spec).

**Blockers / open items:**
- None — spec is locked, skeleton is shipped, agents have clear scope.
- Watch for the **flower/mushroom tag restoration** detail in §14 — agent needs to know that switching back to Grass must rebuild variant tags from the (deterministic) flower+mushroom streams.

</next_steps>