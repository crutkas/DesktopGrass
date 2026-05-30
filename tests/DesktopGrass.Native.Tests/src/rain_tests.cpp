#include "catch.hpp"

#include "Sim.h"
#include "Constants.h"

#include <algorithm>
#include <cstddef>
#include <iterator>
#include <set>

using namespace desktopgrass;

namespace {

Sim MakeRainTestSim(double monitorWidth = 1920.0) {
    return sim_init(CANONICAL_TEST_SEED, monitorWidth, 1.0);
}

int CountRaindrops(const Sim& sim) {
    return static_cast<int>(std::count_if(sim.entities.begin(), sim.entities.end(),
        [](const Entity& e) { return e.kind == EntityKind::Raindrop; }));
}

std::set<uint32_t> ObserveRaindrops(Sim& sim, double seconds, double dt = 0.01) {
    std::set<uint32_t> seen;
    const int steps = static_cast<int>(seconds / dt);
    for (int i = 0; i < steps; ++i) {
        sim_tick(sim, dt, nullptr, 0);
        for (const Entity& e : sim.entities) {
            if (e.kind == EntityKind::Raindrop) seen.insert(e.seed);
        }
    }
    return seen;
}

} // namespace

TEST_CASE("EntityKind::Raindrop is pinned", "[rain][enum]") {
    REQUIRE(static_cast<int>(EntityKind::None)       == 0);
    REQUIRE(static_cast<int>(EntityKind::Tumbleweed) == 1);
    REQUIRE(static_cast<int>(EntityKind::Snowflake)  == 2);
    REQUIRE(static_cast<int>(EntityKind::Sheep)      == 3);
    REQUIRE(static_cast<int>(EntityKind::Cat)        == 4);
    REQUIRE(static_cast<int>(EntityKind::Raindrop)   == 5);
}

TEST_CASE("Raindrop PRNG salt is distinct", "[rain][constants]") {
    const uint64_t salts[] = {
        REGROW_PRNG_SALT,
        FLOWER_PRNG_SALT,
        MUSHROOM_PRNG_SALT,
        AMBIENT_GUST_PRNG_SALT,
        CACTUS_PRNG_SALT,
        TUMBLEWEED_PRNG_SALT,
        CRITTER_PRNG_SALT,
        SNOWFLAKE_PRNG_SALT,
        PINE_PRNG_SALT,
        RAINDROP_PRNG_SALT,
    };
    for (std::size_t i = 0; i < std::size(salts); ++i) {
        for (std::size_t j = i + 1; j < std::size(salts); ++j) {
            REQUIRE(salts[i] != salts[j]);
        }
    }
}

TEST_CASE("Rain constants are pinned", "[rain][constants]") {
    REQUIRE(RAINDROP_PRNG_SALT == 0xD40F0A1DD40F0A1Dull);
    REQUIRE(RAINDROP_EMIT_RATE_PER_1920DIP == Approx(6.0));
    REQUIRE(RAINDROP_LENGTH_MIN == Approx(4.0));
    REQUIRE(RAINDROP_LENGTH_MAX == Approx(7.0));
    REQUIRE(RAINDROP_THICKNESS == Approx(0.9));
    REQUIRE(RAINDROP_FALL_SPEED_MIN == Approx(240.0));
    REQUIRE(RAINDROP_FALL_SPEED_MAX == Approx(360.0));
    REQUIRE(RAINDROP_DRIFT_MIN == Approx(-8.0));
    REQUIRE(RAINDROP_DRIFT_MAX == Approx(8.0));
    REQUIRE(RAINDROP_COLOR == 0x88B0C4D0u);
    REQUIRE(RAINDROP_LIFETIME_PADDING_SEC == Approx(0.3));
}

TEST_CASE("Rain does not emit in Desert or Winter", "[rain][scene]") {
    Sim sim = MakeRainTestSim();

    sim_set_scene(sim, Scene::Desert);
    sim_tick(sim, 5.0, nullptr, 0);
    REQUIRE(CountRaindrops(sim) == 0);

    sim_set_scene(sim, Scene::Winter);
    sim_tick(sim, 5.0, nullptr, 0);
    REQUIRE(CountRaindrops(sim) == 0);
}

TEST_CASE("Rain emits in Grass", "[rain][scene]") {
    Sim sim = MakeRainTestSim();

    const std::set<uint32_t> seen = ObserveRaindrops(sim, 5.0);

    REQUIRE(seen.size() >= 20);
}

TEST_CASE("Rain emission rate scales with monitor width", "[rain][rate]") {
    Sim wide = MakeRainTestSim(1920.0);
    Sim narrow = MakeRainTestSim(960.0);

    const std::size_t wideCount = ObserveRaindrops(wide, 5.0).size();
    const std::size_t narrowCount = ObserveRaindrops(narrow, 5.0).size();

    REQUIRE(wideCount > narrowCount);
    REQUIRE(static_cast<double>(wideCount) >= static_cast<double>(narrowCount) * 1.5);
    REQUIRE(static_cast<double>(wideCount) <= static_cast<double>(narrowCount) * 2.8);
}

TEST_CASE("Raindrop fields are within expected ranges", "[rain][entities]") {
    Sim sim = MakeRainTestSim();

    sim_tick_entities(sim, 0.0);

    REQUIRE(CountRaindrops(sim) >= 1);
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Raindrop) continue;
        REQUIRE(e.size >= RAINDROP_LENGTH_MIN);
        REQUIRE(e.size <= RAINDROP_LENGTH_MAX);
        REQUIRE(e.y == Approx(-e.size - 2.0).margin(1e-12));
        REQUIRE(e.y < 0.0);
        REQUIRE(e.vy >= RAINDROP_FALL_SPEED_MIN);
        REQUIRE(e.vy <= RAINDROP_FALL_SPEED_MAX);
        REQUIRE(e.vx >= RAINDROP_DRIFT_MIN);
        REQUIRE(e.vx <= RAINDROP_DRIFT_MAX);
        REQUIRE(e.lifetime > 0.0);
    }
}

TEST_CASE("Raindrop expires via lifetime", "[rain][entities]") {
    Sim sim = MakeRainTestSim();
    sim.currentScene = Scene::Desert;
    sim.entities.clear();

    Entity e{};
    e.kind = EntityKind::Raindrop;
    e.lifetime = 0.5;
    e.age = 0.4;
    e.vy = RAINDROP_FALL_SPEED_MIN;
    sim.entities.push_back(e);

    sim_tick_entities(sim, 0.2);

    REQUIRE(CountRaindrops(sim) == 0);
}

TEST_CASE("Raindrop PRNG draw order is bit-identical", "[rain][prng]") {
    Sim sim = MakeRainTestSim();
    Prng side{};
    prng_init(side, CANONICAL_TEST_SEED ^ RAINDROP_PRNG_SALT);
    const double lambda = RAINDROP_EMIT_RATE_PER_1920DIP * sim.monitorWidth / 1920.0;
    double expectedNext = 0.0;

    for (int i = 0; i < 5; ++i) {
        sim.globalTime = sim.nextRaindropSpawnTime;
        sim_tick_entities(sim, 0.0);

        REQUIRE(CountRaindrops(sim) == i + 1);
        const Entity& e = sim.entities.back();
        const double expectedSize = prng_uniform(side, RAINDROP_LENGTH_MIN, RAINDROP_LENGTH_MAX);
        const double expectedX = prng_uniform(side, -10.0, sim.monitorWidth + 10.0);
        const double expectedFallSpeed = prng_uniform(side, RAINDROP_FALL_SPEED_MIN, RAINDROP_FALL_SPEED_MAX);
        const double expectedVx = prng_uniform(side, RAINDROP_DRIFT_MIN, RAINDROP_DRIFT_MAX);
        const uint32_t expectedSeed = prng_next_u32(side);
        expectedNext += prng_exponential(side, lambda);

        REQUIRE(e.size == Approx(expectedSize).margin(1e-12));
        REQUIRE(e.x == Approx(expectedX).margin(1e-12));
        REQUIRE(e.y == Approx(-expectedSize - 2.0).margin(1e-12));
        REQUIRE(e.vx == Approx(expectedVx).margin(1e-12));
        REQUIRE(e.vy == Approx(expectedFallSpeed).margin(1e-12));
        REQUIRE(e.rotation == Approx(0.0));
        REQUIRE(e.rotationSpeed == Approx(0.0));
        REQUIRE(e.seed == expectedSeed);
        REQUIRE(e.lifetime == Approx((sim.windowHeight + expectedSize) / expectedFallSpeed
                                      + RAINDROP_LIFETIME_PADDING_SEC).margin(1e-12));
        REQUIRE(sim.nextRaindropSpawnTime == Approx(expectedNext).margin(1e-12));
    }
}

TEST_CASE("Scene switch from Grass to Desert soft-fades rain", "[rain][scene]") {
    Sim sim = MakeRainTestSim();
    sim_tick_entities(sim, 0.0);

    std::set<uint32_t> before;
    for (const Entity& e : sim.entities) {
        if (e.kind == EntityKind::Raindrop) before.insert(e.seed);
    }
    REQUIRE_FALSE(before.empty());

    sim_set_scene(sim, Scene::Desert);
    REQUIRE(CountRaindrops(sim) == static_cast<int>(before.size()));

    sim_tick(sim, 0.2, nullptr, 0);
    for (const Entity& e : sim.entities) {
        if (e.kind == EntityKind::Raindrop) REQUIRE(before.count(e.seed) == 1);
    }

    sim_tick(sim, 2.0, nullptr, 0);
    REQUIRE(CountRaindrops(sim) == 0);
}
