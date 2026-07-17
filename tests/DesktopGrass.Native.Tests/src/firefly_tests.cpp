// firefly_tests.cpp - §17.7 ambient Firefly tests.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>
#include <cmath>
#include <vector>

using namespace desktopgrass;

namespace {
constexpr double Monitor1920 = 1920.0;

int count_kind(const Sim& sim, EntityKind kind) {
    return static_cast<int>(std::count_if(sim.entities.begin(), sim.entities.end(),
        [kind](const Entity& e) { return e.kind == kind; }));
}

Sim build_grass_sim(uint64_t seed = CANONICAL_TEST_SEED) {
    Sim sim = sim_init(seed, Monitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Grass);
    return sim;
}

int prng_count(Prng& side, int minCount, int maxCount) {
    const double draw = prng_uniform(side, static_cast<double>(minCount), static_cast<double>(maxCount + 1));
    int count = static_cast<int>(std::floor(draw));
    if (count < minCount) count = minCount;
    if (count > maxCount) count = maxCount;
    return count;
}
} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(FireflyTests)
{
public:
TEST_METHOD(FireflyConstantsArePinnedToSpecValues) {
    Assert::IsTrue(FIREFLY_COUNT_MIN == 3);
    Assert::IsTrue(FIREFLY_COUNT_MAX == 6);
    Assert::IsTrue(FIREFLY_DRIFT_SPEED_MIN == Near(4.0));
    Assert::IsTrue(FIREFLY_DRIFT_SPEED_MAX == Near(10.0));
    Assert::IsTrue(FIREFLY_BODY_RADIUS == Near(1.2));
    Assert::IsTrue(FIREFLY_GLOW_RADIUS == Near(5.0));
    Assert::IsTrue(FIREFLY_BLINK_PERIOD_MIN == Near(1.4));
    Assert::IsTrue(FIREFLY_BLINK_PERIOD_MAX == Near(2.6));
    Assert::IsTrue(FIREFLY_BLINK_DUTY == Near(0.55));
    Assert::IsTrue(FIREFLY_BLINK_FADE == Near(0.30));
    Assert::IsTrue(FIREFLY_DRIFT_FREQ_X == Near(0.4));
    Assert::IsTrue(FIREFLY_DRIFT_FREQ_Y == Near(0.6));
    Assert::IsTrue(FIREFLY_DRIFT_AMP_X == Near(0.6));
    Assert::IsTrue(FIREFLY_DRIFT_AMP_Y == Near(8.0));
    Assert::IsTrue(FIREFLY_ALTITUDE_MIN == Near(8.0));
    Assert::IsTrue(FIREFLY_ALTITUDE_MAX == Near(55.0));
    Assert::IsTrue(FIREFLY_BODY_COLOR == 0xFFFFEE88u);
    Assert::IsTrue(FIREFLY_GLOW_COLOR_RGB == 0xEEDD66u);
    Assert::IsTrue(FIREFLY_GLOW_ALPHA_MAX == 110);
    Assert::IsTrue(FIREFLY_BODY_ALPHA_MAX == 255);
    Assert::IsTrue(FIREFLY_PRNG_SALT == 0xF13EF1E7777ull);
}

TEST_METHOD(GrassGenerationProducesFireflyCountInRange) {
    for (uint64_t i = 0; i < 128; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15ull;
        Sim sim = build_grass_sim(seed);
        Assert::IsTrue(count_kind(sim, EntityKind::Firefly) >= FIREFLY_COUNT_MIN);
        Assert::IsTrue(count_kind(sim, EntityKind::Firefly) <= FIREFLY_COUNT_MAX);
    }
}

TEST_METHOD(FirefliesAreGrassSceneOnly) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Desert);
    Assert::IsTrue(count_kind(sim, EntityKind::Firefly) == 0);
    sim_set_scene(sim, Scene::Winter);
    Assert::IsTrue(count_kind(sim, EntityKind::Firefly) == 0);
}

TEST_METHOD(GeneratedFirefliesHaveSpeedAltitudeAndBlinkPeriodRanges) {
    Sim sim = build_grass_sim();
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Firefly) continue;
        Assert::IsTrue(e.baseSpeed >= FIREFLY_DRIFT_SPEED_MIN);
        Assert::IsTrue(e.baseSpeed <  FIREFLY_DRIFT_SPEED_MAX);
        Assert::IsTrue(e.altitudeAnchor >= FIREFLY_ALTITUDE_MIN);
        Assert::IsTrue(e.altitudeAnchor <  FIREFLY_ALTITUDE_MAX);
        Assert::IsTrue(e.blinkPeriod >= FIREFLY_BLINK_PERIOD_MIN);
        Assert::IsTrue(e.blinkPeriod <  FIREFLY_BLINK_PERIOD_MAX);
    }
}

TEST_METHOD(FireflyPRNGDrawOrderMatchesSideStream) {
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ FIREFLY_PRNG_SALT);
    Sim sim = build_grass_sim();

    const int expectedCount = prng_count(side, FIREFLY_COUNT_MIN, FIREFLY_COUNT_MAX);
    Assert::IsTrue(count_kind(sim, EntityKind::Firefly) == expectedCount);

    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Firefly) continue;
        const double xFrac = prng_uniform(side, 0.0, 1.0);
        const double yFrac = prng_uniform(side, 0.0, 1.0);
        const uint64_t vxSign = prng_next_u64(side) & 1ull;
        const double expectedDir = vxSign != 0ull ? 1.0 : -1.0;
        const double expectedSpeed = prng_uniform(side, FIREFLY_DRIFT_SPEED_MIN, FIREFLY_DRIFT_SPEED_MAX);
        const double expectedBlinkPeriod = prng_uniform(side, FIREFLY_BLINK_PERIOD_MIN, FIREFLY_BLINK_PERIOD_MAX);
        const double expectedBlinkPhase = prng_uniform(side, 0.0, 1.0);
        const double expectedPhaseY = prng_uniform(side, 0.0, 2.0 * 3.14159265358979323846);
        const double expectedPhaseX = prng_uniform(side, 0.0, 2.0 * 3.14159265358979323846);
        const double expectedAltitude = FIREFLY_ALTITUDE_MIN + yFrac * (FIREFLY_ALTITUDE_MAX - FIREFLY_ALTITUDE_MIN);
        const double expectedVx = expectedDir * expectedSpeed * (1.0 + FIREFLY_DRIFT_AMP_X * std::sin(expectedPhaseX));

        Assert::IsTrue(e.x == Near(xFrac * Monitor1920));
        Assert::IsTrue(e.altitudeAnchor == Near(expectedAltitude));
        Assert::IsTrue(e.baseSpeed == Near(expectedSpeed));
        Assert::IsTrue(e.blinkPeriod == Near(expectedBlinkPeriod));
        Assert::IsTrue(e.blinkPhase == Near(expectedBlinkPhase));
        Assert::IsTrue(e.vx == Near(expectedVx));
        Assert::IsTrue(e.phaseY == Near(expectedPhaseY));
        Assert::IsTrue(e.phaseX == Near(expectedPhaseX));
        ++seen;
    }
    Assert::IsTrue(seen == expectedCount);
}

TEST_METHOD(FireflyEdgeWrapPreservesAltitudeAnchor) {
    Sim sim = build_grass_sim();
    auto it = std::find_if(sim.entities.begin(), sim.entities.end(), [](const Entity& e) { return e.kind == EntityKind::Firefly; });
    Assert::IsTrue(it != sim.entities.end());
    const double margin = FIREFLY_GLOW_RADIUS;
    it->x = Monitor1920 + margin + 1.0;
    it->vx = std::abs(it->vx);
    const double altitude = it->altitudeAnchor;
    sim.currentScene = Scene::Desert;

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(it->x == Near(-margin));
    Assert::IsTrue(it->altitudeAnchor == Near(altitude));
}

TEST_METHOD(FirefliesDoNotInteractWithCutsOrPets) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim.entities.clear();
    Entity firefly{};
    firefly.kind = EntityKind::Firefly;
    firefly.x = 500.0;
    firefly.y = sim.windowHeight - STRIP_HEIGHT - 5.0;
    firefly.vx = FIREFLY_DRIFT_SPEED_MIN;
    firefly.baseSpeed = FIREFLY_DRIFT_SPEED_MIN;
    firefly.altitudeAnchor = FIREFLY_ALTITUDE_MIN;
    firefly.blinkPeriod = FIREFLY_BLINK_PERIOD_MIN;
    firefly.lifetime = -1.0;
    sim.entities.push_back(firefly);
    Entity sheep{};
    sheep.kind = EntityKind::Sheep;
    sheep.x = firefly.x;
    sheep.y = sim.windowHeight - SHEEP_BODY_HEIGHT - SHEEP_LEG_LENGTH;
    sheep.vx = SHEEP_WALK_SPEED_MIN;
    sheep.state = SHEEP_STATE_WALKING;
    sheep.stateTimer = 10.0;
    sim.entities.push_back(sheep);

    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x = firefly.x;
    ev.y = firefly.y;
    sim_apply_click(sim, ev);

    Assert::IsTrue(sim.entities[0].kind == EntityKind::Firefly);
    Assert::IsTrue(sim.entities[0].baseSpeed == Near(FIREFLY_DRIFT_SPEED_MIN));
    Assert::IsTrue(sim.entities[1].state == SHEEP_STATE_WALKING);
    for (const Blade& b : sim.blades) Assert::IsTrue(b.cutAnimStart < 0.0);
}

TEST_METHOD(FireflyBlinkBrightnessHasOnAndOffPhases) {
    const double period = 2.0;
    Assert::IsTrue(firefly_blink_brightness(period * 0.25, period, 0.0) == Near(1.0));
    Assert::IsTrue(firefly_blink_brightness(period * 0.80, period, 0.0) == Near(0.0));
}

TEST_METHOD(FireflyPhasesDecorrelateVisibleBrightness) {
    const double period = 2.0;
    const double phases[] = { 0.0, 0.0375, 0.075, 0.1125, 0.25, 0.80 };
    std::vector<double> distinct;
    for (double phase : phases) {
        const double b = firefly_blink_brightness(0.0, period, phase);
        bool seen = false;
        for (double existing : distinct) {
            if (std::fabs(existing - b) < 1e-6) { seen = true; break; }
        }
        if (!seen) distinct.push_back(b);
    }
    Assert::IsTrue(distinct.size() >= 4);
}
};
}
