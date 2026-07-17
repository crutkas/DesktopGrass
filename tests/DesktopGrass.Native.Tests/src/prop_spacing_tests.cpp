#include "TestHelpers.h"

#include "Sim.h"
#include "Constants.h"

#include <vector>

using namespace desktopgrass;

namespace {

constexpr double kMonitor1920 = 1920.0;
constexpr double kDensity     = 1.0;
constexpr uint64_t kSeed      = 0xDE5C70F6A55ED511ull;

struct Prop {
    double leftEdge;
    double rightEdge;
};

double cactus_half_width(const Blade& b) {
    return (b.cactusType != 0) ? b.cactusWidth * 1.55 : b.cactusWidth * 0.5;
}

double pine_half_width(const Blade& b) {
    double hw = (b.treeVariant == 1) ? b.pineWidth * 4.0 : b.pineWidth * 0.5;
    if (b.treeBackground) hw *= TREE_BG_SCALE;
    return hw;
}

// Walk the prop list left-to-right and verify that every adjacent pair has
// at least PROP_MIN_GAP_DIP between the right edge of one and the left edge
// of the next. The generators emit props in baseX order so a single linear
// pass is sufficient.
void require_spacing(const std::vector<Prop>& props, double minGap) {
    Assert::IsTrue(props.size() >= 1);
    for (std::size_t i = 1; i < props.size(); ++i) {
        const double gap = props[i].leftEdge - props[i - 1].rightEdge;
        Assert::IsTrue(gap >= minGap);
    }
}

}  // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(PropSpacingTests)
{
public:
TEST_METHOD(DesertCactiKeepAtLeastPROPMINGAPDIPBetweenNeighbours) {
    Sim sim = sim_init(kSeed, kMonitor1920, kDensity);
    sim_set_scene(sim, Scene::Desert);

    std::vector<Prop> cacti;
    for (const Blade& b : sim.blades) {
        if (!b.isCactus) continue;
        const double hw = cactus_half_width(b);
        cacti.push_back({b.baseX - hw, b.baseX + hw});
    }
    require_spacing(cacti, PROP_MIN_GAP_DIP);
}

TEST_METHOD(WinterPinesKeepAtLeastPROPMINGAPDIPBetweenSameLayerNeighbours) {
    Sim sim = sim_init(kSeed, kMonitor1920, kDensity);
    sim_set_scene(sim, Scene::Winter);

    std::vector<Prop> fgPines;
    std::vector<Prop> bgPines;
    for (const Blade& b : sim.blades) {
        if (!b.isPine) continue;
        const double hw = pine_half_width(b);
        Prop p{b.baseX - hw, b.baseX + hw};
        if (b.treeBackground) bgPines.push_back(p);
        else                  fgPines.push_back(p);
    }
    require_spacing(fgPines, PROP_MIN_GAP_DIP);
    require_spacing(bgPines, PROP_MIN_GAP_DIP);
}

TEST_METHOD(AutumnMaplesKeepAtLeastPROPMINGAPDIPBetweenNeighbours) {
    Sim sim = sim_init(kSeed, kMonitor1920, kDensity);
    sim_set_scene(sim, Scene::Autumn);

    std::vector<Prop> maples;
    for (const Blade& b : sim.blades) {
        if (!b.isMaple) continue;
        const double hw = b.mapleCanopyRadius;
        maples.push_back({b.baseX - hw, b.baseX + hw});
    }
    require_spacing(maples, PROP_MIN_GAP_DIP);
}

TEST_METHOD(OceanCoralKeepAtLeastPROPMINGAPDIPBetweenNeighbours) {
    Sim sim = sim_init(kSeed, kMonitor1920, kDensity);
    sim_set_scene(sim, Scene::Ocean);

    std::vector<Prop> coral;
    for (const Blade& b : sim.blades) {
        if (!b.isCoral) continue;
        const double hw = b.coralWidth * 0.5;
        coral.push_back({b.baseX - hw, b.baseX + hw});
    }
    require_spacing(coral, PROP_MIN_GAP_DIP);
}

TEST_METHOD(PropSpacingRuleReducesButDoesnTDecimateThePopulation) {
    // Sanity check that gap rejection isn't aggressive enough to break the
    // existing "near-spec probability" tests — each scene should still place
    // at least a handful of props on a 1920-DIP window with canonical seed.
    Sim sim = sim_init(kSeed, kMonitor1920, kDensity);

    sim_set_scene(sim, Scene::Desert);
    int cactusCount = 0;
    for (const Blade& b : sim.blades) if (b.isCactus) ++cactusCount;
    Assert::IsTrue(cactusCount >= 1);

    sim_set_scene(sim, Scene::Winter);
    int pineCount = 0;
    for (const Blade& b : sim.blades) if (b.isPine) ++pineCount;
    Assert::IsTrue(pineCount >= 3);

    sim_set_scene(sim, Scene::Autumn);
    int mapleCount = 0;
    for (const Blade& b : sim.blades) if (b.isMaple) ++mapleCount;
    Assert::IsTrue(mapleCount >= 1);

    sim_set_scene(sim, Scene::Ocean);
    int coralCount = 0;
    for (const Blade& b : sim.blades) if (b.isCoral) ++coralCount;
    Assert::IsTrue(coralCount >= 5);
}
};
}
