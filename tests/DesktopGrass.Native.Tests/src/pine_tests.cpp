// pine_tests.cpp - §15.1 Winter pine trees (slot-bound, mirrors §14 cacti).

#include "../third_party/catch2/catch.hpp"
#include "Sim.h"
#include "snapshot_data.h"

#include <cmath>
#include <cstddef>

using namespace desktopgrass;

namespace {

constexpr double kMonitor1920 = 1920.0;

struct ExpectedPine {
    std::size_t slotIndex = 0;
    double height = 0.0;
    double width = 0.0;
    int tierCount = 0;
};

ExpectedPine first_expected_pine(std::size_t bladeCount) {
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED ^ PINE_PRNG_SALT);

    for (std::size_t i = 0; i < bladeCount; ++i) {
        const double r = prng_uniform(p, 0.0, 1.0);
        if (r >= PINE_PROBABILITY) continue;

        ExpectedPine expected{};
        expected.slotIndex = i;
        expected.height = prng_uniform(p, PINE_HEIGHT_MIN, PINE_HEIGHT_MAX);
        expected.width  = prng_uniform(p, PINE_WIDTH_MIN,  PINE_WIDTH_MAX);
        const double tierDraw = prng_uniform(p,
            static_cast<double>(PINE_TIER_COUNT_MIN),
            static_cast<double>(PINE_TIER_COUNT_MAX + 1));
        int tiers = static_cast<int>(std::floor(tierDraw));
        if (tiers < PINE_TIER_COUNT_MIN) tiers = PINE_TIER_COUNT_MIN;
        if (tiers > PINE_TIER_COUNT_MAX) tiers = PINE_TIER_COUNT_MAX;
        expected.tierCount = tiers;
        return expected;
    }

    FAIL("canonical seed produced no pine slot");
    return {};
}

} // anonymous

TEST_CASE("Pine constants are pinned", "[pine][constants]") {
    REQUIRE(PINE_PROBABILITY == Approx(0.006));
    REQUIRE(PINE_HEIGHT_MIN == Approx(36.0));
    REQUIRE(PINE_HEIGHT_MAX == Approx(72.0));
    REQUIRE(PINE_WIDTH_MIN  == Approx(16.0));
    REQUIRE(PINE_WIDTH_MAX  == Approx(28.0));
    REQUIRE(PINE_TIER_COUNT_MIN == 2);
    REQUIRE(PINE_TIER_COUNT_MAX == 4);
    REQUIRE(PINE_TIP_TAPER == Approx(0.25));
    REQUIRE(PINE_TIER_OVERLAP == Approx(0.15));
    REQUIRE(PINE_SNOW_CAP_FRACTION == Approx(0.30));
    REQUIRE(PINE_COLOR == 0xFF1B5E20u);
    REQUIRE(PINE_PRNG_SALT == 0x50494E4550494E45ull);
}

TEST_CASE("sim_set_scene Winter promotes some slots to pines", "[pine][scene]") {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Winter);

    REQUIRE(sim.currentScene == Scene::Winter);
    std::size_t pineCount = 0;
    for (const Blade& b : sim.blades) {
        if (b.isPine) {
            ++pineCount;
            REQUIRE(b.pineTierCount >= PINE_TIER_COUNT_MIN);
            REQUIRE(b.pineTierCount <= PINE_TIER_COUNT_MAX);
            REQUIRE(b.pineHeight >= PINE_HEIGHT_MIN);
            REQUIRE(b.pineHeight <= PINE_HEIGHT_MAX);
            REQUIRE(b.pineWidth  >= PINE_WIDTH_MIN);
            REQUIRE(b.pineWidth  <= PINE_WIDTH_MAX);
        }
    }
    REQUIRE(pineCount >= 1);
    REQUIRE(pineCount <= 20);
}

TEST_CASE("First pine matches the spec-derived PRNG snapshot", "[pine][snapshot]") {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    const ExpectedPine expected = first_expected_pine(sim.blades.size());

    sim_set_scene(sim, Scene::Winter);

    REQUIRE(expected.slotIndex < sim.blades.size());
    const Blade& b = sim.blades[expected.slotIndex];
    REQUIRE(b.isPine);
    REQUIRE(b.pineTierCount == expected.tierCount);
    REQUIRE(b.pineHeight == Approx(expected.height).margin(1e-12));
    REQUIRE(b.pineWidth  == Approx(expected.width).margin(1e-12));
}

TEST_CASE("Grass scene restores pine slots to vanilla variants", "[pine][restore]") {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, DEFAULT_DENSITY);
    const ExpectedPine expected = first_expected_pine(sim.blades.size());
    REQUIRE(expected.slotIndex < sim.blades.size());

    Blade& target = sim.blades[expected.slotIndex];
    target.isFlower = true;
    target.isMushroom = true;
    target.originalIsFlower = true;
    target.originalIsMushroom = true;

    sim_set_scene(sim, Scene::Winter);
    REQUIRE(sim.blades[expected.slotIndex].isPine);
    REQUIRE_FALSE(sim.blades[expected.slotIndex].isFlower);
    REQUIRE_FALSE(sim.blades[expected.slotIndex].isMushroom);

    sim_set_scene(sim, Scene::Grass);
    REQUIRE_FALSE(sim.blades[expected.slotIndex].isPine);
    REQUIRE(sim.blades[expected.slotIndex].isFlower);
    REQUIRE(sim.blades[expected.slotIndex].isMushroom);
}

TEST_CASE("Winter scene leaves the canonical first blade geometry bit-identical", "[pine][snapshot]") {
    Sim sim = sim_init(CANONICAL_TEST_SEED, kMonitor1920, 1.0);
    REQUIRE(sim.blades.size() == desktopgrass::test::CANONICAL_BLADE_COUNT);

    sim_set_scene(sim, Scene::Winter);

    const Blade& first = sim.blades[0];
    const auto& expected = desktopgrass::test::CANONICAL_FIRST_10[0];
    REQUIRE(first.baseX == Approx(expected.baseX).margin(1e-12));
    REQUIRE(first.height == Approx(expected.height).margin(1e-12));
    REQUIRE(first.thickness == Approx(expected.thickness).margin(1e-12));
    REQUIRE(first.hue == expected.hue);
    REQUIRE(first.swayPhaseOffset == Approx(expected.sway).margin(1e-12));
    REQUIRE(first.stiffness == Approx(expected.stiffness).margin(1e-12));
}
