// scene_tests.cpp
//
// Scene infrastructure tests (architecture.md §13).
//
// Coverage:
//   * Scene enum discriminants match the spec ({Grass=0, Desert=1, Winter=2, Autumn=3}).
//   * sim_init defaults currentScene to SCENE_DEFAULT (= Grass).
//   * sim_set_scene does not perturb blade positions/dimensions/hues or
//     any non-scene PRNG stream.
//   * Per-scene palette tables are 6 entries each with full-alpha ARGB.
//   * SCENE_PALETTES[Grass] is bit-identical to the original §4 PALETTE.

#include "TestHelpers.h"
#include "Sim.h"
#include "snapshot_data.h"

#include <cstdint>

using namespace desktopgrass;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(SceneTests)
{
public:
TEST_METHOD(SceneEnumHasSpecLockedDiscriminants) {
    Assert::IsTrue(static_cast<int>(Scene::Grass)  == 0);
    Assert::IsTrue(static_cast<int>(Scene::Desert) == 1);
    Assert::IsTrue(static_cast<int>(Scene::Winter) == 2);
    Assert::IsTrue(static_cast<int>(Scene::Autumn) == 3);
    Assert::IsTrue(static_cast<int>(Scene::Ocean)  == 4);
    Assert::IsTrue(SCENE_COUNT == 5);
    Assert::IsTrue(static_cast<int>(SCENE_DEFAULT) == 0);
}

TEST_METHOD(SimInitDefaultsCurrentSceneToGrass) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    Assert::IsTrue(sim.currentScene == Scene::Grass);
}

TEST_METHOD(SimSetSceneDoesNotPerturbBladeGeometryOrHues) {
    Sim a = sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);
    Sim b = sim_init(CANONICAL_TEST_SEED, 1920.0, 1.0);

    // Same seed → same blades initially.
    Assert::IsTrue(a.blades.size() == b.blades.size());
    Assert::IsTrue(a.blades.size() == desktopgrass::test::CANONICAL_BLADE_COUNT);

    sim_set_scene(b, Scene::Desert);

    Assert::IsTrue(b.currentScene == Scene::Desert);
    Assert::IsTrue(a.currentScene == Scene::Grass);
    Assert::IsTrue(a.blades.size() == b.blades.size());
    for (size_t i = 0; i < a.blades.size(); ++i) {
        Assert::IsTrue(a.blades[i].baseX     == Near(b.blades[i].baseX));
        Assert::IsTrue(a.blades[i].height    == Near(b.blades[i].height));
        Assert::IsTrue(a.blades[i].thickness == Near(b.blades[i].thickness));
        Assert::IsTrue(a.blades[i].hue       == b.blades[i].hue);
    }
    // Desert cacti may mutate variant tags, but geometry and ambient PRNG stay untouched.
    Assert::IsTrue(a.ambientPrng.state == b.ambientPrng.state);
    Assert::IsTrue(a.nextAmbientGustTime == Near(b.nextAmbientGustTime));
}

TEST_METHOD(SimSetSceneRoundTripsThroughAllValues) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim_set_scene(sim, Scene::Desert);  Assert::IsTrue(sim.currentScene == Scene::Desert);
    sim_set_scene(sim, Scene::Winter);  Assert::IsTrue(sim.currentScene == Scene::Winter);
    sim_set_scene(sim, Scene::Autumn);  Assert::IsTrue(sim.currentScene == Scene::Autumn);
    sim_set_scene(sim, Scene::Ocean);   Assert::IsTrue(sim.currentScene == Scene::Ocean);
    sim_set_scene(sim, Scene::Grass);   Assert::IsTrue(sim.currentScene == Scene::Grass);
}

TEST_METHOD(PerScenePaletteTablesAre6ARGBEntriesWithFullAlpha) {
    for (int s = 0; s < SCENE_COUNT; ++s) {
        for (int i = 0; i < PALETTE_SIZE; ++i) {
            const uint32_t argb = SCENE_PALETTES[s][i];
            const uint8_t alpha = static_cast<uint8_t>((argb >> 24) & 0xFFu);
            Assert::IsTrue(alpha == 0xFFu);
        }
    }
}

TEST_METHOD(GrassScenePaletteIsBitIdenticalToTheOriginal4PALETTE) {
    for (int i = 0; i < PALETTE_SIZE; ++i) {
        Assert::IsTrue(SCENE_PALETTES[static_cast<int>(Scene::Grass)][i] == PALETTE[i]);
    }
}

TEST_METHOD(DesertPaletteValuesMatchSpec13) {
    constexpr uint32_t expected[PALETTE_SIZE] = {
        0xFFC9A26Bu, 0xFFB48A56u, 0xFFD9B57Au,
        0xFF8F6E3Fu, 0xFFE6C896u, 0xFFA67843u,
    };
    for (int i = 0; i < PALETTE_SIZE; ++i) {
        Assert::IsTrue(SCENE_PALETTES[static_cast<int>(Scene::Desert)][i] == expected[i]);
        Assert::IsTrue(DESERT_PALETTE[i] == expected[i]);
    }
}

TEST_METHOD(WinterPaletteValuesMatchSpec13) {
    constexpr uint32_t expected[PALETTE_SIZE] = {
        0xFFE8EEF5u, 0xFFB7C4D2u, 0xFFCBD8E5u,
        0xFFD7E2EEu, 0xFFA8B7C6u, 0xFFEEF3F8u,
    };
    for (int i = 0; i < PALETTE_SIZE; ++i) {
        Assert::IsTrue(SCENE_PALETTES[static_cast<int>(Scene::Winter)][i] == expected[i]);
        Assert::IsTrue(WINTER_PALETTE[i] == expected[i]);
    }
}
};
}
