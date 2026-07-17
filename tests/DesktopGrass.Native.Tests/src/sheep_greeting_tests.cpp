// sheep_greeting_tests.cpp
//
// §16 sheep proximity-greeting tests. Mirrors Win2D SheepGreetingTests.cs.

#include "TestHelpers.h"
#include "Sim.h"

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <vector>

using namespace desktopgrass;

namespace {

constexpr double Monitor1920 = 1920.0;
constexpr double EligibleAge = 2.0;
constexpr double LongTimer = 10.0;

Sim build_sheep_sim() {
    Sim sim = sim_init(CANONICAL_TEST_SEED, Monitor1920, DEFAULT_DENSITY);
    sim_set_critter(sim, CritterKind::Sheep);
    return sim;
}

std::vector<std::size_t> sheep_indices(const Sim& sim) {
    std::vector<std::size_t> indices;
    for (std::size_t i = 0; i < sim.entities.size(); ++i) {
        if (sim.entities[i].kind == EntityKind::Sheep) indices.push_back(i);
    }
    return indices;
}

void set_sheep(Sim& sim, std::size_t index, double x, double vx,
               uint8_t state = SHEEP_STATE_WALKING,
               double age = EligibleAge,
               double stateTimer = LongTimer) {
    Entity& e = sim.entities[index];
    e.x = x;
    e.vx = vx;
    e.state = state;
    e.age = age;
    e.stateTimer = stateTimer;
}

std::vector<std::size_t> prepare_two_sheep(Sim& sim, double gap = 40.0,
                                           double ageA = EligibleAge,
                                           double ageB = EligibleAge) {
    std::vector<std::size_t> indices = sheep_indices(sim);
    Assert::IsTrue(indices.size() >= 2);

    set_sheep(sim, indices[0], 500.0, -20.0, SHEEP_STATE_WALKING, ageA);
    set_sheep(sim, indices[1], 500.0 + gap, 18.0, SHEEP_STATE_WALKING, ageB);
    for (std::size_t n = 2; n < indices.size(); ++n) {
        set_sheep(sim, indices[n], 1000.0 + 150.0 * static_cast<double>(n), 16.0);
    }
    return indices;
}

int advance_side_past_sheep_generation(Prng& side) {
    const double countDraw = prng_uniform(side, SHEEP_COUNT_MIN, SHEEP_COUNT_MAX + 1);
    int expectedCount = static_cast<int>(std::floor(countDraw));
    if (expectedCount < SHEEP_COUNT_MIN) expectedCount = SHEEP_COUNT_MIN;
    if (expectedCount > SHEEP_COUNT_MAX) expectedCount = SHEEP_COUNT_MAX;

    for (int i = 0; i < expectedCount; ++i) {
        const double margin = SHEEP_BODY_RADIUS + 8.0;
        (void)prng_uniform(side, margin, Monitor1920 - margin);
        (void)prng_uniform(side, SHEEP_WALK_SPEED_MIN, SHEEP_WALK_SPEED_MAX);
        (void)prng_uniform(side, 0.0, 1.0);
        (void)prng_next_u32(side);
        (void)prng_uniform(side, SHEEP_WALK_DURATION_MIN, SHEEP_WALK_DURATION_MAX);
        (void)prng_index(side, static_cast<uint32_t>(sizeof(SHEEP_NAME_POOL) / sizeof(SHEEP_NAME_POOL[0])));
    }
    return expectedCount;
}

int count_sheep_in_state(const Sim& sim, uint8_t state) {
    return static_cast<int>(std::count_if(sim.entities.begin(), sim.entities.end(),
        [state](const Entity& e) {
            return e.kind == EntityKind::Sheep && e.state == state;
        }));
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(SheepGreetingTests)
{
public:
TEST_METHOD(SheepGreetingConstantsArePinnedToSpecValues) {
    Assert::IsTrue(SHEEP_STATE_GREETING       == 5);
    Assert::IsTrue(SHEEP_GREET_RADIUS         == Near(50.0));
    Assert::IsTrue(SHEEP_GREET_DURATION_MIN   == Near(1.6));
    Assert::IsTrue(SHEEP_GREET_DURATION_MAX   == Near(2.8));
    Assert::IsTrue(SHEEP_GREET_MIN_AGE        == Near(1.5));
    Assert::IsTrue(SHEEP_GREET_HEAD_BOB_FREQ  == Near(4.5));
    Assert::IsTrue(SHEEP_GREET_HEAD_BOB_AMP   == Near(0.7));
}

TEST_METHOD(SheepCuriousConstantsArePinnedToSpecValues) {
    Assert::IsTrue(SHEEP_CURIOUS_RADIUS        == Near(80.0));
    Assert::IsTrue(SHEEP_CURIOUS_HEAD_TURN_MAX == Near(0.55));
}


TEST_METHOD(EligibleNearbySheepEnterGreetingFacingEachOther) {
    Sim sim = build_sheep_sim();
    const std::vector<std::size_t> indices = prepare_two_sheep(sim);

    sim_tick_entities(sim, 0.016);

    const Entity& a = sim.entities[indices[0]];
    const Entity& b = sim.entities[indices[1]];
    Assert::IsTrue(a.state == SHEEP_STATE_GREETING);
    Assert::IsTrue(b.state == SHEEP_STATE_GREETING);
    Assert::IsTrue(a.stateTimer >= SHEEP_GREET_DURATION_MIN);
    Assert::IsTrue(a.stateTimer <= SHEEP_GREET_DURATION_MAX);
    Assert::IsTrue(a.stateTimer == Near(b.stateTimer));
    Assert::IsTrue(a.vx > 0.0);
    Assert::IsTrue(b.vx < 0.0);
}

TEST_METHOD(FarApartEligibleSheepDoNotGreet) {
    Sim sim = build_sheep_sim();
    const std::vector<std::size_t> indices = prepare_two_sheep(sim, 200.0);

    for (int i = 0; i < 3; ++i) sim_tick_entities(sim, 0.016);

    Assert::IsTrue(sim.entities[indices[0]].state == SHEEP_STATE_WALKING);
    Assert::IsTrue(sim.entities[indices[1]].state == SHEEP_STATE_WALKING);
}

TEST_METHOD(SheepUnderGreetingMinimumAgeDoNotGreet) {
    Sim sim = build_sheep_sim();
    const std::vector<std::size_t> indices = prepare_two_sheep(sim, 40.0, 0.5, EligibleAge);

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(sim.entities[indices[0]].state == SHEEP_STATE_WALKING);
    Assert::IsTrue(sim.entities[indices[1]].state == SHEEP_STATE_WALKING);
}

TEST_METHOD(SleepingHoppingAndGreetingSheepAreNotGreetingEligible) {
    const uint8_t blockedStates[] = {
        SHEEP_STATE_SLEEPING,
        SHEEP_STATE_HOPPING,
        SHEEP_STATE_GREETING,
    };

    for (uint8_t blockedState : blockedStates) {
        Sim sim = build_sheep_sim();
        const std::vector<std::size_t> indices = prepare_two_sheep(sim);
        set_sheep(sim, indices[0], 500.0, -20.0, blockedState, EligibleAge);

        sim_tick_entities(sim, 0.016);

        Assert::IsTrue(sim.entities[indices[0]].state == blockedState);
        Assert::IsTrue(sim.entities[indices[1]].state == SHEEP_STATE_WALKING);
    }
}

TEST_METHOD(GreetingExpiryReturnsSheepToWalkingWithVxFlipped) {
    Sim sim = build_sheep_sim();
    const std::vector<std::size_t> indices = prepare_two_sheep(sim);

    sim_tick_entities(sim, 0.016);
    const double duration = sim.entities[indices[0]].stateTimer;
    const double aGreetingVx = sim.entities[indices[0]].vx;
    const double bGreetingVx = sim.entities[indices[1]].vx;

    sim_tick_entities(sim, duration + 0.01);

    Assert::IsTrue(sim.entities[indices[0]].state == SHEEP_STATE_WALKING);
    Assert::IsTrue(sim.entities[indices[1]].state == SHEEP_STATE_WALKING);
    Assert::IsTrue(sim.entities[indices[0]].vx == Near(-aGreetingVx));
    Assert::IsTrue(sim.entities[indices[1]].vx == Near(-bGreetingVx));
}

TEST_METHOD(GreetingTriggerConsumesOnePRNGDrawPerPair) {
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ CRITTER_PRNG_SALT);

    Sim sim = build_sheep_sim();
    const int expectedCount = advance_side_past_sheep_generation(side);
    Assert::IsTrue(static_cast<int>(sheep_indices(sim).size()) == expectedCount);
    const std::vector<std::size_t> indices = prepare_two_sheep(sim);

    const double expectedDuration = prng_uniform(side,
                                                 SHEEP_GREET_DURATION_MIN,
                                                 SHEEP_GREET_DURATION_MAX);
    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(sim.entities[indices[0]].stateTimer == Near(expectedDuration));
    Assert::IsTrue(sim.entities[indices[1]].stateTimer == Near(expectedDuration));
}

TEST_METHOD(SingleSheepCannotEnterGreeting) {
    Sim sim = build_sheep_sim();
    sim.currentScene = Scene::Desert;
    Assert::IsTrue(sim.entities.size() >= 1);
    sim.entities.erase(sim.entities.begin() + 1, sim.entities.end());
    set_sheep(sim, 0, 500.0, 20.0);

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(sim.entities.size() == 1);
    Assert::IsTrue(sim.entities[0].state == SHEEP_STATE_WALKING);
}

TEST_METHOD(ThreeSheepClusterGreetsOnlyTheFirstEncounteredPair) {
    Sim sim = build_sheep_sim();
    std::vector<std::size_t> indices = sheep_indices(sim);
    Assert::IsTrue(indices.size() >= 2);
    if (indices.size() < 3) {
        sim.entities.push_back(sim.entities[indices[1]]);
        indices = sheep_indices(sim);
    }
    Assert::IsTrue(indices.size() >= 3);

    set_sheep(sim, indices[0], 500.0, -20.0);
    set_sheep(sim, indices[1], 540.0, 18.0);
    set_sheep(sim, indices[2], 580.0, 16.0);

    sim_tick_entities(sim, 0.016);

    Assert::IsTrue(sim.entities[indices[0]].state == SHEEP_STATE_GREETING);
    Assert::IsTrue(sim.entities[indices[1]].state == SHEEP_STATE_GREETING);
    Assert::IsTrue(sim.entities[indices[2]].state == SHEEP_STATE_WALKING);
    Assert::IsTrue(count_sheep_in_state(sim, SHEEP_STATE_GREETING) == 2);
}
};
}
