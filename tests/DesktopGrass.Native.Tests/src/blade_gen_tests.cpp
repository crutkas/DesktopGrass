// blade_gen_tests.cpp
//
// Snapshot + invariant tests for procedural blade generation (architecture.md §5).

#include "TestHelpers.h"
#include "Sim.h"
#include "snapshot_data.h"

#include <cmath>
#include <vector>

using namespace desktopgrass;
using namespace desktopgrass::test;

namespace {

void requireBladeEquals(const Blade& actual, const SnapshotBlade& expected) {
    Assert::IsTrue(actual.baseX           == Near(expected.baseX          ).margin(1e-12));
    Assert::IsTrue(actual.height          == Near(expected.height         ).margin(1e-12));
    Assert::IsTrue(actual.thickness       == Near(expected.thickness      ).margin(1e-12));
    Assert::IsTrue(actual.hue             == expected.hue);
    Assert::IsTrue(actual.swayPhaseOffset == Near(expected.sway          ).margin(1e-12));
    Assert::IsTrue(actual.stiffness       == Near(expected.stiffness     ).margin(1e-12));
    Assert::IsTrue(actual.isFlower             == expected.isFlower);
    Assert::IsTrue(actual.flowerHeadColorIdx   == expected.flowerHeadColorIdx);
    Assert::IsTrue(actual.flowerHeadRadius     == Near(expected.flowerHeadRadius).margin(1e-12));
    Assert::IsTrue(actual.heightBonus          == Near(expected.heightBonus    ).margin(1e-12));
}

} // anonymous

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(BladeGenTests)
{
public:
TEST_METHOD(BladeGenerationMatchesTheCanonicalSnapshot) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);

    Assert::IsTrue(blades.size() == CANONICAL_BLADE_COUNT);

    {
        for (std::size_t i = 0; i < 10; ++i) {
            requireBladeEquals(blades[i], CANONICAL_FIRST_10[i]);
        }
    }

    {
        const std::size_t start = blades.size() - 10;
        for (std::size_t i = 0; i < 10; ++i) {
            requireBladeEquals(blades[start + i], CANONICAL_LAST_10[i]);
        }
    }
}

TEST_METHOD(BladeFieldsStayWithinSpecRanges) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);

    constexpr double TWO_PI = 6.283185307179586;
    for (std::size_t i = 0; i < blades.size(); ++i) {
        const Blade& b = blades[i];
        Assert::IsTrue(b.baseX           >= 0.0);
        Assert::IsTrue(b.baseX           <  1920.0);
        Assert::IsTrue(b.height          >= BLADE_HEIGHT_MIN);
        Assert::IsTrue(b.height          <  BLADE_HEIGHT_MAX);
        Assert::IsTrue(b.thickness       >= BLADE_THICKNESS_MIN);
        Assert::IsTrue(b.thickness       <  BLADE_THICKNESS_MAX);
        Assert::IsTrue(b.hue             <  PALETTE_SIZE);
        Assert::IsTrue(b.swayPhaseOffset >= 0.0);
        Assert::IsTrue(b.swayPhaseOffset <  TWO_PI);
        Assert::IsTrue(b.stiffness       >= STIFFNESS_MIN);
        Assert::IsTrue(b.stiffness       <  STIFFNESS_MAX);
        Assert::IsTrue(b.cutHeight        == Near(1.0));
        Assert::IsTrue(b.gustVelocity     == Near(0.0));
        Assert::IsTrue(b.cutAnimStart     == Near(-1.0));
        Assert::IsTrue(b.cutInitialHeight == Near(1.0));
        if (b.isFlower) {
            Assert::IsTrue(b.flowerHeadColorIdx < FLOWER_PALETTE_SIZE);
            Assert::IsTrue(b.flowerHeadRadius >= FLOWER_HEAD_RADIUS_MIN);
            Assert::IsTrue(b.flowerHeadRadius <  FLOWER_HEAD_RADIUS_MAX);
            Assert::IsTrue(b.heightBonus      >= FLOWER_HEIGHT_BONUS_MIN);
            Assert::IsTrue(b.heightBonus      <  FLOWER_HEIGHT_BONUS_MAX);
        } else {
            Assert::IsTrue(b.flowerHeadColorIdx == 0);
            Assert::IsTrue(b.flowerHeadRadius   == Near(0.0));
            Assert::IsTrue(b.heightBonus        == Near(1.0));
        }
    }
}

TEST_METHOD(FlowerCountIsNearConfiguredProbabilityAndOrdinaryBladesUseDefaults) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    Assert::IsTrue(blades.size() > 100);

    std::size_t flowerCount = 0;
    for (const Blade& b : blades) {
        if (b.isFlower) {
            ++flowerCount;
        } else {
            Assert::IsTrue(b.flowerHeadColorIdx == 0);
            Assert::IsTrue(b.flowerHeadRadius   == Near(0.0));
            Assert::IsTrue(b.heightBonus        == Near(1.0));
        }
    }

    const double n  = static_cast<double>(blades.size());
    const double p  = FLOWER_PROBABILITY;
    const double mu = n * p;
    const double sd = std::sqrt(n * p * (1.0 - p));
    Assert::IsTrue(flowerCount >= static_cast<std::size_t>(std::floor(mu - 3.0 * sd)));
    Assert::IsTrue(flowerCount <= static_cast<std::size_t>(std::ceil(mu + 3.0 * sd)));
}

TEST_METHOD(BladeBaseXIsStrictlyIncreasing) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    Assert::IsTrue(blades.size() > 0);
    for (std::size_t i = 1; i < blades.size(); ++i) {
        Assert::IsTrue(blades[i].baseX > blades[i-1].baseX);
    }
}

TEST_METHOD(BladeGenerationIsDeterministicAcrossRepeatRuns) {
    std::vector<Blade> a, b;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, a);
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, b);
    Assert::IsTrue(a.size() == b.size());
    for (std::size_t i = 0; i < a.size(); ++i) {
        Assert::IsTrue(a[i].baseX           == b[i].baseX);
        Assert::IsTrue(a[i].height          == b[i].height);
        Assert::IsTrue(a[i].thickness       == b[i].thickness);
        Assert::IsTrue(a[i].hue             == b[i].hue);
        Assert::IsTrue(a[i].swayPhaseOffset == b[i].swayPhaseOffset);
        Assert::IsTrue(a[i].stiffness       == b[i].stiffness);
    }
}

TEST_METHOD(DensityScalesBladeCountRoughlyLinearly) {
    std::vector<Blade> low, high;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 0.5, low);
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 2.0, high);
    Assert::IsTrue(low.size()  > 0);
    Assert::IsTrue(high.size() > low.size() * 3);  // 4x density ≈ 4x blades, allow slack
}

TEST_METHOD(BladeCountNearPlanDefaultAtDensity125) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.25, blades);
    // Plan target: ~400 blades per 1920 px at the v1 default density of 1.25.
    Assert::IsTrue(blades.size() >= 350);
    Assert::IsTrue(blades.size() <= 450);
}
};
}
