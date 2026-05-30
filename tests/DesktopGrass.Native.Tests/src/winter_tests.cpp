#include "catch.hpp"

#include "Sim.h"
#include "Constants.h"
#include "snapshot_data.h"

#include <cstddef>
#include <cmath>
#include <algorithm>

using namespace desktopgrass;
using namespace desktopgrass::test;

namespace {
constexpr double kTwoPi = 6.28318530717958647692;

Sim MakeWinterTestSim() {
    return sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);
}

void TickUntilFirstSnowflake(Sim& sim) {
    for (int i = 0; i < 10000 && sim.entities.empty(); ++i) {
        sim_tick(sim, 0.01, nullptr, 0);
    }
    REQUIRE_FALSE(sim.entities.empty());
}
}

TEST_CASE("Winter constants are pinned", "[winter][constants]") {
    REQUIRE(SNOWFLAKE_EMIT_RATE_PER_1920DIP == Approx(8.0));
    REQUIRE(SNOWFLAKE_FALL_SPEED_MIN == Approx(20.0));
    REQUIRE(SNOWFLAKE_FALL_SPEED_MAX == Approx(40.0));
    REQUIRE(SNOWFLAKE_SIZE_MIN == Approx(1.5));
    REQUIRE(SNOWFLAKE_SWAY_AMPLITUDE == Approx(10.0));
    REQUIRE(SNOWFLAKE_PRNG_SALT == 0xC0FFEE1CECAFEBABull);
    REQUIRE(SNOW_TIP_RADIUS_FACTOR == Approx(1.25));
    REQUIRE(SNOW_TIP_COLOR == 0xFFFFFFFFu);
}

TEST_CASE("SetScene Winter initializes snowflake scheduler", "[winter][scene]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    REQUIRE(sim.nextSnowflakeSpawnTime > sim.globalTime);
    REQUIRE(sim.nextSnowflakeSpawnTime < 100.0);
}

TEST_CASE("First winter snowflake emits on scheduled tick", "[winter][entities]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    TickUntilFirstSnowflake(sim);

    REQUIRE(sim.entities.size() == 1);
    REQUIRE(sim.entities[0].kind == EntityKind::Snowflake);
}

TEST_CASE("First winter snowflake matches spec-derived PRNG snapshot", "[winter][entities][snapshot]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    TickUntilFirstSnowflake(sim);
    REQUIRE(sim.entities.size() == 1);

    Prng expected{};
    prng_init(expected, CANONICAL_TEST_SEED ^ SNOWFLAKE_PRNG_SALT);
    const double lambda = SNOWFLAKE_EMIT_RATE_PER_1920DIP * sim.monitorWidth / 1920.0;
    const double firstInterval = prng_exponential(expected, lambda);
    const double expectedSize = prng_uniform(expected, SNOWFLAKE_SIZE_MIN, SNOWFLAKE_SIZE_MAX);
    const double expectedX = prng_uniform(expected, -20.0, sim.monitorWidth + 20.0);
    const double expectedFallSpeed = prng_uniform(expected, SNOWFLAKE_FALL_SPEED_MIN, SNOWFLAKE_FALL_SPEED_MAX);
    const double expectedRotation = prng_uniform(expected, 0.0, kTwoPi);
    const double expectedRotationSpeed = prng_uniform(expected, -1.5, 1.5);
    const uint32_t expectedSeed = prng_next_u32(expected);
    const double nextInterval = prng_exponential(expected, lambda);

    const Entity& e = sim.entities[0];
    REQUIRE(e.size == Approx(expectedSize).margin(1e-12));
    REQUIRE(e.x == Approx(expectedX).margin(1e-12));
    REQUIRE(e.vy == Approx(expectedFallSpeed).margin(1e-12));
    REQUIRE(e.rotation == Approx(expectedRotation).margin(1e-12));
    REQUIRE(e.rotationSpeed == Approx(expectedRotationSpeed).margin(1e-12));
    REQUIRE(e.seed == expectedSeed);
    REQUIRE(sim.nextSnowflakeSpawnTime == Approx(firstInterval + nextInterval).margin(1e-12));
}

TEST_CASE("Snowflake sway velocity wobbles from seed phase", "[winter][entities]") {
    Sim sim = MakeWinterTestSim();
    sim.currentScene = Scene::Desert;
    Entity e{};
    e.kind = EntityKind::Snowflake;
    e.seed = 0;
    e.age = 0.0;
    e.lifetime = 100.0;
    sim.entities.push_back(e);

    sim_tick_entities(sim, 0.0);

    const double expectedVx = SNOWFLAKE_SWAY_AMPLITUDE * SNOWFLAKE_SWAY_FREQUENCY * kTwoPi * std::cos(0.0);
    REQUIRE(sim.entities.size() == 1);
    REQUIRE(sim.entities[0].vx == Approx(expectedVx).margin(1e-12));
}

TEST_CASE("Snowflakes are culled after lifetime", "[winter][entities]") {
    Sim sim = MakeWinterTestSim();
    sim.currentScene = Scene::Desert;
    Entity e{};
    e.kind = EntityKind::Snowflake;
    e.lifetime = 1.0;
    e.age = 0.9;
    sim.entities.push_back(e);

    sim_tick_entities(sim, 0.2);

    REQUIRE(sim.entities.empty());
}

TEST_CASE("Snowflakes are culled below ground line", "[winter][entities]") {
    Sim sim = MakeWinterTestSim();
    sim.currentScene = Scene::Desert;
    Entity e{};
    e.kind = EntityKind::Snowflake;
    e.y = sim.windowHeight + 5.0;
    e.lifetime = 100.0;
    sim.entities.push_back(e);

    sim_tick_entities(sim, 0.0);

    REQUIRE(sim.entities.empty());
}

TEST_CASE("Winter snowflake emitter honors max entity cap", "[winter][entities]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);
    sim.nextSnowflakeSpawnTime = sim.globalTime;
    for (int i = 0; i < MAX_ENTITIES_PER_MONITOR; ++i) {
        Entity e{};
        e.kind = EntityKind::Snowflake;
        e.lifetime = 100.0;
        sim.entities.push_back(e);
    }

    sim_tick_entities(sim, 0.0);

    REQUIRE(sim.entities.size() <= static_cast<std::size_t>(MAX_ENTITIES_PER_MONITOR));
    REQUIRE(sim.entities.size() == static_cast<std::size_t>(MAX_ENTITIES_PER_MONITOR));
}

TEST_CASE("Winter scene does not perturb first-blade snapshot", "[winter][snapshot]") {
    Sim sim = MakeWinterTestSim();
    REQUIRE(sim.blades[0].baseX == Approx(CANONICAL_FIRST_10[0].baseX).margin(1e-12));

    sim_set_scene(sim, Scene::Winter);
    REQUIRE(sim.blades[0].baseX == Approx(CANONICAL_FIRST_10[0].baseX).margin(1e-12));

    sim_set_scene(sim, Scene::Grass);
    REQUIRE(sim.blades[0].baseX == Approx(CANONICAL_FIRST_10[0].baseX).margin(1e-12));
}

TEST_CASE("Snowflakes do not emit in non-winter scenes", "[winter][entities][scene]") {
    Sim sim = MakeWinterTestSim();

    sim_set_scene(sim, Scene::Grass);
    sim.nextSnowflakeSpawnTime = 0.0;
    sim_tick(sim, 2.0, nullptr, 0);
    REQUIRE(std::none_of(sim.entities.begin(), sim.entities.end(),
        [](const Entity& e) { return e.kind == EntityKind::Snowflake; }));

    sim_set_scene(sim, Scene::Desert);
    sim.entities.clear();
    sim.nextSnowflakeSpawnTime = 0.0;
    sim_tick(sim, 2.0, nullptr, 0);
    REQUIRE(sim.entities.empty());
}
