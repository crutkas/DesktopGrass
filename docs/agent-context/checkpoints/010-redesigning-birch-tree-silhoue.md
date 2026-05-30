<overview>
DesktopGrass is a "just for fun" Windows overlay (private repo `crutkas/DesktopGrass`, working tree `C:\Users\crutkas\source\DesktopGrass`, branch `main`) painting procedural grass/flowers/mushrooms/cacti/pines across every monitor's bottom strip — click-through, over the taskbar. Two parallel impls — Native (C++/Direct2D) and Win2D (C#/Vortice) — share a single locked spec in `docs/architecture.md`. This segment: shipped Winter biome polish (-50% grass, +25% pine height, mushroom suppression), then added a second tree variant (birch) at +25% density, but user reported the birch "looks like a skeleton" — I'm now mid-redesign to break the cross/microphone-stand silhouette by replacing horizontal T-arm branches with an upward-angled branch fan with snow-tipped blobs.
</overview>

<history>
1. **User**: "winter needs shorter grass, decrease by 50%, make trees 25% taller. should there be mushrooms in winter?"
   - Added `WINTER_GRASS_HEIGHT_SCALE = 0.5` mirroring Desert convention (applied in `compute_blade_stroke` / `ComputeBladeStroke` for non-pine non-mushroom blades when scene == Winter)
   - Bumped `PINE_HEIGHT_MIN` 36→45, `MAX` 72→90 (+25%)
   - Recommended NO mushrooms in Winter (snowy biome shouldn't have fungi); suppressed via `b.isMushroom = false` in `generate_pines_for_winter` loop (every blade, not just promoted slots)
   - Added Winter mushroom-suppression + Winter scale tests (had to rename "Winter constants are pinned" → "Winter grass height scale is pinned" because winter_tests.cpp already used that name)
   - Spec amended: §15 + §11 entries for DESERT/WINTER_GRASS_HEIGHT_SCALE + pine height bumps
   - Both impls 96/96; committed `fd458b1`, pushed, relaunched (PID 41792)

2. **User**: "add 25% more trees too but can we have another variant / style tree as well?"
   - Bumped `PINE_PROBABILITY` 0.006 → 0.0075 (+25%)
   - Designed birch variant: tall thin white trunk + dark horizontal bark marks + 2 pairs of horizontal branch stubs + small snow cap on top
   - Added `treeVariant: byte` field to Blade (0=pine, 1=birch); `BIRCH_VARIANT_PROBABILITY = 0.30`
   - Extended PRNG generator to 5 draws: `r, variantDraw, height, width, tierDraw` (locked order). Width range varies by variant (PINE_WIDTH_* vs BIRCH_TRUNK_WIDTH_*); tierDraw fires for birches too so PRNG state stays bit-identical regardless of variant outcome.
   - Birch constants: `BIRCH_TRUNK_WIDTH_MIN=4.0/MAX=7.0`, `BIRCH_BARK_MARK_COUNT=4`, `BIRCH_BRANCH_PAIRS=2`, `BIRCH_SNOW_CAP_FRACTION=0.18`, `BIRCH_BARK_COLOR=0xFFEFEFE6`, `BIRCH_MARK_COLOR=0xFF2A2A28`
   - Native renderer: added `birchBarkBrush_` + `birchMarkBrush_` ComPtrs (initialized in CreateGraphics, reset in Cleanup); split pine render branch at `if (b.treeVariant == 1)` for new birch path; removed unused `tierStride` variable
   - Win2D renderer: matching `_birchBarkBrush` + `_birchMarkBrush` fields/init/dispose; mirror birch render
   - Tests reworked: `ExpectedPine` → `ExpectedTree` with `variant` field; tests verify both pine AND birch appear over canonical seed; "First tree matches PRNG snapshot" asserts treeVariant + variant-dependent width
   - Spec §15.1 amended with 5-draw order + birch variant subsection; §11 adds 9 BIRCH_* constants
   - Both impls 98/98; committed `1c1a211`, pushed, relaunched (PID 50868)

3. **User**: "this looks like a skeleton"
   - Diagnosed: full-width horizontal bark marks = ribs; symmetric horizontal T-arm branches = arms reaching out → cross/skeleton silhouette
   - Started redesign: `BIRCH_BARK_MARK_COUNT` 4→8 with shorter `BIRCH_BARK_MARK_LENGTH_FRAC=0.40`; `BIRCH_BRANCH_PAIRS` 2→3
   - Bumped constants in both impls

4. **User attached screenshot** (`copilot-image-7350a7.png`) confirming microphone-stand/antenna look even after the first iteration
   - Pivoted: scrapped the symmetric pairs approach
   - Updated constants to `BIRCH_BARK_MARK_COUNT=5`, `BIRCH_BARK_MARK_LENGTH_FRAC=0.50`, replaced `BIRCH_BRANCH_PAIRS=3` with `BIRCH_BRANCH_COUNT=6` (total branches, not pairs — non-symmetric layout)
   - **Compaction triggered here mid-redesign** — renderer code not yet updated; tests reference old `BIRCH_BRANCH_PAIRS` constant which no longer exists
</history>

<work_done>
**Commits this segment (chronological, all pushed):**
- `fd458b1` — Winter biome polish: -50% grass, +25% pines, no mushrooms (96/96 each impl)
- `1c1a211` — Birch tree variant + 25% more trees in Winter (98/98 each impl)

**Uncommitted edits in working tree (birch v2 redesign, IN PROGRESS):**
- `src/DesktopGrass.Native/src/Constants.h` — `BIRCH_BARK_MARK_COUNT=5`, `BIRCH_BARK_MARK_LENGTH_FRAC=0.50`, `BIRCH_BRANCH_COUNT=6` (was `BIRCH_BRANCH_PAIRS=3`)
- `src/DesktopGrass.Win2D/Constants.cs` — same constant changes

**Files with stale references that need fixing:**
- `src/DesktopGrass.Native/src/Renderer.cpp` — still uses `BIRCH_BARK_MARK_COUNT` (now 5, OK) and `BIRCH_BRANCH_PAIRS` (now removed, COMPILE ERROR). Birch render block ~lines 425-510 needs complete rewrite for upward branch fan.
- `src/DesktopGrass.Win2D/GrassWindow.cs` — same stale reference + needs rewrite
- `tests/DesktopGrass.Native.Tests/src/pine_tests.cpp` — `BIRCH_BRANCH_PAIRS` constant assertion (around line 78); need to update to `BIRCH_BRANCH_COUNT`
- `tests/DesktopGrass.Win2D.Tests/SimTests/PineTests.cs` — `BIRCH_BRANCH_PAIRS` constant assertion in `BirchConstantsArePinned`; need to update
- `docs/architecture.md` §11 table + §15.1 birch subsection — references `BIRCH_BRANCH_PAIRS`; need to update

**Build/test state**: Last clean build was at commit `1c1a211` (both 98/98). Current working tree WILL NOT COMPILE — `BIRCH_BRANCH_PAIRS` removed from constants but still referenced in renderers and tests.

**Native process**: PID 50868 from `1c1a211` may still be running (last build).
</work_done>

<technical_details>

## Birch v2 redesign plan (where compaction interrupted)

User confirmed via screenshot that birch still reads as skeleton/antenna. New design:

**Bark marks (lower priority fix):**
- 5 short centered horizontal dashes (`BIRCH_BARK_MARK_COUNT=5`)
- Max length = `trunkW * BIRCH_BARK_MARK_LENGTH_FRAC` (0.50)
- Vary lengths per mark via fixed pattern array (e.g., `{0.20, 0.40, 0.30, 0.45, 0.25}` multipliers) for "broken" look without going off-trunk
- Keep centered horizontally — offsetting L/R goes outside the thin 5-DIP trunk

**Branch fan (main fix):**
- Replace symmetric T-pairs with hardcoded 6-branch table:
  ```
  struct Branch { float trunkFrac; float angleDeg; float side; float lenMul; };
  static const Branch kBranches[BIRCH_BRANCH_COUNT] = {
      {0.45f, 35.0f, +1.0f, 1.2f},
      {0.55f, 50.0f, -1.0f, 1.4f},
      {0.65f, 25.0f, +1.0f, 1.6f},
      {0.72f, 60.0f, -1.0f, 1.0f},
      {0.80f, 20.0f, +1.0f, 1.1f},
      {0.85f, 45.0f, -1.0f, 0.8f},
  };
  ```
- Angles measured from VERTICAL (0° = straight up, +90° = horizontal). Range 15-60° = upward-angled, no horizontal arms.
- Endpoint: `ex = baseX + side * blen * sin(ang); ey = sy - blen * cos(ang)` (minus on y because pixel y grows DOWN, and we want branches going UP visually)
- `branchBaseLen = trunkW * 3.0`, multiplied by `lenMul` → branches 13-26 DIP long for avg trunkW=5.5
- Branch stroke width: `max(1.0, trunkW * 0.35)`
- **Snow blob at each tip**: `d2dContext_->FillEllipse(D2D1::Ellipse(D2D1::Point2F(ex, ey), snowR, snowR), snowTipBrush_.Get());` where `snowR = max(1.5, trunkW * 0.65)`
- Win2D equivalent: `_dc.FillEllipse(new Ellipse(new Vector2(ex, ey), snowR, snowR), _snowTipBrush!);`

**Apex snow cap:**
- Still draw a small white triangle at the trunk top via existing `drawFilledTri` / `DrawFilledPineTri` helper, base width ~`trunkW * 1.4`, height = `totalH * BIRCH_SNOW_CAP_FRACTION` (0.18)

## Compile error to fix immediately
Native Renderer.cpp uses `BIRCH_BRANCH_PAIRS` constant (now removed). Win2D GrassWindow.cs uses `Constants.BIRCH_BRANCH_PAIRS` (now removed). Both will fail to build. Fix by replacing the entire birch render block with the new branch-fan design.

## Test constant assertions to update
Both `pine_tests.cpp` and `PineTests.cs` have `BirchConstantsArePinned` that asserts `BIRCH_BRANCH_PAIRS == 2`. Change to `BIRCH_BRANCH_COUNT == 6`. Also remove/update assertions for `BIRCH_BARK_MARK_COUNT == 4` → `== 5`. Add assertion for `BIRCH_BARK_MARK_LENGTH_FRAC == 0.50`.

## Spec to update
- `docs/architecture.md` §11 constants table: rename `BIRCH_BRANCH_PAIRS` row to `BIRCH_BRANCH_COUNT` (value 6), update `BIRCH_BARK_MARK_COUNT` value to 5, add `BIRCH_BARK_MARK_LENGTH_FRAC` row (0.50)
- §15.1 birch variant subsection: rewrite branch description from "BIRCH_BRANCH_PAIRS pairs of short angled branch stubs near the upper third (alternating sides)" to "BIRCH_BRANCH_COUNT upward-angled branches with snow-tipped blobs, hand-tuned positions/angles for an organic deciduous-tree silhouette"

## Persistent quirks (carry forward)
- **Smoke harness requires unlocked desktop** (memory). Pixel-variance fails when session locked.
- **Don't fleet two agents in parallel on same working tree** (memory). They overwrite each other's csproj edits.
- **Native exe self-locks on rebuild** — always `Stop-Process -Id` first.
- **LNK1000 sometimes hits Native tests** after large changes; `msbuild ... -t:Rebuild` clears it.
- **Win2D tests** file-link `Constants.cs` / `Sim.cs` from app project; `EnableDefaultCompileItems=false` so new test files need explicit `<Compile Include>`.
- **Co-authored-by trailer** required on every commit: `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`
- **PS gotcha**: `& $env:ComSpec /c "... | Select-Object ..."` fails. Capture to `$out`, pipe in PS.
- **PRNG draw order is locked across impls** — any change to `generate_pines_for_winter` / `GeneratePinesForWinter` draw sequence breaks cross-impl bit-identity. Pure rendering changes (the birch redesign) DO NOT touch PRNG so are safe.
- `restore_original_variants` is the UNIVERSAL clean-slate path — any new scene-bound blade variant must add itself there. Pine already added (`isPine, pineTierCount, treeVariant, pineHeight, pineWidth`).
- **C++ angle math**: pixel y grows DOWN; `sin(ang)` for horizontal extent, `cos(ang)` for vertical with NEGATIVE sign for upward (since "up" = smaller y).
- **trunkW arithmetic**: birch trunkW is 4-7 DIP (skinny). Tipped snow blobs and branch widths derive from it via multipliers (`* 0.65`, `* 0.35`, etc.) so they scale with trunk.

## Build/test commands (proven this segment)
```pwsh
cd C:\Users\crutkas\source\DesktopGrass
Get-Process DesktopGrass.Native -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
$vsBat = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat'
$out = & $env:ComSpec /c "call `"$vsBat`" -arch=x64 -host_arch=x64 >nul && cd /d C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native && msbuild DesktopGrass.Native.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m 2>&1 && cd /d C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests && msbuild DesktopGrass.Native.Tests.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m 2>&1"
$out | Select-Object -Last 8
& 'C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\out\Release\DesktopGrass.Native.Tests.exe' --reporter compact 2>&1 | Select-Object -Last 3

dotnet test tests\DesktopGrass.Win2D.Tests -c Release --nologo --verbosity minimal 2>&1 | Select-Object -Last 4
```

## Open considerations
- After branch fan redesign, the existing apex snow cap may visually overlap with the topmost branch's snow blob. Consider shrinking apex cap or omitting it.
- BIRCH_VARIANT_PROBABILITY=0.30 (30% of trees are birches). If new birch silhouette looks good, may want to bump to ~40% for more visual mix.
- Hand-tuned branch table is hardcoded in renderer (not a constant). Acceptable since it's tightly coupled to visual design — but means asymmetric branch layout isn't tweakable from Constants.h.

</technical_details>

<important_files>

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Constants.h`
  - **Uncommitted**: BIRCH_BARK_MARK_COUNT=5, BIRCH_BARK_MARK_LENGTH_FRAC=0.50, BIRCH_BRANCH_COUNT=6 (renamed from PAIRS=3). Lines ~258-267.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Constants.cs`
  - **Uncommitted**: same constant updates around lines ~230-235.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Renderer.cpp`
  - **MUST FIX**: still references `BIRCH_BRANCH_PAIRS` (now removed → compile error). Birch render block at ~lines 425-510 needs complete rewrite using new branch fan table (see Technical Details). Existing `drawFilledTri` lambda is local to the pine branch — reuse it for the apex cap.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\GrassWindow.cs`
  - **MUST FIX**: same compile-error risk for `Constants.BIRCH_BRANCH_PAIRS`. Birch render block at ~lines 353-410 needs rewrite mirroring Native. Helper `DrawFilledPineTri` is available for the apex cap.

- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\src\pine_tests.cpp`
  - **MUST FIX**: `Birch constants are pinned` test (TEST_CASE around line 70) asserts `BIRCH_BARK_MARK_COUNT == 4` (now 5) and `BIRCH_BRANCH_PAIRS == 2` (now removed → `BIRCH_BRANCH_COUNT == 6`). Add assertion for `BIRCH_BARK_MARK_LENGTH_FRAC == 0.50`.

- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Win2D.Tests\SimTests\PineTests.cs`
  - **MUST FIX**: `BirchConstantsArePinned` Fact mirrors the Native test issues. Update assertions accordingly.

- `C:\Users\crutkas\source\DesktopGrass\docs\architecture.md`
  - **MUST UPDATE**: §11 constants table BIRCH_* rows; §15.1 birch variant description (rewrite branch design from "symmetric pairs of horizontal stubs" to "upward-angled branch fan with snow-tipped blobs"). Both currently still describe the v1 design.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.cpp`
  - Generator (`generate_pines_for_winter`) is **STABLE** at `1c1a211`. Do not touch PRNG draw order. The 5-draw sequence `r, variantDraw, height, width, tierDraw` is locked.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Sim.cs`
  - `GeneratePinesForWinter` mirrors Native — stable, do not touch.

</important_files>

<next_steps>

**Resume mid-task: birch v2 redesign (working tree won't compile until renderers + tests updated).**

Immediate next steps (in order):

1. **Rewrite Native birch render branch** in `Renderer.cpp` (lines ~425-510, inside `if (b.treeVariant == 1) { ... }`):
   - Replace symmetric T-arms with hardcoded `kBranches[BIRCH_BRANCH_COUNT]` table from Technical Details
   - For each branch: `DrawLine(start, end, birchBarkBrush_, branchW)` + `FillEllipse(end, snowR, snowTipBrush_)` for snow tip
   - Use angle-from-vertical math: `ex = baseX + side * blen * sin(ang)`, `ey = sy - blen * cos(ang)`
   - Bark marks: 5 short centered dashes with varied lengths per `{0.20, 0.40, 0.30, 0.45, 0.25}` pattern × `BIRCH_BARK_MARK_LENGTH_FRAC` × `trunkW`
   - Keep apex snow triangle (smaller — maybe `* 1.2` width instead of `* 1.4`)

2. **Mirror in Win2D `GrassWindow.cs`** birch branch (lines ~353-410):
   - Same branch table, same math, use `_dc.FillEllipse(new Ellipse(new Vector2(ex, ey), snowR, snowR), _snowTipBrush!)` for tips

3. **Fix Native pine_tests.cpp** `Birch constants are pinned`: change `BIRCH_BARK_MARK_COUNT == 4` → `== 5`, replace `BIRCH_BRANCH_PAIRS == 2` with `BIRCH_BRANCH_COUNT == 6`, add `BIRCH_BARK_MARK_LENGTH_FRAC == 0.50`

4. **Fix Win2D PineTests.cs** `BirchConstantsArePinned`: same updates

5. **Update spec** `docs/architecture.md`:
   - §11 table: rename `BIRCH_BRANCH_PAIRS` row → `BIRCH_BRANCH_COUNT` value 6; update `BIRCH_BARK_MARK_COUNT` to 5; add `BIRCH_BARK_MARK_LENGTH_FRAC` row
   - §15.1 birch subsection: rewrite branch design language

6. **Build + test both impls**: target 98/98 each (no new tests, just modified constants assertion). LNK1000 may hit Native — use `-t:Rebuild` if so.

7. **Commit + push** with message like "Birch redesign: upward branch fan with snow-tipped blobs (fix skeleton silhouette)"

8. **Relaunch Native** (Stop PID 50868, restart) for user visual verification

**Open considerations**:
- Apex snow cap may collide visually with topmost branch blob. If it looks weird, omit the apex cap and let the highest branch's snow blob serve as the crown.
- If the new design still doesn't look right, fallback ideas: (a) drop branches entirely and just do trunk + thicker snow cap on top; (b) try a totally different second variant — round-canopy deciduous (brown trunk + white round blob = stylized snowy tree).

</next_steps>