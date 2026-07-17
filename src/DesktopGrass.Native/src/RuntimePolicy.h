// RuntimePolicy.h
//
// Pure runtime-state reduction shared by the Native host and deterministic
// tests. Windows notifications populate these inputs; this layer decides
// whether a surface renders and at what cadence.

#pragma once

#include <vector>

namespace desktopgrass::runtime {

// Initial conservative caps. GitHub issue #14 owns measurement-based tuning;
// policy always chooses min(configured FPS, applicable cap).
constexpr int kBatteryFpsCap = 12;
constexpr int kSaverFpsCap = 5;

enum class PowerSource {
    Unknown,
    Ac,
    Battery,
    ShortTerm,
};

enum class DisplayState {
    Unknown,
    Off,
    On,
    Dimmed,
};

enum class SessionState {
    Unknown,
    Active,
    Locked,
    Disconnected,
};

enum class PauseReason {
    None,
    Suspended,
    SessionLocked,
    SessionDisconnected,
    DisplayOff,
    Fullscreen,
    Occluded,
    AnimationsDisabled,
};

struct GlobalState {
    PowerSource powerSource = PowerSource::Unknown;
    DisplayState displayState = DisplayState::Unknown;
    SessionState sessionState = SessionState::Unknown;
    bool saverEnabled = false;
    bool suspended = false;
    bool clientAreaAnimationEnabled = true;
};

struct SurfaceState {
    bool fullscreen = false;
    bool occluded = false;
};

struct Decision {
    PauseReason pauseReason = PauseReason::None;
    int targetFps = 0;
    bool render = false;
    bool show = true;

    bool operator==(const Decision& other) const noexcept {
        return pauseReason == other.pauseReason
            && targetFps == other.targetFps
            && render == other.render
            && show == other.show;
    }

    bool operator!=(const Decision& other) const noexcept {
        return !(*this == other);
    }
};

Decision Evaluate(const GlobalState& global,
                  const SurfaceState& surface,
                  int configuredFps) noexcept;

bool IsGlobalPause(PauseReason reason) noexcept;

struct Rect {
    int left = 0;
    int top = 0;
    int right = 0;
    int bottom = 0;

    bool operator==(const Rect& other) const noexcept {
        return left == other.left && top == other.top
            && right == other.right && bottom == other.bottom;
    }
};

bool IsValid(const Rect& rect) noexcept;
bool Covers(const Rect& outer, const Rect& inner) noexcept;

// Returns true only when valid opaque rectangles jointly cover every pixel of
// the valid target rectangle. Invalid occluders are ignored.
bool IsFullyCovered(const Rect& target,
                    const std::vector<Rect>& opaqueRects);

} // namespace desktopgrass::runtime
