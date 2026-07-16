// VisibilityTracker.h
//
// Event-driven Win32 visibility observation. WinEvent callbacks only post a
// coalesced message; HWND/DWM/z-order inspection stays on App's UI thread.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <atomic>
#include <vector>

#include "RuntimePolicy.h"

namespace desktopgrass {

class VisibilityTracker {
public:
    VisibilityTracker() = default;
    ~VisibilityTracker();

    VisibilityTracker(const VisibilityTracker&) = delete;
    VisibilityTracker& operator=(const VisibilityTracker&) = delete;

    bool Start(HWND notificationWindow, UINT notificationMessage);
    void Stop() noexcept;

    // Called by App when it handles notificationMessage.
    void AcknowledgeNotification() noexcept;

    // Returns the known-opaque foreground window bounds. App compares this
    // against each physical monitor so spanning fullscreen windows remain
    // per-monitor.
    bool TryGetForegroundBounds(runtime::Rect& bounds) const noexcept;

    // Conservatively reports full coverage by known-opaque top-level windows
    // above targetHwnd in z-order.
    bool IsFullyOccluded(HWND targetHwnd,
                         const runtime::Rect& targetBounds) const;

private:
    static void CALLBACK WinEventProc(HWINEVENTHOOK hook,
                                      DWORD event,
                                      HWND hwnd,
                                      LONG objectId,
                                      LONG childId,
                                      DWORD eventThread,
                                      DWORD eventTime);

    bool AddHook(DWORD eventMin, DWORD eventMax);
    void Notify() noexcept;

    static VisibilityTracker* instance_;

    HWND notificationWindow_ = nullptr;
    UINT notificationMessage_ = 0;
    std::vector<HWINEVENTHOOK> hooks_;
    std::atomic<bool> notificationPending_{false};
};

} // namespace desktopgrass
