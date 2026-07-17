// GrassWindow.cpp

#include "GrassWindow.h"

#pragma comment(lib, "User32.lib")

namespace desktopgrass {

bool GrassWindow::RegisterWindowClass(HINSTANCE hInst) {
    WNDCLASSEXW wc{};
    wc.cbSize        = sizeof(wc);
    wc.style         = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc   = GrassWindow::WndProc;
    wc.hInstance     = hInst;
    wc.lpszClassName = kWindowClassName;
    wc.hCursor       = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr; // we paint everything; never let GDI clear

    ATOM atom = RegisterClassExW(&wc);
    if (atom == 0) {
        DWORD err = GetLastError();
        if (err != ERROR_CLASS_ALREADY_EXISTS) {
            return false;
        }
    }
    return true;
}

GrassWindow::~GrassWindow() {
    Destroy();
}

bool GrassWindow::Create(HINSTANCE hInst,
                         HWND displayChangeHwnd,
                         const topology::MonitorSnapshot& monitor,
                         const topology::SurfaceSpec& surface,
                         uint64_t seed, double density,
                         double swaySpeed, double swayAmplitude)
{
    if (surface.widthPx <= 0 || surface.heightPx <= 0
        || surface.dpi == 0 || monitor.dpi != surface.dpi) {
        return false;
    }

    displayChangeHwnd_ = displayChangeHwnd;
    monitor_ = monitor;
    surface_ = surface;
    layoutSeed_ = seed;

    const DWORD exStyle =
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST |
        WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
    const DWORD style = WS_POPUP;

    hwnd_ = CreateWindowExW(
        exStyle, kWindowClassName, L"Desktop Grass",
        style,
        surface_.x, surface_.y,
        surface_.widthPx, surface_.heightPx,
        nullptr, nullptr, hInst, this);

    if (!hwnd_) {
        return false;
    }

    const UINT windowDpi = GetDpiForWindow(hwnd_);
    if (windowDpi == 0 || windowDpi != surface_.dpi) {
        OutputDebugStringA(
            "[DesktopGrass] monitor DPI changed while creating a surface\n");
        DestroyWindow(hwnd_);
        hwnd_ = nullptr;
        return false;
    }

    if (!renderer_.Initialize(
            hwnd_, surface_.widthPx, surface_.heightPx, surface_.dpi,
            seed, density,
                              swaySpeed, swayAmplitude)) {
        DestroyWindow(hwnd_);
        hwnd_ = nullptr;
        return false;
    }

    renderer_.SetWindowOriginScreen(surface_.x, surface_.y);
    return true;
}

void GrassWindow::Show() {
    if (hwnd_) {
        ShowWindow(hwnd_, SW_SHOWNOACTIVATE);
    }
}

void GrassWindow::Destroy() {
    if (hwnd_) {
        if (!DestroyWindow(hwnd_)) {
            OutputDebugStringA("[DesktopGrass] DestroyWindow failed\n");
        }
    }
}

bool GrassWindow::MoveTo(const topology::MonitorSnapshot& monitor,
                         const topology::SurfaceSpec& surface) {
    if (!hwnd_ || !topology::HasSameBackingSurface(surface_, surface)) {
        return false;
    }
    if (!SetWindowPos(
            hwnd_, HWND_TOPMOST,
            surface.x, surface.y, surface.widthPx, surface.heightPx,
            SWP_NOACTIVATE | SWP_NOOWNERZORDER)) {
        return false;
    }

    monitor_ = monitor;
    surface_ = surface;
    renderer_.SetWindowOriginScreen(surface_.x, surface_.y);
    return true;
}

void GrassWindow::UpdateTopology(
    const topology::MonitorSnapshot& monitor,
    const topology::SurfaceSpec& surface) {
    if (surface == surface_) {
        monitor_ = monitor;
    }
}

void GrassWindow::RenderFrame(double dt,
                              const InputEvent* events, std::size_t numEvents)
{
    renderer_.RenderFrame(dt, events, numEvents);
}

LRESULT CALLBACK GrassWindow::WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    GrassWindow* self = nullptr;
    if (msg == WM_NCCREATE) {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        self = reinterpret_cast<GrassWindow*>(cs->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        if (self) self->hwnd_ = hwnd;
    } else {
        self = reinterpret_cast<GrassWindow*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    }
    if (self) return self->HandleMessage(hwnd, msg, wp, lp);
    return DefWindowProcW(hwnd, msg, wp, lp);
}

LRESULT GrassWindow::HandleMessage(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
        case WM_CLOSE:
            // The smoke harness sends WM_CLOSE. Forward to the main thread as
            // a request to terminate the message loop.
            PostQuitMessage(0);
            return 0;

        case WM_QUERYENDSESSION:
            if (displayChangeHwnd_) {
                SendMessageW(displayChangeHwnd_, kWmAppSessionEnding, TRUE, lp);
            }
            return TRUE;

        case WM_ENDSESSION:
            if (displayChangeHwnd_) {
                SendMessageW(displayChangeHwnd_, kWmAppSessionEnding, wp, lp);
            }
            return 0;

        case WM_DPICHANGED: {
            // Resizing a live DirectComposition swap chain here can leave the
            // target detached if ResizeBuffers fails during the scale
            // transition. Defer a full per-monitor rebuild to App's message
            // loop, matching the managed implementation.
            if (displayChangeHwnd_) {
                PostMessageW(displayChangeHwnd_, kWmAppDisplayChanged, 0, 0);
            }
            return 0;
        }

        case WM_DESTROY:
            return 0;

        case WM_NCDESTROY:
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
            hwnd_ = nullptr;
            return DefWindowProcW(hwnd, msg, wp, lp);

        default:
            return DefWindowProcW(hwnd, msg, wp, lp);
    }
}

} // namespace desktopgrass
