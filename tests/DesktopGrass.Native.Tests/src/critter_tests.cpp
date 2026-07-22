// critter_tests.cpp
//
// Critter subsystem tests (architecture.md §13.3 / §16). Orthogonal to Scene.
//
// Coverage:
//   * CritterKind discriminants are spec-locked ({None=0, Sheep=1}).
//   * EntityKind::Sheep == 3 (added after the original {None, Tumbleweed,
//     Snowflake} enum).
//   * SHEEP_* and CRITTER_* constants are pinned to spec values.
//   * sim_init defaults sim.currentCritter to None (no sheep until the user
//     opts in via tray).
//   * sim_set_critter(Sheep) on CANONICAL_TEST_SEED + 1920 produces
//     deterministic count K ∈ [SHEEP_COUNT_MIN, SHEEP_COUNT_MAX], with
//     every sheep entity well-formed: kind=Sheep, state=Walking, stateTimer
//     in [WALK_DURATION_MIN, MAX], speed in [WALK_SPEED_MIN, MAX], x within
//     monitor margins.
//   * sim_set_critter(None) erases all sheep but preserves scene entities
//     (snowflakes/tumbleweeds aren't touched).
//   * sim_set_scene preserves the active critter — flipping Grass→Desert
//     re-spawns sheep on the new scene.
//   * Sheep PRNG draw order is bit-identical to a side-stream Prng for the
//     locked sequence (count, then per-sheep: x, speed, dir-coin, seed,
//     stateTimer, nameIndex).
//   * Click within SHEEP_STARTLE_RADIUS pushes a sheep into Hopping, flips
//     vx away from the cursor, and resets age.
//   * Click outside SHEEP_STARTLE_RADIUS leaves sheep state untouched.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>
#include <cmath>
#include <cwchar>

using namespace desktopgrass;

namespace {

int count_kind(const Sim& sim, EntityKind kind) {
    int n = 0;
    for (const Entity& e : sim.entities) if (e.kind == kind) ++n;
    return n;
}

int count_sheep(const Sim& sim) {
    return count_kind(sim, EntityKind::Sheep);
}

const Entity* first_sheep(const Sim& sim) {
    for (const Entity& e : sim.entities) if (e.kind == EntityKind::Sheep) return &e;
    return nullptr;
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(CritterTests)
{
public:
TEST_METHOD(CritterKindHasSpecLockedDiscriminants) {
    Assert::IsTrue(static_cast<int>(CritterKind::None)  == 0);
    Assert::IsTrue(static_cast<int>(CritterKind::Sheep) == 1);
    Assert::IsTrue(static_cast<int>(CritterKind::Cat)   == 2);
    Assert::IsTrue(static_cast<int>(CritterKind::Bunny) == 3);
    Assert::IsTrue(static_cast<int>(CritterKind::Raccoon) == 4);
    Assert::IsTrue(static_cast<int>(EntityKind::Sheep)  == 3);
    Assert::IsTrue(static_cast<int>(EntityKind::Bunny)  == 6);
    Assert::IsTrue(static_cast<int>(EntityKind::Butterfly) == 7);
    Assert::IsTrue(static_cast<int>(EntityKind::Firefly) == 8);
    Assert::IsTrue(static_cast<int>(EntityKind::Raccoon) == 15);
    Assert::IsTrue(CRITTER_DEFAULT == CritterKind::None);
}

TEST_METHOD(SheepConstantsArePinnedToSpecValues) {
    Assert::IsTrue(SHEEP_COUNT_MIN      == 2);
    Assert::IsTrue(SHEEP_COUNT_MAX      == 3);
    Assert::IsTrue(sizeof(PET_COUNT_OPTIONS) / sizeof(PET_COUNT_OPTIONS[0]) == 6);
    for (int i = 0; i < 6; ++i) Assert::IsTrue(PET_COUNT_OPTIONS[i] == i + 1);
    Assert::IsTrue(PET_COUNT_DEFAULT_SHEEP == SHEEP_COUNT_MIN);
    Assert::IsTrue(PET_COUNT_DEFAULT_CAT == CAT_COUNT_MIN);
    Assert::IsTrue(PET_COUNT_MAX_PER_MONITOR == 6);
    Assert::IsTrue(sizeof(SHEEP_NAME_POOL) / sizeof(SHEEP_NAME_POOL[0]) == 8);
    Assert::IsTrue(sizeof(CAT_NAME_POOL) / sizeof(CAT_NAME_POOL[0]) == 8);
    Assert::IsTrue(std::wcscmp(SHEEP_NAME_POOL[0], L"Bessie") == 0);
    Assert::IsTrue(std::wcscmp(SHEEP_NAME_POOL[7], L"Hazel") == 0);
    Assert::IsTrue(std::wcscmp(CAT_NAME_POOL[0], L"Mittens") == 0);
    Assert::IsTrue(std::wcscmp(CAT_NAME_POOL[7], L"Juno") == 0);
    Assert::IsTrue(PET_NAME_HOVER_RADIUS == Near(50.0));
    Assert::IsTrue(PET_NAME_FADE_DURATION == Near(1.5));
    Assert::IsTrue(PET_NAME_FONT_SIZE == Near(11.0));
    Assert::IsTrue(PET_NAME_OFFSET_Y == Near(-8.0));
    Assert::IsTrue(PET_NAME_COLOR == 0xFFFFFFFFu);
    Assert::IsTrue(PET_NAME_SHADOW_COLOR == 0xC0000000u);
    Assert::IsTrue(SHEEP_WALK_SPEED_MIN == Near(14.0));
    Assert::IsTrue(SHEEP_WALK_SPEED_MAX == Near(26.0));
    Assert::IsTrue(SHEEP_BODY_RADIUS    == Near(12.0));
    Assert::IsTrue(SHEEP_HEAD_RADIUS    == Near(5.0));
    Assert::IsTrue(SHEEP_LEG_LENGTH     == Near(5.5));

    Assert::IsTrue(SHEEP_STATE_WALKING  == 0);
    Assert::IsTrue(SHEEP_STATE_GRAZING  == 1);
    Assert::IsTrue(SHEEP_STATE_IDLE     == 2);
    Assert::IsTrue(SHEEP_STATE_SLEEPING == 3);
    Assert::IsTrue(SHEEP_STATE_HOPPING  == 4);

    Assert::IsTrue(SHEEP_HOP_DURATION   == Near(0.55));
    Assert::IsTrue(SHEEP_HOP_HEIGHT     == Near(11.0));
    Assert::IsTrue(SHEEP_STARTLE_RADIUS == Near(64.0));
    Assert::IsTrue(SHEEP_STARTLE_BOOST  == Near(1.6));

    Assert::IsTrue(SHEEP_GRAZE_PROBABILITY     == Near(0.60));
    Assert::IsTrue(SHEEP_IDLE_PROBABILITY      == Near(0.25));
    Assert::IsTrue(SHEEP_SLEEP_FROM_IDLE_PROB  == Near(0.30));
}

TEST_METHOD(SimInitDefaultsCritterToNone) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    Assert::IsTrue(sim.currentCritter == CritterKind::None);
    Assert::IsTrue(count_sheep(sim) == 0);
}

TEST_METHOD(SimSetCritterSheepProducesDeterministicFlock) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);

    Assert::IsTrue(sim.currentCritter == CritterKind::Sheep);
    const int k = count_sheep(sim);
    Assert::IsTrue(k >= SHEEP_COUNT_MIN);
    Assert::IsTrue(k <= SHEEP_COUNT_MAX);

    const double groundY = sim.windowHeight;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Sheep) continue;
        Assert::IsTrue(e.state == SHEEP_STATE_WALKING);
        Assert::IsTrue(e.stateTimer >= SHEEP_WALK_DURATION_MIN);
        Assert::IsTrue(e.stateTimer <  SHEEP_WALK_DURATION_MAX);
        Assert::IsTrue(std::fabs(e.vx) >= SHEEP_WALK_SPEED_MIN);
        Assert::IsTrue(std::fabs(e.vx) <  SHEEP_WALK_SPEED_MAX);
        const double margin = e.size + 8.0;
        Assert::IsTrue(e.x >= margin);
        Assert::IsTrue(e.x <= sim.monitorWidth - margin);
        Assert::IsTrue(e.y == Near(groundY - SHEEP_BODY_HEIGHT - SHEEP_LEG_LENGTH));
        Assert::IsTrue(e.lifetime < 0.0); // infinite — sheep don't expire
        Assert::IsTrue(e.nameIndex < sizeof(SHEEP_NAME_POOL) / sizeof(SHEEP_NAME_POOL[0]));
    }
}

TEST_METHOD(SheepPRNGDrawOrderMatchesASideStream) {
    // Independent side stream that walks the documented sequence:
    //   count
    //   per-sheep: x, speed, dir-coin, seed, stateTimer, nameIndex
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);

    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);

    const double countDraw = prng_uniform(side, SHEEP_COUNT_MIN, SHEEP_COUNT_MAX + 1);
    int expectedCount = static_cast<int>(std::floor(countDraw));
    if (expectedCount < SHEEP_COUNT_MIN) expectedCount = SHEEP_COUNT_MIN;
    if (expectedCount > SHEEP_COUNT_MAX) expectedCount = SHEEP_COUNT_MAX;
    Assert::IsTrue(count_sheep(sim) == expectedCount);

    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Sheep) continue;
        const double margin = SHEEP_BODY_RADIUS + 8.0;
        const double expectedX = prng_uniform(side, margin, 1920.0 - margin);
        const double expectedSpeed = prng_uniform(side, SHEEP_WALK_SPEED_MIN, SHEEP_WALK_SPEED_MAX);
        const double dirCoin = prng_uniform(side, 0.0, 1.0);
        const double expectedDir = (dirCoin < 0.5) ? -1.0 : 1.0;
        const uint32_t expectedSeed = prng_next_u32(side);
        const double expectedTimer = prng_uniform(side, SHEEP_WALK_DURATION_MIN, SHEEP_WALK_DURATION_MAX);
        const uint8_t expectedNameIndex = static_cast<uint8_t>(prng_index(side,
            static_cast<uint32_t>(sizeof(SHEEP_NAME_POOL) / sizeof(SHEEP_NAME_POOL[0]))));

        Assert::IsTrue(e.x == Near(expectedX));
        Assert::IsTrue(e.vx == Near(expectedSpeed * expectedDir));
        Assert::IsTrue(e.seed == expectedSeed);
        Assert::IsTrue(e.stateTimer == Near(expectedTimer));
        Assert::IsTrue(e.nameIndex == expectedNameIndex);
        ++seen;
    }
    Assert::IsTrue(seen == expectedCount);
}

TEST_METHOD(CanonicalCritterNameIndicesAreStableAndSpeciesLocal) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);
    const uint8_t expectedSheepNames[] = { 4, 7 };
    int sheepSeen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Sheep) continue;
        Assert::IsTrue(sheepSeen < static_cast<int>(sizeof(expectedSheepNames) / sizeof(expectedSheepNames[0])));
        Assert::IsTrue(e.nameIndex == expectedSheepNames[sheepSeen]);
        Assert::IsTrue(std::wcscmp(SHEEP_NAME_POOL[e.nameIndex], sheepSeen == 0 ? L"Pippin" : L"Hazel") == 0);
        ++sheepSeen;
    }
    Assert::IsTrue(sheepSeen == 2);

    sim_set_critter(sim, CritterKind::Cat);
    const uint8_t expectedCatNames[] = { 4 };
    int catSeen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Cat) continue;
        Assert::IsTrue(catSeen < static_cast<int>(sizeof(expectedCatNames) / sizeof(expectedCatNames[0])));
        Assert::IsTrue(e.nameIndex == expectedCatNames[catSeen]);
        Assert::IsTrue(e.nameIndex < sizeof(CAT_NAME_POOL) / sizeof(CAT_NAME_POOL[0]));
        Assert::IsTrue(std::wcscmp(CAT_NAME_POOL[e.nameIndex], L"Smokey") == 0);
        ++catSeen;
    }
    Assert::IsTrue(catSeen == 1);
}

TEST_METHOD(SimSetCritterCount0PreservesRandomSheepCountDraw) {
    bool sawMin = false;
    bool sawMax = false;
    for (uint64_t i = 0; i < 64; ++i) {
        const uint64_t seed = CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15ull;
        Sim sim = sim_init(seed, 1920.0, DEFAULT_DENSITY);
        sim_set_critter_count(sim, 3);
        sim_set_critter_count(sim, 0);
        sim_set_critter(sim, CritterKind::Sheep);

        Prng side;
        prng_init(side, seed ^ CRITTER_PRNG_SALT);
        const double countDraw = prng_uniform(side, SHEEP_COUNT_MIN, SHEEP_COUNT_MAX + 1);
        int expectedCount = static_cast<int>(std::floor(countDraw));
        if (expectedCount < SHEEP_COUNT_MIN) expectedCount = SHEEP_COUNT_MIN;
        if (expectedCount > SHEEP_COUNT_MAX) expectedCount = SHEEP_COUNT_MAX;

        Assert::IsTrue(count_sheep(sim) == expectedCount);
        sawMin = sawMin || expectedCount == SHEEP_COUNT_MIN;
        sawMax = sawMax || expectedCount == SHEEP_COUNT_MAX;
    }
    Assert::IsTrue(sawMin);
    Assert::IsTrue(sawMax);
}

TEST_METHOD(FixedSheepCountOverrideSkipsTheCountPRNGDraw) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);
    sim_set_critter_count(sim, 3);

    Assert::IsTrue(sim.critterCountOverride == 3);
    Assert::IsTrue(count_sheep(sim) == 3);

    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);
    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Sheep) continue;
        const double margin = SHEEP_BODY_RADIUS + 8.0;
        const double expectedX = prng_uniform(side, margin, 1920.0 - margin);
        const double expectedSpeed = prng_uniform(side, SHEEP_WALK_SPEED_MIN, SHEEP_WALK_SPEED_MAX);
        const double dirCoin = prng_uniform(side, 0.0, 1.0);
        const double expectedDir = (dirCoin < 0.5) ? -1.0 : 1.0;
        const uint32_t expectedSeed = prng_next_u32(side);
        const double expectedTimer = prng_uniform(side, SHEEP_WALK_DURATION_MIN, SHEEP_WALK_DURATION_MAX);
        const uint8_t expectedNameIndex = static_cast<uint8_t>(prng_index(side,
            static_cast<uint32_t>(sizeof(SHEEP_NAME_POOL) / sizeof(SHEEP_NAME_POOL[0]))));

        Assert::IsTrue(e.x == Near(expectedX));
        Assert::IsTrue(e.vx == Near(expectedSpeed * expectedDir));
        Assert::IsTrue(e.seed == expectedSeed);
        Assert::IsTrue(e.stateTimer == Near(expectedTimer));
        Assert::IsTrue(e.nameIndex == expectedNameIndex);
        ++seen;
    }
    Assert::IsTrue(seen == 3);
}

TEST_METHOD(FixedCritterCountOverrideSupportsTrayRangeAndClamps) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);

    sim_set_critter_count(sim, 6);
    Assert::IsTrue(count_sheep(sim) == 6);

    sim_set_critter_count(sim, 8);
    Assert::IsTrue(count_sheep(sim) == PET_COUNT_MAX_PER_MONITOR);

    sim_set_critter(sim, CritterKind::Cat);
    sim_set_critter_count(sim, 2);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 2);
    Assert::IsTrue(count_sheep(sim) == 0);
}

TEST_METHOD(SimSetCritterNoneClearsAllGroundCritters) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);
    Assert::IsTrue(count_sheep(sim) >= SHEEP_COUNT_MIN);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 0);

    sim_set_critter(sim, CritterKind::None);
    Assert::IsTrue(count_sheep(sim) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Bunny) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Hedgehog) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Raccoon) == 0);
}

TEST_METHOD(RaccoonSelectionAllAndNoneAreBehaviorLocked) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Raccoon);

    Entity* jimothy = nullptr;
    int normalRaccoons = 0;
    for (Entity& e : sim.entities) {
        if (e.kind != EntityKind::Raccoon) continue;
        if (e.nameIndex == 0) jimothy = &e;
        else {
            ++normalRaccoons;
            Assert::IsTrue(e.nameIndex < sizeof(RACCOON_NAME_POOL) / sizeof(RACCOON_NAME_POOL[0]));
        }
    }
    Assert::IsTrue(jimothy != nullptr);
    const int raccoonCount = count_kind(sim, EntityKind::Raccoon);
    Assert::IsTrue(raccoonCount >= RACCOON_COUNT_MIN && raccoonCount <= RACCOON_COUNT_MAX);
    Assert::IsTrue(normalRaccoons == raccoonCount - 1);
    Assert::IsTrue(jimothy->state == RACCOON_STATE_WALKING);
    Assert::IsTrue(jimothy->nameIndex == 0);
    Assert::IsTrue(std::wcscmp(RACCOON_NAME_POOL[jimothy->nameIndex], L"Jimothy") == 0);
    Assert::IsTrue(std::abs(jimothy->vx) >= RACCOON_WALK_SPEED_MIN);
    Assert::IsTrue(std::abs(jimothy->vx) <= RACCOON_WALK_SPEED_MAX);

    jimothy->stateTimer = 0.0;
    sim_tick_entities(sim, 0.01);
    jimothy = nullptr;
    for (Entity& e : sim.entities) if (e.kind == EntityKind::Raccoon && e.nameIndex == 0) { jimothy = &e; break; }
    Assert::IsTrue(jimothy != nullptr);
    Assert::IsTrue(jimothy->state == RACCOON_STATE_SNUFFLING || jimothy->state == RACCOON_STATE_RESTING);

    InputEvent click{ EventType::Click, jimothy->x - 10.0, jimothy->y, 0.0 };
    sim_apply_click(sim, click);
    Assert::IsTrue(jimothy->state == RACCOON_STATE_WALKING);
    Assert::IsTrue(jimothy->vx > 0.0);

    sim_set_critter(sim, CritterKind::Bunny);
    int jimothyCount = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind == EntityKind::Raccoon && e.nameIndex == 0) ++jimothyCount;
    }
    Assert::IsTrue(count_kind(sim, EntityKind::Raccoon) >= RACCOON_COUNT_MIN);
    Assert::IsTrue(jimothyCount == 1);
    Assert::IsTrue(count_kind(sim, EntityKind::Sheep) >= SHEEP_COUNT_MIN);

    sim_set_critter(sim, CritterKind::None);
    Assert::IsTrue(count_kind(sim, EntityKind::Raccoon) == 0);
}

TEST_METHOD(RaccoonCountOverridePreservesExactlyOneJimothy) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Raccoon);

    sim_set_critter_count(sim, 1);
    Assert::IsTrue(count_kind(sim, EntityKind::Raccoon) == 1);
    for (const Entity& e : sim.entities) {
        if (e.kind == EntityKind::Raccoon) Assert::IsTrue(e.nameIndex == 0);
    }

    sim_set_critter_count(sim, 6);
    Assert::IsTrue(count_kind(sim, EntityKind::Raccoon) == 6);
    int jimothyCount = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Raccoon) continue;
        if (e.nameIndex == 0) ++jimothyCount;
        else Assert::IsTrue(e.nameIndex < sizeof(RACCOON_NAME_POOL) / sizeof(RACCOON_NAME_POOL[0]));
    }
    Assert::IsTrue(jimothyCount == 1);
}

TEST_METHOD(SimSetSceneGatesActiveSheepToGrass) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);
    const int sheepCountGrass = count_sheep(sim);
    Assert::IsTrue(sheepCountGrass >= SHEEP_COUNT_MIN);

    sim_set_scene(sim, Scene::Desert);
    Assert::IsTrue(count_sheep(sim) == 0);
    Assert::IsTrue(sim.currentCritter == CritterKind::Sheep);

    sim_set_scene(sim, Scene::Winter);
    Assert::IsTrue(count_sheep(sim) == 0);
    Assert::IsTrue(sim.currentCritter == CritterKind::Sheep);

    sim_set_scene(sim, Scene::Grass);
    Assert::IsTrue(count_sheep(sim) == sheepCountGrass);
}

TEST_METHOD(ClickWithinSHEEPSTARTLERADIUSTriggersHopAway) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);

    Entity* target = nullptr;
    for (Entity& e : sim.entities) {
        if (e.kind == EntityKind::Sheep) { target = &e; break; }
    }
    Assert::IsTrue(target != nullptr);

    // Click 16 DIP to the left of the sheep — well within startle radius,
    // inside the cut band (so the early y-gate doesn't reject).
    const double clickX = target->x - 16.0;
    const double clickY = sim.windowHeight - 20.0;
    target->age = 5.0; // pre-set age to verify reset

    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x = clickX;
    ev.y = clickY;
    ev.time = 0.0;
    sim_apply_click(sim, ev);

    Entity* after = nullptr;
    for (Entity& e : sim.entities) {
        if (e.kind == EntityKind::Sheep) { after = &e; break; }
    }
    Assert::IsTrue(after != nullptr);
    Assert::IsTrue(after->state == SHEEP_STATE_HOPPING);
    Assert::IsTrue(after->stateTimer == Near(SHEEP_HOP_DURATION));
    Assert::IsTrue(after->age == Near(0.0));
    Assert::IsTrue(after->vx > 0.0); // sheep was right of click → vx flipped to +
    Assert::IsTrue(std::fabs(after->vx) <= SHEEP_WALK_SPEED_MAX * SHEEP_STARTLE_BOOST);
}

TEST_METHOD(ClickOutsideSHEEPSTARTLERADIUSLeavesSheepAlone) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);

    Entity* target = nullptr;
    for (Entity& e : sim.entities) {
        if (e.kind == EntityKind::Sheep) { target = &e; break; }
    }
    Assert::IsTrue(target != nullptr);
    const uint8_t stateBefore = target->state;
    const double  vxBefore    = target->vx;

    // Click far away (300 DIP) but still in the cut band.
    const double clickX = target->x + SHEEP_STARTLE_RADIUS + 200.0;
    const double clickY = sim.windowHeight - 20.0;
    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x = clickX;
    ev.y = clickY;
    ev.time = 0.0;
    sim_apply_click(sim, ev);

    Entity* after = nullptr;
    for (Entity& e : sim.entities) {
        if (e.kind == EntityKind::Sheep) { after = &e; break; }
    }
    Assert::IsTrue(after != nullptr);
    Assert::IsTrue(after->state == stateBefore);
    Assert::IsTrue(after->vx == Near(vxBefore));
}
};
}
