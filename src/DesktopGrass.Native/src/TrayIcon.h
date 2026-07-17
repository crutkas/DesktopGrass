// TrayIcon.h
//
// Pure notification-area behavior shared by App and deterministic tests.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>

namespace desktopgrass::tray {

inline constexpr wchar_t kAccessibleName[] = L"Desktop Grass controls";

inline bool IsMenuActivation(UINT notification) noexcept {
    return notification == WM_RBUTTONUP
        || notification == WM_CONTEXTMENU
        || notification == NIN_SELECT
        || notification == NIN_KEYSELECT;
}

inline bool UsesIconRectAnchor(UINT notification) noexcept {
    return notification == WM_CONTEXTMENU
        || notification == NIN_KEYSELECT;
}

inline POINT CallbackAnchor(WPARAM callbackCoordinates) noexcept {
    return POINT{
        static_cast<short>(LOWORD(callbackCoordinates)),
        static_cast<short>(HIWORD(callbackCoordinates)),
    };
}

} // namespace desktopgrass::tray
