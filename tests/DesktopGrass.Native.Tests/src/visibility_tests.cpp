#include "TestHelpers.h"

#include "RuntimePolicy.h"

#include <vector>

using desktopgrass::runtime::Covers;
using desktopgrass::runtime::IsFullyCovered;
using desktopgrass::runtime::Rect;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(VisibilityTests)
{
public:
TEST_METHOD(FullscreenCoverageUsesTheCompletePhysicalMonitor) {
    const Rect monitor{0, 0, 1920, 1080};

    Assert::IsTrue(Covers(Rect{0, 0, 1920, 1080}, monitor));
    Assert::IsFalse(Covers(Rect{0, 0, 1920, 1040}, monitor));
    Assert::IsFalse(Covers(Rect{0, 40, 1920, 1080}, monitor));
}

TEST_METHOD(ASingleOpaqueRectangleCanFullyCoverAGrassStrip) {
    const Rect strip{0, 970, 1920, 1080};

    Assert::IsTrue(IsFullyCovered(strip, {Rect{-10, 900, 1930, 1090}}));
    Assert::IsFalse(IsFullyCovered(strip, {Rect{0, 970, 1919, 1080}}));
}

TEST_METHOD(MultipleOpaqueRectanglesCanJointlyCoverASurface) {
    const Rect strip{0, 970, 1920, 1080};

    Assert::IsTrue(IsFullyCovered(strip, {
        Rect{0, 970, 960, 1080},
        Rect{960, 970, 1920, 1080},
    }));

    Assert::IsFalse(IsFullyCovered(strip, {
        Rect{0, 970, 959, 1080},
        Rect{960, 970, 1920, 1080},
    }));
}

TEST_METHOD(PartialAndOffSurfaceRectanglesDoNotReportFullOcclusion) {
    const Rect strip{100, 500, 900, 600};

    Assert::IsFalse(IsFullyCovered(strip, {
        Rect{0, 0, 1000, 550},
        Rect{-100, 700, 1200, 900},
    }));
    Assert::IsFalse(IsFullyCovered(strip, {
        Rect{-100, -100, 0, 0},
        Rect{1000, 1000, 1100, 1100},
    }));
}

TEST_METHOD(OcclusionRemainsIndependentAcrossMonitors) {
    const Rect leftStrip{-1920, 970, 0, 1080};
    const Rect rightStrip{0, 970, 1920, 1080};
    const std::vector<Rect> rightOnly{Rect{0, 900, 1920, 1080}};

    Assert::IsFalse(IsFullyCovered(leftStrip, rightOnly));
    Assert::IsTrue(IsFullyCovered(rightStrip, rightOnly));
}

TEST_METHOD(InvalidRectanglesNeverCauseSuppression) {
    Assert::IsFalse(IsFullyCovered(Rect{0, 0, 0, 100}, {Rect{0, 0, 100, 100}}));
    Assert::IsFalse(IsFullyCovered(Rect{0, 0, 100, 100}, {
        Rect{10, 10, 10, 50},
        Rect{20, 30, 40, 30},
    }));
}
};
}
