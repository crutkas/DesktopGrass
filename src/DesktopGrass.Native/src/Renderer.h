// Renderer.h
//
// Per-window Direct2D + DXGI renderer attached to a DirectComposition target.
// Owns the swap chain, the D2D device context bound to it, and the per-window
// Sim. Renders the procedural grass once per frame.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <wrl/client.h>
#include <d3d11.h>
#include <dxgi1_3.h>
#include <d2d1_3.h>
#include <dcomp.h>

#include "Sim.h"

namespace desktopgrass {

class Renderer {
public:
    Renderer() = default;
    ~Renderer();

    // Sets up D3D / D2D / DComp on `hwnd` of the given width × height in DIPs,
    // and generates the initial blade list with `seed`. Returns false on
    // failure (logged via OutputDebugString).
    bool Initialize(HWND hwnd, int widthPx, int heightPx,
                    UINT dpi, uint64_t seed, double density);

    // Resize the swap chain & D2D target. Call when the monitor changes size
    // (DPI change, mode change). Leaves Sim intact; caller may regenerate it.
    bool Resize(int widthPx, int heightPx, UINT dpi);

    // Advance the simulation by `dt` seconds, then draw a frame.
    void RenderFrame(double dt,
                     const InputEvent* events,
                     std::size_t numEvents);

    // For windows that have been minimized / occluded: skip rendering but keep
    // the simulation alive.
    void Tick(double dt,
              const InputEvent* events,
              std::size_t numEvents);

    Sim&        GetSim()        { return sim_; }
    const Sim&  GetSim() const  { return sim_; }
    HWND        GetHwnd() const { return hwnd_; }

    void SetWindowOriginScreen(int x, int y) { windowOriginScreenX_ = x; windowOriginScreenY_ = y; }
    int  GetWindowOriginScreenX() const { return windowOriginScreenX_; }
    int  GetWindowOriginScreenY() const { return windowOriginScreenY_; }
    int  GetWidthPx() const  { return widthPx_; }
    int  GetHeightPx() const { return heightPx_; }
    UINT GetDpi() const      { return dpi_; }

private:
    template<class T> using ComPtr = Microsoft::WRL::ComPtr<T>;

    void Cleanup();
    bool CreateDeviceResources();
    bool CreateSwapChainResources(int widthPx, int heightPx);
    void DiscardDeviceResources();
    void DrawGrass();

    HWND                                   hwnd_ = nullptr;
    int                                    widthPx_   = 0;
    int                                    heightPx_  = 0;
    UINT                                   dpi_       = 96;
    int                                    windowOriginScreenX_ = 0;
    int                                    windowOriginScreenY_ = 0;

    ComPtr<ID3D11Device>                   d3dDevice_;
    ComPtr<ID3D11DeviceContext>            d3dContext_;
    ComPtr<IDXGIDevice1>                   dxgiDevice_;
    ComPtr<IDXGIFactory2>                  dxgiFactory_;
    ComPtr<IDXGISwapChain1>                swapChain_;
    ComPtr<ID2D1Factory1>                  d2dFactory_;
    ComPtr<ID2D1Device>                    d2dDevice_;
    ComPtr<ID2D1DeviceContext>             d2dContext_;
    ComPtr<ID2D1Bitmap1>                   d2dTarget_;
    ComPtr<ID2D1SolidColorBrush>           brushes_[SCENE_COUNT][PALETTE_SIZE];
    ComPtr<ID2D1SolidColorBrush>           flowerHeadBrushes_[FLOWER_PALETTE_SIZE];
    ComPtr<ID2D1SolidColorBrush>           mushroomCapBrushes_[MUSHROOM_PALETTE_SIZE];
    ComPtr<ID2D1SolidColorBrush>           mushroomStemBrush_;

    ComPtr<IDCompositionDevice>            dcompDevice_;
    ComPtr<IDCompositionTarget>            dcompTarget_;
    ComPtr<IDCompositionVisual>            dcompVisual_;

    Sim                                    sim_{};
    bool                                   initialized_ = false;
};

} // namespace desktopgrass
