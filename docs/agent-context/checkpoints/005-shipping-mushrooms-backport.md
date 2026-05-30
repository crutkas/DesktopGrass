<overview>
The user is iterating on **DesktopGrass**, a fun Windows desktop overlay app that renders procedural grass on top of the taskbar. The repo (`C:\Users\crutkas\source\DesktopGrass`, private at `crutkas/DesktopGrass`) has four parallel implementations sharing a single algorithmic spec — Native C++, Win2D (Vortice-based C#), WinUI3 (WindowsAppSDK C#), and WPF (vanilla .NET 10 C#). Just shipped flowers feature; now mid-flight on **mushrooms feature** — user accepted Native prototype and asked to "ship it to others" with a stump-stub addition. Strategy: lock spec first (single commit), then commit Native impl, then fleet 3 backport agents in parallel (Win2D, WinUI3, WPF), validate + commit feature + push + relaunch Native.
</overview>

<history>
1. **User: "ok, what is next?"** (pre-compact) — went with README refresh (commit `35f829a`), then flowers feature.

2. **Flowers feature shipped** (commits `23e8632` spec + `866502f` impl):
   - Spec lock in architecture.md §4/§5/§7/§11 with 4 new Blade fields, FLOWER_PRNG_SALT, palette
   - Fleeted 4 parallel sub-agents (one per impl), all succeeded
   - All 4 impls report SAME flower count 17/321 = perfect cross-impl conformance
   - Smoke unique-color counts went up across all 4 impls
   - Native exe relaunched (PID 44708) so user could see flowers

3. **User: "mushrooms sound awesome, can we quickly test something in the native app and then backport if i accept?"**
   - Designed mushroom prototype: 2.5% of slots, short ivory stem + flat-domed colored cap, 6-color palette (red/brown/tan/ivory/pink/dark-brown), rigid, cuttable+regrowable, 4th independent PRNG stream salted `0xBADC0FFEE0FACE21` (preserves all existing streams)
   - Added 6 new Blade fields to Sim.h, 11 constants + palette to Constants.h, mushroom stream draws in Sim.cpp, mushroom render preempting grass+flower path in Renderer.cpp (with brushes allocated in CreateDeviceResources + reset in DiscardDeviceResources)
   - Built Native (succeeded), launched (PID 14760)
   - Reported: prototype on screen, NOT committed (working tree only), gave list of tweak knobs

4. **User: "this is hot. stump stub the mushrooms. ship it to others"**
   - Plan: (a) add stump-stub to Native renderer (when cutHeight < CUT_STUMP_THRESHOLD draw short ivory stem stub at STUMP_HEIGHT, no cap), (b) lock spec in architecture.md (§4 fields + Mushroom palette, §5 mushroom stream + 4th PRNG, §7 mushroom render rule with short-circuit, §11 constants), (c) build + verify Native, (d) commit spec + impl as 2 commits, push, (e) fleet 3 agents (Win2D, WinUI3, WPF), (f) validate + commit feature + push + relaunch
   - Added stump-stub to Native Renderer.cpp (draws short ivory stem of STUMP_HEIGHT with mushroomStemThickness when cutHeight < CUT_STUMP_THRESHOLD, no cap)
   - Wrote spec patches to docs/architecture.md (all 8 edits done): §4 added 6 mushroom blade fields after flower fields, §4 added "Mushroom palette" subsection (6 ARGB colors + MUSHROOM_STEM_COLOR), §5 added `pm` PRNG stream init, §5 added mushroom draw block (1 unconditional probability draw + 5 conditional draws), §5 updated "field-draw order" paragraph for 4 streams, §7 added "Mushroom" subsection with full render rule including stump-stub and short-circuit-before-flower note, §11 added 13 new mushroom constant rows, §11 updated "palette table" closing line
   - Built Native app + Native tests (succeeded; both exes regenerated)
   - **Just completed build at the compaction point** — about to run Native tests, then commit
</history>

<work_done>
**Committed and pushed (current state of `main`):**
- `35f829a` — README/docs refresh (4-impl reality)
- `af70061` — BASE_AMPLITUDE 3.0 → 3.3 (+10% passive sway)
- `d2f8f61` — Added 4th impl: WPF
- `df38d26` — Upgrade all C# projects to .NET 10
- `9fdf28b` — Soften gust factor 1.5 → 0.75
- `040dbaf` — Chord-preserving blade bend
- `23e8632` — Spec lock for flower feature
- `866502f` — Flower feature in all 4 impls

**In flight (UNCOMMITTED working tree changes for mushrooms):**
- `src/DesktopGrass.Native/src/Constants.h` — 11 new mushroom constants + MUSHROOM_PALETTE[6] + MUSHROOM_STEM_COLOR
- `src/DesktopGrass.Native/src/Sim.h` — 6 new Blade fields (isMushroom, mushroomCapColorIdx, mushroomCapWidth, mushroomCapHeight, mushroomStemHeight, mushroomStemThickness) — all defaulted
- `src/DesktopGrass.Native/src/Sim.cpp` — `pMushroom` PRNG stream init + per-blade draws (1 unconditional + 5 conditional)
- `src/DesktopGrass.Native/src/Renderer.h` — added `mushroomCapBrushes_[MUSHROOM_PALETTE_SIZE]` and `mushroomStemBrush_` member ComPtrs
- `src/DesktopGrass.Native/src/Renderer.cpp` — brush allocation in CreateDeviceResources, brush reset in DiscardDeviceResources, mushroom render block at top of DrawGrass loop with stump-stub short-circuit (drawn before grass blade compute) + continue
- `docs/architecture.md` — 8 edits across §4/§5/§7/§11 locking the mushroom spec

**Builds verified just now:**
- DesktopGrass.Native.exe built clean (Release x64)
- DesktopGrass.Native.Tests.exe built clean (Release x64)

**Pending (in order):**
- [ ] Run Native tests to confirm no regression
- [ ] Commit spec (docs/architecture.md only) as standalone commit, push
- [ ] Commit Native impl (5 Native files) as separate commit, push
- [ ] Fleet 3 background agents (Win2D, WinUI3, WPF) with locked spec citation
- [ ] On all 3 returning: validate (build all + tests all + smoke all)
- [ ] Commit final feature commit + push
- [ ] Relaunch Native exe so user can see mushrooms with stub on cut
- [ ] task_complete

**No issues encountered yet.** Build was clean. The stump-stub logic is straightforward (line 16 of the mushroom block: check cutHeight < CUT_STUMP_THRESHOLD, draw stem-only at STUMP_HEIGHT, continue).
</work_done>

<technical_details>
## Mushroom feature design (now locked in `docs/architecture.md`)

**New constants (§11):**
```
MUSHROOM_PROBABILITY        = 0.025          // 2.5%, vs 0.04 for flowers
MUSHROOM_CAP_WIDTH_MIN/MAX  = 4.0 / 8.0      // DIP, radius X
MUSHROOM_CAP_HEIGHT_MIN/MAX = 2.5 / 5.0      // DIP, radius Y (flatter than width)
MUSHROOM_STEM_HEIGHT_MIN/MAX = 4.0 / 10.0   // DIP
MUSHROOM_STEM_THICKNESS_MIN/MAX = 2.0 / 4.0 // DIP
MUSHROOM_PALETTE_SIZE       = 6
MUSHROOM_PRNG_SALT          = 0xBADC0FFEE0FACE21UL
MUSHROOM_STEM_COLOR         = 0xFFF5F5DC     // beige/ivory ARGB
MUSHROOM_PALETTE = [0xFFD32F2F red, 0xFF8D6E63 brown, 0xFFC9A66B tan,
                    0xFFFFF8E1 ivory, 0xFFE57373 dusty pink, 0xFF6D4C41 dark brown]
```

**New Blade fields (additive, defaulted):**
```
isMushroom              = false
mushroomCapColorIdx     = 0
mushroomCapWidth        = 0.0   // radius X (DIP)
mushroomCapHeight       = 0.0   // radius Y (DIP)
mushroomStemHeight      = 0.0   // DIP
mushroomStemThickness   = 0.0   // DIP
```

**Conformance-critical PRNG ordering** (now 4 streams):
```
mainPrng     = Prng.Init(seed)                        // unchanged
flowerPrng   = Prng.Init(seed ^ FLOWER_PRNG_SALT)     // unchanged
regrowPrng   = Prng.Init(seed ^ REGROW_PRNG_SALT)     // unchanged
mushroomPrng = Prng.Init(seed ^ MUSHROOM_PRNG_SALT)   // NEW

for each blade slot:
    // MAIN: step, height, thickness, hue, swayPhase, stiffness (unchanged)
    // REGROW: regrowDelay, regrowDuration (unchanged)
    // FLOWER: prob check; if flower: headColor, headRadius, heightBonus (unchanged)
    // MUSHROOM (NEW):
    //   isMushroom = pm.NextDouble() < MUSHROOM_PROBABILITY  [always 1 draw]
    //   if isMushroom:
    //     capColorIdx = (int)(pm.NextU64() % 6)              [conditional]
    //     capWidth    = lerp(MIN, MAX, pm.NextDouble())
    //     capHeight   = lerp(MIN, MAX, pm.NextDouble())
    //     stemHeight  = lerp(MIN, MAX, pm.NextDouble())
    //     stemThickness = lerp(MIN, MAX, pm.NextDouble())
    //   else: defaults
```

**Render contract (§7):**
- Mushroom slots **short-circuit before grass+flower path** in the renderer
- If `cutHeight < CUT_STUMP_THRESHOLD`: draw short ivory stem at STUMP_HEIGHT (matches grass stub behavior); NO cap; return
- Else: draw stem from `(baseX, groundY)` to `(baseX, groundY - stemHeight*cutHeight)` with `stemThickness` and `MUSHROOM_STEM_COLOR`; draw filled ellipse cap at `(baseX, groundY - stemHeight*cutHeight)` with radii `(capWidth*cutHeight, capHeight*cutHeight)` and `MUSHROOM_PALETTE[capColorIdx]`
- Cap is FILLED (not stroked); stem caps can be round or butt
- **If both isMushroom and isFlower are true: mushroom wins** (spec explicitly states this)
- Mushrooms are rigid: `effectiveLean` is ignored
- Mushrooms are cuttable+regrowable via existing cutHeight machinery (no extra cut/click logic needed)

**Stream conformance gate**: All 4 impls MUST report the same `(isMushroom, capColor, capWidth, capHeight, stemHeight, stemThickness)` sequence per canonical seed. Expected mushroom count for canonical seed (seed=0x6B6173746F, monitorWidth=1920, density=1.0, n=321): mean = 321 * 0.025 = ~8, sd = sqrt(321*0.025*0.975) ≈ 2.80, so 3σ acceptable range is roughly [0, 17]. The actual exact count for the canonical seed needs to be discovered by running Native first, then the 3 backport agents must match.

**Build commands (proven):**
```pwsh
$vsBat = 'C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat'
# Stop running exe first - it locks itself on rebuild
Get-Process DesktopGrass.Native -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
# Build app
& $env:ComSpec /c "call `"$vsBat`" -arch=x64 -host_arch=x64 >nul && cd /d C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native && msbuild DesktopGrass.Native.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m"
# Build tests
& $env:ComSpec /c "call `"$vsBat`" -arch=x64 -host_arch=x64 >nul && cd /d C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests && msbuild DesktopGrass.Native.Tests.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m"
# Run tests
& 'C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\out\Release\DesktopGrass.Native.Tests.exe' --reporter compact

# C# build/test
dotnet build src\DesktopGrass.{Win2D,WinUI3,WPF}\... -c Release --nologo
dotnet test tests\DesktopGrass.{Win2D,WinUI3,WPF}.Tests\... -c Release --nologo --verbosity minimal

# Smoke
pwsh -NoProfile -File 'C:\Users\crutkas\source\DesktopGrass\tests\smoke\Run-SmokeTests.ps1' -Target All -Configuration Release
```

**Naming convention quirk**: 
- Native/Win2D/WPF use SCREAMING_SNAKE_CASE constants (`MUSHROOM_PROBABILITY`, `MUSHROOM_PALETTE`)
- **WinUI3 uses PascalCase** constants (`MushroomProbability`, `MushroomPalette`) — agent must match existing style in `src/DesktopGrass.WinUI3/Constants.cs`

**Fleet agent recipe (proven from flowers)**:
- Background mode, all 3 launched in parallel
- Each gets: full design from this summary, files to edit, exact code skeletons, test file changes, build/test commands, validation gate, instructions NOT to commit/push
- Agents take 5-10 minutes each, all return roughly together
- Match flower count check pattern: expect SAME mushroom count across all 4 impls

**Renderer integration patterns per impl (from flowers work)**:
- **Native**: D2D `DrawLine` + `FillEllipse` directly on `d2dContext_`. Allocate `mushroomCapBrushes_[6]` + `mushroomStemBrush_` parallel to `flowerHeadBrushes_`. Reset in `DiscardDeviceResources`.
- **Win2D (Vortice)**: `_dc.DrawLine(p1, p2, brush, thickness, strokeStyle)` and `_dc.FillEllipse(new Ellipse(center, rx, ry), brush)`. Allocate `_mushroomCapBrushes` + `_mushroomStemBrush` (ID2D1SolidColorBrush). Dispose in `Dispose()`.
- **WinUI3**: Uses Composition API. Allocate `CompositionColorBrush[]` for caps + one for stem. Per-mushroom: create `CompositionLineGeometry` for stem (or just a tall thin rectangle) + `CompositionEllipseGeometry` for cap. Pattern matches `_flowerHeadGeometries` in `GrassRenderer.cs` — allocate upfront, mutate per frame (set radius to Vector2.Zero when invisible). Renderer file: `src/DesktopGrass.WinUI3/GrassRenderer.cs`.
- **WPF**: `dc.DrawLine(pen, p1, p2)` + `dc.DrawEllipse(brush, null, center, rx, ry)`. Allocate 6 frozen SolidColorBrush + 1 stem brush parallel to `_brushes` in GrassCanvas constructor.

**Test additions needed per impl**:
- Determinism test (same seed → same mushroom sequence)
- Probability count within 3-sigma
- Non-mushroom blades have zero/default mushroom fields
- Main-stream non-interference (pin first blade's BaseX=4.941073726820111, Height=24.469991818248864, Thickness=1.5829214329729786, Hue=3)
- Native: `mushroom_tests.cpp` parallel to `flower_tests.cpp`; extend `blade_gen_tests.cpp` `requireBladeEquals` helper with 6 new field checks
- Win2D/WinUI3/WPF: New `MushroomTests.cs` parallel to `FlowerTests.cs`; extend `BladeGenTests.cs`
- WinUI3 only: add new test files to `.csproj` explicit Compile Include (unlike SDK-style projects)
- All impls: existing CutTests / StrokeTests using `default(Blade)` may need `HeightBonus = 1.0` already added during flowers; should not need new mushroom field defaults since defaults are all zero/false

**Native snapshot impact**: `tests/DesktopGrass.Native.Tests/src/snapshot_data.h` currently records `(baseX, height, thickness, hue, swayPhaseOffset, stiffness, isFlower, flowerHeadColorIdx, flowerHeadRadius, heightBonus)`. Mushroom fields are NOT in the snapshot — could be added by extending snapshot_gen.cpp with 6 more columns and regenerating, OR could skip and let the runtime tests cover it. **Recommend NOT extending snapshot** since the per-impl FlowerTests-equivalent tests cover determinism + count, and snapshot regen is expensive. (Confirm: flower added 4 cols; for mushroom skip the snapshot extension to keep the diff small.)

**Important git/process notes**:
- Native exe locks itself on rebuild — MUST `Get-Process DesktopGrass.Native | ForEach-Object { Stop-Process -Id $_.Id -Force }` first
- Stop-Process restriction: must use `-Id <pid>`, never `-Name`
- Co-authored-by trailer for all commits: `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>`
- LF→CRLF warnings on WPF/WinUI3 files are benign
</technical_details>

<important_files>
- `C:\Users\crutkas\source\DesktopGrass\docs\architecture.md` (~520 lines)
  - **Authoritative spec for all 4 impls.** Mushroom feature spec patches in working tree across §4 (blade fields lines ~109-117 add 6 fields after heightBonus; Mushroom palette section after Flower palette around line 130), §5 (added `pm` Prng init, added mushroom draw block in pseudocode, updated field-draw-order paragraph for 4 streams around lines 200-220), §7 (added "Mushroom" subsection with full pseudocode after the chord-preservation paragraph around line 280), §11 (added 13 mushroom constant rows after FLOWER_PRNG_SALT around line 530; updated palette closing line ~540).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Constants.h`
  - Added at end of namespace (around line 90+): 11 new mushroom constants + MUSHROOM_PALETTE[6] + MUSHROOM_STEM_COLOR.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.h`
  - In `struct Blade`, after flower fields (`heightBonus = 1.0`), before `effectiveLean`: added 6 mushroom fields with defaults.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.cpp`
  - In `generate_blades`: after `Prng pFlower; prng_init(pFlower, seed ^ FLOWER_PRNG_SALT);` added `Prng pMushroom; prng_init(pMushroom, seed ^ MUSHROOM_PRNG_SALT);`. In the loop, after the flower draws block, added the mushroom draws block (1 unconditional + 5 conditional draws + else branch with defaults).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Renderer.h` (line ~85)
  - Added `ComPtr<ID2D1SolidColorBrush> mushroomCapBrushes_[MUSHROOM_PALETTE_SIZE];` and `ComPtr<ID2D1SolidColorBrush> mushroomStemBrush_;` after `flowerHeadBrushes_`.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Renderer.cpp`
  - CreateDeviceResources (~line 125-145): added mushroom cap brushes loop + stem brush creation after the flower brushes loop.
  - DiscardDeviceResources (~line 185): added `for (auto& b : mushroomCapBrushes_) b.Reset(); mushroomStemBrush_.Reset();`
  - DrawGrass (~line 315): added mushroom render block at the TOP of the per-blade for loop, BEFORE `compute_blade_stroke`. Block: if `isMushroom`, check stump-stub short-circuit (draw stem stub at STUMP_HEIGHT, continue), else draw stem + filled cap, continue. Preempts grass+flower for that slot.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Constants.cs`, `Sim.cs`, `GrassWindow.cs`
  - Untouched yet; backport target #1. SCREAMING_SNAKE_CASE constants. `DrawBlade` ~line 203; renderer uses `_dc.DrawLine` + needs `_dc.FillEllipse` for cap, allocate 6 `_mushroomCapBrushes` + 1 `_mushroomStemBrush`.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.WinUI3\Constants.cs`, `Sim.cs`, `GrassRenderer.cs`
  - Untouched yet; backport target #2. **PascalCase** constants. Renderer is `GrassRenderer.cs`, uses Composition (`CompositionPathGeometry`, `CompositionEllipseGeometry`). Two stroke call sites: static `ComputeBladeStroke` ~line 237, instance `GetStroke` ~line 411. For mushrooms, allocate `_mushroomCapGeometries` + `_mushroomStemGeometries` parallel to `_flowerHeadGeometries`.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.WPF\Constants.cs`, `Sim.cs`, `GrassCanvas.cs`
  - Untouched yet; backport target #3. SCREAMING_SNAKE_CASE. `GrassCanvas.OnRender(DrawingContext dc)` — add `dc.DrawLine` for stem + `dc.DrawEllipse` for cap, with 6 frozen `_mushroomCapBrushes` + 1 `_mushroomStemBrush`.

- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\src\flower_tests.cpp`
  - Template for `mushroom_tests.cpp`. Pattern: 3 TEST_CASEs — determinism, count within 3-sigma, main-stream non-interference.

- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.{Win2D,WinUI3,WPF}.Tests\SimTests\FlowerTests.cs`
  - Templates for `MushroomTests.cs` per impl. Pattern: 4 [Fact] tests — determinism, count within 3-sigma, main-stream non-interference, non-mushroom blades have zero fields.

- `C:\Users\crutkas\source\DesktopGrass\tests\smoke\Run-SmokeTests.ps1`
  - No changes needed (supports all 4 impls). Expected mushroom impact on unique-color counts: +6 colors per impl × small count.
</important_files>

<next_steps>
**Immediate next steps in order:**

1. **Run Native tests** to confirm no regression: `& 'C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\out\Release\DesktopGrass.Native.Tests.exe' --reporter compact` — expect 45 cases / 63,831+ assertions, all pass. Mushroom fields default to zero so existing tests should not break.

2. **Note actual mushroom count for canonical seed** — write a quick scratch program or just inspect once Native is running. Estimated ~8. This is the number all 4 impls must match for conformance.

3. **Commit spec standalone**: `git add docs/architecture.md; git commit -m "Lock spec for mushroom feature..."` then `git push`. This gives agents a SHA to anchor on.

4. **Commit Native impl**: `git add src/DesktopGrass.Native/; git commit -m "Mushrooms in Native (prototype passes user review)..."` then push. (Optional: include test additions for Native here too — `tests/DesktopGrass.Native.Tests/src/mushroom_tests.cpp` + extend `blade_gen_tests.cpp` `requireBladeEquals`, add to `.vcxproj`.)

5. **Fleet 3 background agents** (general-purpose, all in parallel) — Win2D, WinUI3, WPF. Each prompt should include:
   - The locked spec SHA reference
   - Full design from `<technical_details>` above
   - Naming convention reminder (PascalCase for WinUI3)
   - Files to edit (Constants, Sim, renderer)
   - Exact code skeletons (constants block, generation snippet, render snippet)
   - Test file additions
   - Build + test commands
   - Validation gate (mushroom count must match Native's count, all existing tests must still pass, first blade main-stream pinned values must hold)
   - For each: also extend BladeGenTests determinism test, add new MushroomTests.cs, update .csproj for WinUI3 explicit Compile Include
   - DO NOT commit/push

6. **After all 3 return**: run smoke `-Target All`, expect 4 PASS. Verify all 4 impls report same mushroom count.

7. **Commit feature commit** with message describing the mushroom feature, the 2.5% probability, separate PRNG stream conformance, mushroom-preempts-flower rule, stump-stub on cut, and validation results. Push.

8. **Relaunch Native** so user can verify: `Start-Process 'C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\out\Release\DesktopGrass.Native.exe' -PassThru`.

9. **Call `task_complete`** with summary.

**Open consideration**: Should the Native commit (step 4) include the new `mushroom_tests.cpp` and `blade_gen_tests.cpp` extension, or save those for the feature commit (step 7)? Recommend including tests with impl for tidy history — each impl commit ships its own tests; the final feature commit is just the orchestration message.

**No blockers** — straight execution from here.
</next_steps>