<overview>
DesktopGrass is a "just for fun" Windows overlay (private repo `crutkas/DesktopGrass`, working tree `C:\Users\crutkas\source\DesktopGrass`, branch `main`) that paints procedural grass/flowers/mushrooms across every monitor's bottom strip, click-through, over the taskbar. Two parallel impls — Native (C++/Direct2D) and Win2D (C#/Vortice) — share a single locked spec in `docs/architecture.md`. This segment landed: Phase 3 content (Desert §14 + Winter §15), fixed a cactus-persistence bug on scene switches, scaled down Desert grass height so cacti dominate, and is currently mid-implementation of **§15.1 Pine Trees** (Winter biome anchor mirroring Desert cacti).
</overview>

<history>
1. **(carried from prior context)** User asked to fleet Desert (§14) + Winter (§15) in parallel. I launched both as background `general-purpose` agents on the SAME working tree — they raced. Both completed; Winter (1277s) finished first; Desert (1562s) overwrote Winter's test-project registrations. Sim.cpp/Sim.cs merged cleanly because each agent owned a disjoint `case Scene::...:` branch. Re-registered Winter tests, verified both builds clean + 89/89 tests each impl, smoke produced 1-unique-color false-fail (workstation was locked — verified the bare desktop strip also returns 1 color). Committed as `036452a` "Desert + Winter scene content (§14 + §15) — both impls", pushed. Stored two memories: parallel-fleet anti-pattern + smoke-test requires unlocked desktop.

2. User said "do native" — launched DesktopGrass.Native.exe (PID 49364), told user to use tray Scene▸ submenu.

3. **User reported bug**: "switching to winter, cactus stayed" with attached screenshot showing green cacti in Winter strip. Root cause: `sim_set_scene` only called `restore_original_variants` in the `case Scene::Grass:` branch — Desert→Winter left `isCactus=true`. Fixed in both impls by hoisting the restore loop ABOVE the switch so every transition starts from a clean variant slate. Committed `86f5319`, pushed. Both impls 89/89, relaunched (PID 63916).

4. User said "winter feels like it needs pine trees or something, thoughts". Proposed slot-bound pines (mirror cacti structure): 3 silhouettes, ~0.5% prob, snow-capped tiers. Asked user to confirm via ask_user. User offline — autopilot.

5. User pivoted: "desert needs shorter grass". Added `DESERT_GRASS_HEIGHT_SCALE = 0.5` applied inside `compute_blade_stroke` / `ComputeBladeStroke` for non-cactus, non-mushroom blades when `scene == Desert`. Changed Native `compute_blade_stroke` signature to take `Scene` parameter; updated `Renderer.cpp:459` to pass scene; updated 2 callers in `cut_tests.cpp` to pass `Scene::Grass`. Win2D already took scene parameter. Both 89/89, committed `9039d30`, pushed, relaunched (PID 42040).

6. User repeated: "winter feels like it needs pine trees or something, thoughts". This time shipping autonomously. Currently mid-implementation:
   - Spec patch in `docs/architecture.md` §15.1 + §11 constants (committed `9222c21`, pushed).
   - Native impl in progress: constants ✅, Blade fields ✅, restore_original_variants ✅, generate_pines_for_winter ✅ (in Sim.cpp), scene dispatch in `case Scene::Winter` ✅, pineBrush_ field+init+dispose ✅, render branch with scan-fill triangles + snow caps ✅, snow-tip exclusion for pines ✅.
   - **Not done yet**: Native build/test verification, Win2D impl (mirror everything), tests both impls (pine_tests.cpp / PineTests.cs), final commit + push + relaunch.
</history>

<work_done>
**Current HEAD:** `9222c21` "Spec: §15.1 Pine trees (Winter slot-bound variant)" (pushed). All subsequent edits are uncommitted in working tree.

**Commits this segment (chronological, all pushed):**
- `036452a` — Desert + Winter scene content (§14 + §15) — both impls
- `86f5319` — Fix cactus persistence across scene transitions (§13.1 / §14)
- `9039d30` — Desert: shorter blades so cacti dominate the biome
- `9222c21` — Spec: §15.1 Pine trees (Winter slot-bound variant)

**Uncommitted edits in working tree (Native pine impl, in progress):**
- `src/DesktopGrass.Native/src/Constants.h` — added 13 PINE_* constants after SNOW_TIP block.
- `src/DesktopGrass.Native/src/Sim.h` — added pine fields to `Blade` (isPine, pineTierCount, pineHeight, pineWidth) after cactus block; declared `generate_pines_for_winter(Sim&)` near `generate_tumbleweeds`.
- `src/DesktopGrass.Native/src/Sim.cpp` — added pine clearing to `restore_original_variants`, implemented `generate_pines_for_winter` after `generate_tumbleweeds`, added call from `case Scene::Winter:` branch of `sim_set_scene`.
- `src/DesktopGrass.Native/src/Renderer.h` — added `pineBrush_` ComPtr member after `snowTipBrush_`.
- `src/DesktopGrass.Native/src/Renderer.cpp` — `pineBrush_` init (after `snowTipBrush_` init around line 166), `pineBrush_.Reset()` in `Cleanup` (around line 233), pine render branch (~70 lines after cactus branch around line 420), snow-tip predicate updated to also exclude `!b.isPine`.

**Pine rendering approach (Native, working):**
- Stack of N=`pineTierCount` (2..4) isoceles triangles, each filled via horizontal `DrawLine` scan-lines (`kStep = 0.5f`, line thickness `kStep * 1.5f`). Avoids per-frame PathGeometry allocation.
- Each tier width tapers linearly from `pineWidth` at base to `pineWidth * PINE_TIP_TAPER` (0.25) at top.
- Adjacent tiers overlap by `PINE_TIER_OVERLAP` (0.15).
- Each tier gets a smaller white triangle on top covering `PINE_SNOW_CAP_FRACTION` (0.30) of the tier, with capHalfW = `widthAt * 0.5 * PINE_SNOW_CAP_FRACTION * 1.4` (slightly oversized for visual presence).
- Cut/regrow: `totalH = b.pineHeight * b.cutHeight` scales everything; below `CUT_STUMP_THRESHOLD` shows a short brown vertical line of `max(2.0, pineWidth*0.25)` thickness.

**Tests state (before pine work):** Native 89/89, Win2D 89/89. Pine work adds ~5 cases per impl → expected final 94/94.

**SQL todos:** All 40 todos done as of segment start. Pine work is a new task spawned mid-segment — not yet in todos table (could add `pine-trees-feature` if needed, but it's a single focused task).
</work_done>

<technical_details>

## Pine tree design (committed in spec `9222c21`)
- **§15.1 amendment**: pines mirror cacti structurally — slot-bound, generator-promoted on Scene→Winter, cleared via universal `restore_original_variants`, cuttable/regrowable.
- **Constants (13 entries in §11 table):**
  - `PINE_PROBABILITY = 0.006`
  - `PINE_HEIGHT_MIN = 36.0`, `MAX = 72.0` (DIP)
  - `PINE_WIDTH_MIN = 16.0`, `MAX = 28.0` (DIP)
  - `PINE_TIER_COUNT_MIN = 2`, `MAX = 4`
  - `PINE_TIP_TAPER = 0.25` (top-tier width fraction of base)
  - `PINE_TIER_OVERLAP = 0.15`
  - `PINE_SNOW_CAP_FRACTION = 0.30`
  - `PINE_COLOR = 0xFF1B5E20` (dark green)
  - `PINE_PRNG_SALT = 0x50494E4550494E45ull` ("PINEPINE" packed ASCII — P=0x50, I=0x49, N=0x4E, E=0x45)
- **Per-fire draw order (4 PRNG draws on promotion):** `r`, `pineHeight`, `pineWidth`, `pineTierCount`. Locked for cross-impl bit-identity.
- **Tier count formula:** `floor(uniform(MIN, MAX+1))` then clamp to `[MIN, MAX]` (defensive — uniform can theoretically return MAX+1 due to fp).

## Cactus persistence fix (`86f5319`)
- Hoisted `restore_original_variants` loop ABOVE the switch in `sim_set_scene` (Native) / `SetScene` (Win2D). `Scene::Grass` became a no-op branch (restore already ran).
- **Lesson**: any new scene-bound variant (pine, future biome-specific overlays) MUST extend `restore_original_variants` to clear its fields — that's the universal "clean slate" path now.

## Desert grass scale (`9039d30`)
- `compute_blade_stroke` (Native) signature now takes `Scene` — applied `L *= DESERT_GRASS_HEIGHT_SCALE` when `scene == Desert && !isCactus && !isMushroom`.
- Same change inside `ComputeBladeStroke` (Win2D — already took scene).
- Updated `cut_tests.cpp` 2 callers to pass `Scene::Grass`.
- **Pine consideration**: pines are Winter-only, so DESERT scale doesn't apply. Defensive check would be `&& !isPine` but redundant since Desert never has pines.

## Win2D pine impl plan (not yet done — needs to mirror Native)
- **Constants.cs**: add 13 PINE_* constants after SNOW_TIP block (`public const`).
- **Sim.cs Blade**: add `IsPine`, `PineTierCount`, `PineHeight`, `PineWidth` after cactus fields.
- **Sim.cs**: add `RestoreOriginalVariants` pine clearing; add `GenerateePinesForWinter(Sim sim)` static method; call from `case Scene.Winter:` in `SetScene` BEFORE the snowflake scheduler init (matches Native order).
- **GrassWindow.cs**: add `_pineBrush` field (`private ID2D1SolidColorBrush? _pineBrush;`), init in `CreateGraphics` (using `_d2dFactory` is available at line 37 but only need `_dc.CreateSolidColorBrush`), dispose in `Dispose`, add pine render branch in `DrawBlade` AFTER cactus branch / BEFORE mushroom branch, update snow-tip predicate (line 413) to add `&& !b.IsPine`.

## Pine scan-fill triangle approach (Native, reusable for Win2D)
```cpp
auto drawFilledTri = [&](float cx, float baseY, float topY, float halfW, brush) {
    const float h = baseY - topY;
    if (h <= 0.0f || halfW <= 0.0f) return;
    constexpr float kStep = 0.5f;
    for (float y = baseY; y >= topY; y -= kStep) {
        const float t  = (baseY - y) / h;
        const float hw = halfW * (1.0f - t);
        if (hw <= 0.0f) continue;
        d2dContext_->DrawLine(
            D2D1::Point2F(cx - hw, y),
            D2D1::Point2F(cx + hw, y),
            brush, kStep * 1.5f);
    }
};
```
For Win2D, translate to `_dc.DrawLine(new Vector2(...), new Vector2(...), brush, kStep * 1.5f, _strokeStyle)`. PathGeometry was avoided to dodge per-frame COM allocation overhead.

## Tier stride math (might be a bug to verify)
Currently Native uses:
```cpp
const double tierStride = totalH / tierCount * (1.0 - PINE_TIER_OVERLAP)
                        + totalH / tierCount * PINE_TIER_OVERLAP / tierCount;
```
This `tierStride` variable is declared but **NOT actually used** — the loop body recomputes `baseY = gy - i * tierH * (1.0 - PINE_TIER_OVERLAP)`. Should remove the unused variable to avoid compiler warning.

## Build / test commands (proven this segment)
```pwsh
cd C:\Users\crutkas\source\DesktopGrass
Get-Process DesktopGrass.Native -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
$vsBat = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat'
$out = & $env:ComSpec /c "call `"$vsBat`" -arch=x64 -host_arch=x64 >nul && cd /d C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native && msbuild DesktopGrass.Native.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m 2>&1 && cd /d C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests && msbuild DesktopGrass.Native.Tests.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m 2>&1"
$out | Select-Object -Last 6
& 'C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\out\Release\DesktopGrass.Native.Tests.exe' --reporter compact 2>&1 | Select-Object -Last 3

# Win2D
Get-Process DesktopGrass.Win2D -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
dotnet build src\DesktopGrass.Win2D -c Release --nologo 2>&1 | Select-Object -Last 4
dotnet test  tests\DesktopGrass.Win2D.Tests -c Release --nologo --verbosity minimal 2>&1 | Select-Object -Last 3
```

## Persistent quirks (carry forward)
- **Smoke harness requires unlocked desktop** (stored memory). Don't waste time chasing pixel-variance false-fails when session is locked.
- **Don't fleet two agents in parallel on same working tree** (stored memory). The later one overwrites the earlier's csproj/vcxproj edits.
- **Native exe self-locks on rebuild** — always `Stop-Process -Id` first.
- **Win2D test csproj has `EnableDefaultCompileItems=false`** — every new test file needs explicit `<Compile Include="...">`.
- **Win2D tests file-link `Constants.cs` / `Sim.cs`** from the app project. New Sim members exposed via `public`.
- **Co-authored-by trailer**: `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>` on every commit.
- **PS gotcha**: `& $env:ComSpec /c "... | Select-Object ..."` fails. Capture to `$out` then pipe in PS.
- `restore_original_variants` is now the UNIVERSAL clean-slate path — ANY new scene-bound blade variant must add itself there.
</technical_details>

<important_files>

- `C:\Users\crutkas\source\DesktopGrass\docs\architecture.md`
  - **Spec source of truth.** §15.1 just added (committed `9222c21`). Defines pine generator algorithm, render approach, and snow-tip-exclusion amendment. §11 has 13 new PINE_* constants table entries.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Constants.h`
  - **Uncommitted**: 13 PINE_* constants added after `SNOW_TIP_COLOR` (look ~line 234).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.h`
  - **Uncommitted**: pine fields added to `Blade` after cactus block (around line 102: `isPine`, `pineTierCount`, `pineHeight`, `pineWidth`). `void generate_pines_for_winter(Sim&) noexcept;` declared after `generate_tumbleweeds` (around line 263).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.cpp`
  - **Uncommitted**: pine clearing added to `restore_original_variants` (line 168), `generate_pines_for_winter` implemented after `generate_tumbleweeds` (around line 260), call added in `case Scene::Winter:` in `sim_set_scene` (around line 450, BEFORE snowflake scheduler init).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Renderer.h`
  - **Uncommitted**: `ComPtr<ID2D1SolidColorBrush> pineBrush_;` added after `snowTipBrush_` (line 93).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Renderer.cpp`
  - **Uncommitted**: `pineBrush_` init after `snowTipBrush_` init (~line 166), `pineBrush_.Reset()` in Cleanup (~line 233), pine render branch ~70 lines added after cactus branch (around line 420-490), snow-tip predicate amended to `!b.isPine` (around line 504). **Has unused `tierStride` variable that should be removed before build.**

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Constants.cs`
  - **Not yet touched for pines.** Needs 13 PINE_* constants after SNOW_TIP block (around line 212).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Sim.cs`
  - **Not yet touched for pines.** Needs Blade pine fields after cactus block, `RestoreOriginalVariants` clearing patch, `GeneratePinesForWinter(Sim)` method, call from `case Scene.Winter:` in `SetScene`.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\GrassWindow.cs`
  - **Not yet touched for pines.** Needs `_pineBrush` field (after `_snowTipBrush` line 51), init (after `_snowTipBrush` init), dispose, pine render branch after cactus branch (around line 304), snow-tip predicate amend (line 413).

- `tests/DesktopGrass.Native.Tests/src/` and `tests/DesktopGrass.Win2D.Tests/SimTests/`
  - **Need new `pine_tests.cpp` / `PineTests.cs`** — ~5 cases each. Suggested: constants pinned, SetScene(Winter) generates pines + count is reasonable, first-pine spec-derivable snapshot via side `Prng(CANONICAL_TEST_SEED XOR PINE_PRNG_SALT)`, SetScene(Grass) clears IsPine, §12 first-blade still bit-identical. Must register new files in `DesktopGrass.Native.Tests.vcxproj` and `DesktopGrass.Win2D.Tests.csproj`.

</important_files>

<next_steps>

**Resume mid-task: pine trees implementation.**

Immediate next steps (in order):

1. **Build Native + verify**: rebuild app + tests, confirm `pineBrush_` compiles, tests still pass 89/89. Likely warning to fix: unused `tierStride` variable in Renderer.cpp's pine block — delete the declaration.

2. **Add Native test**: create `tests/DesktopGrass.Native.Tests/src/pine_tests.cpp` with ~5 cases (constants pinned, SetScene(Winter) populates pines, first-pine snapshot via side Prng, restore on Scene→Grass, §12 blade snapshot preserved). Register in `DesktopGrass.Native.Tests.vcxproj` ClCompile group. Target: 89 → 94 tests.

3. **Mirror Win2D impl**:
   - `Constants.cs`: 13 `public const` PINE_* after SNOW_TIP
   - `Sim.cs` Blade: `IsPine`, `PineTierCount`, `PineHeight`, `PineWidth`
   - `Sim.cs` `RestoreOriginalVariants`: add pine clearing
   - `Sim.cs`: add `GeneratePinesForWinter(Sim sim)` mirroring Native (4-draw promotion sequence)
   - `Sim.cs` `SetScene`: call `GeneratePinesForWinter(this);` in `case Scene.Winter:` BEFORE snowflake scheduler init
   - `GrassWindow.cs`: `_pineBrush` field + init + dispose + render branch + snow-tip predicate amend

4. **Add Win2D test**: `tests/DesktopGrass.Win2D.Tests/SimTests/PineTests.cs` mirror Native cases. Register in `DesktopGrass.Win2D.Tests.csproj` Compile group.

5. **Build + test both impls**: target 94/94 each.

6. **Single combined commit + push** with message like "Pine trees (§15.1) — both impls".

7. **Relaunch native** for user visual verification (PID 42040 is from previous version, stop + restart).

**Open considerations:**
- `PINE_PROBABILITY = 0.006` is slightly higher than `CACTUS_PROBABILITY = 0.005` — pines should feel slightly more common in Winter than cacti in Desert (forested feel). If too sparse, tweak constant.
- Snow cap geometry uses `* 1.4` multiplier for visual presence — may need tweak if caps look weird.
- Pine render uses scan-line filling (no PathGeometry) — should perform fine, but if there's frame stutter on Win2D's first scene-switch, fall back to FillGeometry via `_d2dFactory.CreatePathGeometry()`.

</next_steps>