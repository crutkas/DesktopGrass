// mushroom_tests.cpp
//
// Tests for §5 mushroom stream + §7 mushroom-render contract.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>
#include <cmath>
#include <vector>

using namespace desktopgrass;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(MushroomTests)
{
public:
TEST_METHOD(MushroomStreamIsDeterministicForAGivenSeed) {
    std::vector<Blade> a, b;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, a);
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, b);
    Assert::IsTrue(a.size() == b.size());
    for (std::size_t i = 0; i < a.size(); ++i) {
        Assert::IsTrue(a[i].isMushroom            == b[i].isMushroom);
        Assert::IsTrue(a[i].mushroomCapColorIdx   == b[i].mushroomCapColorIdx);
        Assert::IsTrue(a[i].mushroomCapWidth      == b[i].mushroomCapWidth);
        Assert::IsTrue(a[i].mushroomCapHeight     == b[i].mushroomCapHeight);
        Assert::IsTrue(a[i].mushroomStemHeight    == b[i].mushroomStemHeight);
        Assert::IsTrue(a[i].mushroomStemThickness == b[i].mushroomStemThickness);
    }
}

TEST_METHOD(MushroomCountIsWithin3SigmaOfMUSHROOMPROBABILITY) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    Assert::IsTrue(blades.size() > 100);

    std::size_t mushroomCount = 0;
    for (const Blade& b : blades) if (b.isMushroom) ++mushroomCount;

    const double n   = static_cast<double>(blades.size());
    const double p   = MUSHROOM_PROBABILITY;
    const double mu  = n * p;
    const double sd  = std::sqrt(n * p * (1.0 - p));
    // 3-sigma tolerance keeps this test stable across spec-conformant
    // PRNG sequences. For seed=0x6B6173746F, n=321 we expect ~8.03 with
    // sd≈2.80, so the inclusive 3-sigma range is roughly [0, 17].
    const double lo  = std::max(0.0, std::floor(mu - 3.0 * sd));
    Assert::IsTrue(mushroomCount >= static_cast<std::size_t>(lo));
    Assert::IsTrue(mushroomCount <= static_cast<std::size_t>(std::ceil(mu + 3.0 * sd)));
}

TEST_METHOD(MushroomStreamDoesNotPerturbTheMainStream) {
    // The mushroom stream is independent (seed ^ MUSHROOM_PRNG_SALT) so
    // the main-stream first-blade values must still match the canonical.
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    Assert::IsTrue(blades.size() > 0);
    Assert::IsTrue(blades[0].baseX == Near(4.941073726820111).margin(1e-12));
    Assert::IsTrue(blades[0].height == Near(24.469991818248864).margin(1e-12));
}

TEST_METHOD(NonMushroomBladesHaveZeroMushroomFields) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    for (const Blade& b : blades) {
        if (!b.isMushroom) {
            Assert::IsTrue(b.mushroomCapColorIdx   == 0);
            Assert::IsTrue(b.mushroomCapWidth      == 0.0);
            Assert::IsTrue(b.mushroomCapHeight     == 0.0);
            Assert::IsTrue(b.mushroomStemHeight    == 0.0);
            Assert::IsTrue(b.mushroomStemThickness == 0.0);
        }
    }
}

TEST_METHOD(MushroomFieldRangesRespectSpec) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    for (const Blade& b : blades) {
        if (b.isMushroom) {
            Assert::IsTrue(b.mushroomCapColorIdx < MUSHROOM_PALETTE_SIZE);
            Assert::IsTrue(b.mushroomCapWidth      >= MUSHROOM_CAP_WIDTH_MIN);
            Assert::IsTrue(b.mushroomCapWidth      <  MUSHROOM_CAP_WIDTH_MAX);
            Assert::IsTrue(b.mushroomCapHeight     >= MUSHROOM_CAP_HEIGHT_MIN);
            Assert::IsTrue(b.mushroomCapHeight     <  MUSHROOM_CAP_HEIGHT_MAX);
            Assert::IsTrue(b.mushroomStemHeight    >= MUSHROOM_STEM_HEIGHT_MIN);
            Assert::IsTrue(b.mushroomStemHeight    <  MUSHROOM_STEM_HEIGHT_MAX);
            Assert::IsTrue(b.mushroomStemThickness >= MUSHROOM_STEM_THICKNESS_MIN);
            Assert::IsTrue(b.mushroomStemThickness <  MUSHROOM_STEM_THICKNESS_MAX);
        }
    }
}
};
}
