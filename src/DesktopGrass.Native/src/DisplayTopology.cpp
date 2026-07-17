#include "DisplayTopology.h"

#include <algorithm>
#include <iomanip>
#include <set>
#include <sstream>
#include <tuple>

namespace desktopgrass::topology {
namespace {

constexpr std::uint64_t kAppSeed = 0xD3C7C0F30070D511ull;
constexpr std::uint64_t kFnvOffset = 14695981039346656037ull;
constexpr std::uint64_t kFnvPrime = 1099511628211ull;

std::uint64_t StableHash(std::string_view value) noexcept {
    std::uint64_t hash = kFnvOffset;
    for (const unsigned char byte : value) {
        hash ^= byte;
        hash *= kFnvPrime;
    }
    return hash;
}

bool RectIsValid(const PixelRect& rect) noexcept {
    return rect.Width() > 0 && rect.Height() > 0;
}

bool WorkAreaIsInsideMonitor(const MonitorSnapshot& monitor) noexcept {
    return monitor.workArea.left >= monitor.monitorBounds.left
        && monitor.workArea.top >= monitor.monitorBounds.top
        && monitor.workArea.right <= monitor.monitorBounds.right
        && monitor.workArea.bottom <= monitor.monitorBounds.bottom;
}

bool MonitorLess(const MonitorSnapshot& lhs,
                 const MonitorSnapshot& rhs) noexcept {
    return std::tie(lhs.monitorBounds.left,
                    lhs.monitorBounds.top,
                    lhs.monitorBounds.right,
                    lhs.monitorBounds.bottom,
                    lhs.identity.stableId,
                    lhs.identity.sourceId)
         < std::tie(rhs.monitorBounds.left,
                    rhs.monitorBounds.top,
                    rhs.monitorBounds.right,
                    rhs.monitorBounds.bottom,
                    rhs.identity.stableId,
                    rhs.identity.sourceId);
}

bool CurrentSetIsValid(const std::vector<CurrentSurface>& current) noexcept {
    std::vector<MonitorSnapshot> monitors;
    monitors.reserve(current.size());
    for (const CurrentSurface& surface : current) {
        if (surface.surface.widthPx <= 0
            || surface.surface.heightPx <= 0
            || surface.surface.dpi == 0) {
            return false;
        }
        monitors.push_back(surface.monitor);
    }
    return IsValidTopology(monitors, false);
}

} // namespace

bool operator==(const PixelRect& lhs, const PixelRect& rhs) noexcept {
    return lhs.left == rhs.left
        && lhs.top == rhs.top
        && lhs.right == rhs.right
        && lhs.bottom == rhs.bottom;
}

bool operator!=(const PixelRect& lhs, const PixelRect& rhs) noexcept {
    return !(lhs == rhs);
}

bool operator==(const MonitorIdentity& lhs,
                const MonitorIdentity& rhs) noexcept {
    return lhs.stableId == rhs.stableId && lhs.sourceId == rhs.sourceId;
}

bool operator!=(const MonitorIdentity& lhs,
                const MonitorIdentity& rhs) noexcept {
    return !(lhs == rhs);
}

bool operator==(const MonitorSnapshot& lhs,
                const MonitorSnapshot& rhs) noexcept {
    return lhs.identity == rhs.identity
        && lhs.monitorBounds == rhs.monitorBounds
        && lhs.workArea == rhs.workArea
        && lhs.dpi == rhs.dpi
        && lhs.primary == rhs.primary;
}

bool operator!=(const MonitorSnapshot& lhs,
                const MonitorSnapshot& rhs) noexcept {
    return !(lhs == rhs);
}

bool operator==(const SurfaceSpec& lhs, const SurfaceSpec& rhs) noexcept {
    return lhs.x == rhs.x
        && lhs.y == rhs.y
        && lhs.widthPx == rhs.widthPx
        && lhs.heightPx == rhs.heightPx
        && lhs.dpi == rhs.dpi;
}

bool operator!=(const SurfaceSpec& lhs, const SurfaceSpec& rhs) noexcept {
    return !(lhs == rhs);
}

bool HasSameBackingSurface(const SurfaceSpec& lhs,
                           const SurfaceSpec& rhs) noexcept {
    return lhs.widthPx == rhs.widthPx
        && lhs.heightPx == rhs.heightPx
        && lhs.dpi == rhs.dpi;
}

std::string CanonicalizeIdentityComponent(std::string_view value) {
    std::string result(value);
    for (char& c : result) {
        if (c >= 'A' && c <= 'Z') {
            c = static_cast<char>(c - 'A' + 'a');
        }
    }
    return result;
}

std::string MakeStableMonitorId(
    const std::vector<std::string>& targetPaths) {
    std::vector<std::string> canonical;
    canonical.reserve(targetPaths.size());
    for (const std::string& path : targetPaths) {
        std::string value = CanonicalizeIdentityComponent(path);
        if (!value.empty()) {
            canonical.push_back(std::move(value));
        }
    }
    std::sort(canonical.begin(), canonical.end());
    canonical.erase(std::unique(canonical.begin(), canonical.end()),
                    canonical.end());
    if (canonical.empty()) {
        return {};
    }

    std::string joined;
    for (const std::string& path : canonical) {
        joined.append(path);
        joined.push_back('\0');
    }

    std::ostringstream out;
    out << "display-" << std::hex << std::setfill('0') << std::setw(16)
        << StableHash(joined);
    return out.str();
}

std::uint64_t MakeLayoutSeed(const MonitorIdentity& identity) noexcept {
    const std::string_view key = !identity.stableId.empty()
        ? std::string_view(identity.stableId)
        : std::string_view(identity.sourceId);
    return kAppSeed ^ StableHash(key);
}

std::uint64_t MakeLegacyLayoutSeed(const PixelRect& workArea) noexcept {
    const std::uint64_t left = static_cast<std::uint64_t>(
        static_cast<std::int64_t>(workArea.left));
    const std::uint64_t top = static_cast<std::uint64_t>(
        static_cast<std::int64_t>(workArea.top));
    return kAppSeed
        ^ (left * 0xA0761D6478BD642Full)
        ^ (top * 0xE7037ED1A0B428DBull);
}

SurfaceSpec MakeSurfaceSpec(const MonitorSnapshot& monitor,
                            double surfaceHeightDip) noexcept {
    SurfaceSpec result;
    result.x = monitor.workArea.left;
    result.widthPx = monitor.workArea.Width();
    result.dpi = monitor.dpi;
    result.heightPx = static_cast<int>(
        (surfaceHeightDip * static_cast<double>(monitor.dpi) / 96.0) + 0.5);
    result.y = monitor.workArea.bottom - result.heightPx;
    return result;
}

bool IsValidTopology(const std::vector<MonitorSnapshot>& monitors,
                     bool requirePrimary) noexcept {
    if (requirePrimary && monitors.empty()) {
        return false;
    }

    std::set<std::string> stableIds;
    std::set<std::string> sourceIds;
    std::size_t primaryCount = 0;

    for (const MonitorSnapshot& monitor : monitors) {
        if (monitor.identity.stableId.empty()
            && monitor.identity.sourceId.empty()) {
            return false;
        }
        if (!RectIsValid(monitor.monitorBounds)
            || !RectIsValid(monitor.workArea)
            || !WorkAreaIsInsideMonitor(monitor)
            || monitor.dpi == 0) {
            return false;
        }
        if (!monitor.identity.stableId.empty()
            && !stableIds.insert(monitor.identity.stableId).second) {
            return false;
        }
        if (!monitor.identity.sourceId.empty()
            && !sourceIds.insert(monitor.identity.sourceId).second) {
            return false;
        }
        if (monitor.primary) {
            ++primaryCount;
        }
    }

    return !requirePrimary || primaryCount == 1;
}

std::vector<MonitorSnapshot> SortTopology(
    const std::vector<MonitorSnapshot>& monitors) {
    std::vector<MonitorSnapshot> sorted = monitors;
    std::sort(sorted.begin(), sorted.end(), MonitorLess);
    return sorted;
}

bool TopologiesEquivalent(const std::vector<MonitorSnapshot>& lhs,
                          const std::vector<MonitorSnapshot>& rhs) {
    return SortTopology(lhs) == SortTopology(rhs);
}

std::optional<ReconciliationPlan> PlanReconciliation(
    const std::vector<CurrentSurface>& current,
    const std::vector<MonitorSnapshot>& desired,
    double surfaceHeightDip) {
    if (!(surfaceHeightDip > 0.0)
        || !CurrentSetIsValid(current)
        || !IsValidTopology(desired)) {
        return std::nullopt;
    }

    const std::vector<MonitorSnapshot> orderedDesired = SortTopology(desired);
    std::vector<bool> used(current.size(), false);
    std::vector<std::size_t> matches(orderedDesired.size(), kNoSurface);

    for (std::size_t desiredIndex = 0;
         desiredIndex < orderedDesired.size();
         ++desiredIndex) {
        const std::string& stableId =
            orderedDesired[desiredIndex].identity.stableId;
        if (stableId.empty()) {
            continue;
        }
        for (std::size_t currentIndex = 0;
             currentIndex < current.size();
             ++currentIndex) {
            if (!used[currentIndex]
                && current[currentIndex].monitor.identity.stableId == stableId) {
                matches[desiredIndex] = currentIndex;
                used[currentIndex] = true;
                break;
            }
        }
    }

    for (std::size_t desiredIndex = 0;
         desiredIndex < orderedDesired.size();
         ++desiredIndex) {
        if (matches[desiredIndex] != kNoSurface) {
            continue;
        }

        const MonitorSnapshot& target = orderedDesired[desiredIndex];
        if (target.identity.sourceId.empty()) {
            continue;
        }
        for (std::size_t currentIndex = 0;
             currentIndex < current.size();
             ++currentIndex) {
            if (used[currentIndex]) {
                continue;
            }
            const MonitorSnapshot& existing = current[currentIndex].monitor;
            const bool stableIdentityMissing =
                target.identity.stableId.empty()
                || existing.identity.stableId.empty();
            if (stableIdentityMissing
                && existing.identity.sourceId == target.identity.sourceId) {
                matches[desiredIndex] = currentIndex;
                used[currentIndex] = true;
                break;
            }
        }
    }

    ReconciliationPlan plan;
    plan.desired.reserve(orderedDesired.size());

    for (std::size_t desiredIndex = 0;
         desiredIndex < orderedDesired.size();
         ++desiredIndex) {
        PlannedSurface planned;
        planned.currentIndex = matches[desiredIndex];
        planned.monitor = orderedDesired[desiredIndex];
        planned.surface = MakeSurfaceSpec(planned.monitor, surfaceHeightDip);

        if (planned.surface.widthPx <= 0 || planned.surface.heightPx <= 0) {
            return std::nullopt;
        }

        if (planned.currentIndex == kNoSurface) {
            planned.kind = ReconcileKind::Create;
            plan.changed = true;
        } else {
            const CurrentSurface& existing = current[planned.currentIndex];
            if (planned.monitor.identity.stableId.empty()) {
                planned.monitor.identity.stableId =
                    existing.monitor.identity.stableId;
            }

            if (planned.surface == existing.surface) {
                planned.kind = ReconcileKind::Keep;
            } else if (HasSameBackingSurface(planned.surface,
                                             existing.surface)) {
                planned.kind = ReconcileKind::Move;
            } else {
                planned.kind = ReconcileKind::Replace;
            }

            if (planned.kind != ReconcileKind::Keep
                || planned.monitor != existing.monitor
                || planned.currentIndex != desiredIndex) {
                plan.changed = true;
            }
        }

        plan.desired.push_back(std::move(planned));
    }

    for (std::size_t currentIndex = 0;
         currentIndex < current.size();
         ++currentIndex) {
        if (!used[currentIndex]) {
            plan.removals.push_back(currentIndex);
            plan.changed = true;
        }
    }

    return plan;
}

} // namespace desktopgrass::topology
