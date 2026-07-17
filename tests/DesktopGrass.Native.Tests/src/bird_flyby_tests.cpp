// bird_flyby_tests.cpp - §17.8 ambient bird flyby tests.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>
#include <cmath>
#include <vector>

using namespace desktopgrass;

namespace {
constexpr double Monitor1920 = 1920.0;
constexpr double TwoPi = 6.28318530717958647692;

Sim build_sim(uint64_t seed = CANONICAL_TEST_SEED) {
    Sim sim = sim_init(seed, Monitor1920, DEFAULT_DENSITY);
    sim.currentScene = Scene::Grass;
    sim.entities.clear();
    return sim;
}

int count_birds(const Sim& sim) {
    return static_cast<int>(std::count_if(sim.entities.begin(), sim.entities.end(),
        [](const Entity& e) { return e.kind == EntityKind::Bird; }));
}

std::vector<Entity> birds(const Sim& sim) {
    std::vector<Entity> out;
    for (const Entity& e : sim.entities) {
        if (e.kind == EntityKind::Bird) out.push_back(e);
    }
    return out;
}

int prng_count(Prng& side, int minCount, int maxCount) {
    const double draw = prng_uniform(side, static_cast<double>(minCount), static_cast<double>(maxCount + 1));
    int count = static_cast<int>(std::floor(draw));
    if (count < minCount) count = minCount;
    if (count > maxCount) count = maxCount;
    return count;
}

void reset_bird_stream_fresh(Sim& sim, uint64_t seed) {
    prng_init(sim.birdFlybyPrng, seed ^ BIRD_FLYBY_PRNG_SALT);
    sim.nextBirdFlybyAtTime = sim.globalTime;
}

void reset_bird_schedule(Sim& sim, uint64_t seed) {
    prng_init(sim.birdFlybyPrng, seed ^ BIRD_FLYBY_PRNG_SALT);
    sim.nextBirdFlybyAtTime = sim.globalTime + bird_flyby_sample_interval(sim.birdFlybyPrng);
}

uint64_t find_seed_for_flock_size(int size) {
    for (uint64_t i = 1; i < 10000; ++i) {
        Sim sim = build_sim(CANONICAL_TEST_SEED + i);
        reset_bird_stream_fresh(sim, CANONICAL_TEST_SEED + i);
        sim_spawn_bird_flyby(sim);
        if (count_birds(sim) == size) return CANONICAL_TEST_SEED + i;
    }
    return CANONICAL_TEST_SEED;
}

uint64_t find_v_seed(int minSize) {
    for (uint64_t i = 1; i < 10000; ++i) {
        Sim sim = build_sim(CANONICAL_TEST_SEED + i);
        reset_bird_stream_fresh(sim, CANONICAL_TEST_SEED + i);
        sim_spawn_bird_flyby(sim);
        auto flock = birds(sim);
        if (static_cast<int>(flock.size()) >= minSize && flock[0].colorVariant == 0) {
            return CANONICAL_TEST_SEED + i;
        }
    }
    return CANONICAL_TEST_SEED;
}
} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(BirdFlybyTests)
{
public:
TEST_METHOD(BirdFlybyConstantsArePinnedToSpecValues) {
    Assert::IsTrue(BIRD_FLYBY_SPAWN_RATE_PER_HOUR == Near(15.0));
    Assert::IsTrue(BIRD_FLOCK_SIZE_MIN == 3);
    Assert::IsTrue(BIRD_FLOCK_SIZE_MAX == 7);
    Assert::IsTrue(BIRD_FLOCK_FORMATION_SPACING == Near(9.0));
    Assert::IsTrue(BIRD_FLOCK_V_ANGLE_DEG == Near(22.0));
    Assert::IsTrue(BIRD_SPEED_MIN == Near(65.0));
    Assert::IsTrue(BIRD_SPEED_MAX == Near(95.0));
    Assert::IsTrue(BIRD_ALTITUDE_MIN == Near(78.0));
    Assert::IsTrue(BIRD_ALTITUDE_MAX == Near(96.0));
    Assert::IsTrue(BIRD_BODY_LENGTH == Near(3.6));
    Assert::IsTrue(BIRD_WING_SPAN == Near(5.0));
    Assert::IsTrue(BIRD_WING_FLAP_FREQ == Near(7.0));
    Assert::IsTrue(BIRD_WING_FLAP_PHASE_JITTER == Near(0.6));
    Assert::IsTrue(BIRD_BODY_COLOR == 0xFF1A1610u);
    Assert::IsTrue(BIRD_WING_OPEN_RATIO == Near(1.0));
    Assert::IsTrue(BIRD_WING_FOLD_RATIO == Near(0.30));
    Assert::IsTrue(BIRD_FADE_IN_FRAC == Near(0.08));
    Assert::IsTrue(BIRD_FADE_OUT_FRAC == Near(0.08));
    Assert::IsTrue(BIRD_DRIFT_AMP_Y == Near(3.0));
    Assert::IsTrue(BIRD_DRIFT_FREQ_Y == Near(0.8));
    Assert::IsTrue(BIRD_FLYBY_PRNG_SALT == 0xB12D1F1A1B12D1Aull);
}

TEST_METHOD(BirdFlybyPRNGSaltIsUnique) {
    const uint64_t salts[] = {
        REGROW_PRNG_SALT, FLOWER_PRNG_SALT, MUSHROOM_PRNG_SALT,
        AMBIENT_GUST_PRNG_SALT, CACTUS_PRNG_SALT, TUMBLEWEED_PRNG_SALT,
        CRITTER_PRNG_SALT, BUTTERFLY_PRNG_SALT, FIREFLY_PRNG_SALT,
        SNOWFLAKE_PRNG_SALT, PINE_PRNG_SALT,
    };
    for (uint64_t salt : salts) {
        Assert::IsTrue(BIRD_FLYBY_PRNG_SALT != salt);
    }
}

TEST_METHOD(BirdFlybyFlockSizeStaysInRangeOverSeeds) {
    for (uint64_t i = 0; i < 256; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15ull;
        Sim sim = build_sim(seed);
        reset_bird_stream_fresh(sim, seed);
        sim_spawn_bird_flyby(sim);
        Assert::IsTrue(count_birds(sim) >= BIRD_FLOCK_SIZE_MIN);
        Assert::IsTrue(count_birds(sim) <= BIRD_FLOCK_SIZE_MAX);
    }
}

TEST_METHOD(BirdFlybyLeaderAltitudeStaysInRange) {
    for (uint64_t i = 0; i < 128; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i;
        Sim sim = build_sim(seed);
        reset_bird_stream_fresh(sim, seed);
        sim_spawn_bird_flyby(sim);
        auto flock = birds(sim);
        Assert::IsTrue(!flock.empty());
        Assert::IsTrue(flock[0].altitudeAnchor >= BIRD_ALTITUDE_MIN);
        Assert::IsTrue(flock[0].altitudeAnchor <  BIRD_ALTITUDE_MAX);
    }
}

TEST_METHOD(BirdFlybyLeaderSpeedStaysInRange) {
    for (uint64_t i = 0; i < 128; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i;
        Sim sim = build_sim(seed);
        reset_bird_stream_fresh(sim, seed);
        sim_spawn_bird_flyby(sim);
        auto flock = birds(sim);
        Assert::IsTrue(!flock.empty());
        Assert::IsTrue(flock[0].baseSpeed >= BIRD_SPEED_MIN);
        Assert::IsTrue(flock[0].baseSpeed <  BIRD_SPEED_MAX);
        Assert::IsTrue(std::abs(flock[0].vx) == Near(flock[0].baseSpeed));
    }
}

TEST_METHOD(BirdFlybyPRNGDrawOrderMatchesSideStream) {
    const uint64_t seed = 0xB17D5EED1234ull;
    Sim sim = build_sim(seed);
    reset_bird_stream_fresh(sim, seed);
    Prng side;
    prng_init(side, seed ^ BIRD_FLYBY_PRNG_SALT);

    sim_spawn_bird_flyby(sim);
    const int expectedCount = prng_count(side, BIRD_FLOCK_SIZE_MIN, BIRD_FLOCK_SIZE_MAX);
    const uint64_t directionBit = prng_next_u64(side) & 1ull;
    const double direction = directionBit != 0ull ? 1.0 : -1.0;
    const double leaderAltitude = prng_uniform(side, BIRD_ALTITUDE_MIN, BIRD_ALTITUDE_MAX);
    const double leaderSpeed = prng_uniform(side, BIRD_SPEED_MIN, BIRD_SPEED_MAX);
    const uint64_t formationStyle = prng_next_u64(side) & 1ull;

    std::vector<double> wingPhases;
    std::vector<double> driftPhases;
    for (int i = 0; i < expectedCount; ++i) {
        wingPhases.push_back(prng_uniform(side, -BIRD_WING_FLAP_PHASE_JITTER, BIRD_WING_FLAP_PHASE_JITTER));
        driftPhases.push_back(prng_uniform(side, 0.0, TwoPi));
    }

    auto flock = birds(sim);
    Assert::IsTrue(static_cast<int>(flock.size()) == expectedCount);
    Assert::IsTrue(sim.birdFlybyPrng.state == side.state);

    const double spawnX = direction > 0.0 ? -50.0 : Monitor1920 + 50.0;
    const double sinAngle = std::sin(BIRD_FLOCK_V_ANGLE_DEG * 3.14159265358979323846 / 180.0);
    for (int i = 0; i < expectedCount; ++i) {
        const double along = -static_cast<double>(i) * BIRD_FLOCK_FORMATION_SPACING;
        double perpendicular = 0.0;
        if (formationStyle == 0ull) {
            const int armIndex = (i + 1) / 2;
            const double sideSign = (i % 2) == 0 ? 1.0 : -1.0;
            perpendicular = sideSign * static_cast<double>(armIndex) * BIRD_FLOCK_FORMATION_SPACING * sinAngle;
        } else {
            perpendicular = static_cast<double>(i) * BIRD_FLOCK_FORMATION_SPACING * sinAngle;
        }

        const Entity& e = flock[static_cast<std::size_t>(i)];
        Assert::IsTrue(e.x0 == Near(spawnX + direction * along));
        Assert::IsTrue(e.x == Near(e.x0));
        Assert::IsTrue(e.vx == Near(direction * leaderSpeed));
        Assert::IsTrue(e.baseSpeed == Near(leaderSpeed));
        Assert::IsTrue(e.altitudeAnchor == Near(leaderAltitude - perpendicular));
        Assert::IsTrue(e.phaseX == Near(wingPhases[static_cast<std::size_t>(i)]));
        Assert::IsTrue(e.phaseY == Near(driftPhases[static_cast<std::size_t>(i)]));
        Assert::IsTrue(e.formationOffsetAlongFlight == Near(along));
        Assert::IsTrue(e.formationOffsetPerpendicular == Near(perpendicular));
        Assert::IsTrue(e.colorVariant == static_cast<uint8_t>(formationStyle));
        Assert::IsTrue(e.spawnTime == Near(sim.globalTime));
    }
}

TEST_METHOD(BirdFlybysAreGrassSceneOnly) {
    for (Scene scene : { Scene::Desert, Scene::Winter }) {
        Sim sim = build_sim();
        sim_set_scene(sim, scene);
        sim.entities.clear();
        reset_bird_schedule(sim, CANONICAL_TEST_SEED);
        for (int i = 0; i < 8 * 3600; ++i) {
            sim.globalTime += 1.0;
            sim_tick_bird_flybys(sim);
        }
        Assert::IsTrue(count_birds(sim) == 0);
    }
}

TEST_METHOD(BirdFlybyPoissonSpawnsWhenScheduleElapses) {
    Sim sim = build_sim(0xDAD1B17Dull);
    reset_bird_schedule(sim, 0xDAD1B17Dull);
    int flybys = 0;
    for (int i = 0; i < 10 * 3600; ++i) {
        sim.globalTime += 1.0;
        const int before = count_birds(sim);
        sim_tick_bird_flybys(sim);
        if (count_birds(sim) > before) {
            ++flybys;
            sim.entities.clear();
        }
    }

    const double observedPerHour = static_cast<double>(flybys) / 10.0;
    Assert::IsTrue(observedPerHour == Near(BIRD_FLYBY_SPAWN_RATE_PER_HOUR).epsilon(0.15));
}

TEST_METHOD(BirdVFormationGeometryIsLocked) {
    const uint64_t seed = find_v_seed(5);
    Sim sim = build_sim(seed);
    reset_bird_stream_fresh(sim, seed);
    sim_spawn_bird_flyby(sim);
    auto flock = birds(sim);
    Assert::IsTrue(flock.size() >= 5);
    Assert::IsTrue(flock[0].colorVariant == 0);
    Assert::IsTrue(flock[0].formationOffsetAlongFlight == Near(0.0));

    for (std::size_t i = 1; i < flock.size(); ++i) {
        Assert::IsTrue(std::fabs(flock[0].formationOffsetAlongFlight)
            < std::fabs(flock[i].formationOffsetAlongFlight));
        Assert::IsTrue(flock[i - 1].formationOffsetAlongFlight - flock[i].formationOffsetAlongFlight
            == Near(BIRD_FLOCK_FORMATION_SPACING));
        const double expectedSign = (i % 2 == 0) ? 1.0 : -1.0;
        Assert::IsTrue((flock[i].formationOffsetPerpendicular > 0.0 ? 1.0 : -1.0) == expectedSign);
    }
}

TEST_METHOD(BirdWingFlapScaleStaysInRange) {
    for (int i = 0; i < 200; ++i) {
        const double t = i * 0.137;
        const double phase = -BIRD_WING_FLAP_PHASE_JITTER
            + (2.0 * BIRD_WING_FLAP_PHASE_JITTER) * (static_cast<double>(i) / 199.0);
        const double scale = bird_wing_scale(t, phase);
        Assert::IsTrue(scale >= BIRD_WING_FOLD_RATIO);
        Assert::IsTrue(scale <= BIRD_WING_OPEN_RATIO);
    }
}

TEST_METHOD(BirdWingPhasesDecorrelateWithinAFlock) {
    for (uint64_t i = 1; i < 10000; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i;
        Sim sim = build_sim(seed);
        reset_bird_stream_fresh(sim, seed);
        sim_spawn_bird_flyby(sim);
        auto flock = birds(sim);
        if (flock.size() != 5) continue;

        std::vector<double> distinct;
        for (const Entity& e : flock) {
            const double scale = bird_wing_scale(1.234, e.phaseX);
            bool seen = false;
            for (double existing : distinct) {
                if (std::fabs(existing - scale) < 1e-6) { seen = true; break; }
            }
            if (!seen) distinct.push_back(scale);
        }
        if (distinct.size() >= 3) {
            Assert::IsTrue(distinct.size() >= 3);
            return;
        }
    }
    Assert::Fail(L"No decorrelated 5-bird flock found", LINE_INFO());
}

TEST_METHOD(BirdsDespawnPastOppositeBoundary) {
    Sim sim = build_sim();
    sim.currentScene = Scene::Desert;
    sim.entities.clear();
    Entity bird{};
    bird.kind = EntityKind::Bird;
    bird.x = Monitor1920 + 49.0;
    bird.y = 10.0;
    bird.vx = 20.0;
    bird.altitudeAnchor = BIRD_ALTITUDE_MIN;
    bird.lifetime = -1.0;
    sim.entities.push_back(bird);

    sim_tick_entities(sim, 0.2);

    Assert::IsTrue(count_birds(sim) == 0);
}

TEST_METHOD(BirdsDoNotInteractWithCutsOrCritters) {
    Sim sim = build_sim();
    sim.entities.clear();
    Entity bird{};
    bird.kind = EntityKind::Bird;
    bird.x = 500.0;
    bird.y = sim.windowHeight - STRIP_HEIGHT - 10.0;
    bird.vx = BIRD_SPEED_MIN;
    bird.baseSpeed = BIRD_SPEED_MIN;
    bird.altitudeAnchor = BIRD_ALTITUDE_MIN;
    bird.lifetime = -1.0;
    sim.entities.push_back(bird);

    Entity sheep{};
    sheep.kind = EntityKind::Sheep;
    sheep.x = bird.x;
    sheep.y = sim.windowHeight - SHEEP_BODY_HEIGHT - SHEEP_LEG_LENGTH;
    sheep.vx = SHEEP_WALK_SPEED_MIN;
    sheep.state = SHEEP_STATE_WALKING;
    sheep.stateTimer = 10.0;
    sim.entities.push_back(sheep);

    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x = bird.x;
    ev.y = bird.y;
    sim_apply_click(sim, ev);

    Assert::IsTrue(sim.entities[0].kind == EntityKind::Bird);
    Assert::IsTrue(sim.entities[0].baseSpeed == Near(BIRD_SPEED_MIN));
    Assert::IsTrue(sim.entities[1].state == SHEEP_STATE_WALKING);
    for (const Blade& b : sim.blades) Assert::IsTrue(b.cutAnimStart < 0.0);
}

TEST_METHOD(BirdFlybyPoissonInterArrivalsKeepExpectedMean) {
    const uint64_t seed = 0x510B17D00ull;
    Sim sim = build_sim(seed);
    reset_bird_schedule(sim, seed);

    double prev = sim.globalTime;
    double totalInterval = 0.0;
    constexpr int Events = 100;
    for (int i = 0; i < Events; ++i) {
        sim.globalTime = sim.nextBirdFlybyAtTime;
        totalInterval += sim.globalTime - prev;
        prev = sim.globalTime;
        sim_tick_bird_flybys(sim);
        sim.entities.clear();
    }

    const double expectedMean = 3600.0 / BIRD_FLYBY_SPAWN_RATE_PER_HOUR;
    Assert::IsTrue((totalInterval / Events) == Near(expectedMean).epsilon(0.20));
}
};
}
