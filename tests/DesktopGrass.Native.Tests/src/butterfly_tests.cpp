// butterfly_tests.cpp - §17.6 ambient Butterfly tests.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>
#include <cmath>

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

const Entity* first_butterfly(const Sim& sim) {
    for (const Entity& e : sim.entities) if (e.kind == EntityKind::Butterfly) return &e;
    return nullptr;
}
} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(ButterflyTests)
{
public:
TEST_METHOD(ButterflyConstantsArePinnedToSpecValues) {
    Assert::IsTrue(BUTTERFLY_COUNT_MIN == 2);
    Assert::IsTrue(BUTTERFLY_COUNT_MAX == 4);
    Assert::IsTrue(BUTTERFLY_SPEED_MIN == Near(18.0));
    Assert::IsTrue(BUTTERFLY_SPEED_MAX == Near(32.0));
    Assert::IsTrue(BUTTERFLY_BODY_LENGTH == Near(2.4));
    Assert::IsTrue(BUTTERFLY_WING_RADIUS == Near(3.5));
    Assert::IsTrue(BUTTERFLY_WING_OFFSET == Near(2.2));
    Assert::IsTrue(BUTTERFLY_FLUTTER_FREQ == Near(16.0));
    Assert::IsTrue(BUTTERFLY_FLUTTER_MIN_SCALE == Near(0.20));
    Assert::IsTrue(BUTTERFLY_MEANDER_FREQ_Y == Near(0.8));
    Assert::IsTrue(BUTTERFLY_MEANDER_AMP_Y == Near(16.0));
    Assert::IsTrue(BUTTERFLY_MEANDER_FREQ_X == Near(0.5));
    Assert::IsTrue(BUTTERFLY_MEANDER_AMP_X == Near(0.4));
    Assert::IsTrue(BUTTERFLY_ALTITUDE_MIN == Near(18.0));
    Assert::IsTrue(BUTTERFLY_ALTITUDE_MAX == Near(70.0));
    Assert::IsTrue(BUTTERFLY_BODY_COLOR == 0xFF2A2018u);
    Assert::IsTrue(BUTTERFLY_COLOR_COUNT == 5);
    Assert::IsTrue(BUTTERFLY_PRNG_SALT == 0xB07DEF1E0001ull);
}

TEST_METHOD(GrassGenerationProducesButterflyCountInRange) {
    for (uint64_t i = 0; i < 128; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15ull;
        Sim sim = build_grass_sim(seed);
        Assert::IsTrue(count_kind(sim, EntityKind::Butterfly) >= BUTTERFLY_COUNT_MIN);
        Assert::IsTrue(count_kind(sim, EntityKind::Butterfly) <= BUTTERFLY_COUNT_MAX);
    }
}

TEST_METHOD(ButterfliesAreGrassSceneOnly) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Desert);
    Assert::IsTrue(count_kind(sim, EntityKind::Butterfly) == 0);
    sim_set_scene(sim, Scene::Winter);
    Assert::IsTrue(count_kind(sim, EntityKind::Butterfly) == 0);
}

TEST_METHOD(GeneratedButterfliesHaveSpeedAltitudeAndColorRanges) {
    Sim sim = build_grass_sim();
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Butterfly) continue;
        Assert::IsTrue(e.baseSpeed >= BUTTERFLY_SPEED_MIN);
        Assert::IsTrue(e.baseSpeed <  BUTTERFLY_SPEED_MAX);
        Assert::IsTrue(e.altitudeAnchor >= BUTTERFLY_ALTITUDE_MIN);
        Assert::IsTrue(e.altitudeAnchor <  BUTTERFLY_ALTITUDE_MAX);
        Assert::IsTrue(e.colorVariant < BUTTERFLY_COLOR_COUNT);
    }
}

TEST_METHOD(ButterflyPRNGDrawOrderMatchesSideStream) {
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ BUTTERFLY_PRNG_SALT);
    Sim sim = build_grass_sim();

    const int expectedCount = prng_count(side, BUTTERFLY_COUNT_MIN, BUTTERFLY_COUNT_MAX);
    Assert::IsTrue(count_kind(sim, EntityKind::Butterfly) == expectedCount);

    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Butterfly) continue;
        const double xFrac = prng_uniform(side, 0.0, 1.0);
        const double yFrac = prng_uniform(side, 0.0, 1.0);
        const uint64_t vxSign = prng_next_u64(side) & 1ull;
        const double expectedDir = vxSign != 0ull ? 1.0 : -1.0;
        const double expectedSpeed = prng_uniform(side, BUTTERFLY_SPEED_MIN, BUTTERFLY_SPEED_MAX);
        const uint8_t expectedColor = static_cast<uint8_t>(prng_index(side, static_cast<uint32_t>(BUTTERFLY_COLOR_COUNT)));
        const double expectedPhaseY = prng_uniform(side, 0.0, 2.0 * 3.14159265358979323846);
        const double expectedPhaseX = prng_uniform(side, 0.0, 2.0 * 3.14159265358979323846);
        const double expectedAltitude = BUTTERFLY_ALTITUDE_MIN + yFrac * (BUTTERFLY_ALTITUDE_MAX - BUTTERFLY_ALTITUDE_MIN);
        const double expectedVx = expectedDir * expectedSpeed * (1.0 + BUTTERFLY_MEANDER_AMP_X * std::sin(expectedPhaseX));

        Assert::IsTrue(e.x == Near(xFrac * Monitor1920));
        Assert::IsTrue(e.altitudeAnchor == Near(expectedAltitude));
        Assert::IsTrue(e.baseSpeed == Near(expectedSpeed));
        Assert::IsTrue(e.vx == Near(expectedVx));
        Assert::IsTrue(e.colorVariant == expectedColor);
        Assert::IsTrue(e.phaseY == Near(expectedPhaseY));
        Assert::IsTrue(e.phaseX == Near(expectedPhaseX));
        ++seen;
    }
    Assert::IsTrue(seen == expectedCount);
}

TEST_METHOD(ButterflyEdgeWrapPreservesAltitudeAnchor) {
    Sim sim = build_grass_sim();
    auto it = std::find_if(sim.entities.begin(), sim.entities.end(), [](const Entity& e) { return e.kind == EntityKind::Butterfly; });
    Assert::IsTrue(it != sim.entities.end());
    const double margin = BUTTERFLY_WING_OFFSET + BUTTERFLY_WING_RADIUS;
    it->x = Monitor1920 + margin + 1.0;
    it->vx = std::abs(it->vx);
    const double altitude = it->altitudeAnchor;
    sim.currentScene = Scene::Desert;

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(it->x == Near(-margin));
    Assert::IsTrue(it->altitudeAnchor == Near(altitude));
}

TEST_METHOD(ButterfliesDoNotInteractWithCutsOrPets) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim.entities.clear();
    Entity butterfly{};
    butterfly.kind = EntityKind::Butterfly;
    butterfly.x = 500.0;
    butterfly.y = sim.windowHeight - STRIP_HEIGHT - 5.0;
    butterfly.vx = BUTTERFLY_SPEED_MIN;
    butterfly.baseSpeed = BUTTERFLY_SPEED_MIN;
    butterfly.altitudeAnchor = BUTTERFLY_ALTITUDE_MIN;
    butterfly.lifetime = -1.0;
    sim.entities.push_back(butterfly);
    Entity sheep{};
    sheep.kind = EntityKind::Sheep;
    sheep.x = butterfly.x;
    sheep.y = sim.windowHeight - SHEEP_BODY_HEIGHT - SHEEP_LEG_LENGTH;
    sheep.vx = SHEEP_WALK_SPEED_MIN;
    sheep.state = SHEEP_STATE_WALKING;
    sheep.stateTimer = 10.0;
    sim.entities.push_back(sheep);

    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x = butterfly.x;
    ev.y = butterfly.y;
    sim_apply_click(sim, ev);

    Assert::IsTrue(sim.entities[0].kind == EntityKind::Butterfly);
    Assert::IsTrue(sim.entities[0].baseSpeed == Near(BUTTERFLY_SPEED_MIN));
    Assert::IsTrue(sim.entities[1].state == SHEEP_STATE_WALKING);
    for (const Blade& b : sim.blades) Assert::IsTrue(b.cutAnimStart < 0.0);
}

TEST_METHOD(ButterflyWingScaleStaysWithinFlutterBounds) {
    for (int i = 0; i < 200; ++i) {
        const double t = i * 0.05;
        const double scale = butterfly_wing_scale(t, 1.3);
        Assert::IsTrue(scale >= BUTTERFLY_FLUTTER_MIN_SCALE);
        Assert::IsTrue(scale <= 1.0);
    }
}
};
}
