// cat_coat_tests.cpp
//
// §17 Cat coat palette and deterministic coat variant tests. Mirrors Win2D CatCoatTests.cs.

#include "TestHelpers.h"
#include "Sim.h"

#include <cmath>
#include <cstdint>

using namespace desktopgrass;

namespace {

int count_kind(const Sim& sim, EntityKind kind) {
    int n = 0;
    for (const Entity& e : sim.entities) if (e.kind == kind) ++n;
    return n;
}

constexpr CatCoatPalette EXPECTED_CAT_COATS[CAT_COAT_VARIANT_COUNT] = {
    { 0xFF6B6259u, 0xFF3D3733u, 0xFF6B6259u, 0xFF3D3733u, 0xFF1A1614u },
    { 0xFFD89A6Fu, 0xFFA56B40u, 0xFFD89A6Fu, 0xFFA56B40u, 0xFF2B1A0Eu },
    { 0xFF2A2522u, 0xFF140F0Cu, 0xFF2A2522u, 0xFF140F0Cu, 0xFFD9B85Bu },
    { 0xFFEDE9E1u, 0xFFBDB7ABu, 0xFFEDE9E1u, 0xFFBDB7ABu, 0xFF1F1817u },
    { 0xFF7A5F3Cu, 0xFF4E3F26u, 0xFF7A5F3Cu, 0xFF4E3F26u, 0xFF1A1108u },
    { 0xFFC9B898u, 0xFF8E7F6Bu, 0xFFC9B898u, 0xFF8E7F6Bu, 0xFF2E251Du },
};

uint8_t next_cat_coat_after_prefix(Prng& side) {
    (void)prng_uniform(side, CAT_BODY_RADIUS + 8.0, 1920.0 - (CAT_BODY_RADIUS + 8.0));
    (void)prng_uniform(side, CAT_WALK_SPEED_MIN, CAT_WALK_SPEED_MAX);
    (void)prng_uniform(side, 0.0, 1.0);
    (void)prng_next_u32(side);
    (void)prng_uniform(side, CAT_WALK_DURATION_MIN, CAT_WALK_DURATION_MAX);
    (void)prng_index(side, static_cast<uint32_t>(sizeof(CAT_NAME_POOL) / sizeof(CAT_NAME_POOL[0])));
    return static_cast<uint8_t>(prng_index(side, static_cast<uint32_t>(CAT_COAT_VARIANT_COUNT)));
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(CatCoatTests)
{
public:
TEST_METHOD(CatCoatVariantCountIsPinned) {
    Assert::IsTrue(CAT_COAT_VARIANT_COUNT == 6);
}

TEST_METHOD(CatCoatPaletteZeroMatchesBackwardCompatibleAliases) {
    Assert::IsTrue(CAT_COAT_PALETTES[0].body == CAT_BODY_COLOR);
    Assert::IsTrue(CAT_COAT_PALETTES[0].leg  == CAT_LEG_COLOR);
    Assert::IsTrue(CAT_COAT_PALETTES[0].face == CAT_FACE_COLOR);
    Assert::IsTrue(CAT_COAT_PALETTES[0].ear  == CAT_EAR_COLOR);
    Assert::IsTrue(CAT_COAT_PALETTES[0].ink  == CAT_INK_COLOR);
}

TEST_METHOD(AllCatCoatPalettesArePinned) {
    for (int i = 0; i < CAT_COAT_VARIANT_COUNT; ++i) {
        Assert::IsTrue(CAT_COAT_PALETTES[i].body == EXPECTED_CAT_COATS[i].body);
        Assert::IsTrue(CAT_COAT_PALETTES[i].leg  == EXPECTED_CAT_COATS[i].leg);
        Assert::IsTrue(CAT_COAT_PALETTES[i].face == EXPECTED_CAT_COATS[i].face);
        Assert::IsTrue(CAT_COAT_PALETTES[i].ear  == EXPECTED_CAT_COATS[i].ear);
        Assert::IsTrue(CAT_COAT_PALETTES[i].ink  == EXPECTED_CAT_COATS[i].ink);
    }
}

TEST_METHOD(CatCoatBodyColorsAreDistinct) {
    for (int i = 0; i < CAT_COAT_VARIANT_COUNT; ++i) {
        for (int j = i + 1; j < CAT_COAT_VARIANT_COUNT; ++j) {
            Assert::IsTrue(CAT_COAT_PALETTES[i].body != CAT_COAT_PALETTES[j].body);
        }
    }
}

TEST_METHOD(CanonicalCatFlockPinsDeterministicCoatVariants) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);

    const uint8_t expectedCoats[] = { 1 };
    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Cat) continue;
        Assert::IsTrue(seen < static_cast<int>(sizeof(expectedCoats) / sizeof(expectedCoats[0])));
        Assert::IsTrue(e.coatVariantIndex == expectedCoats[seen]);
        ++seen;
    }
    Assert::IsTrue(seen == static_cast<int>(sizeof(expectedCoats) / sizeof(expectedCoats[0])));
}

TEST_METHOD(CatCoatPRNGDrawFollowsNameIndex) {
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);

    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);

    const double countDraw = prng_uniform(side, CAT_COUNT_MIN, CAT_COUNT_MAX + 1);
    int expectedCount = static_cast<int>(std::floor(countDraw));
    if (expectedCount < CAT_COUNT_MIN) expectedCount = CAT_COUNT_MIN;
    if (expectedCount > CAT_COUNT_MAX) expectedCount = CAT_COUNT_MAX;
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == expectedCount);

    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Cat) continue;
        const uint8_t expectedCoat = next_cat_coat_after_prefix(side);
        Assert::IsTrue(e.coatVariantIndex == expectedCoat);
        ++seen;
    }
    Assert::IsTrue(seen == expectedCount);
}

TEST_METHOD(GeneratedCatCoatsAlwaysStayWithinPaletteRange) {
    for (uint64_t i = 0; i < 128; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15ull;
        Sim sim = sim_init(seed, 1920.0, DEFAULT_DENSITY);
        sim_set_critter(sim, CritterKind::Cat);

        int seen = 0;
        for (const Entity& e : sim.entities) {
            if (e.kind != EntityKind::Cat) continue;
            Assert::IsTrue(e.coatVariantIndex < CAT_COAT_VARIANT_COUNT);
            ++seen;
        }
        Assert::IsTrue(seen >= CAT_COUNT_MIN);
        Assert::IsTrue(seen <= CAT_COUNT_MAX);
    }
}

TEST_METHOD(SheepKeepDefaultCoatVariantZero) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);

    Assert::IsTrue(count_kind(sim, EntityKind::Sheep) >= SHEEP_COUNT_MIN);
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Sheep) continue;
        Assert::IsTrue(e.coatVariantIndex == 0);
    }
}

TEST_METHOD(FixedCatCountCoatPRNGSkipsOnlyTheCountDraw) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);
    sim_set_critter_count(sim, 3);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 3);

    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);

    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Cat) continue;
        const uint8_t expectedCoat = next_cat_coat_after_prefix(side);
        Assert::IsTrue(e.coatVariantIndex == expectedCoat);
        ++seen;
    }
    Assert::IsTrue(seen == 3);
}
};
}
