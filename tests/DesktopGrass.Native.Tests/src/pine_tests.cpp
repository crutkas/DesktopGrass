// pine_tests.cpp - §15.1 Winter pine trees (slot-bound, mirrors §14 cacti).

#include "TestHelpers.h"
#include "Sim.h"
#include "snapshot_data.h"

#include <cmath>
#include <cstddef>
#include <vector>

using namespace desktopgrass;

namespace {

constexpr double kMonitor1920 = 1920.0;

struct ExpectedTree {
    std::size_t slotIndex = 0;
    uint8_t variant = 0;
    double height = 0.0;
    double width = 0.0;
    int tierCount = 0;
};

ExpectedTree first_expected_tree(std::size_t bladeCount) {
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED ^ PINE_PRNG_SALT);

    for (std::size_t i = 0; i < bladeCount; ++i) {
        const double r = prng_uniform(p, 0.0, 1.0);
        if (r >= PINE_PROBABILITY) continue;

        ExpectedTree expected{};
        expected.slotIndex = i;
        const double variantDraw = prng_uniform(p, 0.0, 1.0);
        expected.variant = variantDraw < BIRCH_VARIANT_PROBABILITY ? 1 : 0;
        expected.height = prng_uniform(p, PINE_HEIGHT_MIN, PINE_HEIGHT_MAX);
        if (expected.variant == 1) {
            expected.width = prng_uniform(p, BIRCH_TRUNK_WIDTH_MIN, BIRCH_TRUNK_WIDTH_MAX);
        } else {
            expected.width = prng_uniform(p, PINE_WIDTH_MIN, PINE_WIDTH_MAX);
        }
        const double tierDraw = prng_uniform(p,
            static_cast<double>(PINE_TIER_COUNT_MIN),
            static_cast<double>(PINE_TIER_COUNT_MAX + 1));
        int tiers = static_cast<int>(std::floor(tierDraw));
        if (tiers < PINE_TIER_COUNT_MIN) tiers = PINE_TIER_COUNT_MIN;
        if (tiers > PINE_TIER_COUNT_MAX) tiers = PINE_TIER_COUNT_MAX;
        expected.tierCount = tiers;
        return expected;
    }

    Assert::Fail(L"Canonical seed produced no tree slot", LINE_INFO());
    return {};
}

} // anonymous

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(PineTests)
{
public:
TEST_METHOD(PineConstantsArePinned) {
    Assert::IsTrue(PINE_PROBABILITY == Near(0.0075));
    Assert::IsTrue(PINE_HEIGHT_MIN == Near(45.0));
    Assert::IsTrue(PINE_HEIGHT_MAX == Near(90.0));
    Assert::IsTrue(PINE_WIDTH_MIN  == Near(28.0));
    Assert::IsTrue(PINE_WIDTH_MAX  == Near(48.0));
    Assert::IsTrue(PINE_TIER_COUNT_MIN == 2);
    Assert::IsTrue(PINE_TIER_COUNT_MAX == 4);
    Assert::IsTrue(PINE_TIP_TAPER == Near(0.25));
    Assert::IsTrue(PINE_TIER_OVERLAP == Near(0.15));
    Assert::IsTrue(PINE_SNOW_CAP_FRACTION == Near(0.30));
    Assert::IsTrue(PINE_COLOR == 0xFF1B5E20u);
    Assert::IsTrue(PINE_PRNG_SALT == 0x50494E4550494E45ull);
}

TEST_METHOD(BirchConstantsArePinned) {
    Assert::IsTrue(BIRCH_VARIANT_PROBABILITY == Near(0.30));
    Assert::IsTrue(BIRCH_TRUNK_WIDTH_MIN == Near(4.0));
    Assert::IsTrue(BIRCH_TRUNK_WIDTH_MAX == Near(7.0));
    Assert::IsTrue(BIRCH_BARK_MARK_COUNT == 5);
    Assert::IsTrue(BIRCH_BARK_MARK_LENGTH_FRAC == Near(0.50));
    Assert::IsTrue(BIRCH_BRANCH_COUNT == 6);
    Assert::IsTrue(BIRCH_SNOW_CAP_FRACTION == Near(0.18));
    Assert::IsTrue(BIRCH_BARK_COLOR == 0xFFEFEFE6u);
    Assert::IsTrue(BIRCH_MARK_COLOR == 0xFF2A2A28u);
}

TEST_METHOD(SimSetSceneWinterPromotesSomeSlotsToTrees) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Winter);

    Assert::IsTrue(sim.currentScene == Scene::Winter);
    std::size_t treeCount = 0;
    for (const Blade& b : sim.blades) {
        if (b.isPine) {
            ++treeCount;
            Assert::IsTrue(b.pineTierCount >= PINE_TIER_COUNT_MIN);
            Assert::IsTrue(b.pineTierCount <= PINE_TIER_COUNT_MAX);
            Assert::IsTrue(b.pineHeight >= PINE_HEIGHT_MIN);
            Assert::IsTrue(b.pineHeight <= PINE_HEIGHT_MAX);
            const double widthMin = (b.treeVariant == 1) ? BIRCH_TRUNK_WIDTH_MIN : PINE_WIDTH_MIN;
            const double widthMax = (b.treeVariant == 1) ? BIRCH_TRUNK_WIDTH_MAX : PINE_WIDTH_MAX;
            Assert::IsTrue(b.pineWidth >= widthMin);
            Assert::IsTrue(b.pineWidth <= widthMax);
        }
    }
    Assert::IsTrue(treeCount >= 1);
    Assert::IsTrue(treeCount <= 25);
}

TEST_METHOD(FirstTreeMatchesTheSpecDerivedPRNGSnapshot) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    const ExpectedTree expected = first_expected_tree(sim.blades.size());

    sim_set_scene(sim, Scene::Winter);

    Assert::IsTrue(expected.slotIndex < sim.blades.size());
    const Blade& b = sim.blades[expected.slotIndex];
    Assert::IsTrue(b.isPine);
    Assert::IsTrue(b.treeVariant == expected.variant);
    Assert::IsTrue(b.pineHeight == Near(expected.height).margin(1e-12));
    Assert::IsTrue(b.pineWidth  == Near(expected.width).margin(1e-12));
    Assert::IsTrue(b.pineTierCount == expected.tierCount);
}

TEST_METHOD(GrassSceneRestoresTreeSlotsToVanillaVariants) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    const ExpectedTree expected = first_expected_tree(sim.blades.size());
    Assert::IsTrue(expected.slotIndex < sim.blades.size());

    Blade& target = sim.blades[expected.slotIndex];
    target.isFlower = true;
    target.isMushroom = true;
    target.originalIsFlower = true;
    target.originalIsMushroom = true;

    sim_set_scene(sim, Scene::Winter);
    Assert::IsTrue(sim.blades[expected.slotIndex].isPine);
    Assert::IsFalse(sim.blades[expected.slotIndex].isFlower);
    Assert::IsFalse(sim.blades[expected.slotIndex].isMushroom);

    sim_set_scene(sim, Scene::Grass);
    Assert::IsFalse(sim.blades[expected.slotIndex].isPine);
    Assert::IsTrue(sim.blades[expected.slotIndex].treeVariant == 0);
    Assert::IsTrue(sim.blades[expected.slotIndex].isFlower);
    Assert::IsTrue(sim.blades[expected.slotIndex].isMushroom);
}

TEST_METHOD(WinterProducesBothPineAndBirchVariantsOverCanonicalSeed) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Winter);

    std::size_t pineCount = 0;
    std::size_t birchCount = 0;
    for (const Blade& b : sim.blades) {
        if (!b.isPine) continue;
        if (b.treeVariant == 0) {
            ++pineCount;
            Assert::IsTrue(b.pineWidth >= PINE_WIDTH_MIN);
            Assert::IsTrue(b.pineWidth <= PINE_WIDTH_MAX);
        } else {
            Assert::IsTrue(b.treeVariant == 1);
            ++birchCount;
            Assert::IsTrue(b.pineWidth >= BIRCH_TRUNK_WIDTH_MIN);
            Assert::IsTrue(b.pineWidth <= BIRCH_TRUNK_WIDTH_MAX);
        }
    }
    Assert::IsTrue(pineCount >= 1);
    Assert::IsTrue(birchCount >= 1);
}

TEST_METHOD(WinterSceneSuppressesMushroomsOnEverySlot) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    // Pre-mark a handful of slots as mushrooms; Winter must clear them all.
    for (std::size_t i = 0; i < sim.blades.size(); i += 17) {
        sim.blades[i].isMushroom = true;
        sim.blades[i].originalIsMushroom = true;
    }

    sim_set_scene(sim, Scene::Winter);

    for (const Blade& b : sim.blades) Assert::IsFalse(b.isMushroom);

    // Switching back to Grass must restore the original mushroom flags.
    sim_set_scene(sim, Scene::Grass);
    Assert::IsTrue(sim.blades[0].isMushroom == sim.blades[0].originalIsMushroom);
}

TEST_METHOD(WinterGrassHeightScaleIsPinned) {
    Assert::IsTrue(WINTER_GRASS_HEIGHT_SCALE == Near(0.5));
}

TEST_METHOD(TreeDepthConstantsArePinned) {
    Assert::IsTrue(TREE_BACKGROUND_PROBABILITY == Near(0.45));
    Assert::IsTrue(TREE_BG_SCALE == Near(0.62));
    Assert::IsTrue(TREE_BG_OPACITY == Near(0.78f));
}

TEST_METHOD(WinterMixesForegroundAndBackgroundTrees) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Winter);

    std::size_t fg = 0;
    std::size_t bg = 0;
    for (const Blade& b : sim.blades) {
        if (!b.isPine) continue;
        if (b.treeBackground) ++bg; else ++fg;
    }
    Assert::IsTrue(fg >= 1);
    Assert::IsTrue(bg >= 1);
}

TEST_METHOD(TreeDepthAssignmentIsDeterministicAcrossReEntry) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Winter);

    std::vector<bool> firstPass;
    for (const Blade& b : sim.blades) {
        if (b.isPine) firstPass.push_back(b.treeBackground);
    }

    // Leaving and re-entering Winter must reproduce the same depth layout.
    sim_set_scene(sim, Scene::Grass);
    sim_set_scene(sim, Scene::Winter);

    std::size_t idx = 0;
    for (const Blade& b : sim.blades) {
        if (!b.isPine) continue;
        Assert::IsTrue(idx < firstPass.size());
        Assert::IsTrue(b.treeBackground == firstPass[idx]);
        ++idx;
    }
    Assert::IsTrue(idx == firstPass.size());
}

TEST_METHOD(NonWinterScenesClearTheTreeBackgroundFlag) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Winter);
    sim_set_scene(sim, Scene::Grass);
    for (const Blade& b : sim.blades) Assert::IsFalse(b.treeBackground);
}

TEST_METHOD(WinterSceneLeavesTheCanonicalFirstBladeGeometryBitIdentical) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, 1.0);
    Assert::IsTrue(sim.blades.size() == desktopgrass::test::CANONICAL_BLADE_COUNT);

    sim_set_scene(sim, Scene::Winter);

    const Blade& first = sim.blades[0];
    const auto& expected = desktopgrass::test::CANONICAL_FIRST_10[0];
    Assert::IsTrue(first.baseX == Near(expected.baseX).margin(1e-12));
    Assert::IsTrue(first.height == Near(expected.height).margin(1e-12));
    Assert::IsTrue(first.thickness == Near(expected.thickness).margin(1e-12));
    Assert::IsTrue(first.hue == expected.hue);
    Assert::IsTrue(first.swayPhaseOffset == Near(expected.sway).margin(1e-12));
    Assert::IsTrue(first.stiffness == Near(expected.stiffness).margin(1e-12));
}
};
}
