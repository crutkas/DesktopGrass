// hedgehog_tests.cpp
//
// §17.9 Hedgehog critter tests. Mirrors Win2D HedgehogTests.cs.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>
#include <cmath>
#include <cwchar>

using namespace desktopgrass;

namespace {

constexpr double Monitor1920 = 1920.0;

int count_kind(const Sim& sim, EntityKind kind) {
    return static_cast<int>(std::count_if(sim.entities.begin(), sim.entities.end(),
        [kind](const Entity& e) { return e.kind == kind; }));
}

Sim build_sim(uint64_t seed = CANONICAL_TEST_SEED) {
    return sim_init(seed, Monitor1920, DEFAULT_DENSITY);
}

Sim build_grass_sim(uint64_t seed = CANONICAL_TEST_SEED) {
    Sim sim = build_sim(seed);
    sim_set_scene(sim, Scene::Grass);
    sim_set_critter(sim, CritterKind::Bunny);
    return sim;
}

Entity hedgehog_entity(double x = 500.0, double vx = HEDGEHOG_WALK_SPEED_MIN) {
    Entity e{};
    e.kind = EntityKind::Hedgehog;
    e.size = HEDGEHOG_BODY_RADIUS;
    e.x = x;
    e.y = STRIP_HEIGHT + HEADROOM - HEDGEHOG_BODY_HEIGHT - HEDGEHOG_LEG_LENGTH;
    e.vx = vx;
    e.vy = 0.0;
    e.rotationSpeed = std::abs(vx);
    e.lifetime = -1.0;
    e.state = HEDGEHOG_STATE_WALKING;
    e.stateTimer = HEDGEHOG_WALK_DURATION_MIN;
    e.previousState = HEDGEHOG_STATE_WALKING;
    return e;
}

InputEvent click_event(double x, double y) {
    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x = x;
    ev.y = y;
    ev.time = 0.0;
    return ev;
}

int prng_count(Prng& side, int minCount, int maxCount) {
    const double draw = prng_uniform(side, static_cast<double>(minCount), static_cast<double>(maxCount + 1));
    int count = static_cast<int>(std::floor(draw));
    if (count < minCount) count = minCount;
    if (count > maxCount) count = maxCount;
    return count;
}

void advance_sheep(Prng& side, int count) {
    for (int i = 0; i < count; ++i) {
        const double margin = SHEEP_BODY_RADIUS + 8.0;
        (void)prng_uniform(side, margin, Monitor1920 - margin);
        (void)prng_uniform(side, SHEEP_WALK_SPEED_MIN, SHEEP_WALK_SPEED_MAX);
        (void)prng_uniform(side, 0.0, 1.0);
        (void)prng_next_u32(side);
        (void)prng_uniform(side, SHEEP_WALK_DURATION_MIN, SHEEP_WALK_DURATION_MAX);
        (void)prng_index(side, static_cast<uint32_t>(sizeof(SHEEP_NAME_POOL) / sizeof(SHEEP_NAME_POOL[0])));
    }
}

void advance_cats(Prng& side, int count) {
    for (int i = 0; i < count; ++i) {
        const double margin = CAT_BODY_RADIUS + 8.0;
        (void)prng_uniform(side, margin, Monitor1920 - margin);
        (void)prng_uniform(side, CAT_WALK_SPEED_MIN, CAT_WALK_SPEED_MAX);
        (void)prng_uniform(side, 0.0, 1.0);
        (void)prng_next_u32(side);
        (void)prng_uniform(side, CAT_WALK_DURATION_MIN, CAT_WALK_DURATION_MAX);
        (void)prng_index(side, static_cast<uint32_t>(sizeof(CAT_NAME_POOL) / sizeof(CAT_NAME_POOL[0])));
        (void)prng_index(side, static_cast<uint32_t>(CAT_COAT_VARIANT_COUNT));
    }
}

void advance_bunnies(Prng& side, int count) {
    for (int i = 0; i < count; ++i) {
        (void)prng_uniform(side, 0.0, 1.0);
        (void)prng_next_u64(side);
        (void)prng_uniform(side, BUNNY_HOP_SPEED_MIN, BUNNY_HOP_SPEED_MAX);
        (void)prng_index(side, static_cast<uint32_t>(sizeof(BUNNY_NAME_POOL) / sizeof(BUNNY_NAME_POOL[0])));
    }
}

bool hedgehog_name_in_pool(const Entity& e) {
    if (e.nameIndex >= sizeof(HEDGEHOG_NAME_POOL) / sizeof(HEDGEHOG_NAME_POOL[0])) return false;
    const wchar_t* name = HEDGEHOG_NAME_POOL[e.nameIndex];
    for (const wchar_t* candidate : HEDGEHOG_NAME_POOL) {
        if (std::wcscmp(name, candidate) == 0) return true;
    }
    return false;
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(HedgehogTests)
{
public:
TEST_METHOD(HedgehogConstantsArePinnedToSpecValues) {
    Assert::IsTrue(HEDGEHOG_COUNT_MIN == 0);
    Assert::IsTrue(HEDGEHOG_COUNT_MAX == 1);
    Assert::IsTrue(HEDGEHOG_COUNT_PROBABILITY == Near(0.55));
    Assert::IsTrue(HEDGEHOG_WALK_SPEED_MIN == Near(4.0));
    Assert::IsTrue(HEDGEHOG_WALK_SPEED_MAX == Near(8.0));
    Assert::IsTrue(HEDGEHOG_BODY_RADIUS == Near(9.0));
    Assert::IsTrue(HEDGEHOG_BODY_HEIGHT == Near(5.5));
    Assert::IsTrue(HEDGEHOG_HEAD_RADIUS == Near(3.6));
    Assert::IsTrue(HEDGEHOG_NOSE_RADIUS == Near(0.8));
    Assert::IsTrue(HEDGEHOG_LEG_LENGTH == Near(2.5));
    Assert::IsTrue(HEDGEHOG_SPIKE_COUNT == 14);
    Assert::IsTrue(HEDGEHOG_SPIKE_LENGTH == Near(3.0));
    Assert::IsTrue(HEDGEHOG_SPIKE_WIDTH == Near(1.4));
    Assert::IsTrue(HEDGEHOG_SPIKE_ARC_START_DEG == Near(-20.0));
    Assert::IsTrue(HEDGEHOG_SPIKE_ARC_END_DEG == Near(200.0));
    Assert::IsTrue(HEDGEHOG_BODY_COLOR == 0xFF5C4633u);
    Assert::IsTrue(HEDGEHOG_SPIKE_COLOR == 0xFF3A2A1Fu);
    Assert::IsTrue(HEDGEHOG_SPIKE_TIP_COLOR == 0xFF1E150Eu);
    Assert::IsTrue(HEDGEHOG_NOSE_COLOR == 0xFF1A1208u);
    Assert::IsTrue(HEDGEHOG_EYE_COLOR == 0xFF1A1208u);
    Assert::IsTrue(HEDGEHOG_STATE_WALKING == 0);
    Assert::IsTrue(HEDGEHOG_STATE_SNUFFLING == 1);
    Assert::IsTrue(HEDGEHOG_STATE_IDLE == 2);
    Assert::IsTrue(HEDGEHOG_STATE_SLEEPING == 3);
    Assert::IsTrue(HEDGEHOG_STATE_CURLED == 4);
    Assert::IsTrue(HEDGEHOG_WALK_DURATION_MIN == Near(6.0));
    Assert::IsTrue(HEDGEHOG_WALK_DURATION_MAX == Near(12.0));
    Assert::IsTrue(HEDGEHOG_SNUFFLE_DURATION_MIN == Near(3.0));
    Assert::IsTrue(HEDGEHOG_SNUFFLE_DURATION_MAX == Near(6.0));
    Assert::IsTrue(HEDGEHOG_IDLE_DURATION_MIN == Near(1.5));
    Assert::IsTrue(HEDGEHOG_IDLE_DURATION_MAX == Near(3.0));
    Assert::IsTrue(HEDGEHOG_SLEEP_DURATION_MIN == Near(10.0));
    Assert::IsTrue(HEDGEHOG_SLEEP_DURATION_MAX == Near(25.0));
    Assert::IsTrue(HEDGEHOG_CURL_DURATION_MIN == Near(3.0));
    Assert::IsTrue(HEDGEHOG_CURL_DURATION_MAX == Near(5.5));
    Assert::IsTrue(HEDGEHOG_SNUFFLE_PROBABILITY == Near(0.55));
    Assert::IsTrue(HEDGEHOG_IDLE_PROBABILITY == Near(0.30));
    Assert::IsTrue(HEDGEHOG_SLEEP_PROB == Near(0.50));
    Assert::IsTrue(HEDGEHOG_STARTLE_RADIUS == Near(70.0));
    Assert::IsTrue(HEDGEHOG_SNUFFLE_HEAD_FREQ == Near(5.0));
    Assert::IsTrue(HEDGEHOG_SNUFFLE_HEAD_AMP == Near(0.7));
    Assert::IsTrue(HEDGEHOG_WADDLE_FREQ == Near(4.0));
    Assert::IsTrue(HEDGEHOG_WADDLE_AMP == Near(0.8));
    Assert::IsTrue(HEDGEHOG_ZZZ_CYCLE_SEC == Near(SHEEP_ZZZ_CYCLE_SEC));
    Assert::IsTrue(HEDGEHOG_ZZZ_RISE == Near(SHEEP_ZZZ_RISE * 0.5));
    Assert::IsTrue(HEDGEHOG_ZZZ_SIZE_START == Near(SHEEP_ZZZ_SIZE_START * 0.6));
    Assert::IsTrue(HEDGEHOG_ZZZ_SIZE_END == Near(SHEEP_ZZZ_SIZE_END * 0.6));
    Assert::IsTrue(sizeof(HEDGEHOG_NAME_POOL) / sizeof(HEDGEHOG_NAME_POOL[0]) == 12);
    Assert::IsTrue(std::wcscmp(HEDGEHOG_NAME_POOL[0], L"Bristle") == 0);
    Assert::IsTrue(std::wcscmp(HEDGEHOG_NAME_POOL[11], L"Burdock") == 0);
}

TEST_METHOD(HedgehogCountDistributionIsProbabilisticRareSighting) {
    constexpr int N = 1000;
    int present = 0;
    for (uint64_t i = 0; i < N; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15ull;
        Sim sim = build_grass_sim(seed);
        const int count = count_kind(sim, EntityKind::Hedgehog);
        Assert::IsTrue(count >= HEDGEHOG_COUNT_MIN);
        Assert::IsTrue(count <= HEDGEHOG_COUNT_MAX);
        present += count;
    }
    Assert::IsTrue(static_cast<double>(present) / N == Near(HEDGEHOG_COUNT_PROBABILITY).margin(0.05));
}

TEST_METHOD(HedgehogsAreGrassSceneOnly) {
    Sim sim = build_sim();
    sim_set_scene(sim, Scene::Desert);
    Assert::IsTrue(count_kind(sim, EntityKind::Hedgehog) == 0);
    sim_set_scene(sim, Scene::Winter);
    Assert::IsTrue(count_kind(sim, EntityKind::Hedgehog) == 0);
}

TEST_METHOD(GeneratedHedgehogsHaveSpeedRange) {
    bool sawHedgehog = false;
    for (uint64_t i = 0; i < 128; ++i) {
        Sim sim = build_grass_sim(CANONICAL_TEST_SEED + i * 0xD1B54A32D192ED03ull);
        for (const Entity& e : sim.entities) {
            if (e.kind != EntityKind::Hedgehog) continue;
            sawHedgehog = true;
            Assert::IsTrue(std::abs(e.vx) >= HEDGEHOG_WALK_SPEED_MIN);
            Assert::IsTrue(std::abs(e.vx) <= HEDGEHOG_WALK_SPEED_MAX);
            Assert::IsTrue(e.rotationSpeed == Near(std::abs(e.vx)));
        }
    }
    Assert::IsTrue(sawHedgehog);
}

TEST_METHOD(GeneratedHedgehogsHaveNamesInPool) {
    bool sawHedgehog = false;
    for (uint64_t i = 0; i < 128; ++i) {
        Sim sim = build_grass_sim(CANONICAL_TEST_SEED + i * 0x94D049BB133111EBull);
        for (const Entity& e : sim.entities) {
            if (e.kind != EntityKind::Hedgehog) continue;
            sawHedgehog = true;
            Assert::IsTrue(hedgehog_name_in_pool(e));
        }
    }
    Assert::IsTrue(sawHedgehog);
}

TEST_METHOD(HedgehogPRNGDrawOrderFollowsSheepCatsAndBunnies) {
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);
    Sim sim = build_grass_sim();

    const int sheepCount = prng_count(side, SHEEP_COUNT_MIN, SHEEP_COUNT_MAX);
    advance_sheep(side, sheepCount);
    const int catCount = prng_count(side, CAT_COUNT_MIN, CAT_COUNT_MAX);
    advance_cats(side, catCount);
    const int bunnyCount = prng_count(side, BUNNY_COUNT_MIN, BUNNY_COUNT_MAX);
    advance_bunnies(side, bunnyCount);

    const double hasDraw = prng_uniform(side, 0.0, 1.0);
    const int hedgehogCount = hasDraw < HEDGEHOG_COUNT_PROBABILITY ? 1 : 0;
    Assert::IsTrue(count_kind(sim, EntityKind::Hedgehog) == hedgehogCount);

    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Hedgehog) continue;
        const double margin = HEDGEHOG_BODY_RADIUS + 8.0;
        const double xFrac = prng_uniform(side, 0.0, 1.0);
        const double expectedX = margin + xFrac * (Monitor1920 - 2.0 * margin);
        const uint64_t vxSign = prng_next_u64(side) & 1ull;
        const double expectedDir = vxSign != 0ull ? 1.0 : -1.0;
        const double expectedSpeed = prng_uniform(side, HEDGEHOG_WALK_SPEED_MIN, HEDGEHOG_WALK_SPEED_MAX);
        const uint8_t expectedName = static_cast<uint8_t>(prng_index(side,
            static_cast<uint32_t>(sizeof(HEDGEHOG_NAME_POOL) / sizeof(HEDGEHOG_NAME_POOL[0]))));
        Assert::IsTrue(e.x == Near(expectedX));
        Assert::IsTrue(e.vx == Near(expectedDir * expectedSpeed));
        Assert::IsTrue(e.nameIndex == expectedName);
        ++seen;
    }
    Assert::IsTrue(seen == hedgehogCount);
}

TEST_METHOD(HedgehogEdgeBounceFlipsDirection) {
    Sim sim = build_sim();
    sim.currentScene = Scene::Desert;
    sim.entities.clear();
    Entity e = hedgehog_entity(Monitor1920 - (HEDGEHOG_BODY_RADIUS + 2.0) + 0.1, HEDGEHOG_WALK_SPEED_MIN);
    e.stateTimer = 10.0;
    sim.entities.push_back(e);

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(sim.entities.front().vx < 0.0);
}

TEST_METHOD(HedgehogStartleRadiusCurlsWithoutFlippingVx) {
    Sim sim = build_sim();
    sim.entities.clear();
    Entity e = hedgehog_entity(500.0, -HEDGEHOG_WALK_SPEED_MIN);
    e.state = HEDGEHOG_STATE_WALKING;
    e.stateTimer = 10.0;
    sim.entities.push_back(e);

    sim_apply_click(sim, click_event(e.x + 10.0, e.y));
    Assert::IsTrue(sim.entities.front().state == HEDGEHOG_STATE_CURLED);
    Assert::IsTrue(sim.entities.front().vx == Near(-HEDGEHOG_WALK_SPEED_MIN));
    Assert::IsTrue(sim.entities.front().stateTimer >= HEDGEHOG_CURL_DURATION_MIN);
    Assert::IsTrue(sim.entities.front().stateTimer <= HEDGEHOG_CURL_DURATION_MAX);

    Sim outside = build_sim();
    outside.entities.clear();
    Entity far = hedgehog_entity(500.0, HEDGEHOG_WALK_SPEED_MIN);
    outside.entities.push_back(far);
    sim_apply_click(outside, click_event(far.x + HEDGEHOG_STARTLE_RADIUS + 10.0, far.y));
    Assert::IsTrue(outside.entities.front().state == HEDGEHOG_STATE_WALKING);
    Assert::IsTrue(outside.entities.front().vx == Near(HEDGEHOG_WALK_SPEED_MIN));
}

TEST_METHOD(HedgehogCurlAutoUncurlsToPreviousState) {
    Sim sim = build_sim();
    sim.entities.clear();
    Entity e = hedgehog_entity(500.0, HEDGEHOG_WALK_SPEED_MIN);
    e.state = HEDGEHOG_STATE_IDLE;
    e.stateTimer = 2.5;
    sim.entities.push_back(e);

    sim_apply_click(sim, click_event(e.x, e.y));
    Assert::IsTrue(sim.entities.front().state == HEDGEHOG_STATE_CURLED);
    sim_tick_entities(sim, HEDGEHOG_CURL_DURATION_MAX + 0.1);

    Assert::IsTrue(sim.entities.front().state == HEDGEHOG_STATE_IDLE);
    Assert::IsTrue(sim.entities.front().vx == Near(HEDGEHOG_WALK_SPEED_MIN));
}

TEST_METHOD(HedgehogWakesFromSleepOnStartleAndDoesNotResumeSleep) {
    Sim sim = build_sim();
    sim.entities.clear();
    Entity e = hedgehog_entity(500.0, HEDGEHOG_WALK_SPEED_MIN);
    e.state = HEDGEHOG_STATE_SLEEPING;
    e.stateTimer = 10.0;
    sim.entities.push_back(e);

    sim_apply_click(sim, click_event(e.x + 10.0, e.y));
    Assert::IsTrue(sim.entities.front().state == HEDGEHOG_STATE_CURLED);
    Assert::IsTrue(sim.entities.front().state != HEDGEHOG_STATE_SLEEPING);
    sim_tick_entities(sim, HEDGEHOG_CURL_DURATION_MAX + 0.1);

    Assert::IsTrue(sim.entities.front().state == HEDGEHOG_STATE_WALKING);
    Assert::IsTrue(sim.entities.front().state != HEDGEHOG_STATE_SLEEPING);
}

TEST_METHOD(HedgehogStateTransitionProbabilitiesAreStable) {
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);
    constexpr int N = 10000;
    int snuffle = 0;
    int idle = 0;
    int sleep = 0;
    for (int i = 0; i < N; ++i) {
        const uint8_t state = hedgehog_choose_rest_state(p);
        if (state == HEDGEHOG_STATE_SNUFFLING) ++snuffle;
        else if (state == HEDGEHOG_STATE_IDLE) ++idle;
        else if (state == HEDGEHOG_STATE_SLEEPING) ++sleep;
    }

    const double sleepProb = HEDGEHOG_SLEEP_PROB;
    const double activeWeight = HEDGEHOG_SNUFFLE_PROBABILITY + HEDGEHOG_IDLE_PROBABILITY;
    const double expectedSnuffle = (1.0 - sleepProb) * HEDGEHOG_SNUFFLE_PROBABILITY / activeWeight;
    const double expectedIdle = (1.0 - sleepProb) * HEDGEHOG_IDLE_PROBABILITY / activeWeight;
    Assert::IsTrue(static_cast<double>(sleep) / N == Near(sleepProb).margin(0.02));
    Assert::IsTrue(static_cast<double>(snuffle) / N == Near(expectedSnuffle).margin(0.02));
    Assert::IsTrue(static_cast<double>(idle) / N == Near(expectedIdle).margin(0.02));
}

TEST_METHOD(HedgehogSleepProbabilityIsStable) {
    constexpr int N = 20000;
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED ^ 0x1234ull);
    int sleep = 0;
    for (int i = 0; i < N; ++i) {
        if (hedgehog_choose_rest_state(p) == HEDGEHOG_STATE_SLEEPING) ++sleep;
    }
    Assert::IsTrue(static_cast<double>(sleep) / N == Near(HEDGEHOG_SLEEP_PROB).margin(0.02));
}

TEST_METHOD(HedgehogHasNoActiveInteractionStates) {
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED ^ 0xCAFEull);
    for (int i = 0; i < 1000; ++i) {
        const uint8_t state = hedgehog_choose_rest_state(p);
        Assert::IsTrue((state == HEDGEHOG_STATE_SNUFFLING
              || state == HEDGEHOG_STATE_IDLE
              || state == HEDGEHOG_STATE_SLEEPING));
    }

    Sim sim = build_sim();
    sim.entities.clear();
    Entity e = hedgehog_entity(500.0, HEDGEHOG_WALK_SPEED_MIN);
    e.stateTimer = 10.0;
    sim.entities.push_back(e);
    sim_tick_entities(sim, 0.016);
    Assert::IsTrue(sim.entities.front().state == HEDGEHOG_STATE_WALKING);
    Assert::IsTrue(std::abs(sim.entities.front().vx) == Near(HEDGEHOG_WALK_SPEED_MIN));
}
};
}
