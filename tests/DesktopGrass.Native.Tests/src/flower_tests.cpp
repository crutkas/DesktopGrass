// flower_tests.cpp
//
// Tests for §5 flower stream + §7 head-render contract.

#include "TestHelpers.h"
#include "Sim.h"

#include <cmath>
#include <vector>

using namespace desktopgrass;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(FlowerTests)
{
public:
TEST_METHOD(FlowerStreamIsDeterministicForAGivenSeed) {
    std::vector<Blade> a, b;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, a);
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, b);
    Assert::IsTrue(a.size() == b.size());
    for (std::size_t i = 0; i < a.size(); ++i) {
        Assert::IsTrue(a[i].isFlower           == b[i].isFlower);
        Assert::IsTrue(a[i].flowerHeadColorIdx == b[i].flowerHeadColorIdx);
        Assert::IsTrue(a[i].flowerHeadRadius   == b[i].flowerHeadRadius);
        Assert::IsTrue(a[i].heightBonus        == b[i].heightBonus);
    }
}

TEST_METHOD(FlowerCountIsWithin3SigmaOfFLOWERPROBABILITY) {
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    Assert::IsTrue(blades.size() > 100);

    std::size_t flowerCount = 0;
    for (const Blade& b : blades) if (b.isFlower) ++flowerCount;

    const double n   = static_cast<double>(blades.size());
    const double p   = FLOWER_PROBABILITY;
    const double mu  = n * p;
    const double sd  = std::sqrt(n * p * (1.0 - p));
    // 3-sigma tolerance keeps this test stable across spec-conformant
    // PRNG sequences. For seed=0x6B6173746F, n=321 we expect ~12.84 with
    // sd≈3.51, so [2,24] is the acceptable range.
    Assert::IsTrue(flowerCount >= static_cast<std::size_t>(std::floor(mu - 3.0 * sd)));
    Assert::IsTrue(flowerCount <= static_cast<std::size_t>(std::ceil(mu + 3.0 * sd)));
}

TEST_METHOD(FlowerStreamDoesNotPerturbTheMainStream) {
    // Regenerate blades and assert the main-stream fields match the
    // canonical snapshot. This is implicitly covered by blade_gen_tests
    // (the first/last 10 still match), but pin it here for clarity.
    std::vector<Blade> blades;
    generate_blades(CANONICAL_TEST_SEED, 1920.0, 1.0, blades);
    Assert::IsTrue(blades.size() > 0);
    Assert::IsTrue(blades[0].baseX == Near(4.941073726820111).margin(1e-12));
    Assert::IsTrue(blades[0].height == Near(24.469991818248864).margin(1e-12));
}
};
}
