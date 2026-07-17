// prng_tests.cpp
//
// Conformance + snapshot tests for the PRNG (architecture.md §3).

#include "TestHelpers.h"
#include "Sim.h"
#include "snapshot_data.h"

using namespace desktopgrass;
using namespace desktopgrass::test;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(PrngTests)
{
public:
TEST_METHOD(PRNGMatchesTheCanonical16OutputSnapshot) {
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED);

    for (std::size_t i = 0; i < 16; ++i) {
        uint64_t v = prng_next_u64(p);
        Assert::IsTrue(v == CANONICAL_PRNG_SNAPSHOT[i]);
    }
}

TEST_METHOD(PRNGIsDeterministicForAGivenSeed) {
    Prng a, b;
    prng_init(a, CANONICAL_TEST_SEED);
    prng_init(b, CANONICAL_TEST_SEED);
    for (int i = 0; i < 1000; ++i) {
        Assert::IsTrue(prng_next_u64(a) == prng_next_u64(b));
    }
}

TEST_METHOD(PRNGDecorrelatesSeed0ViaSplitmix64) {
    // seed == 0 must not produce a stuck-at-zero PRNG.
    Prng p;
    prng_init(p, 0);
    Assert::IsTrue(p.state != 0);
    uint64_t a = prng_next_u64(p);
    uint64_t b = prng_next_u64(p);
    Assert::IsTrue(a != 0);
    Assert::IsTrue(b != 0);
    Assert::IsTrue(a != b);
}

TEST_METHOD(PrngNextUnitIsIn01) {
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED);
    for (int i = 0; i < 10000; ++i) {
        double u = prng_next_unit(p);
        Assert::IsTrue(u >= 0.0);
        Assert::IsTrue(u <  1.0);
    }
}

TEST_METHOD(PrngUniformStaysWithinLoHi) {
    Prng p;
    prng_init(p, 12345);
    for (int i = 0; i < 10000; ++i) {
        double v = prng_uniform(p, 8.0, 40.0);
        Assert::IsTrue(v >= 8.0);
        Assert::IsTrue(v <  40.0);
    }
}

TEST_METHOD(PrngIndexIsIn0N) {
    Prng p;
    prng_init(p, 42);
    bool sawZero = false;
    bool sawFive = false;
    for (int i = 0; i < 10000; ++i) {
        uint32_t v = prng_index(p, PALETTE_SIZE);
        Assert::IsTrue(v < PALETTE_SIZE);
        if (v == 0) sawZero = true;
        if (v == 5) sawFive = true;
    }
    // Distribution sanity. Not strict — just confirms we cover both extremes.
    Assert::IsTrue(sawZero);
    Assert::IsTrue(sawFive);
}
};
}
