// ocean_tests.cpp
//
// Ocean scene tests (architecture.md §17). Mirror of the Win2D OceanTests so
// the coral blade variant, bubble emitter, and fish swimmers stay in lockstep
// across impls.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>

using namespace desktopgrass;

namespace {

constexpr double kMonitor1920 = 1920.0;

Sim make_ocean_sim(uint64_t seed = CANONICAL_TEST_SEED,
                   double width = kMonitor1920,
                   double density = DEFAULT_DENSITY) {
    Sim sim = sim_init(seed, width, density);
    sim_set_scene(sim, Scene::Ocean);
    return sim;
}

int count_kind(const Sim& sim, EntityKind kind) {
    return static_cast<int>(std::count_if(sim.entities.begin(), sim.entities.end(),
        [kind](const Entity& e) { return e.kind == kind; }));
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(OceanTests)
{
public:
TEST_METHOD(OceanSceneGeneratesAtLeastOneCoralAndKeepsValuesInRange) {
    Sim sim = make_ocean_sim();

    int coralCount = 0;
    for (const Blade& b : sim.blades) {
        if (!b.isCoral) continue;
        ++coralCount;
        Assert::IsFalse(b.isPine);
        Assert::IsFalse(b.isCactus);
        Assert::IsFalse(b.isMaple);
        Assert::IsFalse(b.isFlower);
        Assert::IsFalse(b.isMushroom);
        Assert::IsTrue(b.coralHeight >= CORAL_HEIGHT_MIN);
        Assert::IsTrue(b.coralHeight <= CORAL_HEIGHT_MAX);
        Assert::IsTrue(b.coralWidth  >= CORAL_WIDTH_MIN);
        Assert::IsTrue(b.coralWidth  <= CORAL_WIDTH_MAX);
        Assert::IsTrue(static_cast<int>(b.coralType)     >= 0);
        Assert::IsTrue(static_cast<int>(b.coralType)     <= CORAL_TYPE_COUNT  - 1);
        Assert::IsTrue(static_cast<int>(b.coralColorIdx) >= 0);
        Assert::IsTrue(static_cast<int>(b.coralColorIdx) <= CORAL_COLOR_COUNT - 1);
    }
    Assert::IsTrue(coralCount > 0);
}

TEST_METHOD(OceanSceneSpawnsInitialFishAtOrAboveTheTargetMinimum) {
    Sim sim = make_ocean_sim();

    const int fishCount = count_kind(sim, EntityKind::Fish);
    Assert::IsTrue(fishCount >= FISH_COUNT_MIN);
    Assert::IsTrue(fishCount <= FISH_COUNT_MAX);
}

TEST_METHOD(OceanFishCountRoundsHalfToEvenDeterministically) {
    // scaled = 2.5 * width / 1920. Widths chosen so scaled lands exactly on a
    // .5 tie; round-half-to-even must pick the even neighbor (NOT half-up),
    // matching C# Math.Round and independent of the FPU rounding mode.
    Sim tie25 = make_ocean_sim(CANONICAL_TEST_SEED, 1920.0); // scaled 2.5 -> 2
    Assert::IsTrue(count_kind(tie25, EntityKind::Fish) == 2);

    Sim tie45 = make_ocean_sim(CANONICAL_TEST_SEED, 3456.0); // scaled 4.5 -> 4
    Assert::IsTrue(count_kind(tie45, EntityKind::Fish) == 4);
}

TEST_METHOD(OceanTickEmitsBubblesOverTime) {
    Sim sim = make_ocean_sim();

    const double dt = 1.0 / 60.0;
    for (int i = 0; i < 600; ++i) {
        sim.globalTime += dt;
        sim_tick_entities(sim, dt);
    }

    Assert::IsTrue(count_kind(sim, EntityKind::Bubble) > 0);
}

TEST_METHOD(SwitchingFromOceanToGrassWipesBubblesAndFish) {
    Sim sim = make_ocean_sim();

    const double dt = 1.0 / 60.0;
    for (int i = 0; i < 120; ++i) {
        sim.globalTime += dt;
        sim_tick_entities(sim, dt);
    }
    Assert::IsTrue(count_kind(sim, EntityKind::Fish) > 0);

    sim_set_scene(sim, Scene::Grass);

    Assert::IsTrue(count_kind(sim, EntityKind::Bubble) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Fish) == 0);
    Assert::IsTrue(std::none_of(sim.blades.begin(), sim.blades.end(),
        [](const Blade& b) { return b.isCoral; }));
}

TEST_METHOD(OceanPaletteIsPinnedInScenePalettes) {
    for (int i = 0; i < PALETTE_SIZE; ++i) {
        Assert::IsTrue(SCENE_PALETTES[static_cast<int>(Scene::Ocean)][i] == OCEAN_PALETTE[i]);
    }
}
};
}
