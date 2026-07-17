// cat_tests.cpp
//
// §17 Cat critter tests. Mirrors Win2D CatTests.cs.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>
#include <cmath>

using namespace desktopgrass;

namespace {

int count_kind(const Sim& sim, EntityKind kind) {
    return static_cast<int>(std::count_if(sim.entities.begin(), sim.entities.end(),
        [kind](const Entity& e) { return e.kind == kind; }));
}

Entity* first_kind(Sim& sim, EntityKind kind) {
    for (Entity& e : sim.entities) if (e.kind == kind) return &e;
    return nullptr;
}

const Entity* first_kind(const Sim& sim, EntityKind kind) {
    for (const Entity& e : sim.entities) if (e.kind == kind) return &e;
    return nullptr;
}

void keep_first_cat_only(Sim& sim) {
    Entity* cat = first_kind(sim, EntityKind::Cat);
    Assert::IsTrue(cat != nullptr);
    const Entity copy = *cat;
    sim.entities.clear();
    sim.entities.push_back(copy);
}

InputEvent click_event(double x, double y) {
    InputEvent ev{};
    ev.type = EventType::Click;
    ev.x = x;
    ev.y = y;
    ev.time = 0.0;
    return ev;
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(CatTests)
{
public:
TEST_METHOD(CritterKindCatAndCRITTERCOUNTArePinned) {
    Assert::IsTrue(static_cast<int>(CritterKind::None)  == 0);
    Assert::IsTrue(static_cast<int>(CritterKind::Sheep) == 1);
    Assert::IsTrue(static_cast<int>(CritterKind::Cat)   == 2);
    Assert::IsTrue(static_cast<int>(CritterKind::Bunny) == 3);
    Assert::IsTrue(CRITTER_COUNT == 4);
    Assert::IsTrue(CRITTER_DEFAULT == CritterKind::None);
}

TEST_METHOD(EntityKindCatIsPinned) {
    Assert::IsTrue(static_cast<int>(EntityKind::None)       == 0);
    Assert::IsTrue(static_cast<int>(EntityKind::Tumbleweed) == 1);
    Assert::IsTrue(static_cast<int>(EntityKind::Snowflake)  == 2);
    Assert::IsTrue(static_cast<int>(EntityKind::Sheep)      == 3);
    Assert::IsTrue(static_cast<int>(EntityKind::Cat)        == 4);
}

TEST_METHOD(CatConstantsArePinnedToSpecValues) {
    Assert::IsTrue(CAT_COUNT_MIN == 1);
    Assert::IsTrue(CAT_COUNT_MAX == 2);
    Assert::IsTrue(CAT_WALK_SPEED_MIN == Near(10.0));
    Assert::IsTrue(CAT_WALK_SPEED_MAX == Near(22.0));
    Assert::IsTrue(CAT_POUNCE_SPEED   == Near(60.0));

    Assert::IsTrue(CAT_BODY_RADIUS    == Near(11.0));
    Assert::IsTrue(CAT_BODY_HEIGHT    == Near(7.0));
    Assert::IsTrue(CAT_HEAD_RADIUS    == Near(4.5));
    Assert::IsTrue(CAT_LEG_LENGTH     == Near(5.0));
    Assert::IsTrue(CAT_TAIL_LENGTH    == Near(13.0));
    Assert::IsTrue(CAT_TAIL_THICKNESS == Near(1.6));
    Assert::IsTrue(CAT_EAR_HEIGHT     == Near(4.5));

    Assert::IsTrue(CAT_BODY_COLOR == 0xFF6B6259u);
    Assert::IsTrue(CAT_LEG_COLOR  == 0xFF3D3733u);
    Assert::IsTrue(CAT_FACE_COLOR == 0xFF6B6259u);
    Assert::IsTrue(CAT_EAR_COLOR  == 0xFF3D3733u);
    Assert::IsTrue(CAT_INK_COLOR  == 0xFF1A1614u);

    Assert::IsTrue(CAT_WALK_PERIOD    == Near(0.50));
    Assert::IsTrue(CAT_LEG_CYCLE_AMP  == Near(1.6));
    Assert::IsTrue(CAT_HEAD_BOB_AMP   == Near(0.4));
    Assert::IsTrue(CAT_TAIL_SWAY_FREQ == Near(1.2));
    Assert::IsTrue(CAT_TAIL_SWAY_AMP  == Near(0.35));

    Assert::IsTrue(CAT_STATE_WALKING  == SHEEP_STATE_WALKING);
    Assert::IsTrue(CAT_STATE_IDLE     == SHEEP_STATE_IDLE);
    Assert::IsTrue(CAT_STATE_SLEEPING == SHEEP_STATE_SLEEPING);
    Assert::IsTrue(CAT_STATE_POUNCING == SHEEP_STATE_HOPPING);

    Assert::IsTrue(CAT_WALK_DURATION_MIN  == Near(6.0));
    Assert::IsTrue(CAT_WALK_DURATION_MAX  == Near(10.0));
    Assert::IsTrue(CAT_IDLE_DURATION_MIN  == Near(4.0));
    Assert::IsTrue(CAT_IDLE_DURATION_MAX  == Near(8.0));
    Assert::IsTrue(CAT_SLEEP_DURATION_MIN == Near(20.0));
    Assert::IsTrue(CAT_SLEEP_DURATION_MAX == Near(40.0));
    Assert::IsTrue(CAT_POUNCE_DURATION    == Near(0.45));

    Assert::IsTrue(CAT_IDLE_PROBABILITY == Near(0.65));
    Assert::IsTrue(CAT_SLEEP_PROBABILITY == Near(0.30));
    Assert::IsTrue(CAT_SLEEP_FROM_IDLE_PROB == Near(0.50));

    Assert::IsTrue(CAT_POUNCE_RADIUS == Near(80.0));
    Assert::IsTrue(CAT_POUNCE_HEIGHT == Near(9.0));
    Assert::IsTrue(CAT_CURIOUS_RADIUS == Near(100.0));
    Assert::IsTrue(CAT_CURIOUS_HEAD_TURN_MAX == Near(0.7));
}

TEST_METHOD(SimInitDefaultsToNoneAndDoesNotGenerateCatsUntilSelected) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    Assert::IsTrue(sim.currentCritter == CritterKind::None);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 0);

    sim_set_critter(sim, CritterKind::Cat);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) >= CAT_COUNT_MIN);
}

TEST_METHOD(SimSetCritterCatProducesDeterministicCats) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);

    Assert::IsTrue(sim.currentCritter == CritterKind::Cat);
    const int k = count_kind(sim, EntityKind::Cat);
    Assert::IsTrue(k >= CAT_COUNT_MIN);
    Assert::IsTrue(k <= CAT_COUNT_MAX);

    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Cat) continue;
        Assert::IsTrue(e.state == CAT_STATE_WALKING);
        Assert::IsTrue(e.stateTimer >= CAT_WALK_DURATION_MIN);
        Assert::IsTrue(e.stateTimer <  CAT_WALK_DURATION_MAX);
        Assert::IsTrue(std::fabs(e.vx) >= CAT_WALK_SPEED_MIN);
        Assert::IsTrue(std::fabs(e.vx) <  CAT_WALK_SPEED_MAX);
        const double margin = e.size + 8.0;
        Assert::IsTrue(e.x >= margin);
        Assert::IsTrue(e.x <= sim.monitorWidth - margin);
        Assert::IsTrue(e.y == Near(sim.windowHeight - CAT_BODY_HEIGHT - CAT_LEG_LENGTH));
        Assert::IsTrue(e.size == Near(CAT_BODY_RADIUS));
        Assert::IsTrue(e.lifetime < 0.0);
        Assert::IsTrue(e.nameIndex < sizeof(CAT_NAME_POOL) / sizeof(CAT_NAME_POOL[0]));
        Assert::IsTrue(e.coatVariantIndex < CAT_COAT_VARIANT_COUNT);
    }
}

TEST_METHOD(CatPRNGDrawOrderMatchesASideStream) {
    // count, then per-cat: x, speed, dir-coin, seed, stateTimer, nameIndex, coatVariantIndex
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);

    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);

    const double countDraw = prng_uniform(side, CAT_COUNT_MIN, CAT_COUNT_MAX + 1);
    int expectedCount = static_cast<int>(std::floor(countDraw));
    if (expectedCount < CAT_COUNT_MIN) expectedCount = CAT_COUNT_MIN;
    if (expectedCount > CAT_COUNT_MAX) expectedCount = CAT_COUNT_MAX;
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == expectedCount);

    int seen = 0;
    for (const Entity& e : sim.entities) {
        if (e.kind != EntityKind::Cat) continue;
        const double margin = CAT_BODY_RADIUS + 8.0;
        const double expectedX = prng_uniform(side, margin, 1920.0 - margin);
        const double expectedSpeed = prng_uniform(side, CAT_WALK_SPEED_MIN, CAT_WALK_SPEED_MAX);
        const double dirCoin = prng_uniform(side, 0.0, 1.0);
        const double expectedDir = (dirCoin < 0.5) ? -1.0 : 1.0;
        const uint32_t expectedSeed = prng_next_u32(side);
        const double expectedTimer = prng_uniform(side, CAT_WALK_DURATION_MIN, CAT_WALK_DURATION_MAX);
        const uint8_t expectedNameIndex = static_cast<uint8_t>(prng_index(side,
            static_cast<uint32_t>(sizeof(CAT_NAME_POOL) / sizeof(CAT_NAME_POOL[0]))));
        const uint8_t expectedCoatVariantIndex = static_cast<uint8_t>(prng_index(side,
            static_cast<uint32_t>(CAT_COAT_VARIANT_COUNT)));

        Assert::IsTrue(e.x == Near(expectedX));
        Assert::IsTrue(e.vx == Near(expectedSpeed * expectedDir));
        Assert::IsTrue(e.seed == expectedSeed);
        Assert::IsTrue(e.stateTimer == Near(expectedTimer));
        Assert::IsTrue(e.nameIndex == expectedNameIndex);
        Assert::IsTrue(e.coatVariantIndex == expectedCoatVariantIndex);
        ++seen;
    }
    Assert::IsTrue(seen == expectedCount);
}

TEST_METHOD(SimSetCritterNoneClearsAmbientCats) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) >= CAT_COUNT_MIN);

    sim_set_critter(sim, CritterKind::None);
    Assert::IsTrue(sim.currentCritter == CritterKind::None);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Bunny) == 0);
}

TEST_METHOD(SwitchingBetweenCritterSpeciesReplacesThePreviousSpecies) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) >= CAT_COUNT_MIN);
    Assert::IsTrue(count_kind(sim, EntityKind::Sheep) == 0);

    sim_set_critter(sim, CritterKind::Sheep);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Sheep) >= SHEEP_COUNT_MIN);

    sim_set_critter(sim, CritterKind::Cat);
    Assert::IsTrue(count_kind(sim, EntityKind::Sheep) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) >= CAT_COUNT_MIN);
}

TEST_METHOD(SimSetSceneGatesActiveCatToGrass) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);
    const int catsGrass = count_kind(sim, EntityKind::Cat);
    Assert::IsTrue(catsGrass >= CAT_COUNT_MIN);

    sim_set_scene(sim, Scene::Desert);
    Assert::IsTrue(sim.currentCritter == CritterKind::Cat);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 0);

    sim_set_scene(sim, Scene::Winter);
    Assert::IsTrue(sim.currentCritter == CritterKind::Cat);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 0);

    sim_set_scene(sim, Scene::Grass);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == catsGrass);
}

TEST_METHOD(ClickWithinCATPOUNCERADIUSPouncesTowardTheClick) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);
    keep_first_cat_only(sim);

    Entity& cat = sim.entities.front();
    cat.x = 500.0;
    cat.vx = -CAT_WALK_SPEED_MIN;
    cat.age = 5.0;

    sim_apply_click(sim, click_event(cat.x + 16.0, sim.windowHeight - 20.0));

    const Entity& after = sim.entities.front();
    Assert::IsTrue(after.state == CAT_STATE_POUNCING);
    Assert::IsTrue(after.stateTimer == Near(CAT_POUNCE_DURATION));
    Assert::IsTrue(after.age == Near(0.0));
    Assert::IsTrue(after.vx == Near(CAT_POUNCE_SPEED));
}

TEST_METHOD(ClickOutsideCATPOUNCERADIUSLeavesCatAlone) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);
    keep_first_cat_only(sim);

    Entity& cat = sim.entities.front();
    cat.x = 500.0;
    cat.vx = -CAT_WALK_SPEED_MIN;
    const uint8_t stateBefore = cat.state;
    const double vxBefore = cat.vx;

    sim_apply_click(sim, click_event(cat.x + CAT_POUNCE_RADIUS + 5.0, sim.windowHeight - 20.0));

    const Entity& after = sim.entities.front();
    Assert::IsTrue(after.state == stateBefore);
    Assert::IsTrue(after.vx == Near(vxBefore));
}

TEST_METHOD(CatsDoNotGreetOtherCats) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);
    keep_first_cat_only(sim);

    Entity first = sim.entities.front();
    first.x = 400.0;
    first.vx = CAT_WALK_SPEED_MIN;
    first.state = CAT_STATE_WALKING;
    first.stateTimer = 10.0;
    first.age = SHEEP_GREET_MIN_AGE + 1.0;
    Entity second = first;
    second.x = first.x + 20.0;
    second.vx = -CAT_WALK_SPEED_MIN;
    sim.entities.clear();
    sim.entities.push_back(first);
    sim.entities.push_back(second);

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 2);
    for (const Entity& e : sim.entities) {
        if (e.kind == EntityKind::Cat) Assert::IsTrue(e.state != SHEEP_STATE_GREETING);
    }
}

TEST_METHOD(CatsDoNotGreetSheep) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Cat);
    keep_first_cat_only(sim);

    Entity cat = sim.entities.front();
    cat.x = 400.0;
    cat.vx = CAT_WALK_SPEED_MIN;
    cat.state = CAT_STATE_WALKING;
    cat.stateTimer = 10.0;
    cat.age = SHEEP_GREET_MIN_AGE + 1.0;

    Entity sheep{};
    sheep.kind = EntityKind::Sheep;
    sheep.size = SHEEP_BODY_RADIUS;
    sheep.x = cat.x + 20.0;
    sheep.y = sim.windowHeight - SHEEP_BODY_HEIGHT - SHEEP_LEG_LENGTH;
    sheep.vx = -SHEEP_WALK_SPEED_MIN;
    sheep.age = SHEEP_GREET_MIN_AGE + 1.0;
    sheep.lifetime = -1.0;
    sheep.state = SHEEP_STATE_WALKING;
    sheep.stateTimer = 10.0;

    sim.entities.clear();
    sim.entities.push_back(cat);
    sim.entities.push_back(sheep);

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 1);
    Assert::IsTrue(count_kind(sim, EntityKind::Sheep) == 1);
    for (const Entity& e : sim.entities) Assert::IsTrue(e.state != SHEEP_STATE_GREETING);
}
};
}
