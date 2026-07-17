#include "../third_party/catch2/catch.hpp"

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
    REQUIRE(found != plan.desired.end());
    return *found;
}

} // namespace

TEST_CASE("Topology reconciliation is idempotent and enumeration-order independent",
          "[topology]") {
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

    REQUIRE(plan.has_value());
    REQUIRE_FALSE(plan->changed);
    REQUIRE(plan->removals.empty());
    REQUIRE(plan->desired.size() == 2);
    REQUIRE(planned_for(*plan, "left").kind == ReconcileKind::Keep);
    REQUIRE(planned_for(*plan, "primary").kind == ReconcileKind::Keep);
}

TEST_CASE("Topology reconciliation creates and removes only unmatched monitors",
          "[topology]") {
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

    REQUIRE(plan.has_value());
    REQUIRE(plan->changed);
    REQUIRE(planned_for(*plan, "a").kind == ReconcileKind::Keep);
    REQUIRE(planned_for(*plan, "c").kind == ReconcileKind::Create);
    REQUIRE(plan->removals == std::vector<std::size_t>{ 1 });
}

TEST_CASE("Primary switch and coordinate reorder move existing surfaces",
          "[topology]") {
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

    REQUIRE(plan.has_value());
    REQUIRE(planned_for(*plan, "a").kind == ReconcileKind::Move);
    REQUIRE(planned_for(*plan, "b").kind == ReconcileKind::Move);
    REQUIRE(plan->removals.empty());
}

TEST_CASE("Mixed DPI replaces only the changed monitor surface", "[topology]") {
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

    REQUIRE(plan.has_value());
    REQUIRE(planned_for(*plan, "a").kind == ReconcileKind::Keep);
    REQUIRE(planned_for(*plan, "b").kind == ReconcileKind::Replace);
}

TEST_CASE("Taskbar work areas classify moves and backing replacements",
          "[topology]") {
    const PixelRect full = rect(0, 0, 1920, 1080);

    SECTION("bottom to top keeps backing and moves") {
        const MonitorSnapshot bottom = monitor(
            "a", "source-a", full, rect(0, 0, 1920, 1040), 96, true);
        const MonitorSnapshot top = monitor(
            "a", "source-a", full, rect(0, 40, 1920, 1080), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(bottom) },
            std::vector<MonitorSnapshot>{ top }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->desired[0].kind == ReconcileKind::Move);
    }

    SECTION("top taskbar thickness changes metadata only") {
        const MonitorSnapshot thin = monitor(
            "a", "source-a", full, rect(0, 40, 1920, 1080), 96, true);
        const MonitorSnapshot thick = monitor(
            "a", "source-a", full, rect(0, 80, 1920, 1080), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(thin) },
            std::vector<MonitorSnapshot>{ thick }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->changed);
        REQUIRE(plan->desired[0].kind == ReconcileKind::Keep);
    }

    SECTION("bottom taskbar thickness moves") {
        const MonitorSnapshot thin = monitor(
            "a", "source-a", full, rect(0, 0, 1920, 1040), 96, true);
        const MonitorSnapshot thick = monitor(
            "a", "source-a", full, rect(0, 0, 1920, 1000), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(thin) },
            std::vector<MonitorSnapshot>{ thick }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->desired[0].kind == ReconcileKind::Move);
    }

    SECTION("adding a side taskbar replaces the narrower backing") {
        const MonitorSnapshot noTaskbar = monitor(
            "a", "source-a", full, full, 96, true);
        const MonitorSnapshot left = monitor(
            "a", "source-a", full, rect(40, 0, 1920, 1080), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(noTaskbar) },
            std::vector<MonitorSnapshot>{ left }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->desired[0].kind == ReconcileKind::Replace);
    }

    SECTION("right to left with equal thickness moves") {
        const MonitorSnapshot right = monitor(
            "a", "source-a", full, rect(0, 0, 1880, 1080), 96, true);
        const MonitorSnapshot left = monitor(
            "a", "source-a", full, rect(40, 0, 1920, 1080), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(right) },
            std::vector<MonitorSnapshot>{ left }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->desired[0].kind == ReconcileKind::Move);
    }
}

TEST_CASE("Orientation and resolution use actual backing dimensions",
          "[topology]") {
    const MonitorSnapshot landscape = monitor(
        "a", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);

    SECTION("portrait rotation replaces width") {
        const MonitorSnapshot portrait = monitor(
            "a", "source-a", rect(0, 0, 1080, 1920),
            rect(0, 0, 1080, 1880), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(landscape) },
            std::vector<MonitorSnapshot>{ portrait }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->desired[0].kind == ReconcileKind::Replace);
    }

    SECTION("vertical-only resolution change moves bottom anchor") {
        const MonitorSnapshot taller = monitor(
            "a", "source-a", rect(0, 0, 1920, 1200),
            rect(0, 0, 1920, 1160), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(landscape) },
            std::vector<MonitorSnapshot>{ taller }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->desired[0].kind == ReconcileKind::Move);
    }
}

TEST_CASE("Stable identity and source fallback avoid cross-assignment",
          "[topology]") {
    const MonitorSnapshot existing = monitor(
        "stable-a", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);

    SECTION("temporary missing target identity uses source and retains stable id") {
        const MonitorSnapshot desired = monitor(
            "", "source-a", rect(0, 0, 1920, 1080),
            rect(0, 0, 1920, 1040), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(existing) },
            std::vector<MonitorSnapshot>{ desired }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->desired[0].kind == ReconcileKind::Keep);
        REQUIRE(plan->desired[0].monitor.identity.stableId == "stable-a");
        REQUIRE(plan->removals.empty());
    }

    SECTION("different stable target on same source is a remove and create") {
        const MonitorSnapshot replacement = monitor(
            "stable-b", "source-a", rect(0, 0, 1920, 1080),
            rect(0, 0, 1920, 1040), 96, true);
        const auto plan = PlanReconciliation(
            std::vector<CurrentSurface>{ current(existing) },
            std::vector<MonitorSnapshot>{ replacement }, 110.0);
        REQUIRE(plan.has_value());
        REQUIRE(plan->desired[0].kind == ReconcileKind::Create);
        REQUIRE(plan->removals == std::vector<std::size_t>{ 0 });
    }
}

TEST_CASE("Topology rejects duplicate identities and invalid geometry",
          "[topology]") {
    const MonitorSnapshot a = monitor(
        "same", "source-a", rect(0, 0, 1920, 1080),
        rect(0, 0, 1920, 1040), 96, true);
    MonitorSnapshot b = monitor(
        "same", "source-b", rect(1920, 0, 3840, 1080),
        rect(1920, 0, 3840, 1080), 96, false);

    REQUIRE_FALSE(IsValidTopology(std::vector<MonitorSnapshot>{ a, b }));

    b.identity.stableId = "b";
    b.identity.sourceId = "source-a";
    REQUIRE_FALSE(IsValidTopology(std::vector<MonitorSnapshot>{ a, b }));

    b.identity.sourceId = "source-b";
    b.dpi = 0;
    REQUIRE_FALSE(IsValidTopology(std::vector<MonitorSnapshot>{ a, b }));

    b.dpi = 96;
    b.workArea.right = b.monitorBounds.right + 1;
    REQUIRE_FALSE(IsValidTopology(std::vector<MonitorSnapshot>{ a, b }));
}

TEST_CASE("Stable IDs and layout seeds are deterministic", "[topology]") {
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
    REQUIRE_FALSE(first.empty());
    REQUIRE(first == second);

    MonitorIdentity identity{ first, "source-a" };
    REQUIRE(MakeLayoutSeed(identity) == MakeLayoutSeed(identity));

    MonitorIdentity fallback{ "", "source-a" };
    REQUIRE(MakeLayoutSeed(fallback) != MakeLayoutSeed(identity));
    REQUIRE(MakeLegacyLayoutSeed(rect(-1920, -100, 0, 980))
            == MakeLegacyLayoutSeed(rect(-1920, -100, 0, 980)));
}
