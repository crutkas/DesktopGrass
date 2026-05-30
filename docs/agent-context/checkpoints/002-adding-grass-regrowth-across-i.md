<overview>
Adding **grass regrowth** (after cut, blades wait ~30-90s then linearly regrow over ~2-4s) to all three DesktopGrass impls (Native C++, Win2D C#, WinUI3 C#). Constraint: must preserve the cross-impl bit-identical pixel count (10,787 unique colors for canonical seed 0x6B6173746F) to keep the conformance gate intact. Solution: per-blade regrowth jitter sampled from a SECOND xorshift64 stream seeded `seed XOR REGROW_PRNG_SALT` so the main PRNG stream is unchanged.
</overview>

<history>
1. **User asked "does the grass grow back?"** (one-line question after the prior position-fix work was committed)
   - Confirmed via grep in Sim.cpp and plan.md (line 159) that regrowth was explicitly parked for v2.
   - Explained the current behavior (`cutHeight → 0` permanently) and proposed a design.
   - Asked the user for timing preferences via `ask_user`; got "user not available, work autonomously."
   - Selected the recommended option: slow & natural (30–90s delay, 2–4s fade-in, jittered).

2. **Designed the regrowth mechanism**:
   - Independent second PRNG stream for jitter (preserves conformance).
   - 3 new Blade fields: `regrowDelay`, `regrowDuration`, `regrowStart` (-1 = idle sentinel).
   - `advance_cut` extended into a 2-phase function: cut anim → schedule regrowth → wait → animate up.
   - `sim_apply_click` clears `regrowStart` when re-cutting a regrowing blade.
   - Guard: only schedule regrowth if both `regrowDelay > 0` AND `regrowDuration > 0` — so test fixtures using `default(Blade)` stay dormant (preserves existing CutTests behavior).

3. **Implemented Native impl**:
   - Updated `Constants.h`, `Sim.h` (added fields with in-class default initializers including `regrowStart = -1.0`), `Sim.cpp` (dual-PRNG generation, extended advance_cut, recut cancel).
   - First build: 3 NaN failures in cut_tests.cpp because test factories used `Blade b{};` (zeroing regrowStart to 0, not -1).
   - Fixed via in-class initializer `regrowStart = -1.0` + guard on positive delay/duration → 34 existing tests pass.
   - Added `regrowth_tests.cpp` (7 cases): cut-schedules-regrowth, linear-over-duration, re-click-restarts-from-current, click-on-stump-noop, determinism, range-validation, default-blade-stays-cut.
   - Result: 41 cases, 60,548 assertions PASS.

4. **Implemented Win2D + WinUI3 impls** (mirroring Native):
   - Updated `Constants.cs`, `Sim.cs` in both projects.
   - Added `TestSetBlades` helper to WinUI3 Sim (since `Blades` has `private set`).
   - Created `SimTests/RegrowthTests.cs` for each.
   - First test run showed unchanged counts (38, 42) — `.csproj` files use `EnableDefaultCompileItems=false` so I had to explicitly add the new `.cs` files.
   - After fix: Win2D 45 PASS, WinUI3 49 PASS.

5. **Built app projects** to verify production builds compile — both Win2D and WinUI3 built successfully (no errors).

6. **In progress** when summary was triggered: about to run full smoke test (`Run-SmokeTests.ps1 -Target All`) to confirm 10,787 conformance holds, then update `docs/architecture.md`, commit, push.
</history>

<work_done>
**Files modified**:
- `src/DesktopGrass.Native/src/Constants.h` — added REGROW_DELAY_MIN/MAX (30/90s), REGROW_DURATION_MIN/MAX (2/4s), REGROW_PRNG_SALT (0xDEADBEEFCAFEBABE).
- `src/DesktopGrass.Native/src/Sim.h` — added Blade fields `regrowDelay = 0.0`, `regrowDuration = 0.0`, `regrowStart = -1.0` (with in-class default initializers).
- `src/DesktopGrass.Native/src/Sim.cpp` — dual-PRNG in `generate_blades`, extended `advance_cut` (2 phases + positive-guards), `sim_apply_click` clears regrowStart on recut.
- `src/DesktopGrass.Win2D/Constants.cs` — same constant block (REGROW_*, REGROW_PRNG_SALT).
- `src/DesktopGrass.Win2D/Sim.cs` — Blade fields added, GenerateBlades dual-PRNG, ApplyClick clears RegrowStart, AdvanceCut 2-phase + guards.
- `src/DesktopGrass.WinUI3/Constants.cs` — same constant block (PascalCase: RegrowDelayMin, etc.).
- `src/DesktopGrass.WinUI3/Sim.cs` — same Blade additions, dual-PRNG Generate, ApplyClick clears, AdvanceCut 2-phase, **+ new internal helper `TestSetBlades`**.

**Files created**:
- `tests/DesktopGrass.Native.Tests/src/regrowth_tests.cpp` (7 Catch2 cases).
- `tests/DesktopGrass.Win2D.Tests/SimTests/RegrowthTests.cs` (7 xunit Facts).
- `tests/DesktopGrass.WinUI3.Tests/SimTests/RegrowthTests.cs` (7 xunit Facts).

**Project files updated**:
- `tests/DesktopGrass.Native.Tests/DesktopGrass.Native.Tests.vcxproj` — added `<ClCompile Include="src\regrowth_tests.cpp" />`.
- `tests/DesktopGrass.Win2D.Tests/DesktopGrass.Win2D.Tests.csproj` — added `<Compile Include="SimTests\RegrowthTests.cs" />` (required because `EnableDefaultCompileItems=false`).
- `tests/DesktopGrass.WinUI3.Tests/DesktopGrass.WinUI3.Tests.csproj` — same.

**SQL todo state**:
- `regrowth-spec` → in_progress (NOT yet written — needs architecture.md update for §4 table, §5 generation, §9 regrowth subsection, §11 constants table).
- `regrowth-native`, `regrowth-native-tests`, `regrowth-win2d`, `regrowth-win2d-tests`, `regrowth-winui3`, `regrowth-winui3-tests` → done.
- `regrowth-conformance` → in_progress (full smoke not yet re-run; only `dotnet test` ran).
- `regrowth-commit` → pending.

**Test results so far**:
- Native: 41 cases, 60,548 assertions PASS.
- Win2D: 45 tests PASS.
- WinUI3: 49 tests PASS.
- All three app projects built OK in Release.
- Full end-to-end smoke (`Run-SmokeTests.ps1 -Target All`) **not yet run after regrowth changes** — must verify still shows 10,787 unique colors across all three.
</work_done>

<technical_details>
- **Conformance preservation trick**: regrowth jitter sampled from `Prng(seed ^ 0xDEADBEEFCAFEBABE)`, completely independent of the main generation PRNG. Main stream's draw count per blade is UNCHANGED (still 6: spacing, height, thickness, hue, swayPhaseOffset, stiffness). Result: `baseX`, `height`, etc. are bit-identical to pre-regrowth; smoke test (which runs only ~3s, no clicks, no regrowth) produces identical 10,787 unique colors. The Native verification this works was implicit in the existing snapshot tests passing.

- **NaN trap**: `Blade b{};` zero-inits doubles. With `regrowStart` defaulting to 0 (not -1), Phase 2 of `advance_cut` would fire at globalTime=0 with elapsed=0, regrowDuration=0 → NaN propagation. Fixed two ways: (1) in-class `= -1.0` initializer on regrowStart in struct definitions where it would take effect, and (2) defensive `if (regrowDelay > 0 && regrowDuration > 0)` guard before scheduling regrowth + `if (regrowDuration <= 0)` guard in Phase 2. The guard is the bulletproof part — even if a Blade somehow has regrowStart=0 set, with zero duration it short-circuits.

- **C# struct field initializers gotcha**: `default(Blade)` skips field initializers (only `new Blade()` runs them). Win2D uses `Blade b = default;` in GenerateBlades, so I rely on explicit `b.RegrowStart = -1.0` assignment there + the positive-value guards in advance_cut to handle the default-Blade case.

- **WinUI3 test injection**: `Sim.Blades` has `private set`. Added `internal void TestSetBlades(Blade[])` alongside existing `TestApplyClick`/`TestSetGlobalTime` helpers in Sim.cs.

- **Csproj quirk**: Both Win2D and WinUI3 test projects set `<EnableDefaultCompileItems>false</EnableDefaultCompileItems>` and explicitly list each `.cs` file. Adding a new test file requires editing the csproj — `dotnet test` silently runs only existing 38/42 tests if you forget. This bit me on the first run.

- **Lifecycle order in advance_cut** (all impls):
  1. If `cutAnimStart >= 0` → run cut animation; on completion (t>=1) reset cutHeight=0, cutAnimStart=-1, and if delay+duration > 0 schedule regrowStart = globalTime + regrowDelay.
  2. Else if `regrowStart >= 0` AND `globalTime >= regrowStart` AND `regrowDuration > 0` → run regrowth, linear 0→1.
  3. Else: idle.

- **Re-cut during regrowth**: ApplyClick filter `b.cutHeight <= 0.0 continue` means clicking a stump (cutHeight==0, waiting to regrow) is a no-op. Clicking a mid-regrowth blade (cutHeight > 0) proceeds: sets cutAnimStart=globalTime, cutInitialHeight=current cutHeight, clears regrowStart. Cut animation then runs from current height back to 0 in 200ms, then schedules new regrowth.

- **Density values**: Win2D uses `DEFAULT_DENSITY = 1.25` in Constants but tests use 1.0; WinUI3 uses 1.0. Conformance gate is run-time agnostic (pixel variance).

- **Build commands verified**:
  - Native: `& cmd /c "call VsDevCmd.bat ... && msbuild DesktopGrass.Native.vcxproj -p:Configuration=Release -p:Platform=x64"`
  - Native tests: same with `DesktopGrass.Native.Tests.vcxproj`
  - C# projects: `dotnet build/test` directly (msbuild from VS doesn't matter here).
  - VS path used: `C:\Program Files\Microsoft Visual Studio\18\Enterprise\Common7\Tools\VsDevCmd.bat` (VS 18 Enterprise, MSBuild 18.6.3).

- **Native exe lock**: `Stop-Process -Id <PID>` required before rebuild — running Native locks the exe. PID 47696 was the post-fix instance launched for the user; killed before this work began.

- **Open questions**:
  - End-to-end smoke not yet confirmed at 10,787 after regrowth changes (high confidence it'll pass — main stream unchanged — but unverified).
  - Spec doc (`docs/architecture.md`) not yet updated. Needs §4 (3 new fields), §5 (dual-PRNG generation pseudocode), §9 ("Regrowth" subsection with apply_click cancellation + advance_cut 2-phase pseudocode), §11 (constants table). Also §2 line 36 still says "work area is not used" which is stale after the prior commit (low priority drive-by).
  - Should the smoke harness `Get-GrassStripPixelVariance` change at all? No — regrowth doesn't fire within the smoke duration.
</technical_details>

<important_files>
- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.cpp`
  - generate_blades (lines ~59-100): now uses two Prng instances; new fields populated at end of inner loop.
  - advance_cut (lines ~112-148): 2-phase with guards; rewrite of the original ~12-line function.
  - sim_apply_click (lines ~175-195): added `b.regrowStart = -1.0;` after cutAnimStart assignment.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Sim.h`
  - Blade struct (lines ~39-69): new regrowDelay/Duration/Start fields with in-class initializers (= 0.0, 0.0, -1.0).

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Native\src\Constants.h`
  - Lines ~46-58: REGROW_* constants + REGROW_PRNG_SALT.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.Win2D\Sim.cs`
  - GenerateBlades, ApplyClick, AdvanceCut all rewritten parallel to Native.

- `C:\Users\crutkas\source\DesktopGrass\src\DesktopGrass.WinUI3\Sim.cs`
  - Same changes + added `TestSetBlades(Blade[])` helper alongside existing test helpers.

- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Native.Tests\src\regrowth_tests.cpp` (new, 7 cases).
- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.Win2D.Tests\SimTests\RegrowthTests.cs` (new, 7 Facts).
- `C:\Users\crutkas\source\DesktopGrass\tests\DesktopGrass.WinUI3.Tests\SimTests\RegrowthTests.cs` (new, 7 Facts).

- `C:\Users\crutkas\source\DesktopGrass\docs\architecture.md` (NOT yet updated — pending)
  - Needs updates to: §4 Blade table (lines 99-115), §5 generation pseudocode (lines 133-175), §9 add "Regrowth" subsection (currently lines 326-379), §11 constants table (lines 437-470), §2 line 36 stale "work area is not used" comment.

- `C:\Users\crutkas\.copilot\session-state\e286b6d3-8e11-4aa2-b2d7-87ceb1f5de22\plan.md`
  - Line 159 has "Regrowth of cut grass over minutes" under "Future iterations (parked)". After this work ships, that should move out of "future iterations" or be marked done.

- Repo: `C:\Users\crutkas\source\DesktopGrass\` on `main`, last commit `531fcda` (the work-area fix). Pushed to `https://github.com/crutkas/DesktopGrass` (private).
</important_files>

<next_steps>
**Immediate next steps**:
1. Run `pwsh -NoProfile -File 'C:\Users\crutkas\source\DesktopGrass\tests\smoke\Run-SmokeTests.ps1' -Target All -Configuration Release` and confirm all three show **10,787 unique colors** (or at minimum, all three show the same number ≥50 — bit-identical is the conformance bar).
2. Update `docs/architecture.md`:
   - §2 line 36: replace stale "work area is not used" sentence with new "anchors to the work area" (drive-by).
   - §4 Blade data model table: add `regrowDelay`, `regrowDuration`, `regrowStart` rows.
   - §5 Procedural generation: add the second-Prng setup + add two PRNG draws at end of inner loop (and explicit note that draw order is `regrowDelay` then `regrowDuration` from the salt-seeded stream).
   - §9 Cut state animation: rename to "Cut & regrowth state animation"; extend advance_cut pseudocode to show 2-phase; show apply_click clearing regrowStart on recut; describe lifecycle.
   - §11 Constants table: add REGROW_DELAY_MIN/MAX, REGROW_DURATION_MIN/MAX, REGROW_PRNG_SALT rows.
3. Update `plan.md` line 159: mark regrowth shipped or remove from parked list.
4. `git add -A`, commit with message describing regrowth + dual-PRNG conformance preservation + 21 new tests across 3 impls, push to `origin/main`.
5. Optional: relaunch Native for user to see (though regrowth takes 30+ seconds to observe).

**Cleanup**:
- Mark `regrowth-spec`, `regrowth-conformance`, `regrowth-commit` done in SQL when each step completes.

**Open question**: do I need to update `tests/smoke/Smoke.Common.psm1`? No — smoke runs ~3s, no clicks, no regrowth, sample region unchanged.
</next_steps>