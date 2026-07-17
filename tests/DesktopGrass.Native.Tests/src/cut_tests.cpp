// cut_tests.cpp
//
// Cut state animation tests (architecture.md §9).

#include "TestHelpers.h"
#include "Sim.h"
#include "snapshot_data.h"

#include <cmath>
#include <vector>

using namespace desktopgrass;
using namespace desktopgrass::test;

namespace {

Sim make_sim_with_blades(std::initializer_list<double> baseXs) {
    Sim sim;
    sim.windowHeight = STRIP_HEIGHT + HEADROOM;
    for (double x : baseXs) {
        Blade b{};
        b.baseX            = x;
        b.height           = 20.0;
        b.thickness        = 1.5;
        b.swayPhaseOffset  = 0.0;
        b.stiffness        = 1.0;
        b.cutHeight        = 1.0;
        b.cutInitialHeight = 1.0;
        b.cutAnimStart     = -1.0;
        sim.blades.push_back(b);
    }
    return sim;
}

InputEvent click(double x, double y, double t) {
    return InputEvent{ EventType::Click, x, y, t };
}

} // anonymous

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(CutTests)
{
public:
TEST_METHOD(ClickInsideCutBandAnimatesBladesWithinRadiusTo0) {
    Sim sim = make_sim_with_blades({100.0, 110.0, 200.0});
    const double y_in_band = sim.windowHeight - 40.0; // inside strip

    InputEvent ev = click(100.0, y_in_band, 0.0);
    sim_tick(sim, 0.0, &ev, 1);

    // Apply 5 ticks of 50 ms (total = 250 ms > CUT_DURATION_SEC).
    for (int i = 0; i < 5; ++i) {
        sim_tick(sim, 0.05, nullptr, 0);
    }

    Assert::IsTrue(sim.blades[0].cutHeight == Near(0.0));
    Assert::IsTrue(sim.blades[0].cutAnimStart == Near(-1.0));
    Assert::IsTrue(sim.blades[1].cutHeight == Near(0.0));
    // Blade at 200 is outside CUT_RADIUS = 30.
    Assert::IsTrue(sim.blades[2].cutHeight == Near(1.0));
    Assert::IsTrue(sim.blades[2].cutAnimStart == Near(-1.0));
}

TEST_METHOD(CutAnimationIsLinearOverCUTDURATIONSEC) {
    Sim sim = make_sim_with_blades({100.0});
    const double y = sim.windowHeight - 40.0;

    InputEvent ev = click(100.0, y, 0.0);
    sim_tick(sim, 0.0, &ev, 1);
    // After tick(0.0) globalTime = 0 still; cutAnimStart = 0.

    // 50 ms in → cutHeight ≈ 0.75.
    sim_tick(sim, 0.05, nullptr, 0);
    Assert::IsTrue(sim.blades[0].cutHeight == Near(0.75).margin(1e-9));

    // 100 ms in → 0.5.
    sim_tick(sim, 0.05, nullptr, 0);
    Assert::IsTrue(sim.blades[0].cutHeight == Near(0.5).margin(1e-9));

    // 150 ms in → 0.25.
    sim_tick(sim, 0.05, nullptr, 0);
    Assert::IsTrue(sim.blades[0].cutHeight == Near(0.25).margin(1e-9));

    // 200 ms in → 0.0 and idle.
    sim_tick(sim, 0.05, nullptr, 0);
    Assert::IsTrue(sim.blades[0].cutHeight == Near(0.0).margin(1e-9));
    Assert::IsTrue(sim.blades[0].cutAnimStart < 0.0);
}

TEST_METHOD(ClickOutsideCutBandIsIgnored) {
    Sim sim = make_sim_with_blades({100.0});
    const double y_above = sim.windowHeight - STRIP_HEIGHT - 5.0;

    InputEvent ev = click(100.0, y_above, 0.0);
    sim_tick(sim, 0.0, &ev, 1);

    Assert::IsTrue(sim.blades[0].cutHeight == Near(1.0));
    Assert::IsTrue(sim.blades[0].cutAnimStart < 0.0);
}

TEST_METHOD(RepeatClickOnInFlightBladeIsIdempotent) {
    Sim sim = make_sim_with_blades({100.0});
    const double y = sim.windowHeight - 40.0;

    InputEvent first = click(100.0, y, 0.0);
    sim_tick(sim, 0.0, &first, 1);

    // Mid-animation second click → should not reset cutAnimStart.
    sim_tick(sim, 0.05, nullptr, 0);  // 0.05 elapsed; cutHeight = 0.75
    const double startSnapshot = sim.blades[0].cutAnimStart;
    const double heightSnapshot = sim.blades[0].cutHeight;

    InputEvent second = click(100.0, y, 0.05);
    sim_tick(sim, 0.0, &second, 1);

    Assert::IsTrue(sim.blades[0].cutAnimStart    == Near(startSnapshot));
    Assert::IsTrue(sim.blades[0].cutInitialHeight == Near(1.0));
    Assert::IsTrue(sim.blades[0].cutHeight == Near(heightSnapshot));
}

TEST_METHOD(ClickOnAlreadyCutBladeIsANoOp) {
    Sim sim = make_sim_with_blades({100.0});
    sim.blades[0].cutHeight        = 0.0;
    sim.blades[0].cutInitialHeight = 0.0;
    sim.blades[0].cutAnimStart     = -1.0;

    const double y = sim.windowHeight - 40.0;
    InputEvent ev = click(100.0, y, 0.0);
    sim_tick(sim, 0.0, &ev, 1);

    Assert::IsTrue(sim.blades[0].cutHeight    == Near(0.0));
    Assert::IsTrue(sim.blades[0].cutAnimStart  < 0.0);
}

TEST_METHOD(BladesOutsideCutRadiusAreUntouched) {
    Sim sim = make_sim_with_blades({100.0, 131.0, 200.0});
    const double y = sim.windowHeight - 40.0;

    InputEvent ev = click(100.0, y, 0.0);
    sim_tick(sim, 0.0, &ev, 1);

    Assert::IsTrue(sim.blades[0].cutAnimStart >= 0.0);
    Assert::IsTrue(sim.blades[1].cutAnimStart  < 0.0);
    Assert::IsTrue(sim.blades[2].cutAnimStart  < 0.0);
}

TEST_METHOD(ComputeBladeStrokeDegeneratesToAStumpUnderThreshold) {
    Blade b{};
    b.baseX            = 100.0;
    b.height           = 20.0;
    b.thickness        = 1.5;
    b.hue              = 2;
    b.cutHeight        = 0.04;       // below CUT_STUMP_THRESHOLD = 0.05
    b.effectiveLean    = 5.0;
    b.cutInitialHeight = 1.0;
    b.cutAnimStart     = 0.0;

    Stroke s = compute_blade_stroke(b, 110.0, Scene::Grass);
    Assert::IsTrue(s.tip.x == Near(100.0));
    Assert::IsTrue(s.tip.y == Near(110.0 - STUMP_HEIGHT));
    Assert::IsTrue(s.argb  == PALETTE[2]);
}

TEST_METHOD(ComputeBladeStrokeProducesVerticalLineWhenLeanIsZero) {
    Blade b{};
    b.baseX         = 100.0;
    b.height        = 20.0;
    b.thickness     = 1.5;
    b.hue           = 1;
    b.cutHeight     = 1.0;
    b.effectiveLean = 0.0;

    Stroke s = compute_blade_stroke(b, 110.0, Scene::Grass);
    Assert::IsTrue(s.base.x    == Near(100.0));
    Assert::IsTrue(s.base.y    == Near(110.0));
    Assert::IsTrue(s.tip.x     == Near(100.0));
    Assert::IsTrue(s.tip.y     == Near(90.0));
    Assert::IsTrue(s.control.x == Near(100.0));
}

// ---------------------------------------------------------------------------
// Cut-floor (stubble) variation
// ---------------------------------------------------------------------------

TEST_METHOD(GeneratedBladesGetAPerBladeCutFloorWithinSpecRange) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    Assert::IsTrue(blades.size() > 50);

    for (const Blade& b : blades) {
        Assert::IsTrue(b.cutFloor >= CUT_FLOOR_MIN);
        Assert::IsTrue(b.cutFloor <  CUT_FLOOR_MAX);
        // Stubble must render as a short blade, never a degenerate stump.
        Assert::IsTrue(b.cutFloor >= CUT_STUMP_THRESHOLD);
    }

    // The whole point is variation: not every blade settles at the same height.
    bool varies = false;
    for (std::size_t i = 1; i < blades.size(); ++i) {
        if (blades[i].cutFloor != blades[0].cutFloor) { varies = true; break; }
    }
    Assert::IsTrue(varies);
}

TEST_METHOD(CutSettlesAtThePerBladeStubbleFloorNotFlatZero) {
    Blade b{};
    b.height           = 20.0;
    b.thickness        = 1.5;
    b.cutHeight        = 1.0;
    b.cutInitialHeight = 1.0;
    b.cutFloor         = 0.12;
    b.cutAnimStart     = 0.0;

    // Advance past the full cut duration.
    advance_cut(b, CUT_DURATION_SEC + 0.01);

    Assert::IsTrue(b.cutHeight    == Near(0.12));
    Assert::IsTrue(b.cutAnimStart == Near(-1.0));
}

TEST_METHOD(CutDownAnimationLerpsTowardTheFloor) {
    Blade b{};
    b.height           = 20.0;
    b.cutHeight        = 1.0;
    b.cutInitialHeight = 1.0;
    b.cutFloor         = 0.10;
    b.cutAnimStart     = 0.0;

    // Half-way through the cut: lerp(1.0 -> 0.10) at t=0.5 = 0.10 + 0.90*0.5.
    advance_cut(b, CUT_DURATION_SEC * 0.5);
    Assert::IsTrue(b.cutHeight == Near(0.10 + 0.90 * 0.5).margin(1e-9));
}

TEST_METHOD(RegrowthGrowsBackFromTheFloorToFullHeight) {
    Blade b{};
    b.height         = 20.0;
    b.cutFloor       = 0.10;
    b.cutHeight      = 0.10;
    b.cutAnimStart   = -1.0;
    b.regrowDuration = 0.4;
    b.regrowStart    = 0.0;

    // Half-way through regrowth: lerp(0.10 -> 1.0) at t=0.5.
    advance_cut(b, 0.2);
    Assert::IsTrue(b.cutHeight == Near(0.10 + 0.90 * 0.5).margin(1e-9));

    // Fully regrown.
    advance_cut(b, 0.4);
    Assert::IsTrue(b.cutHeight == Near(1.0).margin(1e-9));
}

TEST_METHOD(ZeroFloorBladesStillCollapseFullyBackCompat) {
    Blade b{};
    b.height           = 20.0;
    b.cutHeight        = 1.0;
    b.cutInitialHeight = 1.0;
    b.cutFloor         = 0.0;
    b.cutAnimStart     = 0.0;

    advance_cut(b, CUT_DURATION_SEC + 0.01);
    Assert::IsTrue(b.cutHeight == Near(0.0));
}
};
}
