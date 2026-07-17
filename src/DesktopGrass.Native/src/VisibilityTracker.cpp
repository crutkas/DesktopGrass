// VisibilityTracker.cpp

#include "VisibilityTracker.h"

#include <dwmapi.h>

#include <algorithm>
#include <atomic>
#include <cwchar>
#include <iterator>

#pragma comment(lib, "Dwmapi.lib")

namespace desktopgrass {

namespace {

runtime::Rect ToRuntimeRect(const RECT& rect) noexcept {
    return runtime::Rect{rect.left, rect.top, rect.right, rect.bottom};
}

bool TryGetCloaked(HWND hwnd, bool& cloaked) noexcept {
    DWORD cloakState = 0;
    const HRESULT result = DwmGetWindowAttribute(
        hwnd, DWMWA_CLOAKED, &cloakState, sizeof(cloakState));
    if (FAILED(result)) return false;
    cloaked = cloakState != 0;
    return true;
}

bool IsShellSurface(HWND hwnd) noexcept {
    if (hwnd == GetShellWindow() || hwnd == GetDesktopWindow()) {
        return true;
    }

    wchar_t className[64]{};
    if (GetClassNameW(
            hwnd, className, static_cast<int>(std::size(className))) == 0) {
        return false;
    }

    return std::wcscmp(className, L"Progman") == 0
        || std::wcscmp(className, L"WorkerW") == 0
        || std::wcscmp(className, L"Shell_TrayWnd") == 0
        || std::wcscmp(className, L"Shell_SecondaryTrayWnd") == 0;
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
    if (!hwnd
        || IsShellSurface(hwnd)
        || !IsWindowVisible(hwnd)
        || IsIconic(hwnd)) {
        return false;
    }

    bool cloaked = false;
    if (!TryGetCloaked(hwnd, cloaked) || cloaked) return false;

    SetLastError(ERROR_SUCCESS);
    const LONG_PTR exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
    if (exStyle == 0 && GetLastError() != ERROR_SUCCESS) return false;
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

struct VisibilityTracker::CallbackState {
    std::mutex mutex;
    HWND notificationWindow = nullptr;
    UINT notificationMessage = 0;
    bool active = true;
    std::atomic<bool> notificationPending{false};
};

std::mutex VisibilityTracker::callbackRegistryMutex_;
std::weak_ptr<VisibilityTracker::CallbackState>
    VisibilityTracker::callbackRegistry_;

VisibilityTracker::~VisibilityTracker() {
    Stop();
}

bool VisibilityTracker::Start(HWND notificationWindow,
                              UINT notificationMessage)
{
    if (!notificationWindow || notificationMessage < WM_APP) return false;
    if (!hooks_.empty()) {
        const std::shared_ptr<CallbackState> state = callbackState_;
        if (!state) return false;
        std::lock_guard<std::mutex> lock(state->mutex);
        return state->active
            && state->notificationWindow == notificationWindow
            && state->notificationMessage == notificationMessage;
    }

    const auto state = std::make_shared<CallbackState>();
    state->notificationWindow = notificationWindow;
    state->notificationMessage = notificationMessage;

    {
        std::lock_guard<std::mutex> lock(callbackRegistryMutex_);
        if (!callbackRegistry_.expired()) return false;
        callbackRegistry_ = state;
    }
    callbackState_ = state;

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
    const std::shared_ptr<CallbackState> state = callbackState_;
    if (state) {
        {
            std::lock_guard<std::mutex> lock(callbackRegistryMutex_);
            if (callbackRegistry_.lock() == state) {
                callbackRegistry_.reset();
            }
        }
        {
            std::lock_guard<std::mutex> lock(state->mutex);
            state->active = false;
            state->notificationWindow = nullptr;
            state->notificationMessage = 0;
            state->notificationPending.store(
                false, std::memory_order_release);
        }
    }

    for (auto it = hooks_.rbegin(); it != hooks_.rend(); ++it) {
        if (*it) UnhookWinEvent(*it);
    }
    hooks_.clear();
    callbackState_.reset();
}

void VisibilityTracker::AcknowledgeNotification() noexcept {
    const std::shared_ptr<CallbackState> state = callbackState_;
    if (state) {
        state->notificationPending.store(false, std::memory_order_release);
    }
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

void CALLBACK VisibilityTracker::WinEventProc(
    HWINEVENTHOOK,
    DWORD event,
    HWND,
    LONG objectId,
    LONG childId,
    DWORD,
    DWORD)
{
    if (event >= EVENT_OBJECT_CREATE) {
        if (objectId != OBJID_WINDOW || childId != CHILDID_SELF) return;
    }

    std::shared_ptr<CallbackState> state;
    {
        std::lock_guard<std::mutex> lock(callbackRegistryMutex_);
        state = callbackRegistry_.lock();
    }
    if (!state) return;

    std::lock_guard<std::mutex> lock(state->mutex);
    if (!state->active
        || !state->notificationWindow
        || state->notificationMessage == 0) {
        return;
    }

    bool expected = false;
    if (state->notificationPending.compare_exchange_strong(
            expected, true, std::memory_order_acq_rel)) {
        if (!PostMessageW(
                state->notificationWindow,
                state->notificationMessage,
                0,
                0)) {
            state->notificationPending.store(
                false, std::memory_order_release);
        }
    }
}

} // namespace desktopgrass
