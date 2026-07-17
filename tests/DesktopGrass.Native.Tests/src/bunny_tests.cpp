// bunny_tests.cpp
//
// §18 Bunny critter tests. Mirrors Win2D BunnyTests.cs.

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

Sim build_grass_sim(uint64_t seed = CANONICAL_TEST_SEED) {
    Sim sim = sim_init(seed, Monitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Grass);
    sim_set_critter(sim, CritterKind::Bunny);
    return sim;
}

Entity bunny_entity(double x = 500.0, double vx = BUNNY_HOP_SPEED_MIN) {
    Entity e{};
    e.kind = EntityKind::Bunny;
    e.size = BUNNY_BODY_RADIUS;
    e.x = x;
    e.y = STRIP_HEIGHT + HEADROOM - BUNNY_BODY_HEIGHT - BUNNY_LEG_LENGTH;
    e.vx = vx;
    e.rotationSpeed = std::abs(vx);
    e.lifetime = -1.0;
    e.state = BUNNY_STATE_HOPPING;
    e.stateTimer = BUNNY_HOP_DURATION;
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

bool bunny_name_in_pool(const Entity& e) {
    if (e.nameIndex >= sizeof(BUNNY_NAME_POOL) / sizeof(BUNNY_NAME_POOL[0])) return false;
    const wchar_t* name = BUNNY_NAME_POOL[e.nameIndex];
    for (const wchar_t* candidate : BUNNY_NAME_POOL) {
        if (std::wcscmp(name, candidate) == 0) return true;
    }
    return false;
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(BunnyTests)
{
public:
TEST_METHOD(BunnyConstantsArePinnedToSpecValues) {
    Assert::IsTrue(BUNNY_COUNT_MIN == 1);
    Assert::IsTrue(BUNNY_COUNT_MAX == 2);
    Assert::IsTrue(BUNNY_HOP_SPEED_MIN == Near(22.0));
    Assert::IsTrue(BUNNY_HOP_SPEED_MAX == Near(38.0));
    Assert::IsTrue(BUNNY_BODY_RADIUS == Near(8.0));
    Assert::IsTrue(BUNNY_BODY_HEIGHT == Near(6.5));
    Assert::IsTrue(BUNNY_HEAD_RADIUS == Near(4.2));
    Assert::IsTrue(BUNNY_EAR_HEIGHT == Near(9.0));
    Assert::IsTrue(BUNNY_EAR_WIDTH == Near(2.2));
    Assert::IsTrue(BUNNY_EAR_SPACING == Near(3.0));
    Assert::IsTrue(BUNNY_LEG_LENGTH == Near(4.0));
    Assert::IsTrue(BUNNY_TAIL_RADIUS == Near(2.4));
    Assert::IsTrue(BUNNY_BODY_COLOR == 0xFF8A6A4Au);
    Assert::IsTrue(BUNNY_BELLY_COLOR == 0xFFC4A98Du);
    Assert::IsTrue(BUNNY_EAR_COLOR == 0xFF8A6A4Au);
    Assert::IsTrue(BUNNY_EAR_INNER_COLOR == 0xFFD9A0A0u);
    Assert::IsTrue(BUNNY_TAIL_COLOR == 0xFFF7F4EBu);
    Assert::IsTrue(BUNNY_EYE_COLOR == 0xFF1A1208u);
    Assert::IsTrue(BUNNY_NOSE_COLOR == 0xFF8A4040u);
    Assert::IsTrue(BUNNY_STATE_HOPPING == 0);
    Assert::IsTrue(BUNNY_STATE_GRAZING == 1);
    Assert::IsTrue(BUNNY_STATE_IDLE == 2);
    Assert::IsTrue(BUNNY_STATE_SLEEPING == 3);
    Assert::IsTrue(BUNNY_STATE_STARTLED == 4);
    Assert::IsTrue(BUNNY_HOP_DURATION == Near(0.40));
    Assert::IsTrue(BUNNY_HOP_HEIGHT == Near(8.0));
    Assert::IsTrue(BUNNY_HOP_GAP_MIN == Near(0.05));
    Assert::IsTrue(BUNNY_HOP_GAP_MAX == Near(0.20));
    Assert::IsTrue(BUNNY_GRAZE_DURATION_MIN == Near(2.5));
    Assert::IsTrue(BUNNY_GRAZE_DURATION_MAX == Near(4.5));
    Assert::IsTrue(BUNNY_IDLE_DURATION_MIN == Near(2.0));
    Assert::IsTrue(BUNNY_IDLE_DURATION_MAX == Near(4.0));
    Assert::IsTrue(BUNNY_SLEEP_DURATION_MIN == Near(6.0));
    Assert::IsTrue(BUNNY_SLEEP_DURATION_MAX == Near(12.0));
    Assert::IsTrue(BUNNY_GRAZE_PROBABILITY == Near(0.55));
    Assert::IsTrue(BUNNY_IDLE_PROBABILITY == Near(0.30));
    Assert::IsTrue(BUNNY_SLEEP_PROB == Near(0.05));
    Assert::IsTrue(BUNNY_STARTLE_RADIUS == Near(90.0));
    Assert::IsTrue(BUNNY_STARTLE_BOOST == Near(2.0));
    Assert::IsTrue(BUNNY_STARTLE_HOP_HEIGHT == Near(14.0));
    Assert::IsTrue(BUNNY_STARTLE_DURATION == Near(3.0));
    Assert::IsTrue(BUNNY_NOSE_TWITCH_FREQ == Near(6.0));
    Assert::IsTrue(BUNNY_NOSE_TWITCH_AMP == Near(0.5));
    Assert::IsTrue(BUNNY_EAR_WIGGLE_FREQ == Near(1.2));
    Assert::IsTrue(BUNNY_EAR_WIGGLE_AMP == Near(0.20));
    Assert::IsTrue(BUNNY_ZZZ_CYCLE_SEC == Near(SHEEP_ZZZ_CYCLE_SEC));
    Assert::IsTrue(BUNNY_ZZZ_RISE == Near(SHEEP_ZZZ_RISE * 0.7));
    Assert::IsTrue(BUNNY_ZZZ_SIZE_START == Near(SHEEP_ZZZ_SIZE_START * 0.7));
    Assert::IsTrue(BUNNY_ZZZ_SIZE_END == Near(SHEEP_ZZZ_SIZE_END * 0.7));
    Assert::IsTrue(sizeof(BUNNY_NAME_POOL) / sizeof(BUNNY_NAME_POOL[0]) == 12);
    Assert::IsTrue(std::wcscmp(BUNNY_NAME_POOL[0], L"Clover") == 0);
    Assert::IsTrue(std::wcscmp(BUNNY_NAME_POOL[11], L"Snowdrop") == 0);
}

TEST_METHOD(GrassGenerationProducesBunnyCountInRange) {
    for (uint64_t i = 0; i < 128; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15ull;
        Sim sim = build_grass_sim(seed);
        const int bunnies = count_kind(sim, EntityKind::Bunny);
        Assert::IsTrue(bunnies >= BUNNY_COUNT_MIN);
        Assert::IsTrue(bunnies <= BUNNY_COUNT_MAX);
    }
}

TEST_METHOD(BunniesAreGrassSceneOnly) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Desert);
    Assert::IsTrue(count_kind(sim, EntityKind::Bunny) == 0);
    sim_set_scene(sim, Scene::Winter);
    Assert::IsTrue(count_kind(sim, EntityKind::Bunny) == 0);
    sim_set_critter(sim, CritterKind::Bunny);
    Assert::IsTrue(count_kind(sim, EntityKind::Bunny) == 0);
}

TEST_METHOD(GeneratedBunniesHaveSpeedRange) {
    Sim sim = build_grass_sim();
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Bunny) continue;
        Assert::IsTrue(std::abs(e.vx) >= BUNNY_HOP_SPEED_MIN);
        Assert::IsTrue(std::abs(e.vx) < BUNNY_HOP_SPEED_MAX);
        Assert::IsTrue(e.rotationSpeed == Near(std::abs(e.vx)));
    }
}

TEST_METHOD(GeneratedBunniesHaveNamesInPool) {
    Sim sim = build_grass_sim();
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Bunny) continue;
        Assert::IsTrue(bunny_name_in_pool(e));
    }
}

TEST_METHOD(BunnyPRNGDrawOrderFollowsSheepAndCats) {
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);

    Sim sim = build_grass_sim();

    const int sheepCount = prng_count(side, SHEEP_COUNT_MIN, SHEEP_COUNT_MAX);
    advance_sheep(side, sheepCount);
    const int catCount = prng_count(side, CAT_COUNT_MIN, CAT_COUNT_MAX);
    advance_cats(side, catCount);
    const int bunnyCount = prng_count(side, BUNNY_COUNT_MIN, BUNNY_COUNT_MAX);
    Assert::IsTrue(count_kind(sim, EntityKind::Bunny) == bunnyCount);

    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Bunny) continue;
        const double margin = BUNNY_BODY_RADIUS + 8.0;
        const double xFrac = prng_uniform(side, 0.0, 1.0);
        const double expectedX = margin + xFrac * (Monitor1920 - 2.0 * margin);
        const uint64_t vxSign = prng_next_u64(side) & 1ull;
        const double expectedDir = vxSign != 0ull ? 1.0 : -1.0;
        const double expectedSpeed = prng_uniform(side, BUNNY_HOP_SPEED_MIN, BUNNY_HOP_SPEED_MAX);
        const uint8_t expectedName = static_cast<uint8_t>(prng_index(side,
            static_cast<uint32_t>(sizeof(BUNNY_NAME_POOL) / sizeof(BUNNY_NAME_POOL[0]))));
        Assert::IsTrue(e.x == Near(expectedX));
        Assert::IsTrue(e.vx == Near(expectedDir * expectedSpeed));
        Assert::IsTrue(e.nameIndex == expectedName);
        ++seen;
    }
    Assert::IsTrue(seen == bunnyCount);
}

TEST_METHOD(BunnyEdgeBounceFlipsDirection) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim.currentScene = Scene::Desert;
    sim.entities.clear();
    Entity e = bunny_entity(Monitor1920 - (BUNNY_BODY_RADIUS + 2.0) + 0.1, BUNNY_HOP_SPEED_MIN);
    e.stateTimer = 10.0;
    sim.entities.push_back(e);

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(sim.entities.front().vx < 0.0);
}

TEST_METHOD(BunnyStartleRadiusHopsAwayAndOutsideClickDoesNothing) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim.entities.clear();
    Entity e = bunny_entity(500.0, -BUNNY_HOP_SPEED_MIN);
    e.state = BUNNY_STATE_IDLE;
    e.stateTimer = 3.0;
    sim.entities.push_back(e);

    sim_apply_click(sim, click_event(500.0 - 20.0, e.y));
    Assert::IsTrue(sim.entities.front().state == BUNNY_STATE_STARTLED);
    Assert::IsTrue(sim.entities.front().vx > 0.0);
    Assert::IsTrue(sim.entities.front().stateTimer == Near(BUNNY_STARTLE_DURATION));

    Entity after = sim.entities.front();
    const double vxBefore = after.vx;
    const uint8_t stateBefore = after.state;
    sim_apply_click(sim, click_event(after.x + BUNNY_STARTLE_RADIUS + 10.0, after.y));
    Assert::IsTrue(sim.entities.front().state == stateBefore);
    Assert::IsTrue(sim.entities.front().vx == Near(vxBefore));
}

TEST_METHOD(BunnyWakesFromSleepOnStartle) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim.entities.clear();
    Entity e = bunny_entity(500.0, BUNNY_HOP_SPEED_MIN);
    e.state = BUNNY_STATE_SLEEPING;
    e.stateTimer = 10.0;
    sim.entities.push_back(e);

    sim_apply_click(sim, click_event(e.x + 10.0, e.y));

    Assert::IsTrue(sim.entities.front().state == BUNNY_STATE_STARTLED);
    Assert::IsTrue(sim.entities.front().state != BUNNY_STATE_SLEEPING);
    Assert::IsTrue(sim.entities.front().vx < 0.0);
}

TEST_METHOD(BunnyHopArcIsBounded) {
    Assert::IsTrue(bunny_hop_y_offset(0.0, false) == Near(0.0));
    Assert::IsTrue(bunny_hop_y_offset(BUNNY_HOP_DURATION, false) == Near(0.0).margin(1e-12));
    const double peak = bunny_hop_y_offset(BUNNY_HOP_DURATION * 0.5, false);
    Assert::IsTrue(peak > 0.0);
    Assert::IsTrue(peak <= BUNNY_HOP_HEIGHT);
}

TEST_METHOD(BunnyStateTransitionProbabilitiesAreStable) {
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);
    constexpr int N = 10000;
    int graze = 0;
    int idle = 0;
    int sleep = 0;
    for (int i = 0; i < N; ++i) {
        const uint8_t state = bunny_choose_rest_state(p);
        if (state == BUNNY_STATE_GRAZING) ++graze;
        else if (state == BUNNY_STATE_IDLE) ++idle;
        else if (state == BUNNY_STATE_SLEEPING) ++sleep;
    }

    const double sleepProb = BUNNY_SLEEP_PROB;
    const double activeWeight = BUNNY_GRAZE_PROBABILITY + BUNNY_IDLE_PROBABILITY;
    const double expectedGraze = (1.0 - sleepProb) * BUNNY_GRAZE_PROBABILITY / activeWeight;
    const double expectedIdle = (1.0 - sleepProb) * BUNNY_IDLE_PROBABILITY / activeWeight;
    Assert::IsTrue(static_cast<double>(sleep) / N == Near(sleepProb).margin(0.02));
    Assert::IsTrue(static_cast<double>(graze) / N == Near(expectedGraze).margin(0.02));
    Assert::IsTrue(static_cast<double>(idle) / N == Near(expectedIdle).margin(0.02));
}

TEST_METHOD(BunnySleepProbabilityIsStable) {
    constexpr int N = 20000;
    Prng p;
    prng_init(p, CANONICAL_TEST_SEED ^ 0x1234ull);
    int sleep = 0;
    for (int i = 0; i < N; ++i) {
        if (bunny_choose_rest_state(p) == BUNNY_STATE_SLEEPING) ++sleep;
    }
    Assert::IsTrue(static_cast<double>(sleep) / N == Near(BUNNY_SLEEP_PROB).margin(0.02));
}
};
}
