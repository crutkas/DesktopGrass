<overview>
The user is iterating on **DesktopGrass**, a fun Windows desktop overlay app that renders procedural grass on top of the taskbar. The repo (`C:\Users\crutkas\source\DesktopGrass`, pushed to `crutkas/DesktopGrass` as private) has four parallel implementations sharing a single algorithmic spec — Native C++, Win2D (Vortice-based C#), WinUI3 (WindowsAppSDK C#), and WPF (vanilla C#). Current task: **add procedurally-generated flowers** ("non-grass things") to all 4 impls via a fleet of sub-agents, with conformance preserved through a separate PRNG stream. Strategy: lock the spec in `docs/architecture.md` first, then fleet 4 impl agents in parallel against the locked spec, then validate + commit + relaunch.
</overview>

<history>
1. **User: "ok, what is next?"** — Suggested 5 options (flowers/trees, wind, tray polish, CI, README). User unavailable, so I went with **README refresh** as the lowest-risk autonomous choice.
   - Rewrote README.md: 3 → 4 impls table, corrected stack descriptions (Vortice for Win2D, unpackaged-self-contained for WinUI3), moved regrowth from future → shipped, added Run-It commands per impl, added Conformance section
   - Patched `docs/comparison.md`: "three" → "four" sweep in 5 places, updated blade count estimate (400 → 600), reframed smoke-color narrative (original 11,642 → current 1.6k-3.5k range)
   - Commit `35f829a` pushed

2. **User: "lets do that"** — Referring to the next suggested feature: **flowers/non-grass things**. Now in flight.
   - Stopped running Native exe
   - Started reading architecture.md (§4 blade data model line 99, §5 procedural generation line 136, §7 Bezier rendering line 222, §11 constants table line 490) to plan the spec patch
   - Was about to write the flower spec patch when context compaction triggered
</history>

<work_done>
**Committed and pushed (all on `main`):**
- `35f829a` — README + comparison.md refresh for four-impl reality
- `af70061` — `BASE_AMPLITUDE` 3.0 → 3.3 (+10% passive sway)
- `d2f8f61` — Added 4th impl: `DesktopGrass.WPF` (vanilla .NET 10 WPF + DrawingContext)
- `df38d26` — Upgrade all C# projects from .NET 8 → .NET 10
- `9fdf28b` — Soften gust: `GUST_TO_LEAN_FACTOR` 1.5 → 0.75
- `040dbaf` — Chord-preserving blade bend (`MAX_LEAN_FRACTION = 0.95`)
- Earlier: regrowth (`7f300cf`), density+slow sway (`f1f084b`), narrower sway+more density (`7d3d12d`), rooted bend (`ca990d2`)

**Current state of repo:**
- 4 conformant impls, all building, all tests passing (Native 41/60,548 assertions; Win2D 45; WinUI3 49; WPF 45 = 180 tests total)
- Cross-impl smoke baseline: Native 1998 / Win2D 2081 / WinUI3 1662 / WPF 2052 unique colors (all PASS)
- README/docs accurate for the 4-impl, .NET 10, chord-preserving-bend reality

**In flight (flowers feature, untouched as of compaction):**
- [ ] Lock spec in `docs/architecture.md` (§4 add flower fields to blade table, §5 add flower PRNG stream + generation, §7 add flower-head render rule, §11 add 8 new constants)
- [ ] Commit the spec change as a standalone commit (so agents reference a locked target)
- [ ] Fleet 4 impl agents in parallel (one per impl: Native, Win2D, WinUI3, WPF) with the spec citation + detailed instructions
- [ ] Validate: build all + tests all + smoke all
- [ ] Commit + push feature commit
- [ ] Relaunch Native for user verification
</work_done>

<technical_details>
**Flower feature design (locked in my head, not yet in spec):**

New constants (add to §11 table):
- `FLOWER_PROBABILITY = 0.04` (4% of blade slots)
- `FLOWER_HEIGHT_BONUS_MIN = 1.20`, `FLOWER_HEIGHT_BONUS_MAX = 1.50`
- `FLOWER_HEAD_RADIUS_MIN = 1.8` DIP, `FLOWER_HEAD_RADIUS_MAX = 3.0` DIP
- `FLOWER_PALETTE_SIZE = 6`
- `FLOWER_PRNG_SALT = 0xC0FFEEFACE0FFE5UL`
- `FLOWER_PALETTE = [0xFFFFEB3B yellow, 0xFFFFA726 orange, 0xFFFF80AB pink, 0xFFE1BEE7 lavender, 0xFFFFFFFF white, 0xFFEF5350 red]`

New Blade fields (additive):
- `IsFlower: bool`
- `FlowerHeadColorIdx: int` (0..5)
- `FlowerHeadRadius: double`
- `HeightBonus: double` (1.0 for non-flowers, 1.2–1.5 for flowers)

**Conformance-critical PRNG ordering** (the key insight):
```
mainPrng    = Prng.Init(seed)                      // unchanged
flowerPrng  = Prng.Init(seed XOR FLOWER_PRNG_SALT) // NEW
regrowPrng  = Prng.Init(seed XOR REGROW_PRNG_SALT) // existing

for each blade slot:
    // MAIN: step, height, thickness, hue, swayPhase, stiffness (unchanged)
    // FLOWER:
    //   isFlower = flowerPrng.NextDouble() < FLOWER_PROBABILITY   [always 1 draw]
    //   if isFlower:
    //     headColorIdx = (int)(flowerPrng.NextU64() % 6)           [conditional]
    //     headRadius   = lerp(FLOWER_HEAD_RADIUS_MIN, _MAX, flowerPrng.NextDouble())
    //     heightBonus  = lerp(FLOWER_HEIGHT_BONUS_MIN, _MAX, flowerPrng.NextDouble())
    //   else: headColorIdx=0; headRadius=0; heightBonus=1.0       [no extra draws]
    // REGROW: regrowDelay, regrowDuration (unchanged)
```
**Main stream stays bit-identical** because flower draws are on a separate stream. Conditional draws are OK as long as all impls do exactly the same thing.

**Render contract** (§7 extension):
- Compute stroke with `L = Height * HeightBonus * cutHeight` (HeightBonus=1.0 for non-flowers ⇒ no change to non-flower geometry)
- If `IsFlower`: after drawing Bezier stem, draw a filled circle at the tip with `FLOWER_PALETTE[FlowerHeadColorIdx]` and radius `FlowerHeadRadius` (Native: D2D `FillEllipse`; Win2D: same via Vortice; WinUI3: `FillCircle` on Win2D canvas; WPF: `DrawingContext.DrawEllipse(brush, null, center, r, r)`)
- Cut animation: head tip moves with the cut anim (since tip is derived from L which scales with cutHeight) — automatically correct
- Regrowth: `IsFlower` is set at generation, persists across cut/regrow — flowers regrow as flowers

**Native snapshot impact**: `tests/DesktopGrass.Native.Tests/snapshot_data.h` currently records `(baseX, height, thickness, hue, swayPhase, stiffness)` — all main-stream. Since main stream is unchanged, snapshot stays bit-identical for those columns. **Should add 4 new columns** (`isFlower, headColorIdx, headRadius, heightBonus`) and regen via `snapshot_gen.cpp` to validate flower-stream conformance. Recipe (verified earlier):
```pwsh
& $env:ComSpec /c 'call "...VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul && cd /d <tests\Native.Tests> && cl /nologo /std:c++17 /EHsc /O2 /I..\..\src\DesktopGrass.Native\src /Fe:snapshot_gen.exe snapshot_gen.cpp ..\..\src\DesktopGrass.Native\src\Sim.cpp && snapshot_gen.exe > snapshot_data_generated.h'
```

**Test additions**: each impl gets a `FlowerTests.cs` (or extension to BladeGenTests) covering:
- Determinism: same seed → same (isFlower, headColor, radius, bonus) sequence across runs
- Probability: flower count in canonical-seed generation ≈ FLOWER_PROBABILITY × total within ±3σ
- Main-stream non-interference: blade.BaseX/Height/Thickness/Hue match pre-flower expected values (already covered by existing tests staying green)

Native gets equivalent `flower_tests.cpp`.

**Fleet pattern (proven)**: 4 parallel general-purpose agents, each gets:
- Exact constant values
- Exact PRNG ordering
- Exact code-skeleton snippets
- Test file changes (with values)
- Build/test commands
- Validation gate (must green-light tests before reporting)

**Build commands (all verified post-.NET-10):**
- Native: `& $env:ComSpec /c 'call "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul && cd /d <dir> && msbuild <proj>.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m'`
- C# build/test: `dotnet build -c Release --nologo` / `dotnet test -c Release --nologo`
- Smoke: `pwsh -NoProfile -File 'C:\Users\crutkas\source\DesktopGrass\tests\smoke\Run-SmokeTests.ps1' -Target All -Configuration Release`

**Critical sub-agent failure mode learned**: The WPF agent took 23 min and failed with a CAPIError connection error after producing all files. Files were salvageable; I just ran the validation manually. So future fleets: if an agent fails, check whether files landed before retrying.

**Process management**: Native exe gets locked on rebuild — must `Get-Process DesktopGrass.Native -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }` before building.

**Stop-Process restriction in this env**: must use `-Id <pid>`, not `-Name`. Always loop `foreach ($p in $procs) { Stop-Process -Id $p.Id -Force }`.
</technical_details>

<important_files>
- `docs/architecture.md` (lines 99–187 §4+§5, 222–278 §7, 490–530 §11)
  - **Authoritative spec** for all 4 impls. Flower feature requires edits to §4 (add flower fields to blade table around line 117), §5 (extend `generate_blades` around line 160–185 with flower stream), §7 (add flower-head render rule + height-bonus L formula around line 244), §11 (add 8 constant rows around line 525).
- `src/DesktopGrass.Native/src/Constants.h`, `src/DesktopGrass.Native/src/Sim.cpp`
  - Native impl. Add flower constants + struct fields + gen + render. `compute_blade_stroke` ~line 240; `update_blade_dynamics` ~line 118; generation in Sim.cpp.
- `src/DesktopGrass.Win2D/Constants.cs`, `src/DesktopGrass.Win2D/Sim.cs`, `src/DesktopGrass.Win2D/GrassWindow.cs`
  - Win2D impl. `ComputeBladeStroke` ~line 316; renderer uses Vortice `_dc.DrawGeometry`. Add `_dc.FillEllipse` for flower heads.
- `src/DesktopGrass.WinUI3/Constants.cs`, `src/DesktopGrass.WinUI3/Sim.cs`
  - WinUI3 impl. Two stroke call sites: static `ComputeBladeStroke` ~line 237, instance `GetStroke` ~line 417. Renderer uses Microsoft.Graphics.Win2D; `FillCircle` for heads.
- `src/DesktopGrass.WPF/Constants.cs`, `src/DesktopGrass.WPF/Sim.cs`, `src/DesktopGrass.WPF/GrassCanvas.cs`
  - WPF impl. `GrassCanvas.OnRender(DrawingContext dc)` — add `dc.DrawEllipse(brush, null, tip, r, r)` for flower heads.
- `tests/DesktopGrass.Native.Tests/snapshot_data.h`, `tests/DesktopGrass.Native.Tests/snapshot_gen.cpp`
  - Native canonical snapshot. Add 4 flower columns. Regenerate via `snapshot_gen.cpp`.
- `tests/DesktopGrass.{Win2D,WinUI3,WPF}.Tests/SimTests/BladeGenTests.cs`
  - Add flower stream determinism + probability tests.
- `tests/smoke/Run-SmokeTests.ps1` — no changes needed (already supports all 4 impls).
- `C:/Users/crutkas/.copilot/session-state/e286b6d3-8e11-4aa2-b2d7-87ceb1f5de22/plan.md` — Session plan.

Repo: `C:\Users\crutkas\source\DesktopGrass` on `main`, pushed to `https://github.com/crutkas/DesktopGrass` (private).
</important_files>

<next_steps>
**Immediate next steps** (after compaction):

1. **Read `docs/architecture.md` §4, §5, §7, §11 in full** to know the exact insertion points for the flower spec patch.

2. **Edit `docs/architecture.md`** with the flower spec (using the design locked in `<technical_details>` above). Specifically:
   - §4: add 4 new fields (`isFlower`, `flowerHeadColorIdx`, `flowerHeadRadius`, `heightBonus`) to the blade data table, after the regrowth fields.
   - §4: add a "Flower palette" subsection with 6 ARGB colors after the existing palette.
   - §5: extend `generate_blades` pseudocode to add the flower PRNG stream and conditional flower draws, with explicit ordering note (main → flower → regrowth).
   - §7: change `L = b->height * b->cutHeight` to `L = b->height * b->heightBonus * b->cutHeight`; add a post-Bezier "draw flower head" rule with `radius = flowerHeadRadius` at tip.
   - §11: add 8 new rows (`FLOWER_PROBABILITY`, `FLOWER_HEIGHT_BONUS_MIN/MAX`, `FLOWER_HEAD_RADIUS_MIN/MAX`, `FLOWER_PALETTE_SIZE`, `FLOWER_PRNG_SALT`, and note FLOWER_PALETTE table).

3. **Commit spec change standalone**:
   ```
   git commit -m "Lock spec for flower feature (separate PRNG stream, head circle at tip)"
   ```
   Push so agents reference the same SHA.

4. **Fleet 4 impl agents in parallel** (one per impl). Each prompt should include:
   - Full design from `<technical_details>` above
   - Files to edit (Constants, Sim, renderer, tests)
   - Exact code skeletons (constants block, generation snippet, render snippet)
   - Test expectations (determinism + probability count for canonical seed)
   - Build + test commands
   - Validation gate: must green-light their impl's tests before reporting
   - For Native agent specifically: also regen `snapshot_data.h` via `snapshot_gen.cpp` and add the 4 flower columns to both the .h and the gen .cpp.

5. **After all 4 return**: run smoke `-Target All`, expect 4 PASS with slightly higher unique-color counts (flower heads add new colors to the palette).

6. **Commit feature commit** with message describing the flower feature, the 4% probability, the separate PRNG stream conformance-preservation, and validation results. Push.

7. **Relaunch Native** so user can verify on resume: `Start-Process 'C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\out\Release\DesktopGrass.Native.exe' -PassThru`.

8. **Call `task_complete`** with summary including diagnosis ("user requested non-grass things"), implementation ("4% of blade slots randomly become flowers via separate PRNG stream"), test/smoke results, and a note about possible follow-ups (small tree, mushrooms, day/night palette).

**Open considerations**:
- Should `FLOWER_PROBABILITY` be 0.04 (subtle, ~24-30 flowers in ~720 blades) or higher like 0.08 (~58 flowers)? Going with 0.04 for subtlety; user can ask to bump.
- The Native snapshot regen requires building `snapshot_gen.exe` separately from the test exe. Recipe verified earlier; sub-agent must follow it exactly.
- WPF `DrawingContext.DrawEllipse` takes a `Pen` parameter; pass `null` for stroke-less filled circle.
</next_steps>