// entity_skeleton_tests.cpp
//
// Entity subsystem skeleton tests (architecture.md §13.2).
//
// Coverage:
//   * EntityKind discriminants match the spec ({None=0, Tumbleweed=1,
//     Snowflake=2}).
//   * MAX_ENTITIES_PER_MONITOR is the locked cap (= 64).
//   * sim_init defaults sim.entities to empty, capacity >= cap.
//   * sim_set_scene clears entities (currently a no-op since the Grass
//     scene generates none; §14/§15 add per-scene generators).
//   * sim_tick_entities is safe on empty (no exceptions, no growth).
//   * Tick on empty entities does not perturb other sim state (blades
//     untouched, ambient PRNG untouched).

#include "TestHelpers.h"
#include "Sim.h"

using namespace desktopgrass;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(EntitySkeletonTests)
{
public:
TEST_METHOD(EntityKindHasSpecLockedDiscriminants) {
    Assert::IsTrue(static_cast<int>(EntityKind::None)       == 0);
    Assert::IsTrue(static_cast<int>(EntityKind::Tumbleweed) == 1);
    Assert::IsTrue(static_cast<int>(EntityKind::Snowflake)  == 2);
    Assert::IsTrue(static_cast<int>(EntityKind::Sheep)      == 3);
    Assert::IsTrue(static_cast<int>(EntityKind::Cat)        == 4);
    Assert::IsTrue(static_cast<int>(EntityKind::Bunny)      == 6);
    Assert::IsTrue(static_cast<int>(EntityKind::Butterfly)  == 7);
    Assert::IsTrue(static_cast<int>(EntityKind::Firefly)    == 8);
    Assert::IsTrue(static_cast<int>(EntityKind::Bird)       == 9);
    Assert::IsTrue(MAX_ENTITIES_PER_MONITOR == 64);
}

TEST_METHOD(SimInitReservesEntitiesCapacity) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    Assert::IsTrue(sim.entities.empty());
    Assert::IsTrue(sim.entities.capacity() >= static_cast<std::size_t>(MAX_ENTITIES_PER_MONITOR));
    Assert::IsTrue(sim.entitySeed == CANONICAL_TEST_SEED);
}

TEST_METHOD(SimSetSceneClearsEntities) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);
    // Push a fake entity directly to verify scene-transition removal runs.
    Entity fake{};
    fake.kind = EntityKind::Tumbleweed;
    fake.x = 100.0;
    sim.entities.push_back(fake);
    Assert::IsTrue(sim.entities.size() == 1);

    sim_set_scene(sim, Scene::Winter);
    Assert::IsTrue(sim.entities.empty());
    Assert::IsTrue(sim.entities.capacity() >= static_cast<std::size_t>(MAX_ENTITIES_PER_MONITOR));
}

TEST_METHOD(SimTickEntitiesIsANoOpOnEmptyOutsideGrass) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);
    sim.currentScene = Scene::Desert;
    const auto bladesBefore = sim.blades.size();
    const auto prngBefore   = sim.ambientPrng.state;

    sim_tick_entities(sim, 0.016);
    sim_tick_entities(sim, 0.5);

    Assert::IsTrue(sim.entities.empty());
    Assert::IsTrue(sim.blades.size() == bladesBefore);
    Assert::IsTrue(sim.ambientPrng.state == prngBefore);
}

TEST_METHOD(SimTickEntitiesAdvancesAPopulatedEntity) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);
    sim.currentScene = Scene::Desert;
    Entity e{};
    e.kind          = EntityKind::Tumbleweed;
    e.x             = 100.0;
    e.y             = 50.0;
    e.vx            = 50.0;    // DIP/sec
    e.vy            = 0.0;
    e.size          = 10.0;
    e.rotation      = 0.5;
    e.rotationSpeed = 1.0;     // rad/sec
    e.age           = 0.0;
    e.lifetime      = -1.0;    // infinite
    e.seed          = 0xDEADBEEF;
    sim.entities.push_back(e);

    const double dt = 0.5;
    sim_tick_entities(sim, dt);

    Assert::IsTrue(sim.entities.size() == 1);
    const Entity& after = sim.entities[0];
    Assert::IsTrue(after.x == Near(100.0 + 50.0 * dt));
    Assert::IsTrue(after.y == Near(50.0));
    Assert::IsTrue(after.rotation == Near(0.5 + 1.0 * dt));
    Assert::IsTrue(after.age == Near(0.0 + dt));
    Assert::IsTrue(after.kind == EntityKind::Tumbleweed);
}

TEST_METHOD(SimTickCallsSimTickEntitiesWiringCheck) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);
    sim.currentScene = Scene::Desert;
    Entity e{};
    e.kind = EntityKind::Snowflake;
    e.x = 0.0; e.y = 0.0;
    e.vx = 10.0; e.vy = 20.0;
    e.size = 2.0;
    e.age = 0.0; e.lifetime = 100.0;
    sim.entities.push_back(e);

    sim_tick(sim, 0.1, nullptr, 0);
    Assert::IsTrue(sim.entities.size() == 1);
    Assert::IsTrue(sim.entities[0].x == Near(1.0));   // 10 * 0.1
    Assert::IsTrue(sim.entities[0].y == Near(2.0));   // 20 * 0.1
}
};
}
