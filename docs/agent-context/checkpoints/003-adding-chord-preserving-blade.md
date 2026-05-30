<overview>
Iteratively tuning the **DesktopGrass** desktop-overlay app (3 parallel impls: Native C++, Win2D C#, WinUI3 C#) for visual quality. Each tuning round is **fleeted out** to 4 parallel sub-agents (one per impl + spec), then validated with cross-impl smoke + committed + pushed. Most recent ask: blades still "stretch" when bent because tip stays at the same Y while moving sideways, making the painted Bezier path longer than the blade. Fixing with **chord-preservation geometry** (tip arcs over and drops vertically as it leans, like a rigid stick on a hinge).
</overview>

<history>
1. **"does the grass grow back?"** — Built regrowth feature across all 3 impls (30-90s delay then 2-4s linear growback). Used a second xorshift64 PRNG stream salted with `seed XOR REGROW_PRNG_SALT` to keep the main stream bit-identical (conformance preserved). Committed `7f300cf`.

2. **"slow down sway and 50% more grass"** — Fleeted 4 sub-agents. Halved `BASE_SWAY_SPEED` (2π/3 → π/3, 3s→6s period), unified `DEFAULT_DENSITY = 1.5`. Renamed WinUI3's `BaseSwaySpeedMatchesThreeSecondPeriod` test. Committed `f1f084b`.

3. **"run the native one"** — Launched Native exe (PID 47684).

4. **"reduce side-to-side by 50%, 50% more grass, 25% shorter, fleet it"** — Fleeted 4 agents. `BASE_AMPLITUDE` 6.0→3.0, `DEFAULT_DENSITY` 1.5→2.25, `BLADE_HEIGHT_MIN/MAX` 8/40→6/30. Required regenerating Native canonical snapshot (`snapshot_data.h`) via `snapshot_gen.cpp` since heights changed (heights are exactly 0.75× old values, baseX/thickness/hue/sway/stiffness bit-identical). Updated `5.99` hardcode in C# SwayTests to `Constants.BaseAmplitude * 0.999`. Committed `7d3d12d`.

5. **"mouse-over movement looks like a stretch, not a forced move; grass bends from the top more"** — Diagnosed: control point was perpendicular to base-tip line at midpoint, giving symmetric arc (base tilted as much as tip). Fixed to **rooted bend**: control directly above base at 60% of blade height. Vertical tangent at root, curve trails toward tip. Updated WinUI3 `ZeroLeanStrokeIsVertical` expectation (ControlY 97.5 → 95). Committed `ca990d2`.

6. **"way better, however it still stretches"** — Current request. Diagnosed: tip stays at `groundY - height*cutHeight` regardless of lean, so painted Bezier path grows as lean grows = visible stretching. Fix: chord-preservation. Added new constant `MAX_LEAN_FRACTION = 0.95`. New geometry: `dropFactor = sqrt(1 - (lean/L)²)`, `tipY = groundY - L * dropFactor`, controlY also scaled by dropFactor. Fleeted 4 agents (all background-running at compaction time):
   - `tune-native-4`
   - `tune-win2d-4`
   - `tune-winui3-4`
   - `tune-spec-4`
</history>

<work_done>
**Latest commit on `main`**: `ca990d2` "Fix bend geometry: rooted control point instead of perpendicular offset"

**Commit history (recent)**:
- `ca990d2` rooted-bend (control directly above base)
- `7d3d12d` narrower sway (3 DIP), 25% shorter blades, +50% density (2.25)
- `f1f084b` slow sway to 6s + density 1.5
- `7f300cf` regrowth (30-90s delay, 2-4s growback)
- `531fcda` taskbar work-area anchor

**Currently in flight** (4 background agents on the chord-preservation change):
- [ ] `tune-native-4` — Add `MAX_LEAN_FRACTION = 0.95` to `Constants.h`; rewrite `compute_blade_stroke` main branch in `Sim.cpp` (no test updates expected — only zero-lean Native test exists).
- [ ] `tune-win2d-4` — Add `MAX_LEAN_FRACTION = 0.95` to `Constants.cs`; rewrite `ComputeBladeStroke` in `Sim.cs`; update `CutTests.UncutBladeStrokeUsesEffectiveLean` TipY assertion (80.0 → `110 - sqrt(875)` ≈ 80.418).
- [ ] `tune-winui3-4` — Add `MaxLeanFraction = 0.95` to `Constants.cs`; rewrite BOTH call sites in `Sim.cs` (static `ComputeBladeStroke` ~line 237 + instance `GetStroke` ~line 417); update `StrokeTests.UncutBladeStrokeUsesEffectiveLean` TipY assertion + comment.
- [ ] `tune-spec-4` — Rewrite §7 in `docs/architecture.md` with chord-preserving pseudocode; add `MAX_LEAN_FRACTION` row to §11 constants table.

**Pending after agents return**:
- [ ] Run smoke (`Run-SmokeTests.ps1 -Target All`) — expect all PASS, slightly different unique-color counts.
- [ ] `git add -A && git commit && git push` with detailed message about chord preservation.
- [ ] Relaunch Native for user to verify (`Start-Process .../DesktopGrass.Native.exe`).
- [ ] Call `task_complete`.
</work_done>

<technical_details>
**Chord preservation formula** (the in-flight change):
```
L = height * cutHeight
lean = clamp(effectiveLean, -0.95*L, +0.95*L)
dropFactor = sqrt(1 - (lean/L)²)  // == cos(bend angle from vertical)
tipX = baseX + lean
tipY = groundY - L * dropFactor
controlX = baseX                                          // rooted (vertical tangent)
controlY = groundY - L * CTRL_OFFSET_FACTOR * dropFactor   // also drops by dropFactor
```
At zero lean: dropFactor=1 → tipY/controlY identical to prior rooted-bend formula (zero-lean tests still pass unchanged). At max clamp (0.95L): dropFactor ≈ 0.312, blade nearly horizontal but never flat.

**Why clamp**: peak gust = `MAX_CURSOR_SPEED * IMPULSE_SCALE * GUST_TO_LEAN_FACTOR` = 4000 * 0.003 * 1.5 = 18 DIP, easily exceeds L for a 6-DIP min-height blade. Without clamp, `sqrt(negative)` would NaN.

**Test fixture impact**: Both `UncutBladeStrokeUsesEffectiveLean` tests (Win2D + WinUI3) use Height=30, EffectiveLean=5 → old TipY=80, new TipY = 110 - sqrt(900-25) = 110 - sqrt(875) ≈ 80.418. Zero-lean tests (Native + WinUI3 `ZeroLeanStrokeIsVertical`) unchanged.

**Sub-agent fleet pattern**: 4 parallel agents (one per impl + spec). Each gets exact constant values, the new code block, list of test files to grep for breakages, build/test commands. They report back; I run cross-impl smoke as integration gate, then commit/push.

**Conformance preservation trick**: PRNG draw order/count never changes across these tunings. Heights change but main stream stays bit-identical. The Native `snapshot_data.h` only needs `height` column updates — `CANONICAL_BLADE_COUNT = 321` and `CANONICAL_PRNG_SNAPSHOT` stay bit-identical.

**Build commands** (verified working):
- Native: `& $env:ComSpec /c 'call "C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul && cd /d <dir> && msbuild <proj>.vcxproj -p:Configuration=Release -p:Platform=x64 -nologo -v:m'`
- C#: `dotnet build -c Release --nologo -v:m` / `dotnet test -c Release --nologo -v:m`
- Smoke: `pwsh -NoProfile -File 'C:\Users\crutkas\source\DesktopGrass\tests\smoke\Run-SmokeTests.ps1' -Target All -Configuration Release`

**Native exe lock**: Must `Get-Process DesktopGrass.Native -ErrorAction SilentlyContinue | Stop-Process -Force` before rebuilding if running.

**Native snapshot regen recipe** (corrected from stale comment in snapshot_data.h):
```pwsh
& $env:ComSpec /c 'call "...VsDevCmd.bat" -arch=x64 -host_arch=x64 >nul && cd /d <tests\Native.Tests> && cl /nologo /std:c++17 /EHsc /O2 /I..\..\src\DesktopGrass.Native\src /Fe:snapshot_gen.exe snapshot_gen.cpp ..\..\src\DesktopGrass.Native\src\Sim.cpp && snapshot_gen.exe > snapshot_data_generated.h'
```
(Without `/Fo` — hit MSVC D8036 with it.)

**Test counts as of last green**:
- Native: 41 cases / 60,548 assertions
- Win2D: 45 tests
- WinUI3: 49 tests
- Smoke colors (post-rooted-bend): Native 3104, Win2D 3108, WinUI3 2419

**Current display/env**: 4K (3840×2160) at 200% DPI, taskbar 48px bottom-docked. WorkArea.Bottom = 2112.

**Open question**: After chord preservation lands, the blades may still feel violent under fast cursor passes because the clamp will pin blades flat. May warrant a follow-up to reduce `GUST_TO_LEAN_FACTOR` or `IMPULSE_SCALE` — wait for user feedback.
</technical_details>

<important_files>
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.cpp`
  - `compute_blade_stroke` ~line 240. Agent `tune-native-4` is rewriting the main branch to chord-preserving geometry.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Constants.h`
  - Agent `tune-native-4` adding `MAX_LEAN_FRACTION = 0.95` constant.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Sim.cs`
  - `ComputeBladeStroke` ~line 316. Agent `tune-win2d-4` rewriting.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Constants.cs`
  - Agent `tune-win2d-4` adding `MAX_LEAN_FRACTION = 0.95`.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.WinUI3\Sim.cs`
  - TWO call sites: static `ComputeBladeStroke` ~line 237, instance `GetStroke` ~line 417. Agent `tune-winui3-4` rewriting both.
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.WinUI3\Constants.cs`
  - Agent `tune-winui3-4` adding `MaxLeanFraction = 0.95`.
- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Win2D.Tests\SimTests\CutTests.cs`
  - `UncutBladeStrokeUsesEffectiveLean` ~line 164. Agent updating TipY 80.0 → `110 - Math.Sqrt(875)`.
- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.WinUI3.Tests\SimTests\StrokeTests.cs`
  - `UncutBladeStrokeUsesEffectiveLean` ~line 31. Agent updating same.
  - `ZeroLeanStrokeIsVertical` ~line 53. UNCHANGED (zero lean → dropFactor=1).
  - `GetStrokeMatchesStaticComputeBladeStroke` ~line 77. Will catch any divergence between Sim.cs's two call sites.
- `C:\Users\crutkas\source\DesktopGrass\docs\architecture.md`
  - §7 (currently rooted bend) being rewritten by `tune-spec-4`. §11 constants table gets new MAX_LEAN_FRACTION row.
- `C:\Users\crutkas\source\DesktopGrass\tests\smoke\Run-SmokeTests.ps1` — Cross-impl smoke harness (no edits needed).
- `C:\Users\crutkas\.copilot\session-state\e286b6d3-8e11-4aa2-b2d7-87ceb1f5de22\plan.md` — Session plan.

Repo: `C:\Users\crutkas\source\DesktopGrass` on `main`, pushed to `https://github.com/crutkas/DesktopGrass` (private).
</important_files>

<next_steps>
**Immediate next steps** (after compaction):
1. **Wait for 4 background agents to complete** — `tune-native-4`, `tune-win2d-4`, `tune-winui3-4`, `tune-spec-4`. They were just launched. Read each with `read_agent` as completion notifications arrive.
2. **Run cross-impl smoke** once all 4 are green:
   ```
   Get-Process DesktopGrass* -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }
   pwsh -NoProfile -File 'C:\Users\crutkas\source\DesktopGrass\tests\smoke\Run-SmokeTests.ps1' -Target All -Configuration Release
   ```
   Expect: all 3 PASS, unique-color counts will shift (likely lower since blades occupy less vertical extent when bent — but >> 50 threshold).
3. **Commit + push** with message describing chord preservation, the new `MAX_LEAN_FRACTION = 0.95` constant, the `dropFactor = sqrt(1 - (lean/L)²)` formula, and the two updated C# test expectations. Include `Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>` trailer.
4. **Relaunch Native** for user to verify: `Start-Process 'C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\out\Release\DesktopGrass.Native.exe' -PassThru`
5. **Call `task_complete`** with summary including: diagnosis ("tip stayed at fixed Y → painted Bezier path grew with lean"), fix ("chord preservation: tip arcs over and drops as it leans"), test counts, smoke results.

**Possible follow-up** (only if user reports more issues): reduce `GUST_TO_LEAN_FACTOR` from 1.5 or `IMPULSE_SCALE` from 0.003 so the chord-preservation clamp isn't hit as often under fast cursor passes.
</next_steps>