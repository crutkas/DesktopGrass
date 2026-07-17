// MouseHook.cpp

#include "MouseHook.h"

#include <atomic>
#include <chrono>

namespace desktopgrass {

namespace {

std::atomic<MouseEventQueue*> g_queue{nullptr};
std::atomic<bool>             g_registrationActive{false};
LARGE_INTEGER                 g_qpcFreq{};
LARGE_INTEGER                 g_qpcStart{};

HHOOK install_platform_hook(HOOKPROC callback) noexcept {
    return SetWindowsHookExW(
        WH_MOUSE_LL, callback, GetModuleHandleW(nullptr), 0);
}

BOOL uninstall_platform_hook(HHOOK hook) noexcept {
    return UnhookWindowsHookEx(hook);
}

constexpr MouseHookPlatform kDefaultPlatform{
    install_platform_hook,
    uninstall_platform_hook,
};

double now_seconds() noexcept {
    LARGE_INTEGER c;
    QueryPerformanceCounter(&c);
    return static_cast<double>(c.QuadPart - g_qpcStart.QuadPart) /
           static_cast<double>(g_qpcFreq.QuadPart);
}

LRESULT CALLBACK LowLevelMouseProc(int nCode, WPARAM wParam, LPARAM lParam) {
    // Per spec: always pass the event through. Never consume.
    if (nCode == HC_ACTION) {
        MouseEventQueue* q = g_queue.load(std::memory_order_acquire);
        if (q) {
            const MSLLHOOKSTRUCT* m = reinterpret_cast<const MSLLHOOKSTRUCT*>(lParam);
            RawMouseEvent ev{};
            ev.timeSeconds = now_seconds();
            ev.screenX     = m->pt.x;
            ev.screenY     = m->pt.y;

            switch (wParam) {
                case WM_MOUSEMOVE:
                    ev.type = EventType::Move;
                    q->push(ev);
                    break;
                case WM_LBUTTONDOWN:
                    ev.type = EventType::Click;
                    q->push(ev);
                    break;
                default:
                    break;
            }
        }
    }
    return CallNextHookEx(nullptr, nCode, wParam, lParam);
}

} // anonymous

MouseHookRegistration::MouseHookRegistration() noexcept
    : platform_(kDefaultPlatform) {}

MouseHookRegistration::MouseHookRegistration(
    MouseHookPlatform platform) noexcept
    : platform_(platform) {}

MouseHookRegistration::~MouseHookRegistration() {
    Reset();
}

bool MouseHookRegistration::Install(MouseEventQueue* queue) noexcept {
    if (!queue || hook_ || !platform_.install || !platform_.uninstall) {
        return false;
    }

    bool expected = false;
    if (!g_registrationActive.compare_exchange_strong(
            expected, true, std::memory_order_acq_rel)) {
        return false;
    }

    if (g_qpcFreq.QuadPart == 0) {
        QueryPerformanceFrequency(&g_qpcFreq);
        QueryPerformanceCounter(&g_qpcStart);
    }

    g_queue.store(queue, std::memory_order_release);
    hook_ = platform_.install(LowLevelMouseProc);
    if (!hook_) {
        g_queue.store(nullptr, std::memory_order_release);
        g_registrationActive.store(false, std::memory_order_release);
        return false;
    }
    return true;
}

void MouseHookRegistration::Reset() noexcept {
    if (!hook_) return;

    g_queue.store(nullptr, std::memory_order_release);
    platform_.uninstall(hook_);
    hook_ = nullptr;
    g_registrationActive.store(false, std::memory_order_release);
}

} // namespace desktopgrass
