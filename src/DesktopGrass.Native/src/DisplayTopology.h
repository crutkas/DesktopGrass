#pragma once

#include <cstddef>
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace desktopgrass::topology {

struct PixelRect {
    int left = 0;
    int top = 0;
    int right = 0;
    int bottom = 0;

    int Width() const noexcept { return right - left; }
    int Height() const noexcept { return bottom - top; }
};

bool operator==(const PixelRect& lhs, const PixelRect& rhs) noexcept;
bool operator!=(const PixelRect& lhs, const PixelRect& rhs) noexcept;

struct MonitorIdentity {
    std::string stableId;
    std::string sourceId;
};

bool operator==(const MonitorIdentity& lhs, const MonitorIdentity& rhs) noexcept;
bool operator!=(const MonitorIdentity& lhs, const MonitorIdentity& rhs) noexcept;

struct MonitorSnapshot {
    MonitorIdentity identity;
    PixelRect monitorBounds;
    PixelRect workArea;
    std::uint32_t dpi = 0;
    bool primary = false;
};

bool operator==(const MonitorSnapshot& lhs, const MonitorSnapshot& rhs) noexcept;
bool operator!=(const MonitorSnapshot& lhs, const MonitorSnapshot& rhs) noexcept;

struct SurfaceSpec {
    int x = 0;
    int y = 0;
    int widthPx = 0;
    int heightPx = 0;
    std::uint32_t dpi = 0;
};

bool operator==(const SurfaceSpec& lhs, const SurfaceSpec& rhs) noexcept;
bool operator!=(const SurfaceSpec& lhs, const SurfaceSpec& rhs) noexcept;
bool HasSameBackingSurface(const SurfaceSpec& lhs, const SurfaceSpec& rhs) noexcept;

struct CurrentSurface {
    MonitorSnapshot monitor;
    SurfaceSpec surface;
};

enum class ReconcileKind {
    Keep,
    Move,
    Replace,
    Create,
};

inline constexpr std::size_t kNoSurface = static_cast<std::size_t>(-1);

struct PlannedSurface {
    ReconcileKind kind = ReconcileKind::Create;
    std::size_t currentIndex = kNoSurface;
    MonitorSnapshot monitor;
    SurfaceSpec surface;
};

struct ReconciliationPlan {
    std::vector<PlannedSurface> desired;
    std::vector<std::size_t> removals;
    bool changed = false;
};

std::string CanonicalizeIdentityComponent(std::string_view value);
std::string MakeStableMonitorId(const std::vector<std::string>& targetPaths);
std::uint64_t MakeLayoutSeed(const MonitorIdentity& identity) noexcept;
std::uint64_t MakeLegacyLayoutSeed(const PixelRect& workArea) noexcept;

SurfaceSpec MakeSurfaceSpec(const MonitorSnapshot& monitor,
                            double surfaceHeightDip) noexcept;

bool IsValidTopology(const std::vector<MonitorSnapshot>& monitors,
                     bool requirePrimary = true) noexcept;
std::vector<MonitorSnapshot> SortTopology(
    const std::vector<MonitorSnapshot>& monitors);
bool TopologiesEquivalent(const std::vector<MonitorSnapshot>& lhs,
                          const std::vector<MonitorSnapshot>& rhs);

std::optional<ReconciliationPlan> PlanReconciliation(
    const std::vector<CurrentSurface>& current,
    const std::vector<MonitorSnapshot>& desired,
    double surfaceHeightDip);

} // namespace desktopgrass::topology
