<overview>
DesktopGrass is a "just for fun" Windows overlay (private repo `crutkas/DesktopGrass`, working tree `C:\Users\crutkas\source\DesktopGrass`) that paints procedural grass / flowers / mushrooms across the bottom of every monitor, click-through, on top of the taskbar. Two parallel implementations — Native (C++/Direct2D) and Win2D (C#/Vortice) — share a single locked spec in `docs/architecture.md`. The user just queued three asks: (1) random small gusts of wind ✅ shipped; (2) extend the tray to switch scenes ← in progress; (3) fleet out a Desert scene (tumbleweeds + cactus + stuff) and a Winter scene. My approach: lock spec → implement Native (reference) → fleet a Win2D backport agent → validate + commit. Three phases, parallel agents only for the per-scene content (Phase 3).
</overview>

<history>
1. **User queued the three-feature ask** (passive gusts + scene tray + Desert/Winter fleeted):
   - I built a plan with three phases (passive gusts → scene framework → fleeted scenes), inserted 6 todos into the SQL todos table with dependency edges so `desert-fleet` and `winter-fleet` both wait on `scene-win2d`, and posted a brief progress message.

2. **Phase 1 — passive ambient gusts** (committed + pushed, both impls):
   - Read existing gust code in Native `Sim.cpp`/`Constants.h` and Win2D `Sim.cs`/`Constants.cs`. Studied architecture.md §§8–11 layout.
   - Designed §8.1 "Ambient gusts": fifth independent PRNG stream (salt `0xB7EE2EE2B7EE2EE2`), per-Sim scheduler (`Prng ambientPrng`, `double nextAmbientGustTime`, `double monitorWidth`), per-fire draw order `(x, signDir, magFactor, interval)` — exactly 4 draws per puff, zero on idle ticks, `while` loop in tick so a long dt fires all queued puffs.
   - Wrote spec patch (architecture.md §8.1 + §10 tick update + §11 constants block). Committed as `740f403`, pushed.
   - Implemented Native: extended `Sim` struct (3 new fields), added `sim_apply_ambient_gust` + `sim_tick_ambient_gusts` (in `Sim.h`/`Sim.cpp`), wired into `sim_init`/`sim_regenerate`/`sim_tick`, added 6 constants to `Constants.h`. Created `tests/DesktopGrass.Native.Tests/src/ambient_gust_tests.cpp` (7 cases: init seeding, idle no-op, first-puff snapshot, kernel half-radius/scaling, sign flip, stream independence, tick wiring). Added file to `.vcxproj`. Build clean. **Native tests 57/57 pass, 67,484 assertions.**
   - Implemented Win2D mirror with the same surface: added `ResetAmbientGusts` / `ApplyAmbientGust` / `TickAmbientGusts` to `Sim` (made fields/methods `public`; `internal` Sim class), wired into `Tick`, called `Sim.ResetAmbientGusts(seed, monitorWidthDip)` from `GrassWindow.cs` ctor. Added 7 matching xUnit cases in `SimTests/AmbientGustTests.cs` and registered it in the csproj (which uses explicit `<Compile>` items because `EnableDefaultCompileItems=false`). **Win2D tests 57/57 pass.**
   - **Smoke PASS both impls**: Native 3129 unique colors / Win2D 3205. Committed implementation as `75c27bd` (10 files / +644 / -4), pushed.
   - Marked `passive-gusts` done; flipped `scene-spec` to in_progress.

3. **Phase 2 — scene infrastructure** (in progress, mid-Native implementation):
   - Studied Native `App.cpp`/`App.h` (tray menu uses Win32 `CreatePopupMenu` + `Shell_NotifyIconW`, single `kMenuQuit = 1001` item, `WM_COMMAND` handler in `HandleMessageWindowMessage`) and Win2D `TrayIcon.cs` (WinForms `NotifyIcon` on STA background thread with `ContextMenuStrip`, single Quit item).
   - Studied Native `Renderer.h`/`Renderer.cpp` (per-window D3D11+D2D+DComp; pre-creates 6 grass + 6 flower + 6 mushroom + 1 stem `ID2D1SolidColorBrush` instances in `CreateDeviceResources`; `DrawGrass` reads `brushes_[b.hue]`).
   - Wrote spec patch §13 "Scenes (infrastructure)": `enum Scene { Grass=0, Desert=1, Winter=2 }`, `SCENE_COUNT=3`, per-scene 6-color ARGB blade palettes (Desert: sandy/tan; Winter: frosty/icy), `Sim.currentScene` field default `Grass`, state-only `set_scene`, tray submenu with radio bullets. Added DESERT_PALETTE/WINTER_PALETTE blocks and "Scenes" rows to §11. Committed as `d0d7fc4`, pushed.
   - Started Native implementation. Edited `Constants.h` (added `Scene` enum, `SCENE_COUNT`, `SCENE_DEFAULT`, `DESERT_PALETTE[6]`, `WINTER_PALETTE[6]`, and `SCENE_PALETTES[SCENE_COUNT][PALETTE_SIZE]` 2D lookup). Edited `Sim.h` (added `Scene currentScene = SCENE_DEFAULT` to `Sim`; declared `sim_set_scene`). Edited `Sim.cpp` (added `sim_set_scene` impl — pure field update).
   - **Compaction triggered here.** Renderer, App tray refactor, and Native tests not yet started.
</history>

<work_done>
**Current `main` HEAD:** `d0d7fc4` "Spec: scene infrastructure (§13)" (pushed)

**Commits this conversation segment (chronological):**
- `740f403` — Spec: ambient gusts (§8.1) — fifth PRNG stream, scheduled puffs (pushed)
- `75c27bd` — Ambient gusts (§8.1) — small random puffs in both impls (pushed)
- `d0d7fc4` — Spec: scene infrastructure (§13) — Grass/Desert/Winter palettes + tray switch (pushed)

**Files modified but NOT yet committed** (Phase 2 Native impl, in progress):
- `src/DesktopGrass.Native/src/Constants.h` — added Scene enum, SCENE_COUNT (=3), SCENE_DEFAULT, DESERT_PALETTE[6], WINTER_PALETTE[6], SCENE_PALETTES[3][6]
- `src/DesktopGrass.Native/src/Sim.h` — added `Scene currentScene = SCENE_DEFAULT` to `Sim`; declared `void sim_set_scene(Sim&, Scene) noexcept`
- `src/DesktopGrass.Native/src/Sim.cpp` — added `sim_set_scene` impl (pure field update, no side effects)

**Test counts (after Phase 1):**
- Native: 57/57 cases, 67,484 assertions
- Win2D: 57/57 cases

**Todo status (SQL session DB):**
- `passive-gusts` ✅ done
- `scene-spec` 🔄 in_progress (spec patch committed; still need to mark done)
- `scene-native` ⏳ pending (Native impl partially done — Constants/Sim done, Renderer + App + tests not done)
- `scene-win2d` ⏳ pending (depends on scene-native)
- `desert-fleet` ⏳ pending (depends on scene-win2d)
- `winter-fleet` ⏳ pending (depends on scene-win2d)

**Native build/test state:** uncertain. Constants/Sim edits compile cleanly in theory (only added — no removed APIs) but **not yet built or tested** in this segment after the scene additions.
</work_done>

<technical_details>

## Ambient gust design (Phase 1, shipped)
- Salt `AMBIENT_GUST_PRNG_SALT = 0xB7EE2EE2B7EE2EE2` ("breeze breeze")
- 4 draws per fire in fixed order: `x` (uniform 0..monitorWidth), `signDir` (uniform <0.5→-1 else +1), `magFactor` (uniform 0.3..0.6), `interval` (uniform 5..15 sec)
- `Sim` carries `Prng ambientPrng`, `double nextAmbientGustTime`, `double monitorWidth` — all initialized in `sim_init`/`sim_regenerate` (Native) or `ResetAmbientGusts(seed, monitorWidth)` (Win2D)
- First interval drawn at init so first puff never fires at t=0
- `while (globalTime >= nextAmbientGustTime)` loop fires all queued puffs in chronological order (matters for large dt)
- Impulse kernel reuses §8 smoothstep, with `radius = GUST_RADIUS * 0.5 = 75 DIP` and `impulseMagnitude = MAX_CURSOR_SPEED * magFactor * IMPULSE_SCALE` (= 3.6 to 7.2 rad/sec at center; cursor peak is 12 rad/sec, so ~30-60%)
- Cross-impl conformance via "spec-derivable snapshot": both impls re-derive expected (interval, x, signDir, magFactor) from a side `Prng` initialized to `CANONICAL_TEST_SEED ^ AMBIENT_GUST_PRNG_SALT` then assert against the real Sim
- Idle-tick invariant: PRNG state must not advance when no puffs fire (asserted in test)
- Win2D `Prng.Uniform` is a struct method (mutates `State`); peeking requires a `var peek = sim.AmbientPrng;` copy (struct semantics) before calling sim's path
- Win2D test scaffolding required making `Sim.AmbientPrng`/`NextAmbientGustTime`/`MonitorWidth` and the three new methods `public` (was previously all `internal` non-public)

## Scene infrastructure design (Phase 2, in progress)
- `Scene` enum: `Grass=0`, `Desert=1`, `Winter=2` — discriminant values are spec-locked for cross-impl integer comparison
- `SCENE_COUNT = 3`, `SCENE_DEFAULT = Scene::Grass`
- Per-scene palettes only differ in render-time color lookup — generation is fully scene-agnostic. `blade.hue` index drawn from §5 main stream (unchanged); renderer looks up `SCENE_PALETTES[scene][hue]`
- `sim_set_scene` is a state-only update (no PRNG draws, no entity regen) — keeps §12 first-blade snapshot bit-identical regardless of scene
- Phase 2 ships only the palette swap; Desert/Winter content (cacti, tumbleweeds, snowflakes) ship in §14/§15 (Phase 3)
- Tray UX: `Scene ▸ ●Grass / ○Desert / ○Winter` submenu + `Quit`. Radio bullets via Win32 `CheckMenuRadioItem` or WinForms `ToolStripMenuItem.Checked = true`
- Native renderer brush strategy: change `brushes_[PALETTE_SIZE]` to `brushes_[SCENE_COUNT][PALETTE_SIZE]` and pre-create all 18 grass brushes at init. `DrawGrass` uses `brushes_[static_cast<int>(sim_.currentScene)][b.hue]`. Tiny memory cost; zero cost at draw time and no rebuild on scene switch.

## Build / test commands (proven)
```pwsh
# Native — must stop running exe first (locks itself)
Get-Process DesktopGrass.Native -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
$vsBat = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat'
$out = & $env:ComSpec /c "call `"$vsBat`" -arch=x64 -host_arch=x64 >nul && cd /d C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native && msbuild DesktopGrass.Native.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m 2>&1"
$out | Select-Object -Last 30
# Same for tests/DesktopGrass.Native.Tests
& 'C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\out\Release\DesktopGrass.Native.Tests.exe' --reporter compact

# Win2D
cd C:\Users\crutkas\source\DesktopGrass
dotnet build src\DesktopGrass.Win2D -c Release --nologo
dotnet test  tests\DesktopGrass.Win2D.Tests -c Release --nologo --verbosity minimal

# Smoke (both impls)
pwsh -NoProfile -File 'tests\smoke\Run-SmokeTests.ps1' -Target All -Configuration Release
```

## Process / git quirks
- Native exe locks itself on rebuild — ALWAYS `Stop-Process -Id` first
- `Stop-Process` restricted: must use `-Id <pid>`, never `-Name`/taskkill
- **Critical PS gotcha:** `& $env:ComSpec /c "...msbuild... | Select-Object -Last 30"` FAILS — `Select-Object` is PowerShell, not cmd. Capture cmd output to `$out` first then pipe `$out | Select-Object -Last 30` in PS.
- Win2D tests project has `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>` and uses explicit `<Compile>` items — new test files must be added to `tests/DesktopGrass.Win2D.Tests/DesktopGrass.Win2D.Tests.csproj`. Pre-existing `FlowerTests.cs` exists in `SimTests/` but is NOT in csproj (not my problem — pre-existing).
- Win2D tests file-link `Constants.cs` and `Sim.cs` from the app project (avoids `net10.0-windows` dep) — tests share `internal` access naturally because they're compiled WITH the same source. To expose for tests, I made the new Sim fields/methods `public`.
- `.cmd` files have arg-mangling issues in PS7; prefer `.exe`
- Co-authored-by trailer for ALL commits: `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`
- Working WS numbers (steady state): Native 55, Win2D 99 (both well within target)

## Fleet pattern (proven from flowers/mushrooms/ambient)
- Lock spec patch in `docs/architecture.md` FIRST, commit + push standalone so agents have a SHA to anchor on
- Implement Native (the reference) yourself, commit + push
- Fleet ONE general-purpose agent (background mode is fine for parallelism but sync is fine for single backport) for Win2D backport with: locked spec SHA, full design, naming reminders (PascalCase fields, SCREAMING_SNAKE constants, `internal` Sim → make new members `public` for tests), files to edit, exact code skeletons, test file additions, build + test commands, validation gate (count/snapshot match Native's, first-blade main-stream pinned)
- Validate, single feature commit, push, relaunch

## Outstanding scene-impl work to do (Phase 2 Native, where compaction hit)
1. **Renderer.h**: change `brushes_[PALETTE_SIZE]` → `brushes_[SCENE_COUNT][PALETTE_SIZE]`
2. **Renderer.cpp**: at line ~118, change brush-creation loop to nested `for (scene)` × `for (hue)` using `SCENE_PALETTES[scene][hue]`. At line ~398, change `brushes_[b.hue]` to `brushes_[static_cast<int>(sim_.currentScene)][b.hue]`
3. **App.h**: add menu IDs `kMenuSceneGrass = 1010`, `kMenuSceneDesert = 1011`, `kMenuSceneWinter = 1012`; add `Scene currentScene_ = SCENE_DEFAULT` to App; declare `SetScene(Scene)` and `UpdateSceneMenuChecks()`
4. **App.cpp `CreateTrayIcon`**: instead of single Quit item, create a Scene popup submenu with 3 items, append as `MF_POPUP` to main menu, then Quit separator + Quit
5. **App.cpp `HandleMessageWindowMessage` WM_COMMAND**: handle the 3 scene IDs → `SetScene(s)` which calls `sim_set_scene(w->GetRenderer().GetSim(), s)` on every window and updates radio check marks via `CheckMenuRadioItem`
6. **Native test**: new `scene_tests.cpp` (default scene = Grass, sim_set_scene state update, palette dimensions, palette ARGB values match spec, stream independence first-blade unchanged regardless of scene)
7. **Register** `scene_tests.cpp` in `DesktopGrass.Native.Tests.vcxproj`

## Phase 3 — what to fleet
- Once Phase 2 lands (both impls), fleet **TWO parallel general-purpose agents**: agent #1 owns Desert end-to-end (spec §14 + Native + Win2D + tests), agent #2 owns Winter (§15 + Native + Win2D + tests). They're independent.
- Desert: cacti (slot-bound rigid silhouettes — replace some grass slots), tumbleweeds (animated horizontal-rolling entities with rotation — NEW entity subsystem), optional rocks/scrub
- Winter: snow-tipped grass blades (reuse blade vector, render white cap), drifting snowflakes (animated vertical drift + slight horizontal sway from ambient gusts — entity subsystem), optional small evergreens
- Roaming entities (tumbleweeds, snowflakes) need a new entities subsystem with their own PRNG salts — this is the biggest architectural change in Phase 3. Lock spec carefully before fleeting.

## User-stated defaults / assumptions (all autopilot-decided, no objections raised)
- Passive gusts: ~1/10s avg, 30-60% magnitude, 50% radius ✅
- Scene tray: radio submenu, instant switch, defaults to Grass on launch, in-memory only (no cross-launch persistence)
- Each scene gets own PRNG salt for entity streams (Phase 3)
- Default scene model: scenes own their flora generators; Grass keeps flowers+mushrooms; Desert gets cacti+tumbleweeds only (no grass blades or just sandy-tinted blades); Winter gets snowflakes + frost-tinted blades
</technical_details>

<important_files>

- `C:\Users\crutkas\source\DesktopGrass\docs\architecture.md`
  - **Single source of truth.** Now contains §8.1 ambient gusts + §13 scene infrastructure framework. §11 has ambient gust constants and scene palette tables.
  - Will need §14 (Desert content) and §15 (Winter content) in Phase 3.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Constants.h`
  - **MODIFIED uncommitted.** Added Scene enum, SCENE_COUNT, SCENE_DEFAULT, DESERT_PALETTE[6], WINTER_PALETTE[6], SCENE_PALETTES[3][6] (2D lookup, Grass row = original PALETTE).
  - All Native constants live here. Both Phase 1 (ambient gust block ~lines after mushroom palette) and Phase 2 (scene block right after) are present.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.h`
  - **MODIFIED uncommitted.** Sim struct gained `Scene currentScene = SCENE_DEFAULT;` (after the ambient gust fields). New free function `void sim_set_scene(Sim&, Scene) noexcept;` declared right after `sim_tick_ambient_gusts`.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.cpp`
  - **MODIFIED uncommitted.** Added `sim_set_scene` impl after `sim_tick_ambient_gusts` — pure field assign, no other side effects. Section header comment "Scenes (§13)".

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Renderer.h` + `Renderer.cpp`
  - **NEXT TO MODIFY.** `Renderer.h:84` is `ComPtr<ID2D1SolidColorBrush> brushes_[PALETTE_SIZE];` — change to `brushes_[SCENE_COUNT][PALETTE_SIZE]`. `Renderer.cpp:118-123` is the brush-creation loop; `Renderer.cpp:398` is the draw-time lookup `brushes_[b.hue]`.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\App.h` + `App.cpp`
  - **NEXT TO MODIFY.** App.h has `kMenuQuit = 1001` only (line 24). App.cpp:109-132 `CreateTrayIcon` builds the menu with one item; App.cpp:288-322 `HandleMessageWindowMessage` handles WM_COMMAND with one branch. Need to refactor to scene submenu + WM_COMMAND switch + new `SetScene` method that broadcasts to every `w->GetRenderer().GetSim()`.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Constants.cs`, `Sim.cs`, `GrassWindow.cs`, `TrayIcon.cs`
  - **WILL BE BACKPORTED BY FLEETED AGENT.** Win2D mirror of all the above. `TrayIcon.cs` uses WinForms `NotifyIcon` + `ContextMenuStrip` on STA thread — totally different API from C++ Win32 menu, hence worth fleeting.

- `tests/DesktopGrass.Native.Tests/src/ambient_gust_tests.cpp` and `SimTests/AmbientGustTests.cs`
  - **NEW this segment, committed.** Templates for new feature tests (init seeding, idle no-op, cross-impl snapshot via spec-derivable expected values, kernel checks, stream independence, tick wiring).

- `tests/DesktopGrass.Native.Tests/DesktopGrass.Native.Tests.vcxproj`
  - Test files registered explicitly. Currently has `ambient_gust_tests.cpp` (committed). Need to add `scene_tests.cpp` in Phase 2.

- `tests/DesktopGrass.Win2D.Tests/DesktopGrass.Win2D.Tests.csproj`
  - Has `EnableDefaultCompileItems=false` so test files must be explicitly listed. `AmbientGustTests.cs` is registered. Pre-existing `FlowerTests.cs` is NOT in the csproj — pre-existing issue, not in scope.

- `tests/DesktopGrass.Native.Tests/src/snapshot_data.h`
  - Contains `CANONICAL_PRNG_SNAPSHOT[16]`, `CANONICAL_BLADE_COUNT = 321`, `CANONICAL_FIRST_10[10]`, `CANONICAL_LAST_10[10]`. Used by ambient_gust_tests.cpp "stream independence" check via `desktopgrass::test::CANONICAL_FIRST_10[0]`.

</important_files>

<next_steps>

**Immediate next steps when work resumes (Phase 2 Native completion):**

1. **Edit `src/DesktopGrass.Native/src/Renderer.h`** — change `ComPtr<ID2D1SolidColorBrush> brushes_[PALETTE_SIZE];` to `brushes_[SCENE_COUNT][PALETTE_SIZE];`
2. **Edit `src/DesktopGrass.Native/src/Renderer.cpp`** — wrap the lines ~118-123 brush-creation loop with an outer `for (int s = 0; s < SCENE_COUNT; ++s)` and read `SCENE_PALETTES[s][i]`; update the `DrawGrass` line ~398 to `brushes_[static_cast<int>(sim_.currentScene)][b.hue]`; also update the `for (auto& b : brushes_) b.Reset();` at line ~196 to a nested reset loop.
3. **Edit `src/DesktopGrass.Native/src/App.h`** — add 3 new constexpr menu IDs, add `Scene currentScene_ = SCENE_DEFAULT;` member, declare `void SetScene(Scene)`, declare `HMENU sceneMenu_ = nullptr` for the submenu handle.
4. **Edit `src/DesktopGrass.Native/src/App.cpp`** — refactor `CreateTrayIcon` to build a Scene submenu (CreatePopupMenu + 3 items with `CheckMenuRadioItem`) attached to the main popup via `AppendMenuW(MF_POPUP, sceneMenu_, L"Scene")`, then Quit. Refactor `HandleMessageWindowMessage` WM_COMMAND to handle 3 scene IDs. Implement `SetScene` to update `currentScene_`, call `sim_set_scene` on every `windows_[i]->GetRenderer().GetSim()`, and update menu radio marks. Destroy `sceneMenu_` in destructor.
5. **Create `tests/DesktopGrass.Native.Tests/src/scene_tests.cpp`** — cases: default scene = Grass; sim_set_scene assigns the field; `SCENE_PALETTES[0]` matches `PALETTE`; DESERT and WINTER palettes are 6 entries each with alpha 0xFF; stream independence (first blade unchanged across all 3 scenes).
6. **Register** `scene_tests.cpp` in `tests/DesktopGrass.Native.Tests/DesktopGrass.Native.Tests.vcxproj`.
7. **Build Native + tests, run tests, smoke.** Target: 60+ tests pass.
8. **Commit Native scene infrastructure** as one focused commit, push.

**Then (Phase 2 backport):**

9. **Fleet ONE general-purpose agent (background mode)** for Win2D scene infrastructure backport. Prompt should include:
   - The just-pushed commit SHA(s) anchor (spec `d0d7fc4` + Native impl SHA)
   - Full design summary from <technical_details>
   - Naming conventions reminder (PascalCase Win2D fields, SCREAMING_SNAKE constants, make new Sim members `public` for test access)
   - Files to edit: `Constants.cs` (add `Scene` enum, `SCENE_COUNT`, `SCENE_DEFAULT`, DESERT_PALETTE, WINTER_PALETTE, SCENE_PALETTES 2D array), `Sim.cs` (add `Scene CurrentScene = Scene.Grass` field, `SetScene` method), `GrassWindow.cs` (use scene palette in renderer — find the grass-drawing path), `TrayIcon.cs` (refactor ContextMenuStrip: ToolStripMenuItem "Scene" parent + 3 child ToolStripMenuItems with Checked radio behavior + Quit), test file `SimTests/SceneTests.cs` registered in csproj.
   - Validation gate: Win2D tests count must increase by N cases matching Native's new scene_tests; first-blade snapshot still pinned regardless of scene; smoke PASS.
   - DO NOT commit/push.
10. On agent return: validate, build, test, smoke. Single commit + push.

**Then (Phase 3 — only after Phase 2 fully lands):**

11. Lock spec §14 (Desert) and §15 (Winter). Include the new roaming-entity subsystem (the big architectural lift for tumbleweeds/snowflakes) and their PRNG salts.
12. Fleet TWO parallel general-purpose agents (background mode):
    - **Agent #1 — Desert:** spec §14 + Native + Win2D + tests for cacti (slot-bound) and tumbleweeds (roaming entities).
    - **Agent #2 — Winter:** spec §15 + Native + Win2D + tests for snowflakes (roaming) and snow-tipped blades.
13. Validate both, commit each as separate feature commit, push.
14. Relaunch Native so user can see all 3 scenes in action.

**Open architectural decisions for Phase 3** (carry forward):
- Entity subsystem design (slot-bound vs free-roaming). My intent: introduce `Sim::entities` vector with type-tagged entries (Tumbleweed, Snowflake), per-type update/render hooks, per-scene generator gate.
- Whether scene-switch regenerates entities (`sim_set_scene` will need to grow from state-only to generator-aware in Phase 3)
- Tumbleweed roll direction: random-per-tumbleweed, mostly L→R with prevailing wind
- Snowflake density: continuous low-density drift, slight horizontal sway from ambient gusts

</next_steps>