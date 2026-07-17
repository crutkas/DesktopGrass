// RuntimeNotifications.h
//
// Receiver-neutral ownership and decoding for power, suspend, and session
// notifications. The current receiver is App's message window; callers can
// move that responsibility to another HWND without changing RuntimePolicy.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "RuntimePolicy.h"

namespace desktopgrass {

struct RuntimeNotificationApi {
    HPOWERNOTIFY (*registerPowerSetting)(HWND receiver, const GUID& setting);
    void (*unregisterPowerSetting)(HPOWERNOTIFY notification);
    HPOWERNOTIFY (*registerSuspendResume)(HWND receiver);
    void (*unregisterSuspendResume)(HPOWERNOTIFY notification);
    bool (*registerSession)(HWND receiver);
    void (*unregisterSession)(HWND receiver);
    void (*seedState)(runtime::GlobalState& state, DWORD& sessionId);
    runtime::SessionState (*querySessionState)(DWORD sessionId);
};

const RuntimeNotificationApi& GetSystemRuntimeNotificationApi() noexcept;

struct RuntimeNotificationResult {
    bool handled = false;
    bool stateChanged = false;
    bool visibilityChanged = false;
    LRESULT result = 0;
};

class RuntimeNotifications {
public:
    RuntimeNotifications() noexcept;
    explicit RuntimeNotifications(const RuntimeNotificationApi& api) noexcept;
    ~RuntimeNotifications();

    RuntimeNotifications(const RuntimeNotifications&) = delete;
    RuntimeNotifications& operator=(const RuntimeNotifications&) = delete;

    bool Start(HWND receiver) noexcept;
    void Stop() noexcept;

    RuntimeNotificationResult Dispatch(
        UINT message, WPARAM wParam, LPARAM lParam) noexcept;

    const runtime::GlobalState& State() const noexcept { return state_; }
    bool IsStarted() const noexcept { return started_; }

private:
    RuntimeNotificationResult HandlePowerBroadcast(
        WPARAM event, LPARAM data) noexcept;
    RuntimeNotificationResult HandleSessionChange(
        WPARAM event, LPARAM sessionId) noexcept;

    RuntimeNotificationApi api_;
    runtime::GlobalState state_{};
    HWND receiver_ = nullptr;
    HPOWERNOTIFY acdcPowerNotification_ = nullptr;
    HPOWERNOTIFY saverNotification_ = nullptr;
    HPOWERNOTIFY displayNotification_ = nullptr;
    HPOWERNOTIFY suspendResumeNotification_ = nullptr;
    DWORD sessionId_ = 0xFFFFFFFFu;
    bool wtsNotificationRegistered_ = false;
    bool started_ = false;
};

} // namespace desktopgrass
