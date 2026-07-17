// autumn_tests.cpp
//
// Autumn scene tests (architecture.md §16.5).

#include "TestHelpers.h"
#include "Persistence.h"
#include "Sim.h"
#include "snapshot_data.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <filesystem>

using namespace desktopgrass;

namespace {

constexpr double kMonitor1920 = 1920.0;
constexpr double kEpsilon = 1e-9;
constexpr double kTwoPi = 6.28318530717958647692;

Sim make_sim(uint64_t seed = CANONICAL_TEST_SEED,
             double width = kMonitor1920,
             double density = DEFAULT_DENSITY) {
    return sim_init(seed, width, density);
}

Sim make_autumn_sim(uint64_t seed = CANONICAL_TEST_SEED,
                    double width = kMonitor1920,
                    double density = DEFAULT_DENSITY) {
    Sim sim = make_sim(seed, width, density);
    sim_set_scene(sim, Scene::Autumn);
    return sim;
}

int count_kind(const Sim& sim, EntityKind kind) {
    return static_cast<int>(std::count_if(sim.entities.begin(), sim.entities.end(),
        [kind](const Entity& e) { return e.kind == kind; }));
}

int count_maples(const Sim& sim) {
    return static_cast<int>(std::count_if(sim.blades.begin(), sim.blades.end(),
        [](const Blade& b) { return b.isMaple; }));
}

int count_new_leaf_spawns(Sim& sim, double seconds, double dt = 0.05) {
    int count = 0;
    const int steps = static_cast<int>(std::ceil(seconds / dt));
    for (int i = 0; i < steps; ++i) {
        sim_tick(sim, dt, nullptr, 0);
        for (const Entity& e : sim.entities) {
            if (e.kind == EntityKind::Leaf && e.age == Near(0.0).margin(kEpsilon)) {
                ++count;
            }
        }
    }
    return count;
}

const Entity& spawn_next_leaf(Sim& sim) {
    const double dt = std::max(0.0, sim.nextLeafSpawnTime - sim.globalTime);
    sim_tick(sim, dt, nullptr, 0);
    auto it = std::find_if(sim.entities.rbegin(), sim.entities.rend(),
        [](const Entity& e) { return e.kind == EntityKind::Leaf && e.age == Near(0.0).margin(kEpsilon); });
    Assert::IsTrue(it != sim.entities.rend());
    return *it;
}

const Blade* first_maple(const Sim& sim) {
    auto it = std::find_if(sim.blades.begin(), sim.blades.end(),
        [](const Blade& b) { return b.isMaple; });
    return it == sim.blades.end() ? nullptr : &*it;
}

Sim make_autumn_sim_with_maple(uint64_t* outSeed = nullptr) {
    for (uint64_t offset = 0; offset < 512; ++offset) {
        const uint64_t seed = CANONICAL_TEST_SEED + offset;
        Sim sim = make_autumn_sim(seed);
        if (count_maples(sim) > 0) {
            if (outSeed) *outSeed = seed;
            return sim;
        }
    }
    Assert::Fail(L"Unable to find deterministic seed with a maple", LINE_INFO());
    return make_autumn_sim();
}

// Find an Autumn sim that contains at least one leafy (non-bare) maple, and
// return a pointer to it. The returned pointer is valid for the lifetime of
// the returned-by-out sim.
inline const Blade* first_leafy_maple(const Sim& sim) {
    auto it = std::find_if(sim.blades.begin(), sim.blades.end(),
        [](const Blade& b) { return b.isMaple && !b.mapleIsBare; });
    return it == sim.blades.end() ? nullptr : &*it;
}

Sim make_autumn_sim_with_leafy_maple() {
    for (uint64_t offset = 0; offset < 2048; ++offset) {
        Sim sim = make_autumn_sim(CANONICAL_TEST_SEED + offset);
        if (first_leafy_maple(sim) != nullptr) return sim;
    }
    Assert::Fail(L"Unable to find deterministic seed with a leafy maple", LINE_INFO());
    return make_autumn_sim();
}

std::filesystem::path autumn_state_path() {
    std::filesystem::path dir = std::filesystem::current_path()
        / ".copilot-scratch"
        / "native-autumn-tests";
    std::error_code ec;
    std::filesystem::remove_all(dir, ec);
    std::filesystem::create_directories(dir);
    return dir / "state.json";
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(AutumnTests)
{
public:
TEST_METHOD(AutumnSceneCountBumpsToFive) {
    Assert::IsTrue(SCENE_COUNT == 5);
}

TEST_METHOD(AutumnSceneEnumValueIsPinned) {
    Assert::IsTrue(static_cast<int>(Scene::Autumn) == 3);
}

TEST_METHOD(AutumnPaletteIsPinnedInScenePalettes) {
    for (int i = 0; i < PALETTE_SIZE; ++i) {
        Assert::IsTrue(SCENE_PALETTES[static_cast<int>(Scene::Autumn)][i] == AUTUMN_PALETTE[i]);
    }
}

TEST_METHOD(AutumnDoesNotChangeDefaultScene) {
    Assert::IsTrue(SCENE_DEFAULT == Scene::Grass);
}

TEST_METHOD(LeafConstantsMatchAutumnSpec) {
    Assert::IsTrue(LEAF_SPAWN_RATE_PER_SEC_1920DIP == Near(1.4));
    Assert::IsTrue(LEAF_FALL_SPEED_MIN == Near(14.0));
    Assert::IsTrue(LEAF_FALL_SPEED_MAX == Near(26.0));
    Assert::IsTrue(LEAF_HORIZONTAL_DRIFT_AMP == Near(32.0));
    Assert::IsTrue(LEAF_HORIZONTAL_DRIFT_FREQ == Near(1.4));
    Assert::IsTrue(LEAF_ROTATION_SPEED_MIN == Near(0.8));
    Assert::IsTrue(LEAF_ROTATION_SPEED_MAX == Near(2.4));
    Assert::IsTrue(LEAF_SIZE_MIN == Near(4.0));
    Assert::IsTrue(LEAF_SIZE_MAX == Near(7.0));
    Assert::IsTrue(LEAF_SPAWN_Y_OFFSET == Near(-10.0));
    Assert::IsTrue(LEAF_COLOR_COUNT == 6);
    constexpr uint32_t expected[LEAF_COLOR_COUNT] = {
        0xFFD96B0Cu, 0xFFB54D1Eu, 0xFFE89A3Cu,
        0xFFC23E12u, 0xFFE6C849u, 0xFF8C2E0Fu,
    };
    for (int i = 0; i < LEAF_COLOR_COUNT; ++i) {
        Assert::IsTrue(LEAF_COLORS[i] == expected[i]);
    }
    Assert::IsTrue(LEAF_PRNG_SALT == 0x1EA1DEC1D1EA1D05ull);
}

TEST_METHOD(AutumnLeafSpawnRateIsGatedAndNearMean) {
    Sim autumn = make_autumn_sim();
    const int count = count_new_leaf_spawns(autumn, 100.0);
    Assert::IsTrue(count >= 112);
    Assert::IsTrue(count <= 168);
}

TEST_METHOD(OnlyAutumnSpawnsLeaves) {
    for (Scene scene : { Scene::Grass, Scene::Desert, Scene::Winter }) {
        Sim sim = make_sim();
        sim_set_scene(sim, scene);
        count_new_leaf_spawns(sim, 30.0);
        Assert::IsTrue(count_kind(sim, EntityKind::Leaf) == 0);
    }
}

TEST_METHOD(LeafFallSpeedStaysWithinPinnedRange) {
    Sim sim = make_autumn_sim();
    for (int i = 0; i < 32; ++i) {
        const Entity& e = spawn_next_leaf(sim);
        Assert::IsTrue(e.vy >= LEAF_FALL_SPEED_MIN);
        Assert::IsTrue(e.vy <= LEAF_FALL_SPEED_MAX);
        Assert::IsTrue(e.baseSpeed == Near(e.vy));
    }
}

TEST_METHOD(LeafSizeStaysWithinPinnedRange) {
    Sim sim = make_autumn_sim();
    for (int i = 0; i < 32; ++i) {
        const Entity& e = spawn_next_leaf(sim);
        Assert::IsTrue(e.size >= LEAF_SIZE_MIN);
        Assert::IsTrue(e.size <= LEAF_SIZE_MAX);
    }
}

TEST_METHOD(LeafColorVariantStaysWithinPinnedRange) {
    Sim sim = make_autumn_sim();
    for (int i = 0; i < 32; ++i) {
        const Entity& e = spawn_next_leaf(sim);
        Assert::IsTrue(e.colorVariant < LEAF_COLOR_COUNT);
    }
}

TEST_METHOD(LeafPRNGDrawOrderMatchesSideStream) {
    Sim sim = make_autumn_sim();
    Prng side;
    prng_init(side, CANONICAL_TEST_SEED ^ LEAF_PRNG_SALT);
    const double lambda = LEAF_SPAWN_RATE_PER_SEC_1920DIP * sim.monitorWidth / 1920.0;
    double expectedNext = 0.0;

    for (int i = 0; i < 8; ++i) {
        const Entity& e = spawn_next_leaf(sim);
        const double xFrac = prng_uniform(side, 0.0, 1.0);
        const double expectedSpawnX = xFrac * sim.monitorWidth;
        const double expectedFallSpeed = prng_uniform(side, LEAF_FALL_SPEED_MIN, LEAF_FALL_SPEED_MAX);
        const double expectedPhase = prng_uniform(side, 0.0, kTwoPi);
        const double rotationMag = prng_uniform(side, LEAF_ROTATION_SPEED_MIN, LEAF_ROTATION_SPEED_MAX);
        const double rotationSign = (prng_next_u64(side) & 1ull) != 0ull ? 1.0 : -1.0;
        const double expectedRotation = prng_uniform(side, 0.0, kTwoPi);
        const double expectedSize = prng_uniform(side, LEAF_SIZE_MIN, LEAF_SIZE_MAX);
        const uint8_t expectedColor = static_cast<uint8_t>(prng_index(side, LEAF_COLOR_COUNT));
        expectedNext += prng_exponential(side, lambda);

        Assert::IsTrue(e.x0 == Near(expectedSpawnX).margin(kEpsilon));
        Assert::IsTrue(e.x == Near(expectedSpawnX + LEAF_HORIZONTAL_DRIFT_AMP * std::sin(expectedPhase)).margin(kEpsilon));
        Assert::IsTrue(e.vy == Near(expectedFallSpeed).margin(kEpsilon));
        Assert::IsTrue(e.phaseX == Near(expectedPhase).margin(kEpsilon));
        Assert::IsTrue(e.rotationSpeed == Near(rotationMag * rotationSign).margin(kEpsilon));
        Assert::IsTrue(e.rotation == Near(expectedRotation).margin(kEpsilon));
        Assert::IsTrue(e.size == Near(expectedSize).margin(kEpsilon));
        Assert::IsTrue(e.colorVariant == expectedColor);
        Assert::IsTrue(sim.nextLeafSpawnTime == Near(expectedNext).margin(kEpsilon));
    }
}

TEST_METHOD(LeafDespawnsWhenPastGround) {
    Sim sim = make_autumn_sim();
    Entity e{};
    e.kind = EntityKind::Leaf;
    e.y = sim.windowHeight + 0.1;
    e.lifetime = -1.0;
    sim.entities.push_back(e);
    sim.nextLeafSpawnTime = 1.0e9;
    sim_tick_entities(sim, 0.0);
    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) == 0);
}

TEST_METHOD(LeafIgnoresClickCutInteraction) {
    Sim sim = make_autumn_sim();
    Entity e{};
    e.kind = EntityKind::Leaf;
    e.x = 200.0;
    e.y = sim.windowHeight - 5.0;
    e.size = 5.0;
    e.lifetime = -1.0;
    sim.entities.push_back(e);

    InputEvent click{};
    click.type = EventType::Click;
    click.x = e.x;
    click.y = e.y;
    click.time = sim.globalTime;
    sim_apply_click(sim, click);

    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) == 1);
}

TEST_METHOD(LeafEntityKindValueIsPinned) {
    Assert::IsTrue(static_cast<int>(EntityKind::Leaf) == 11);
}

TEST_METHOD(MapleConstantsMatchAutumnSpec) {
    Assert::IsTrue(MAPLE_PROBABILITY == Near(0.0070));
    Assert::IsTrue(MAPLE_HEIGHT_MIN == Near(50.0));
    Assert::IsTrue(MAPLE_HEIGHT_MAX == Near(85.0));
    Assert::IsTrue(MAPLE_TRUNK_WIDTH_MIN == Near(6.0));
    Assert::IsTrue(MAPLE_TRUNK_WIDTH_MAX == Near(10.0));
    Assert::IsTrue(MAPLE_CANOPY_RADIUS_MIN == Near(14.0));
    Assert::IsTrue(MAPLE_CANOPY_RADIUS_MAX == Near(24.0));
    Assert::IsTrue(MAPLE_TRUNK_COLOR == 0xFF4A2C18u);
    Assert::IsTrue(MAPLE_TRUNK_DARK == 0xFF2F1B0Eu);
    Assert::IsTrue(MAPLE_CANOPY_COLOR_COUNT == 4);
    constexpr uint32_t expected[MAPLE_CANOPY_COLOR_COUNT] = {
        0xFFD96B0Cu, 0xFFE89A3Cu, 0xFFC23E12u, 0xFFE6C849u,
    };
    for (int i = 0; i < MAPLE_CANOPY_COLOR_COUNT; ++i) {
        Assert::IsTrue(MAPLE_CANOPY_COLORS[i] == expected[i]);
    }
    Assert::IsTrue(MAPLE_BARE_FRACTION == Near(0.20));
    Assert::IsTrue(MAPLE_PRNG_SALT == 0xC1AA51EC1AA51Eull);
}

TEST_METHOD(MaplesGenerateOnlyInAutumn) {
    for (Scene scene : { Scene::Grass, Scene::Desert, Scene::Winter }) {
        Sim sim = make_sim();
        sim_set_scene(sim, scene);
        Assert::IsTrue(count_maples(sim) == 0);
    }
    Assert::IsTrue(count_maples(make_autumn_sim_with_maple()) > 0);
}

TEST_METHOD(MaplePromotionProbabilityIsNearSpec) {
    int totalSlots = 0;
    int totalMaples = 0;
    for (uint64_t seed = CANONICAL_TEST_SEED; seed < CANONICAL_TEST_SEED + 200; ++seed) {
        Sim sim = make_autumn_sim(seed);
        totalSlots += static_cast<int>(sim.blades.size());
        totalMaples += count_maples(sim);
    }
    const double fraction = static_cast<double>(totalMaples) / static_cast<double>(totalSlots);
    Assert::IsTrue(fraction >= MAPLE_PROBABILITY * 0.75);
    Assert::IsTrue(fraction <= MAPLE_PROBABILITY * 1.25);
}

TEST_METHOD(MapleHeightStaysWithinPinnedRange) {
    Sim sim = make_autumn_sim_with_maple();
    for (const Blade& b : sim.blades) if (b.isMaple) {
        Assert::IsTrue(b.mapleHeight >= MAPLE_HEIGHT_MIN);
        Assert::IsTrue(b.mapleHeight <= MAPLE_HEIGHT_MAX);
    }
}

TEST_METHOD(MapleTrunkWidthStaysWithinPinnedRange) {
    Sim sim = make_autumn_sim_with_maple();
    for (const Blade& b : sim.blades) if (b.isMaple) {
        Assert::IsTrue(b.mapleTrunkWidth >= MAPLE_TRUNK_WIDTH_MIN);
        Assert::IsTrue(b.mapleTrunkWidth <= MAPLE_TRUNK_WIDTH_MAX);
    }
}

TEST_METHOD(MapleCanopyRadiusStaysWithinPinnedRange) {
    Sim sim = make_autumn_sim_with_maple();
    for (const Blade& b : sim.blades) if (b.isMaple) {
        Assert::IsTrue(b.mapleCanopyRadius >= MAPLE_CANOPY_RADIUS_MIN);
        Assert::IsTrue(b.mapleCanopyRadius <= MAPLE_CANOPY_RADIUS_MAX);
    }
}

TEST_METHOD(MapleCanopyColorVariantStaysWithinPinnedRange) {
    Sim sim = make_autumn_sim_with_maple();
    for (const Blade& b : sim.blades) if (b.isMaple) {
        Assert::IsTrue(b.mapleCanopyColorIdx < MAPLE_CANOPY_COLOR_COUNT);
    }
}

TEST_METHOD(MapleBareFractionIsNearSpec) {
    int totalMaples = 0;
    int totalBare = 0;
    for (uint64_t seed = CANONICAL_TEST_SEED; seed < CANONICAL_TEST_SEED + 400; ++seed) {
        Sim sim = make_autumn_sim(seed);
        for (const Blade& b : sim.blades) if (b.isMaple) {
            ++totalMaples;
            if (b.mapleIsBare) ++totalBare;
        }
    }
    Assert::IsTrue(totalMaples > 100);
    const double fraction = static_cast<double>(totalBare) / static_cast<double>(totalMaples);
    Assert::IsTrue(fraction >= MAPLE_BARE_FRACTION - 0.05);
    Assert::IsTrue(fraction <= MAPLE_BARE_FRACTION + 0.05);
}

TEST_METHOD(MaplePRNGDrawOrderMatchesSideStream) {
    uint64_t seed = 0;
    Sim sim = make_autumn_sim_with_maple(&seed);
    Prng side;
    prng_init(side, seed ^ MAPLE_PRNG_SALT);

    for (std::size_t i = 0; i < sim.blades.size(); ++i) {
        const double r = prng_uniform(side, 0.0, 1.0);
        if (r >= MAPLE_PROBABILITY) {
            Assert::IsFalse(sim.blades[i].isMaple);
            continue;
        }

        const double expectedHeight = prng_uniform(side, MAPLE_HEIGHT_MIN, MAPLE_HEIGHT_MAX);
        const double expectedTrunkWidth = prng_uniform(side, MAPLE_TRUNK_WIDTH_MIN, MAPLE_TRUNK_WIDTH_MAX);
        const double expectedCanopyRadius = prng_uniform(side, MAPLE_CANOPY_RADIUS_MIN, MAPLE_CANOPY_RADIUS_MAX);
        const uint8_t expectedColor = static_cast<uint8_t>(prng_index(side, MAPLE_CANOPY_COLOR_COUNT));
        const bool expectedBare = prng_uniform(side, 0.0, 1.0) < MAPLE_BARE_FRACTION;

        const Blade& b = sim.blades[i];
        Assert::IsTrue(b.isMaple);
        Assert::IsTrue(b.mapleHeight == Near(expectedHeight).margin(kEpsilon));
        Assert::IsTrue(b.mapleTrunkWidth == Near(expectedTrunkWidth).margin(kEpsilon));
        Assert::IsTrue(b.mapleCanopyRadius == Near(expectedCanopyRadius).margin(kEpsilon));
        Assert::IsTrue(b.mapleCanopyColorIdx == expectedColor);
        Assert::IsTrue(b.mapleIsBare == expectedBare);
        return;
    }
    Assert::Fail(L"Expected a maple promotion", LINE_INFO());
}

TEST_METHOD(MaplesAreCuttableThroughExistingCutModel) {
    Sim sim = make_autumn_sim_with_maple();
    const Blade* maple = first_maple(sim);
    Assert::IsTrue(maple != nullptr);
    const double clickX = maple->baseX;

    InputEvent click{};
    click.type = EventType::Click;
    click.x = clickX;
    click.y = sim.windowHeight - 1.0;
    click.time = sim.globalTime;
    sim_apply_click(sim, click);
    sim_tick(sim, CUT_DURATION_SEC + 0.01, nullptr, 0);

    const Blade& cutMaple = *std::find_if(sim.blades.begin(), sim.blades.end(),
        [clickX](const Blade& b) { return b.isMaple && b.baseX == Near(clickX); });
    // Cut blades now settle at their per-blade stubble floor, not flat zero.
    Assert::IsTrue(cutMaple.cutFloor > 0.0);
    Assert::IsTrue(cutMaple.cutHeight == Near(cutMaple.cutFloor).margin(kEpsilon));
}

TEST_METHOD(AutumnIsCritterFree) {
    Sim sim = make_autumn_sim();
    Assert::IsTrue(count_kind(sim, EntityKind::Sheep) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Cat) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Bunny) == 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Hedgehog) == 0);
}


TEST_METHOD(AutumnDoesNotSpawnSnowflakes) {
    Sim sim = make_autumn_sim();
    for (int i = 0; i < 500; ++i) sim_tick(sim, 0.05, nullptr, 0);
    Assert::IsTrue(count_kind(sim, EntityKind::Snowflake) == 0);
}

TEST_METHOD(AutumnScenePersistsRoundTrip) {
    const auto path = autumn_state_path();
    persistence::SetStateFilePathForTest(path.wstring());

    persistence::AppState expected;
    expected.scene = Scene::Autumn;
    Assert::IsTrue(persistence::SaveAppState(expected));

    persistence::AppState actual;
    Assert::IsTrue(persistence::LoadAppState(actual));
    Assert::IsTrue(actual.scene == Scene::Autumn);
}

TEST_METHOD(LeafPuffConstantsArePinned) {
    Assert::IsTrue(LEAF_PUFF_COUNT_MIN == 4);
    Assert::IsTrue(LEAF_PUFF_COUNT_MAX == 7);
    Assert::IsTrue(LEAF_PUFF_BURST_SPEED_MIN == Near(18.0));
    Assert::IsTrue(LEAF_PUFF_BURST_SPEED_MAX == Near(42.0));
    Assert::IsTrue(LEAF_PUFF_DRAG == Near(2.2));
    Assert::IsTrue(LEAF_PUFF_COOLDOWN_SEC == Near(1.5));
    Assert::IsTrue(LEAF_PUFF_HOVER_RADIUS_MUL == Near(1.15));
    Assert::IsTrue(LEAF_PUFF_MIN_CUT_HEIGHT == Near(0.5));
    Assert::IsTrue(LEAF_PUFF_START_OFFSET_FRAC == Near(0.4));
}

TEST_METHOD(HoveringALeafyMapleShedsALeafPuff) {
    Sim sim = make_autumn_sim_with_leafy_maple();
    const Blade* maple = first_leafy_maple(sim);
    Assert::IsTrue(maple != nullptr);
    const double cx = maple->baseX;
    const double cy = sim.windowHeight - maple->mapleHeight * maple->cutHeight;

    const int before = count_kind(sim, EntityKind::Leaf);
    InputEvent mv{};
    mv.type = EventType::Move;
    mv.x = cx;
    mv.y = cy;
    mv.time = sim.globalTime;
    sim_apply_move(sim, mv);

    const int puffed = count_kind(sim, EntityKind::Leaf) - before;
    Assert::IsTrue(puffed >= LEAF_PUFF_COUNT_MIN);
    Assert::IsTrue(puffed <= LEAF_PUFF_COUNT_MAX);

    const bool anyBurst = std::any_of(sim.entities.begin(), sim.entities.end(),
        [](const Entity& e) { return e.kind == EntityKind::Leaf && e.vx != 0.0; });
    Assert::IsTrue(anyBurst);
}

TEST_METHOD(LeafPuffRespectsAPerTreeCooldown) {
    Sim sim = make_autumn_sim_with_leafy_maple();
    const Blade* maple = first_leafy_maple(sim);
    const double cx = maple->baseX;
    const double cy = sim.windowHeight - maple->mapleHeight * maple->cutHeight;

    InputEvent mv{};
    mv.type = EventType::Move;
    mv.x = cx;
    mv.y = cy;
    mv.time = sim.globalTime;
    sim_apply_move(sim, mv);
    const int afterFirst = count_kind(sim, EntityKind::Leaf);
    Assert::IsTrue(afterFirst > 0);

    sim_apply_move(sim, mv);
    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) == afterFirst);

    sim.globalTime += LEAF_PUFF_COOLDOWN_SEC + 0.1;
    mv.time = sim.globalTime;
    sim_apply_move(sim, mv);
    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) > afterFirst);
}

TEST_METHOD(LeafPuffIgnoresCursorAwayFromCanopy) {
    Sim sim = make_autumn_sim_with_leafy_maple();
    const Blade* maple = first_leafy_maple(sim);
    const int before = count_kind(sim, EntityKind::Leaf);

    InputEvent mv{};
    mv.type = EventType::Move;
    mv.x = maple->baseX + 400.0;
    mv.y = sim.windowHeight - maple->mapleHeight * maple->cutHeight;
    mv.time = sim.globalTime;
    sim_apply_move(sim, mv);
    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) == before);
}

TEST_METHOD(LeafPuffDoesNotFireOutsideAutumn) {
    Sim sim = make_autumn_sim_with_leafy_maple();
    const Blade* maple = first_leafy_maple(sim);
    const double cx = maple->baseX;
    const double cy = sim.windowHeight - maple->mapleHeight * maple->cutHeight;
    sim.currentScene = Scene::Grass;

    const int before = count_kind(sim, EntityKind::Leaf);
    InputEvent mv{};
    mv.type = EventType::Move;
    mv.x = cx;
    mv.y = cy;
    mv.time = sim.globalTime;
    sim_apply_move(sim, mv);
    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) == before);
}

TEST_METHOD(PuffBurstDecaysSoLeavesSettleIntoFlutter) {
    Sim sim = make_autumn_sim_with_leafy_maple();
    const Blade* maple = first_leafy_maple(sim);
    InputEvent mv{};
    mv.type = EventType::Move;
    mv.x = maple->baseX;
    mv.y = sim.windowHeight - maple->mapleHeight * maple->cutHeight;
    mv.time = sim.globalTime;
    sim_apply_move(sim, mv);
    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) > 0);

    for (int i = 0; i < 40; ++i) sim_tick(sim, 0.05, nullptr, 0);
    for (const Entity& e : sim.entities) {
        if (e.kind == EntityKind::Leaf) Assert::IsTrue(e.vx == Near(0.0).margin(kEpsilon));
    }
}

TEST_METHOD(ReEnteringAutumnClearsThePuffCooldown) {
    Sim sim = make_autumn_sim_with_leafy_maple();
    const Blade* maple = first_leafy_maple(sim);
    InputEvent mv{};
    mv.type = EventType::Move;
    mv.x = maple->baseX;
    mv.y = sim.windowHeight - maple->mapleHeight * maple->cutHeight;
    mv.time = sim.globalTime;
    sim_apply_move(sim, mv);
    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) > 0);

    // Leaving and re-entering Autumn regenerates the (deterministic) maples and
    // must reset their puff cooldown so the fresh scene can puff immediately.
    sim_set_scene(sim, Scene::Grass);
    sim_set_scene(sim, Scene::Autumn);
    const Blade* maple2 = first_leafy_maple(sim);
    Assert::IsTrue(maple2 != nullptr);

    const int before = count_kind(sim, EntityKind::Leaf);
    InputEvent mv2{};
    mv2.type = EventType::Move;
    mv2.x = maple2->baseX;
    mv2.y = sim.windowHeight - maple2->mapleHeight * maple2->cutHeight;
    mv2.time = sim.globalTime;
    sim_apply_move(sim, mv2);
    Assert::IsTrue(count_kind(sim, EntityKind::Leaf) > before);
}

TEST_METHOD(AutumnPRNGSaltsAreUnique) {
    constexpr std::array<uint64_t, 16> salts = {
        REGROW_PRNG_SALT,
        FLOWER_PRNG_SALT,
        MUSHROOM_PRNG_SALT,
        AMBIENT_GUST_PRNG_SALT,
        CACTUS_PRNG_SALT,
        TUMBLEWEED_PRNG_SALT,
        CRITTER_PRNG_SALT,
        BUTTERFLY_PRNG_SALT,
        FIREFLY_PRNG_SALT,
        BIRD_FLYBY_PRNG_SALT,
        SNOWFLAKE_PRNG_SALT,
        PINE_PRNG_SALT,
        LEAF_PRNG_SALT,
        MAPLE_PRNG_SALT,
        LEAF_PUFF_PRNG_SALT,
    };

    for (std::size_t i = 0; i < salts.size(); ++i) {
        for (std::size_t j = i + 1; j < salts.size(); ++j) {
            Assert::IsTrue(salts[i] != salts[j]);
        }
    }
}
};
}
