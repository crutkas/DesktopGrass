// RuntimePolicy.cpp

#include "RuntimePolicy.h"

#include <algorithm>
#include <utility>

namespace desktopgrass::runtime {

namespace {

Decision Paused(PauseReason reason, bool show) noexcept {
    return Decision{reason, 0, false, show};
}

Rect Intersection(const Rect& a, const Rect& b) noexcept {
    return Rect{
        std::max(a.left, b.left),
        std::max(a.top, b.top),
        std::min(a.right, b.right),
        std::min(a.bottom, b.bottom),
    };
}

void Subtract(const Rect& source,
              const Rect& cover,
              std::vector<Rect>& remaining)
{
    const Rect intersection = Intersection(source, cover);
    if (!IsValid(intersection)) {
        remaining.push_back(source);
        return;
    }

    if (source.top < intersection.top) {
        remaining.push_back(Rect{
            source.left, source.top, source.right, intersection.top,
        });
    }
    if (intersection.bottom < source.bottom) {
        remaining.push_back(Rect{
            source.left, intersection.bottom, source.right, source.bottom,
        });
    }
    if (source.left < intersection.left) {
        remaining.push_back(Rect{
            source.left, intersection.top,
            intersection.left, intersection.bottom,
        });
    }
    if (intersection.right < source.right) {
        remaining.push_back(Rect{
            intersection.right, intersection.top,
            source.right, intersection.bottom,
        });
    }
}

} // anonymous namespace

Decision Evaluate(const GlobalState& global,
                  const SurfaceState& surface,
                  int configuredFps) noexcept
{
    if (global.suspended) {
        return Paused(PauseReason::Suspended, true);
    }
    if (global.sessionState == SessionState::Locked) {
        return Paused(PauseReason::SessionLocked, true);
    }
    if (global.sessionState == SessionState::Disconnected) {
        return Paused(PauseReason::SessionDisconnected, true);
    }
    if (global.displayState == DisplayState::Off) {
        return Paused(PauseReason::DisplayOff, true);
    }
    if (surface.fullscreen) {
        return Paused(PauseReason::Fullscreen, false);
    }
    if (surface.occluded) {
        return Paused(PauseReason::Occluded, false);
    }

    int targetFps = std::max(configuredFps, 1);
    if (global.saverEnabled || global.displayState == DisplayState::Dimmed) {
        targetFps = std::min(targetFps, kSaverFpsCap);
    } else if (global.powerSource == PowerSource::Battery
               || global.powerSource == PowerSource::ShortTerm) {
        targetFps = std::min(targetFps, kBatteryFpsCap);
    }

    return Decision{PauseReason::None, targetFps, true, true};
}

bool IsGlobalPause(PauseReason reason) noexcept {
    switch (reason) {
        case PauseReason::Suspended:
        case PauseReason::SessionLocked:
        case PauseReason::SessionDisconnected:
        case PauseReason::DisplayOff:
            return true;
        default:
            return false;
    }
}

bool IsValid(const Rect& rect) noexcept {
    return rect.left < rect.right && rect.top < rect.bottom;
}

bool Covers(const Rect& outer, const Rect& inner) noexcept {
    return IsValid(outer) && IsValid(inner)
        && outer.left <= inner.left
        && outer.top <= inner.top
        && outer.right >= inner.right
        && outer.bottom >= inner.bottom;
}

bool IsFullyCovered(const Rect& target,
                    const std::vector<Rect>& opaqueRects)
{
    if (!IsValid(target)) return false;

    std::vector<Rect> uncovered{target};
    for (const Rect& opaque : opaqueRects) {
        if (!IsValid(opaque)) continue;

        std::vector<Rect> next;
        next.reserve(uncovered.size() * 2);
        for (const Rect& region : uncovered) {
            Subtract(region, opaque, next);
        }
        uncovered = std::move(next);
        if (uncovered.empty()) return true;
    }

    return false;
}

} // namespace desktopgrass::runtime
