// VisibilityTracker.cpp

#include "VisibilityTracker.h"

#include <dwmapi.h>

#include <algorithm>

#pragma comment(lib, "Dwmapi.lib")

namespace desktopgrass {

namespace {

runtime::Rect ToRuntimeRect(const RECT& rect) noexcept {
    return runtime::Rect{rect.left, rect.top, rect.right, rect.bottom};
}

bool IsCloaked(HWND hwnd) noexcept {
    DWORD cloaked = 0;
    return SUCCEEDED(DwmGetWindowAttribute(
               hwnd, DWMWA_CLOAKED, &cloaked, sizeof(cloaked)))
        && cloaked != 0;
}

bool TryGetWindowBounds(HWND hwnd, runtime::Rect& bounds) noexcept {
    RECT rect{};
    if (SUCCEEDED(DwmGetWindowAttribute(
            hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &rect, sizeof(rect)))
        && runtime::IsValid(ToRuntimeRect(rect))) {
        bounds = ToRuntimeRect(rect);
        return true;
    }

    return GetWindowRect(hwnd, &rect)
        && runtime::IsValid(bounds = ToRuntimeRect(rect));
}

bool IsKnownOpaque(HWND hwnd) noexcept {
    if (!hwnd || !IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd)) {
        return false;
    }

    const LONG_PTR exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
    if ((exStyle & WS_EX_TRANSPARENT) != 0) return false;
    if ((exStyle & WS_EX_NOREDIRECTIONBITMAP) != 0) return false;

    if ((exStyle & WS_EX_LAYERED) != 0) {
        COLORREF colorKey = 0;
        BYTE alpha = 0;
        DWORD flags = 0;
        if (!GetLayeredWindowAttributes(hwnd, &colorKey, &alpha, &flags)) {
            // UpdateLayeredWindow/per-pixel alpha has no queryable opacity.
            return false;
        }
        if ((flags & LWA_COLORKEY) != 0) return false;
        if ((flags & LWA_ALPHA) == 0 || alpha != 255) return false;
    }

    return true;
}

bool TryGetKnownOpaqueBounds(HWND hwnd, runtime::Rect& bounds) noexcept {
    if (!IsKnownOpaque(hwnd)) return false;

    HRGN region = CreateRectRgn(0, 0, 0, 0);
    if (!region) return false;

    const int regionType = GetWindowRgn(hwnd, region);
    if (regionType == NULLREGION || regionType == COMPLEXREGION) {
        DeleteObject(region);
        return false;
    }

    if (regionType == SIMPLEREGION) {
        RECT regionBox{};
        RECT windowRect{};
        const int boxType = GetRgnBox(region, &regionBox);
        DeleteObject(region);
        if (boxType != SIMPLEREGION || !GetWindowRect(hwnd, &windowRect)) {
            return false;
        }

        bounds = runtime::Rect{
            windowRect.left + regionBox.left,
            windowRect.top + regionBox.top,
            windowRect.left + regionBox.right,
            windowRect.top + regionBox.bottom,
        };
        return runtime::IsValid(bounds);
    }

    DeleteObject(region);
    return TryGetWindowBounds(hwnd, bounds);
}

struct OcclusionContext {
    HWND target = nullptr;
    DWORD processId = 0;
    bool foundTarget = false;
    std::vector<runtime::Rect> opaqueRects;
};

BOOL CALLBACK CollectOccludingWindows(HWND hwnd, LPARAM value) {
    auto* context = reinterpret_cast<OcclusionContext*>(value);
    if (hwnd == context->target) {
        context->foundTarget = true;
        return FALSE;
    }

    DWORD processId = 0;
    GetWindowThreadProcessId(hwnd, &processId);
    if (processId == context->processId) return TRUE;

    runtime::Rect bounds;
    if (TryGetKnownOpaqueBounds(hwnd, bounds)) {
        context->opaqueRects.push_back(bounds);
    }
    return TRUE;
}

} // anonymous namespace

VisibilityTracker* VisibilityTracker::instance_ = nullptr;

VisibilityTracker::~VisibilityTracker() {
    Stop();
}

bool VisibilityTracker::Start(HWND notificationWindow,
                              UINT notificationMessage)
{
    if (!notificationWindow || notificationMessage < WM_APP) return false;
    if (instance_ && instance_ != this) return false;
    if (!hooks_.empty()) {
        return notificationWindow_ == notificationWindow
            && notificationMessage_ == notificationMessage;
    }

    notificationWindow_ = notificationWindow;
    notificationMessage_ = notificationMessage;
    notificationPending_.store(false, std::memory_order_release);
    instance_ = this;

    const bool ok =
        AddHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND)
        && AddHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND)
        && AddHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND)
        && AddHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_REORDER)
        && AddHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE)
        && AddHook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED);

    if (!ok) {
        Stop();
        return false;
    }
    return true;
}

void VisibilityTracker::Stop() noexcept {
    for (HWINEVENTHOOK hook : hooks_) {
        if (hook) UnhookWinEvent(hook);
    }
    hooks_.clear();
    if (instance_ == this) instance_ = nullptr;
    notificationPending_.store(false, std::memory_order_release);
    notificationWindow_ = nullptr;
    notificationMessage_ = 0;
}

void VisibilityTracker::AcknowledgeNotification() noexcept {
    notificationPending_.store(false, std::memory_order_release);
}

bool VisibilityTracker::TryGetForegroundBounds(
    runtime::Rect& bounds) const noexcept
{
    HWND foreground = GetForegroundWindow();
    if (!foreground) return false;

    DWORD processId = 0;
    GetWindowThreadProcessId(foreground, &processId);
    if (processId == GetCurrentProcessId()) return false;

    return TryGetKnownOpaqueBounds(foreground, bounds);
}

bool VisibilityTracker::IsFullyOccluded(
    HWND targetHwnd,
    const runtime::Rect& targetBounds) const
{
    if (!targetHwnd || !runtime::IsValid(targetBounds)) return false;

    OcclusionContext context;
    context.target = targetHwnd;
    context.processId = GetCurrentProcessId();
    EnumWindows(CollectOccludingWindows, reinterpret_cast<LPARAM>(&context));
    return context.foundTarget
        && runtime::IsFullyCovered(targetBounds, context.opaqueRects);
}

bool VisibilityTracker::AddHook(DWORD eventMin, DWORD eventMax) {
    HWINEVENTHOOK hook = SetWinEventHook(
        eventMin, eventMax,
        nullptr, WinEventProc,
        0, 0,
        WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    if (!hook) return false;
    hooks_.push_back(hook);
    return true;
}

void VisibilityTracker::Notify() noexcept {
    if (!notificationWindow_ || notificationMessage_ == 0) return;

    bool expected = false;
    if (notificationPending_.compare_exchange_strong(
            expected, true, std::memory_order_acq_rel)) {
        if (!PostMessageW(
                notificationWindow_, notificationMessage_, 0, 0)) {
            notificationPending_.store(false, std::memory_order_release);
        }
    }
}

void CALLBACK VisibilityTracker::WinEventProc(
    HWINEVENTHOOK,
    DWORD event,
    HWND,
    LONG objectId,
    LONG childId,
    DWORD,
    DWORD)
{
    VisibilityTracker* tracker = instance_;
    if (!tracker) return;

    if (event >= EVENT_OBJECT_CREATE) {
        if (objectId != OBJID_WINDOW || childId != CHILDID_SELF) return;
    }
    tracker->Notify();
}

} // namespace desktopgrass
