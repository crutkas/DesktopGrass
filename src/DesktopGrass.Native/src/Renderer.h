// Renderer.h
//
// Per-window Direct2D + DXGI renderer attached to a DirectComposition target.
// Owns the swap chain, the D2D device context bound to it, and the per-window
// Sim. Renders the procedural grass once per frame. Its backing width, height,
// and DPI are fixed for its lifetime; App replaces the owning GrassWindow when
// any of them changes.
//
// The D3D11 device, DXGI device/factory, D2D factory/device, and
// DirectComposition device are process-wide and owned by a
// SharedGraphicsDevices the caller passes to Initialize(). Renderer keeps a
// non-owning pointer to it and never creates, discards, or recreates that
// graph itself — only the process owner (App, Benchmark, or a test) does,
// exactly once per outage, via GraphicsDeviceRecovery.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <wrl/client.h>
#include <d3d11.h>
#include <dxgi1_3.h>
#include <d2d1_3.h>
#include <dcomp.h>
#include <dwrite.h>

#include <unordered_map>

#include "DeviceRecovery.h"
#include "SharedGraphicsDevices.h"
#include "Sim.h"

namespace desktopgrass {

struct RendererTestAccess;

class Renderer {
public:
    Renderer() = default;
    ~Renderer();

    // Borrows the process-wide device graph from `shared` (which must
    // already be Initialize()'d — Renderer never initializes it itself),
    // sets up the per-window D2D context / swap chain / composition target
    // on `hwnd` of the given width × height in DIPs, and generates the
    // initial blade list with `seed`. Returns false on failure (logged via
    // OutputDebugString).
    bool Initialize(SharedGraphicsDevices& shared,
                    HWND hwnd, int widthPx, int heightPx,
                    UINT dpi, uint64_t seed, double density,
                    double swaySpeed = 1.0, double swayAmplitude = 1.0);

    // Advance the simulation by `dt` seconds, then draw a frame.
    void RenderFrame(double dt,
                     const InputEvent* events,
                     std::size_t numEvents);

    // For windows that have been minimized / occluded: skip rendering but keep
    // the simulation alive.
    void Tick(double dt,
              const InputEvent* events,
              std::size_t numEvents);

    // True once this window's own per-window resources are built and
    // rendering normally. False while waiting for the process owner to
    // repair a shared device-loss this window alone cannot fix.
    bool IsGraphicsReady() const { return recovery_.IsReady(); }
    bool NeedsSharedDeviceRecovery() const {
        return sharedDeviceRecoveryRequired_;
    }

    // Drops every per-window graphics resource (D2D device context, swap
    // chain, target bitmap, composition target/visual, and all cached
    // brushes) without touching the shared device graph. Called by the
    // process owner right before it discards SharedGraphicsDevices, so no
    // window keeps drawing against a device that is about to disappear.
    void DiscardPerWindowResources();

    // Clears the confirmed shared-loss request once the process owner has
    // successfully replaced the graph. Per-window rebuilding may still fail,
    // but that must not cause repeated replacement of the healthy new graph.
    void AcknowledgeSharedDeviceRecovery() {
        sharedDeviceRecoveryRequired_ = false;
    }

    // Rebuilds this window's resources against the current shared graph.
    // Used both after process-wide replacement and for isolated per-window
    // retries that must not disturb healthy windows.
    bool RebuildPerWindowResources();

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
    friend struct RendererTestAccess;

    template<class T> using ComPtr = Microsoft::WRL::ComPtr<T>;

    void Cleanup();
    bool CreateDeviceResources();
    bool CreateSwapChainResources(int widthPx, int heightPx);
    DeviceRecoveryAttempt RecreateDeviceResources();
    void HandleDeviceLoss(const DeviceLossInfo& loss);
    void DiscardDeviceResources();
    void DiscardAllGraphicsResources();
    std::optional<DeviceLossInfo> DrawAndPresent();
    void DrawGrass(bool treesOnly, bool backgroundTrees);
    void DrawEntities(const D2D1_POINT_2F* cursorPosition);
    void DrawButterfly(const Entity& e);
    void DrawFirefly(const Entity& e);
    void DrawBird(const Entity& e);
    void DrawCoral(const Blade& b, float groundY);
    void DrawFish(const Entity& e);
    void DrawCat(const Entity& e, const D2D1_POINT_2F* cursorPosition);
    void DrawBunny(const Entity& e);
    void DrawHedgehog(const Entity& e);
    void DrawJimothy(const Entity& e);
    void DrawPetName(const Entity& e, const D2D1_POINT_2F* cursorPosition);
    bool TryGetCursorPositionDip(D2D1_POINT_2F& cursorPosition) const;

    HWND                                   hwnd_ = nullptr;
    int                                    widthPx_   = 0;
    int                                    heightPx_  = 0;
    UINT                                   dpi_       = 96;
    int                                    windowOriginScreenX_ = 0;
    int                                    windowOriginScreenY_ = 0;

    // Non-owning. Set by Initialize(); outlives this Renderer by contract
    // (the process owner declares its SharedGraphicsDevices before its
    // window list). Renderer only ever reads copies from it — it never
    // calls Initialize()/Discard() on it.
    SharedGraphicsDevices*                 shared_ = nullptr;

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
    ComPtr<ID2D1SolidColorBrush>           cactusBrush_;
    ComPtr<ID2D1StrokeStyle>               roundStrokeStyle_;
    ComPtr<ID2D1SolidColorBrush>           tumbleweedBrush_;
    ComPtr<ID2D1SolidColorBrush>           snowflakeBrush_;
    ComPtr<ID2D1SolidColorBrush>           leafBrushes_[LEAF_COLOR_COUNT];
    ComPtr<ID2D1SolidColorBrush>           snowTipBrush_;
    ComPtr<ID2D1SolidColorBrush>           snowBankShadowBrush_;
    ComPtr<ID2D1SolidColorBrush>           pineBrush_;
    ComPtr<ID2D1SolidColorBrush>           pineShadowBrush_;
    ComPtr<ID2D1SolidColorBrush>           pineHighlightBrush_;
    ComPtr<ID2D1SolidColorBrush>           birchBarkBrush_;
    ComPtr<ID2D1SolidColorBrush>           birchMarkBrush_;
    ComPtr<ID2D1SolidColorBrush>           mapleTrunkBrush_;
    ComPtr<ID2D1SolidColorBrush>           mapleTrunkDarkBrush_;
    ComPtr<ID2D1SolidColorBrush>           mapleCanopyBrushes_[MAPLE_CANOPY_COLOR_COUNT];
    ComPtr<ID2D1SolidColorBrush>           coralBrushes_[CORAL_COLOR_COUNT];
    ComPtr<ID2D1SolidColorBrush>           bubbleStrokeBrush_;
    ComPtr<ID2D1SolidColorBrush>           bubbleHighlightBrush_;
    ComPtr<ID2D1SolidColorBrush>           fishBrushes_[FISH_COLOR_COUNT];
    ComPtr<ID2D1SolidColorBrush>           fishFinBrush_;
    ComPtr<ID2D1SolidColorBrush>           sheepBodyBrush_;
    ComPtr<ID2D1SolidColorBrush>           sheepLegBrush_;
    ComPtr<ID2D1SolidColorBrush>           sheepFaceBrush_;
    ComPtr<ID2D1SolidColorBrush>           sheepEarBrush_;
    ComPtr<ID2D1SolidColorBrush>           sheepInkBrush_;
    struct CatCoatBrushSet {
        ComPtr<ID2D1SolidColorBrush> body;
        ComPtr<ID2D1SolidColorBrush> leg;
        ComPtr<ID2D1SolidColorBrush> face;
        ComPtr<ID2D1SolidColorBrush> ear;
        ComPtr<ID2D1SolidColorBrush> ink;
    };
    CatCoatBrushSet catCoatBrushes_[CAT_COAT_VARIANT_COUNT];
    ComPtr<ID2D1SolidColorBrush>           bunnyBodyBrush_;
    ComPtr<ID2D1SolidColorBrush>           bunnyBellyBrush_;
    ComPtr<ID2D1SolidColorBrush>           bunnyEarBrush_;
    ComPtr<ID2D1SolidColorBrush>           bunnyEarInnerBrush_;
    ComPtr<ID2D1SolidColorBrush>           bunnyTailBrush_;
    ComPtr<ID2D1SolidColorBrush>           bunnyEyeBrush_;
    ComPtr<ID2D1SolidColorBrush>           bunnyNoseBrush_;
    ComPtr<ID2D1SolidColorBrush>           hedgehogBodyBrush_;
    ComPtr<ID2D1SolidColorBrush>           hedgehogSpikeBrush_;
    ComPtr<ID2D1SolidColorBrush>           hedgehogSpikeTipBrush_;
    ComPtr<ID2D1SolidColorBrush>           hedgehogNoseBrush_;
    ComPtr<ID2D1SolidColorBrush>           hedgehogEyeBrush_;
    ComPtr<ID2D1SolidColorBrush>           jimothyBodyBrush_;
    ComPtr<ID2D1SolidColorBrush>           jimothyDarkBrush_;
    ComPtr<ID2D1SolidColorBrush>           jimothyFaceBrush_;
    ComPtr<ID2D1SolidColorBrush>           jimothyEarBrush_;
    ComPtr<ID2D1SolidColorBrush>           jimothyEyeBrush_;
    ComPtr<ID2D1SolidColorBrush>           butterflyBodyBrush_;
    ComPtr<ID2D1SolidColorBrush>           butterflyWingBrushes_[BUTTERFLY_COLOR_COUNT];
    ComPtr<ID2D1SolidColorBrush>           butterflyAccentBrushes_[BUTTERFLY_COLOR_COUNT];
    ComPtr<ID2D1SolidColorBrush>           fireflyBodyBrush_;
    ComPtr<ID2D1SolidColorBrush>           fireflyGlowBrush_;
    ComPtr<ID2D1SolidColorBrush>           birdBrush_;
    ComPtr<ID2D1SolidColorBrush>           petNameBrush_;
    ComPtr<ID2D1SolidColorBrush>           petNameShadowBrush_;
    ComPtr<IDWriteFactory>                 dwriteFactory_;
    ComPtr<IDWriteTextFormat>              petNameTextFormat_;

    ComPtr<IDCompositionDevice>            dcompDevice_;
    ComPtr<IDCompositionTarget>            dcompTarget_;
    ComPtr<IDCompositionVisual>            dcompVisual_;

    Sim                                    sim_{};
    std::unordered_map<uint64_t, double>   petNameLastHover_;
    DeviceRecoveryController               recovery_;
    bool                                   sharedDeviceRecoveryRequired_ = false;
};

} // namespace desktopgrass
