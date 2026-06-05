#include "catch.hpp"

#include "Sim.h"
#include "Constants.h"
#include "snapshot_data.h"

#include <cstddef>
#include <cmath>
#include <algorithm>
#include <array>
#include <limits>

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

namespace {
int count_snow_puffs(const Sim& sim) {
    int n = 0;
    for (const Entity& e : sim.entities)
        if (e.kind == EntityKind::SnowPuff) ++n;
    return n;
}

InputEvent WinterClick(const Sim& sim, double x) {
    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x    = x;
    ev.y    = sim.windowHeight - 5.0;
    ev.time = sim.globalTime;
    return ev;
}
}

TEST_CASE("Snow puff constants are pinned", "[winter][puff][constants]") {
    REQUIRE(SNOW_PUFF_COUNT_MIN == 9);
    REQUIRE(SNOW_PUFF_COUNT_MAX == 16);
    REQUIRE(SNOW_PUFF_SIZE_MIN == Approx(2.0));
    REQUIRE(SNOW_PUFF_SIZE_MAX == Approx(4.5));
    REQUIRE(SNOW_PUFF_GRAVITY == Approx(150.0));
    REQUIRE(SNOW_PUFF_DRAG == Approx(1.6));
    REQUIRE(SNOW_PUFF_SPREAD_RAD == Approx(1.25));
    REQUIRE(SNOW_PUFF_PRNG_SALT == 0x5503FF1E5503FF1Eull);
    REQUIRE(WINTER_DRIFT_HEIGHT_SCALE == Approx(0.42));
    REQUIRE(WINTER_DRIFT_BASE_COLOR == 0xFFE8EEF6u);
}

TEST_CASE("Clicking the winter snowbank sheds a snow puff burst", "[winter][puff]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    InputEvent ev = WinterClick(sim, 400.0);
    sim_apply_click(sim, ev);

    const int puffs = count_snow_puffs(sim);
    REQUIRE(puffs >= SNOW_PUFF_COUNT_MIN);
    REQUIRE(puffs <= SNOW_PUFF_COUNT_MAX);

    // Every puff launches upward (y is screen-down, so up is negative vy) and
    // spawns at or above the ground line.
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::SnowPuff) continue;
        REQUIRE(e.vy < 0.0);
        REQUIRE(e.y <= sim.windowHeight + 1e-9);
    }
}

TEST_CASE("Snow puff only fires in Winter", "[winter][puff][scene]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Grass);

    InputEvent ev = WinterClick(sim, 400.0);
    sim_apply_click(sim, ev);

    REQUIRE(count_snow_puffs(sim) == 0);
}

TEST_CASE("A non-finite click sheds no snow puff", "[winter][puff][guard]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x    = std::numeric_limits<double>::quiet_NaN();
    ev.y    = sim.windowHeight - 5.0;
    ev.time = sim.globalTime;
    sim_apply_click(sim, ev);

    REQUIRE(count_snow_puffs(sim) == 0);
}

TEST_CASE("Snow puff burst rises then settles and is culled", "[winter][puff]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    InputEvent ev = WinterClick(sim, 400.0);
    sim_apply_click(sim, ev);
    REQUIRE(count_snow_puffs(sim) > 0);

    // 4 s easily exceeds SNOW_PUFF_LIFETIME_MAX (0.9 s); every puff should be
    // culled (lifetime expiry and/or falling back below the ground line).
    for (int i = 0; i < 200; ++i) sim_tick_entities(sim, 0.02);
    REQUIRE(count_snow_puffs(sim) == 0);
}

TEST_CASE("Snow puff draw order matches a side PRNG stream", "[winter][puff][prng]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    InputEvent ev = WinterClick(sim, 300.0);
    sim_apply_click(sim, ev);

    Prng side{};
    prng_init(side, CANONICAL_TEST_SEED ^ SNOW_PUFF_PRNG_SALT);
    const int expectedCount = SNOW_PUFF_COUNT_MIN
        + static_cast<int>(prng_index(side, SNOW_PUFF_COUNT_MAX - SNOW_PUFF_COUNT_MIN + 1));
    REQUIRE(count_snow_puffs(sim) == expectedCount);

    // The first locked draw inside make_snow_puff is `size`.
    const double expectedSize = prng_uniform(side, SNOW_PUFF_SIZE_MIN, SNOW_PUFF_SIZE_MAX);
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::SnowPuff) continue;
        REQUIRE(e.size == Approx(expectedSize).margin(1e-12));
        break;
    }
}

TEST_CASE("Snow puff salt is unique among winter PRNG salts", "[winter][puff][prng]") {
    const std::array<uint64_t, 14> otherSalts = {
        REGROW_PRNG_SALT, FLOWER_PRNG_SALT, MUSHROOM_PRNG_SALT,
        AMBIENT_GUST_PRNG_SALT, CACTUS_PRNG_SALT, TUMBLEWEED_PRNG_SALT,
        CRITTER_PRNG_SALT, BUTTERFLY_PRNG_SALT, FIREFLY_PRNG_SALT,
        BIRD_FLYBY_PRNG_SALT, SNOWFLAKE_PRNG_SALT,
        PINE_PRNG_SALT, LEAF_PUFF_PRNG_SALT,
    };
    for (uint64_t s : otherSalts) {
        REQUIRE(SNOW_PUFF_PRNG_SALT != s);
    }
}

// ---------------------------------------------------------------------------
// §21.2 snow carve (footprints)
// ---------------------------------------------------------------------------

TEST_CASE("Snow carve constants are pinned", "[winter][carve][constants]") {
    REQUIRE(SNOW_CARVE_BUCKETS == 192);
    REQUIRE(SNOW_CARVE_RADIUS_DIP == Approx(24.0));
    REQUIRE(SNOW_CARVE_DEPTH_PER_CLICK == Approx(7.0));
    REQUIRE(SNOW_CARVE_MAX_DEPTH == Approx(11.0));
    REQUIRE(SNOW_CARVE_REFILL_RATE == Approx(1.8));
}

TEST_CASE("Winter sim initializes the carve heightfield", "[winter][carve]") {
    Sim sim = MakeWinterTestSim();
    REQUIRE(sim.snowCarve.size() == static_cast<size_t>(SNOW_CARVE_BUCKETS));
    sim_set_scene(sim, Scene::Winter);
    REQUIRE(sim.snowCarve.size() == static_cast<size_t>(SNOW_CARVE_BUCKETS));
    for (double c : sim.snowCarve) REQUIRE(c == Approx(0.0));
}

TEST_CASE("Clicking the winter snowbank presses a dent", "[winter][carve]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    // 405 sits exactly on a bucket center (bucketWidth = 1920/192 = 10), so the
    // dent depth there is the full per-click amount at zero falloff distance.
    InputEvent ev = WinterClick(sim, 405.0);
    sim_apply_click(sim, ev);

    REQUIRE(snow_carve_depth_at(sim, 405.0) == Approx(SNOW_CARVE_DEPTH_PER_CLICK));
}

TEST_CASE("Snow carve falls off with distance and stops past the radius", "[winter][carve]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    sim_apply_click(sim, WinterClick(sim, 405.0));

    const double center = snow_carve_depth_at(sim, 405.0);
    const double edge   = snow_carve_depth_at(sim, 425.0); // 20 DIP away (< radius)
    const double beyond = snow_carve_depth_at(sim, 435.0); // 30 DIP away (> radius)

    REQUIRE(center > edge);
    REQUIRE(edge > 0.0);
    REQUIRE(beyond == Approx(0.0));
}

TEST_CASE("Repeated clicks clamp the carve at the max depth", "[winter][carve]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    for (int i = 0; i < 10; ++i) sim_apply_click(sim, WinterClick(sim, 405.0));

    REQUIRE(snow_carve_depth_at(sim, 405.0) == Approx(SNOW_CARVE_MAX_DEPTH));
}

TEST_CASE("Clicks outside Winter never carve", "[winter][carve][scene]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Grass);

    sim_apply_click(sim, WinterClick(sim, 405.0));
    sim_apply_snow_carve(sim, 405.0); // direct call is also guarded

    REQUIRE(snow_carve_depth_at(sim, 405.0) == Approx(0.0));
}

TEST_CASE("Scene changes clear snow footprints", "[winter][carve][scene]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);
    sim_apply_click(sim, WinterClick(sim, 405.0));
    REQUIRE(snow_carve_depth_at(sim, 405.0) > 0.0);

    sim_set_scene(sim, Scene::Grass);
    REQUIRE(snow_carve_depth_at(sim, 405.0) == Approx(0.0));

    sim_set_scene(sim, Scene::Winter);
    REQUIRE(snow_carve_depth_at(sim, 405.0) == Approx(0.0));
}

TEST_CASE("Snow carve settles back over time and never goes negative", "[winter][carve]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);
    sim_apply_click(sim, WinterClick(sim, 405.0));

    sim_decay_snow_carve(sim, 1.0);
    REQUIRE(snow_carve_depth_at(sim, 405.0)
            == Approx(SNOW_CARVE_DEPTH_PER_CLICK - SNOW_CARVE_REFILL_RATE));

    for (int i = 0; i < 100; ++i) sim_decay_snow_carve(sim, 1.0);
    for (double c : sim.snowCarve) REQUIRE(c >= 0.0);
    REQUIRE(snow_carve_depth_at(sim, 405.0) == Approx(0.0));
}

TEST_CASE("A same-frame click lands its full dent before decay", "[winter][carve][ordering]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    // Decay runs before events in sim_tick, so a click delivered in the same
    // frame carves the full depth (the empty field has nothing to settle).
    InputEvent ev = WinterClick(sim, 405.0);
    sim_tick(sim, 0.016, &ev, 1);

    REQUIRE(snow_carve_depth_at(sim, 405.0) == Approx(SNOW_CARVE_DEPTH_PER_CLICK));
}

TEST_CASE("Snow carve query interpolates between buckets", "[winter][carve]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);
    sim_apply_click(sim, WinterClick(sim, 405.0));

    // 400 lies midway between bucket centers 395 and 405; the interpolated value
    // must sit strictly between the two neighbouring bucket depths.
    const double at395 = snow_carve_depth_at(sim, 395.0);
    const double at405 = snow_carve_depth_at(sim, 405.0);
    const double at400 = snow_carve_depth_at(sim, 400.0);
    REQUIRE(at400 > std::min(at395, at405) - 1e-9);
    REQUIRE(at400 < std::max(at395, at405) + 1e-9);
}

TEST_CASE("Snow carve handles edge and non-finite coordinates safely", "[winter][carve][guard]") {
    Sim sim = MakeWinterTestSim();
    sim_set_scene(sim, Scene::Winter);

    // Carving at the strip edges and off-screen must not crash or throw.
    sim_apply_snow_carve(sim, 0.0);
    sim_apply_snow_carve(sim, sim.monitorWidth);
    sim_apply_snow_carve(sim, -50.0);
    sim_apply_snow_carve(sim, sim.monitorWidth + 500.0);
    sim_apply_snow_carve(sim, std::numeric_limits<double>::quiet_NaN());

    // Edge carve near x=0 should register on the first bucket.
    REQUIRE(snow_carve_depth_at(sim, 0.0) > 0.0);

    // Queries off the strip clamp to the nearest edge bucket; NaN returns zero.
    REQUIRE(std::isfinite(snow_carve_depth_at(sim, -100.0)));
    REQUIRE(snow_carve_depth_at(sim, -100.0) >= 0.0);
    REQUIRE(std::isfinite(snow_carve_depth_at(sim, sim.monitorWidth + 1000.0)));
    REQUIRE(snow_carve_depth_at(sim, std::numeric_limits<double>::quiet_NaN()) == Approx(0.0));
}
