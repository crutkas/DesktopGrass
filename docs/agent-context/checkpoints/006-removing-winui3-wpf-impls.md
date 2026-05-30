<overview>
DesktopGrass (private repo `crutkas/DesktopGrass`, working tree `C:\Users\crutkas\source\DesktopGrass`) is a "just for fun" Windows desktop overlay that paints procedural grass / flowers / mushrooms on top of the taskbar. After a 4-impl comparison the codebase is now down to two parallel implementations — Native (C++/Direct2D) and Win2D (C#/Vortice) — sharing a single locked spec in `docs/architecture.md`. The user just queued three big asks: (1) random small gusts of wind, (2) extend the systray menu to switch scenes, (3) fleet out two new scenes — Desert (tumbleweeds, cactus, etc.) and Winter — and figure out the right way to parallelize the work.
</overview>

<history>
1. **Earlier in the session (pre-compact background):** Built grass v1 in 4 stacks (Native, Win2D, WinUI3, WPF), added chord-preserving bend, regrowth, flowers, mushrooms, mushroom stump-stub at 2 px, then bumped mushroom stub to 4.0 DIP (`0fbddc6`).

2. **User: "lets compare now"** (after individually A/B'ing all 4 impls)
   - Killed all 4, relaunched simultaneously, sampled WS + CPU + threads
   - Result table:

     | Impl | PID | WS_MB | CPU% | Threads |
     |---|---|---:|---:|---:|
     | Native | 47120 | 55 | 0.8 | 70 |
     | Win2D  | 42872 | 99 | 3.1 | 78 |
     | WinUI3 | 10364 | 158 | 2.2 | 112 |
     | WPF    | 42820 | 579 | 3.5 | 16 |

3. **User: "lets remove wpf and remove winui"**
   - Deleted `src/DesktopGrass.WPF/`, `src/DesktopGrass.WinUI3/`, both test projects
   - Updated `DesktopGrass.slnx`, `tests/smoke/Run-SmokeTests.ps1` (`-Target` ValidateSet narrowed to `Native|Win2D|All`, `$Targets` table trimmed), `tests/smoke/README.md`, top-level `README.md` (history note pointing at comparison.md), `docs/architecture.md` (all "four"→"both"/"two", added history note), `docs/manual-smoke.md` (dropped WinUI 3 + WPF checklist sections), `docs/comparison.md` (added "Status (post-comparison)" header with the new WS numbers — WPF/WinUI3 rows retained as historical justification), `tests/DesktopGrass.Native.Tests/src/snapshot_data.h` (comment updated)
   - Verified: Native build clean, Win2D build clean, Native 50/50 tests / 67,397 assertions, Win2D 50/50 tests, smoke PASS Native 1999 unique colors / Win2D 2067
   - Committed as `5e9d796` (56 files / -5,936 lines) and pushed

4. **User: "real talk my friend. would rust make any difference"**
   - Answered: no, not for this project. Footprint floor is D3D11 + DXGI + DComp runtime, not language. Native is already up against the floor at 55 MB. Rust would matter for cross-platform (`wgpu`/winit), memory-safe COM lifetimes (small win since ComPtr discipline is tight), `cargo` ergonomics, and OSS recruiting — but it's a stylistic choice not a perf one. The two-impl Native+Win2D conformance pair is doing the work.

5. **User: "here are the next tasks i want done. small gusts of wind randomly. next would be extending the systray to allow scene changes which brings me to the next major set of work i want you to figure out how to fleet out to do is. A desert mode with tumble weeds, cactus, and stuff. Then i'd like a winter theme one. go forward and fleet out as needed"** ← **CURRENT TURN**
   - This summary is being created in response to a context-compaction request immediately after this user message landed. No tool calls have been made yet on this turn for the new work. Next action when work resumes: plan + execute the 3 features (ambient gusts → scene menu → fleeted Desert+Winter scenes).
</history>

<work_done>
**Current `main` HEAD:** `5e9d796` "Drop WinUI 3 and WPF implementations"

**Committed earlier this session (still relevant):**
- `e1f294a` — Lock spec for mushroom feature
- `bacb841` — Mushrooms in Native impl
- `090fd04` — Native mushroom unit tests
- `f6f9d71` — Mushrooms in Win2D + WinUI3 + WPF (now Win2D-only post-deletion)
- `0fbddc6` — Mushroom stump nub 2 px taller (MUSHROOM_STUMP_HEIGHT = 4.0)
- `5e9d796` — Drop WinUI 3 and WPF implementations

**Working tree:** clean (verified before compaction request landed).

**Currently running processes:** none — last surviving Native process was the WS=55 MB one in the comparison; user has not asked to relaunch since the WinUI3/WPF removal commit.

**Test counts (current):**
- Native: 50/50 cases, 67,397 assertions
- Win2D: 50/50 cases
- Smoke: 2 targets (Native, Win2D), both PASS

**For the new ask (NOT yet started):**
- [ ] Ambient/random small gusts of wind
- [ ] Systray scene switcher (Native + Win2D)
- [ ] Desert scene (tumbleweeds, cactus, sand/rocks)
- [ ] Winter scene (snow on ground? bare branches? icicles? snowflakes?)
- [ ] Fleet design — decide how to slice these into parallel sub-agents
</work_done>

<technical_details>
## Repo geometry post-removal

```
DesktopGrass.slnx       # only Native + Win2D projects
src/
  DesktopGrass.Native/      # C++ / Direct2D / DComp (REFERENCE impl)
    src/{Constants.h, Sim.h, Sim.cpp, Renderer.h, Renderer.cpp,
         App.cpp, App.h, GrassWindow.cpp, GrassWindow.h, MouseHook.cpp,
         MouseHook.h, main.cpp, resource.h}
    DesktopGrass.Native.vcxproj, .rc, app.manifest, vcpkg.json
    out/Release/DesktopGrass.Native.exe (Release/x64)
  DesktopGrass.Win2D/       # C# / Vortice / .NET 10
    Constants.cs, Sim.cs, GrassWindow.cs, App.cs, Program.cs,
    MouseHook.cs, TrayIcon.cs, Interop/User32.cs, app.manifest,
    DesktopGrass.Win2D.csproj
    bin/Release/net10.0-windows10.0.19041.0/DesktopGrass.Win2D.exe
tests/
  DesktopGrass.Native.Tests/   # Catch2, vendored single-header
    src/{main.cpp, prng_tests, blade_gen_tests, sway_tests,
         gust_tests, cut_tests, regrowth_tests, flower_tests,
         mushroom_tests, snapshot_data.h, snapshot_gen.cpp(?)}
  DesktopGrass.Win2D.Tests/    # xUnit
    SimTests/{BladeGenTests, CutTests, FlowerTests, GustTests,
              MushroomTests, PrngTests, RegrowthTests, SwayTests}.cs
  smoke/
    Run-SmokeTests.ps1   # -Target Native|Win2D|All
    Smoke.Common.psm1    # has -TitleMatch + BeforeLaunch (vestigial; was for WinUI3)
docs/
  architecture.md      # SINGLE SOURCE OF TRUTH for spec
  comparison.md        # historical 4-impl evaluation
  manual-smoke.md      # Native + Win2D checklists
```

## Spec contract (must hold across both impls)

- **Single source of truth:** `docs/architecture.md`. Both impls port it verbatim. No shared library.
- **PRNG:** xorshift64 seeded via SplitMix64. Canonical test seed = `0x6B6173746F`.
- **Four independent PRNG streams** (current):
  1. Main `p`  — step, height, thickness, hue, swayPhaseOffset, stiffness (6 draws / blade)
  2. Regrowth `pr = seed ^ REGROW_PRNG_SALT` — regrowDelay, regrowDuration
  3. Flower `pf = seed ^ FLOWER_PRNG_SALT` — isFlower, then if true: headColor, headRadius, heightBonus
  4. Mushroom `pm = seed ^ MUSHROOM_PRNG_SALT = 0xBADC0FFEE0FACE21UL` — isMushroom, then if true: capColorIdx, capW, capH, stemH, stemThickness
- **Field-draw order is fixed per stream** (§5). Reordering breaks snapshot tests.
- **Stream independence rule:** main stream's draw count per blade does NOT depend on whether other streams are enabled. Add a new feature → new salt + new stream. NEVER touch existing streams.
- **First blade canonical (seed=0x6B6173746F, monitorWidth=1920, density=1.0):**
  - BaseX=4.941073726820111, Height=24.469991818248864, Thickness=1.5829214329729786, Hue=3
  - 321 blades total, 17 flowers, 7 mushrooms

## Naming conventions

- **Native (C++):** `SCREAMING_SNAKE_CASE` constants in `desktopgrass::` namespace in `Constants.h` (e.g. `MUSHROOM_PROBABILITY`). Blade struct uses `camelCase` fields (`isMushroom`, `mushroomCapColorIdx`).
- **Win2D (C#):** `SCREAMING_SNAKE_CASE` constants on `static class Constants` (e.g. `Constants.MUSHROOM_PROBABILITY`). Blade struct uses `PascalCase` fields (`IsMushroom`, `MushroomCapColorIdx`).

## Build / test commands (proven)

```pwsh
# Native — must stop running exe first (locks itself)
Get-Process DesktopGrass.Native -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
$vsBat = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat'
& $env:ComSpec /c "call `"$vsBat`" -arch=x64 -host_arch=x64 >nul && cd /d C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native && msbuild DesktopGrass.Native.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m"
& $env:ComSpec /c "call `"$vsBat`" -arch=x64 -host_arch=x64 >nul && cd /d C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests && msbuild DesktopGrass.Native.Tests.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m"
& 'C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\out\Release\DesktopGrass.Native.Tests.exe' --reporter compact

# Win2D
cd C:\Users\crutkas\source\DesktopGrass
dotnet build src\DesktopGrass.Win2D -c Release --nologo
dotnet test  tests\DesktopGrass.Win2D.Tests -c Release --nologo --verbosity minimal

# Smoke (both impls)
pwsh -NoProfile -File 'tests\smoke\Run-SmokeTests.ps1' -Target All -Configuration Release

# Launch
& 'C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\out\Release\DesktopGrass.Native.exe'
& 'C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\bin\Release\net10.0-windows10.0.19041.0\DesktopGrass.Win2D.exe'
```

## Process/git quirks

- Native exe locks itself on rebuild — ALWAYS `Stop-Process -Id` first
- `Stop-Process` restricted: must use `-Id <pid>`, never `-Name`
- `.cmd` files have arg-mangling issues in PS7; prefer `.exe`
- Co-authored-by trailer for ALL commits: `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`
- Working WS numbers (steady state): Native 55, Win2D 99 (both well within target)

## Current sway/gust system (existing, for reference when adding ambient gusts)

- **Passive sway:** `BASE_AMPLITUDE = 3.3` DIP, 6 sec period, per-blade `swayPhaseOffset` from main PRNG stream. Sin-driven.
- **Cursor gusts:** `GUST_FACTOR = 0.75`, cursor must be within 110 DIP gust band above ground, blades lean away briefly. Code lives in `Sim.cpp`/`Sim.cs` (gust state per blade), measured against cursor position from `MouseHook`.
- **No current ambient/random gusts** — that's literally the new ask.

## Fleet pattern (proven from flowers/mushrooms)

- Lock spec patch in `docs/architecture.md` FIRST, commit + push standalone so agents have a SHA to anchor on
- Implement Native (the reference) yourself, commit + push
- Discover canonical numbers from Native (e.g. flower count = 17, mushroom count = 7)
- Fleet ONE agent (general-purpose, background mode) for Win2D backport with:
  - Locked spec SHA reference
  - Full design from this summary
  - Naming convention reminder (PascalCase fields, SCREAMING_SNAKE constants)
  - Files to edit (Constants, Sim, GrassWindow)
  - Exact code skeletons
  - Test file additions
  - Build + test commands
  - Validation gate (count must match Native's, first blade main-stream pinned values must hold)
  - DO NOT commit/push
- On return: validate, single feature commit, push, relaunch

## For the 3 new asks specifically

### Ambient gusts
- Should be a **new feature on the existing wind model**, not a new PRNG stream — gusts are time-varying environmental state, not per-blade static
- Likely lives on the simulation's wind/time tick, randomly fires a "synthetic cursor gust" or applies a sin-modulated extra-amplitude offset
- One reasonable design: time-driven Poisson process — every N ms roll a die, on hit spawn a gust with random center X, random strength, random duration, applied via the existing cursor-gust math
- Tunable constants: `AMBIENT_GUST_MEAN_INTERVAL_SEC = 8.0`, `AMBIENT_GUST_STRENGTH_MIN/MAX`, `AMBIENT_GUST_DURATION_SEC = 1.5`, `AMBIENT_GUST_RADIUS_DIP`, possibly a 5th PRNG stream `pa = seed ^ AMBIENT_GUST_PRNG_SALT` so determinism is preserved
- Spec lock then port to both impls (small enough to do both sync, no fleet needed)

### Scene switcher via tray
- Both impls already have a tray (`App.cpp` for Native, `TrayIcon.cs` for Win2D). Currently just "Quit"
- Add menu items: **Grass** (default), **Desert**, **Winter** — radio-group style or just popping a check next to active
- Scene change = swap palette + per-scene flora set + maybe regenerate blades. Needs a `Scene` enum + per-scene constant tables routed through Sim/Renderer
- Hot-swap probably easiest by: keep Sim state, swap rendering palettes + flora kind set + (for Desert) replace some grass blades with cacti/tumbleweeds
- Persist last-selected scene across launches? user didn't say — assume **no** for v1, ephemeral
- Scene state belongs in `App.cpp` / `App.cs` and is passed into the Sim/Renderer on each frame

### Desert scene
- Tumbleweeds — animated entities that roll across the strip horizontally, periodically spawned. Probably NOT slot-bound (unlike flowers/mushrooms) — these are time-driven roaming objects with their own simulation state
- Cacti — slot-bound, rare, rigid; replace some grass blades; 2-3 silhouettes (barrel, saguaro)
- Sand color palette for blades (or hide blades and replace with sparse sage/yellow scrub?)
- Maybe small rocks (slot-bound static graphics)
- Sand particles drifting on wind?

### Winter scene
- Bare branches replacing some blades? snow piles? icicles dripping from screen top?
- Snowflakes falling (similar entity model to tumbleweeds — roaming, not slot-bound)
- Cold-palette blade tinting + frost
- Maybe pine sprigs replacing grass

## Open architecture decisions for the new work

- **Scene model:** does each scene replace the Sim's entire flora generator, or do scenes act as a render-time skin over a shared blade vector? Most flexible: scenes have a `SceneSpec` that names a generator + palette + entity list, and the Sim consults it
- **Roaming entities (tumbleweeds, snowflakes):** these break the current "slot-bound, generated once" model. Need a new entities/particles subsystem. This is the biggest architectural change in the new work
- **Spec discipline:** scene work is BIG. Could blow the architecture.md doc size if locked in detail. Recommend: lock the SCENE FRAMEWORK in architecture.md (a `Scene` concept, the entity subsystem, the tray plumbing) once, then each scene gets its own subsection in §13/§14
- **Cross-impl conformance for roaming entities:** entity PRNG streams must also stay independent. Likely entity streams = `seed ^ <ENTITY>_PRNG_SALT`. Each scene that adds entities adds its own salts.

## Existing memories that apply

- "VS 2022 bundled MSBuild 17.14 can't host .NET SDK 10" — using `dotnet build` directly with `global.json` pinning SDK 10.0.203
- Native uses VS18 Enterprise (`'C:\Program Files\Microsoft Visual Studio\18\Enterprise\...'`)
- Co-authored-by trailer required on commits
</technical_details>

<important_files>
- `C:\Users\crutkas\source\DesktopGrass\docs\architecture.md`
  - **Single source of truth.** All four PRNG streams documented in §5. Mushroom feature spec in §4/§5/§7/§11. After WinUI3/WPF removal, prose says "both implementations" throughout.
  - Will need new §13 "Ambient gusts" + new §14 "Scenes" (or similar) for the next features.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.h`, `Sim.cpp`, `Constants.h`
  - Native simulation core. Currently has main + regrowth + flower + mushroom streams.
  - Will need: 5th PRNG stream for ambient gusts (or time-based RNG state); per-scene flora generator hook; roaming-entity subsystem.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Renderer.cpp`
  - DrawGrass loop already short-circuits mushroom slots BEFORE the grass+flower path. Same pattern for cactus slots.
  - Will need: scene-aware palette switching, entity rendering path (tumbleweed/snowflake), per-scene blade hiding.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\App.cpp`
  - Owns `Shell_NotifyIconW` tray menu. Currently single "Quit" item.
  - Will need: scene radio-group menu items with check marks, scene-change message routing to renderer/sim.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Sim.cs`, `Constants.cs`, `GrassWindow.cs`, `TrayIcon.cs`
  - Win2D mirror of all the above. Fleet target #1.
- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\src\mushroom_tests.cpp` and `tests/DesktopGrass.Win2D.Tests/SimTests/MushroomTests.cs`
  - Templates for new feature tests (determinism, count within 3σ, non-interference with other streams).
- `C:\Users\crutkas\source\DesktopGrass\tests\smoke\Run-SmokeTests.ps1`
  - Smoke harness. Currently 2 targets. Probably needs a scene-cycling test mode in the future, but not required for v1 ambient-gust or scene-switcher.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\MouseHook.cpp` / `MouseHook.h` and Win2D `MouseHook.cs`
  - Existing cursor-gust dispatch path. Ambient gusts will use the same downstream gust application code path, just spawned from a timer instead of the hook.
</important_files>

<next_steps>
**Three asks queued, none started:**

### Step 1 — Ambient/random gusts (small, do both impls in one shot)
1. Design: 5th PRNG stream `pa = seed ^ AMBIENT_GUST_PRNG_SALT` (e.g. `0xAA117ABLE0DEADBEEF` or pick a salt), Poisson timer, on hit spawn a synthetic gust event consumed by existing gust pipeline
2. Lock spec patch in `docs/architecture.md` (new §) + add constants to §11. Commit standalone, push.
3. Implement in Native (Sim.cpp emits gust events on tick, GrassWindow consumes them just like mouse-driven gusts). Build, test, smoke. Commit + push.
4. Implement in Win2D mirror. Build, test, smoke. Commit + push as a feature commit.
5. Optionally relaunch Native so the user can see it.

### Step 2 — Systray scene switcher framework
1. Spec: introduce `Scene` enum (Grass, Desert, Winter), tray menu radio-group items + check marks, scene-change routing to Sim/Renderer. Lock in architecture.md as a new §.
2. Implement in Native: tray menu rebuild with radio items + WM_COMMAND handlers, plumb selected scene into Sim/Renderer via a `currentScene` field, scene-aware constants tables.
3. Implement in Win2D mirror.
4. Initially all 3 menu items work but Desert/Winter render exactly the same as Grass (just the plumbing) — so this lands as a small standalone commit before the actual scene content.

### Step 3 — Desert + Winter scene content (FLEET THIS)
1. Lock framework spec: roaming-entity subsystem (tumbleweeds, snowflakes) + per-scene generator hook + per-scene PRNG salts. This is the big architectural lift.
2. Lock per-scene specs (Desert: cacti slots, tumbleweed entities, sand palette; Winter: bare branches?, snowflake entities, frost palette).
3. Implement scene framework + Desert + Winter in Native (the reference). May be too big for one commit — split into: framework, Desert, Winter.
4. **Fleet pattern for backport:**
   - One agent (general-purpose, background) for "Backport Desert+Winter+scene framework to Win2D". This is one agent because Win2D is the only target; parallelism doesn't help with only 1 target.
   - Provide full design from technical_details above, locked spec SHA, naming convention reminders, file list, code skeletons, validation gate (tumbleweed-count parity, snowflake-count parity, scene-switch parity, first-blade main-stream still pinned).
5. Validate: tests both impls, smoke each scene visually.
6. Commit feature + push + relaunch.

**Plan for figuring out the right scene-by-scene fleet split for step 3:**
- Roaming entities subsystem first (no scene content yet) → 1 commit
- Desert scene → 1 commit
- Winter scene → 1 commit
- Each one fleets the Win2D backport with a focused prompt and a clear validation gate. Don't try to fleet "all of scenes" as one mega-task.

**Open questions to decide as I go (user gave broad direction, will use best judgment per autopilot rules):**
- Should scene selection persist across launches? Default: NO (ephemeral) per session.
- Do non-Grass scenes have flowers/mushrooms? Default: scenes own their own flora generators; Grass keeps flowers+mushrooms, Desert gets cacti+tumbleweeds-only, Winter gets snowflakes+bare-branch-only.
- Tumbleweed roll direction? Default: random per-tumbleweed, mostly left-to-right with prevailing wind, small percentage other way.
- Snowflake density and fall pattern? Default: continuous low-density drift, slight horizontal sway from ambient gusts.

**Resume action:** start with Step 1 (ambient gusts). Begin by reading the current sway/gust code in `src/DesktopGrass.Native/src/Sim.{h,cpp}` and `Constants.h` to find the right insertion point + existing tunables, then write the spec patch.
</next_steps>