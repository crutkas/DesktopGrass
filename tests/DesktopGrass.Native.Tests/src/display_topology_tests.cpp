#include "TestHelpers.h"

#include "DisplayTopology.h"

#include <algorithm>
#include <string>
#include <vector>

using namespace desktopgrass::topology;

namespace {

PixelRect rect(int left, int top, int right, int bottom) {
    return PixelRect{ left, top, right, bottom };
}

MonitorSnapshot monitor(std::string stableId,
                        std::string sourceId,
                        PixelRect monitorBounds,
                        PixelRect workArea,
                        std::uint32_t dpi = 96,
                        bool primary = false) {
    MonitorSnapshot result;
    result.identity.stableId = std::move(stableId);
    result.identity.sourceId = std::move(sourceId);
    result.monitorBounds = monitorBounds;
    result.workArea = workArea;
    result.dpi = dpi;
    result.primary = primary;
    return result;
}

CurrentSurface current(const MonitorSnapshot& snapshot) {
    return CurrentSurface{ snapshot, MakeSurfaceSpec(snapshot, 110.0) };
}

const PlannedSurface& planned_for(const ReconciliationPlan& plan,
                                  const std::string& stableId) {
    const auto found = std::find_if(
        plan.desired.begin(), plan.desired.end(),
        [&](const PlannedSurface& planned) {
            return planned.monitor.identity.stableId == stableId;
        });
    Assert::IsTrue(found != plan.desired.end());
    return *found;
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(DisplayTopologyTests)
{
public:
TEST_METHOD(TopologyReconciliationIsIdempotentAndEnumerationOrderIndependent) {
    const MonitorSnapshot left = monitor(
        "left", R"(\\.\display2)",
        rect(-1920, 0, 0, 1080), rect(-1920, 0, 0, 1040), 96, false);
    const MonitorSnapshot primary = monitor(
        "primary", R"(\\.\display1)",
        rect(0, 0, 2560, 1440), rect(0, 0, 2560, 1400), 144, true);

    const std::vector<CurrentSurface> active{
        current(left),
        current(primary),
    };
    const auto plan = PlanReconciliation(
        active, std::vector<MonitorSnapshot>{ primary, left }, 110.0);

    Assert::IsTrue(plan.has_value());
    Assert::IsFalse(plan->changed);
    Assert::IsTrue(plan->removals.empty());
    Assert::IsTrue(plan->desired.size() == 2);
    Assert::IsTrue(planned_for(*plan, "left").kind == ReconcileKind::Keep);
    Assert::IsTrue(planned_for(*plan, "primary").kind == ReconcileKind::Keep);
}

TEST_METHOD(TopologyReconciliationCreatesAndRemovesOnlyUnmatchedMonitors) {
    const MonitorSnapshot a = monitor(
        "a", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);
    const MonitorSnapshot b = monitor(
        "b", "source-b", rect(1920, 0, 3840, 1080),
        rect(1920, 0, 3840, 1080), 96, false);
    const MonitorSnapshot c = monitor(
        "c", "source-c", rect(-2560, 0, 0, 1440),
        rect(-2560, 0, 0, 1400), 144, false);

    const auto plan = PlanReconciliation(
        std::vector<CurrentSurface>{ current(a), current(b) },
        std::vector<MonitorSnapshot>{ a, c }, 110.0);

    Assert::IsTrue(plan.has_value());
    Assert::IsTrue(plan->changed);
    Assert::IsTrue(planned_for(*plan, "a").kind == ReconcileKind::Keep);
    Assert::IsTrue(planned_for(*plan, "c").kind == ReconcileKind::Create);
    Assert::IsTrue(plan->removals == std::vector<std::size_t>{ 1 });
}

TEST_METHOD(PrimarySwitchAndCoordinateReorderMoveExistingSurfaces) {
    const MonitorSnapshot oldA = monitor(
        "a", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);
    const MonitorSnapshot oldB = monitor(
        "b", "source-b", rect(1920, 0, 3840, 1080),
        rect(1920, 0, 3840, 1080), 96, false);
    const MonitorSnapshot newA = monitor(
        "a", "source-a", rect(-1920, 0, 0, 1080),
        rect(-1920, 0, 0, 1040), 96, false);
    const MonitorSnapshot newB = monitor(
        "b", "source-b", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1080), 96, true);

    const auto plan = PlanReconciliation(
        std::vector<CurrentSurface>{ current(oldA), current(oldB) },
        std::vector<MonitorSnapshot>{ newA, newB }, 110.0);

    Assert::IsTrue(plan.has_value());
    Assert::IsTrue(planned_for(*plan, "a").kind == ReconcileKind::Move);
    Assert::IsTrue(planned_for(*plan, "b").kind == ReconcileKind::Move);
    Assert::IsTrue(plan->removals.empty());
}

TEST_METHOD(MixedDPIReplacesOnlyTheChangedMonitorSurface) {
    const MonitorSnapshot a = monitor(
        "a", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);
    const MonitorSnapshot oldB = monitor(
        "b", "source-b", rect(1920, 0, 4480, 1440),
        rect(1920, 0, 4480, 1400), 120, false);
    MonitorSnapshot newB = oldB;
    newB.dpi = 192;

    const auto plan = PlanReconciliation(
        std::vector<CurrentSurface>{ current(a), current(oldB) },
        std::vector<MonitorSnapshot>{ a, newB }, 110.0);

    Assert::IsTrue(plan.has_value());
    Assert::IsTrue(planned_for(*plan, "a").kind == ReconcileKind::Keep);
    Assert::IsTrue(planned_for(*plan, "b").kind == ReconcileKind::Replace);
}

TEST_METHOD(TaskbarWorkAreasClassifyMovesAndBackingReplacements) {
    const PixelRect full = rect(0, 0, 1920, 1080);

    {
        const MonitorSnapshot bottom = monitor(
            "a", "source-a", full, rect(0, 0, 1920, 1040), 96, true);
        const MonitorSnapshot top = monitor(
            "a", "source-a", full, rect(0, 40, 1920, 1080), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(bottom) },
            std::vector<MonitorSnapshot>{ top }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Move);
    }

    {
        const MonitorSnapshot thin = monitor(
            "a", "source-a", full, rect(0, 40, 1920, 1080), 96, true);
        const MonitorSnapshot thick = monitor(
            "a", "source-a", full, rect(0, 80, 1920, 1080), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(thin) },
            std::vector<MonitorSnapshot>{ thick }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->changed);
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Keep);
    }

    {
        const MonitorSnapshot thin = monitor(
            "a", "source-a", full, rect(0, 0, 1920, 1040), 96, true);
        const MonitorSnapshot thick = monitor(
            "a", "source-a", full, rect(0, 0, 1920, 1000), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(thin) },
            std::vector<MonitorSnapshot>{ thick }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Move);
    }

    {
        const MonitorSnapshot noTaskbar = monitor(
            "a", "source-a", full, full, 96, true);
        const MonitorSnapshot left = monitor(
            "a", "source-a", full, rect(40, 0, 1920, 1080), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(noTaskbar) },
            std::vector<MonitorSnapshot>{ left }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Replace);
    }

    {
        const MonitorSnapshot right = monitor(
            "a", "source-a", full, rect(0, 0, 1880, 1080), 96, true);
        const MonitorSnapshot left = monitor(
            "a", "source-a", full, rect(40, 0, 1920, 1080), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(right) },
            std::vector<MonitorSnapshot>{ left }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Move);
    }
}

TEST_METHOD(OrientationAndResolutionUseActualBackingDimensions) {
    const MonitorSnapshot landscape = monitor(
        "a", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);

    {
        const MonitorSnapshot portrait = monitor(
            "a", "source-a", rect(0, 0, 1080, 1920),
            rect(0, 0, 1080, 1880), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(landscape) },
            std::vector<MonitorSnapshot>{ portrait }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Replace);
    }

    {
        const MonitorSnapshot taller = monitor(
            "a", "source-a", rect(0, 0, 1920, 1200),
            rect(0, 0, 1920, 1160), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(landscape) },
            std::vector<MonitorSnapshot>{ taller }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Move);
    }
}

TEST_METHOD(StableIdentityAndSourceFallbackAvoidCrossAssignment) {
    const MonitorSnapshot existing = monitor(
        "stable-a", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);

    {
        const MonitorSnapshot desired = monitor(
            "", "source-a", rect(0, 0, 1920, 1080),
            rect(0, 0, 1920, 1040), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(existing) },
            std::vector<MonitorSnapshot>{ desired }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Keep);
        Assert::IsTrue(plan->desired[0].monitor.identity.stableId == "stable-a");
        Assert::IsTrue(plan->removals.empty());
    }

    {
        const MonitorSnapshot replacement = monitor(
            "stable-b", "source-a", rect(0, 0, 1920, 1080),
            rect(0, 0, 1920, 1040), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(existing) },
            std::vector<MonitorSnapshot>{ replacement }, 110.0);
        Assert::IsTrue(plan.has_value());
        Assert::IsTrue(plan->desired[0].kind == ReconcileKind::Create);
        Assert::IsTrue(plan->removals == std::vector<std::size_t>{ 0 });
    }
}

TEST_METHOD(TopologyRejectsDuplicateIdentitiesAndInvalidGeometry) {
    const MonitorSnapshot a = monitor(
        "same", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);
    MonitorSnapshot b = monitor(
        "same", "source-b", rect(1920, 0, 3840, 1080),
        rect(1920, 0, 3840, 1080), 96, false);

    Assert::IsFalse(IsValidTopology(std::vector<MonitorSnapshot>{ a, b }));

    b.identity.stableId = "b";
    b.identity.sourceId = "source-a";
    Assert::IsFalse(IsValidTopology(std::vector<MonitorSnapshot>{ a, b }));

    b.identity.sourceId = "source-b";
    b.dpi = 0;
    Assert::IsFalse(IsValidTopology(std::vector<MonitorSnapshot>{ a, b }));

    b.dpi = 96;
    b.workArea.right = b.monitorBounds.right + 1;
    Assert::IsFalse(IsValidTopology(std::vector<MonitorSnapshot>{ a, b }));
}

TEST_METHOD(StableIDsAndLayoutSeedsAreDeterministic) {
    const std::vector<std::string> paths{
        R"(\\?\DISPLAY#DEL1234#B)",
        R"(\\?\display#del1234#a)",
    };
    const std::vector<std::string> reversed{
        R"(\\?\DISPLAY#DEL1234#A)",
        R"(\\?\display#del1234#b)",
    };

    const std::string first = MakeStableMonitorId(paths);
    const std::string second = MakeStableMonitorId(reversed);
    Assert::IsFalse(first.empty());
    Assert::IsTrue(first == second);

    MonitorIdentity identity{ first, "source-a" };
    Assert::IsTrue(MakeLayoutSeed(identity) == MakeLayoutSeed(identity));

    MonitorIdentity fallback{ "", "source-a" };
    Assert::IsTrue(MakeLayoutSeed(fallback) != MakeLayoutSeed(identity));
    Assert::IsTrue(MakeLegacyLayoutSeed(rect(-1920, -100, 0, 980))
            == MakeLegacyLayoutSeed(rect(-1920, -100, 0, 980)));
}
};
}
