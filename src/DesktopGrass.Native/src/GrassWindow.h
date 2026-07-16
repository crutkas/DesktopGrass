// GrassWindow.h
//
// One HWND + one Renderer per monitor. Layered, click-through, topmost,
// no-activate, tool-window — see WS_EX flags listed in the plan and asserted
// by tests/smoke.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <memory>

#include "DisplayTopology.h"
#include "Renderer.h"
#include "MouseHook.h"

namespace desktopgrass {

class GrassWindow {
public:
    static constexpr const wchar_t* kWindowClassName = L"DesktopGrass.Native.Window";
    static constexpr UINT           kWmAppQuit       = WM_APP + 1;
    static constexpr UINT           kWmAppDisplayChanged = WM_APP + 2;
    static constexpr UINT           kWmAppSessionEnding = WM_APP + 3;

    static bool RegisterWindowClass(HINSTANCE hInst);

    GrassWindow() = default;
    ~GrassWindow();

    GrassWindow(const GrassWindow&)            = delete;
    GrassWindow& operator=(const GrassWindow&) = delete;

    // Creates the HWND, attaches a Renderer, generates blades using `seed`.
    bool Create(HINSTANCE hInst,
                HWND displayChangeHwnd,
                const topology::MonitorSnapshot& monitor,
                const topology::SurfaceSpec& surface,
                uint64_t seed, double density,
                double swaySpeed = 1.0, double swayAmplitude = 1.0);

    void Show();
    void Destroy();
    bool MoveTo(const topology::MonitorSnapshot& monitor,
                const topology::SurfaceSpec& surface);
    void UpdateTopology(const topology::MonitorSnapshot& monitor,
                       const topology::SurfaceSpec& surface);
    void RenderFrame(double dt,
                     const InputEvent* events, std::size_t numEvents);

    HWND      GetHwnd()  const { return hwnd_; }
    UINT      GetDpi() const { return surface_.dpi; }
    Renderer& GetRenderer()     { return renderer_; }
    const topology::MonitorSnapshot& GetMonitor() const { return monitor_; }
    const topology::SurfaceSpec& GetSurface() const { return surface_; }
    uint64_t GetLayoutSeed() const { return layoutSeed_; }

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);
    LRESULT HandleMessage(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);

    HWND       hwnd_ = nullptr;
    HWND       displayChangeHwnd_ = nullptr;
    Renderer   renderer_;
    topology::MonitorSnapshot monitor_{};
    topology::SurfaceSpec surface_{};
    uint64_t layoutSeed_ = 0;
};

} // namespace desktopgrass
