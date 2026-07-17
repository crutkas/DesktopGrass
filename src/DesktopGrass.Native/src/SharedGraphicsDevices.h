// SharedGraphicsDevices.h
//
// Process-wide D3D11 + DXGI + Direct2D + DirectComposition device graph.
// Exactly one instance is owned by the process host (App, or a standalone
// harness such as Benchmark) and outlives every GrassWindow/Renderer it
// backs. Renderer never creates a D3D11 device, DXGI device/factory, D2D
// factory/device, or DirectComposition device itself anymore — it borrows
// copies of these from a SharedGraphicsDevices the owner constructs once,
// and pulls fresh copies again after the owner discards and recreates the
// graph following a coordinated device loss.
//
// SharedGraphicsDevices only ever holds device-level (adapter-scoped)
// objects that are safe and correct to share across every monitor's
// surface. Anything intrinsically per-window — the D2D device context, swap
// chain, target bitmap, and composition target/visual — stays owned by
// Renderer exactly as before; this type has no knowledge of windows, sizes,
// or DPI.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <wrl/client.h>
#include <d3d11.h>
#include <dxgi1_3.h>
#include <d2d1_3.h>
#include <dcomp.h>

namespace desktopgrass {

class SharedGraphicsDevices {
public:
    template<class T> using ComPtr = Microsoft::WRL::ComPtr<T>;

    SharedGraphicsDevices() = default;
    ~SharedGraphicsDevices() = default;

    SharedGraphicsDevices(const SharedGraphicsDevices&)            = delete;
    SharedGraphicsDevices& operator=(const SharedGraphicsDevices&) = delete;

    // Creates the device graph from scratch (D3D11 device, falling back to
    // WARP; the DXGI device/factory it exposes; the D2D factory/device; and
    // the DirectComposition device). Safe to call again after Discard() to
    // recreate following a coordinated device loss. Returns false on
    // failure (logged via OutputDebugString); the graph is left fully
    // discarded so IsReady() reports false and no caller can observe a
    // half-built graph.
    bool Initialize();

    // Releases every device-level object this instance owns. Windows that
    // already hold copies of the old ComPtrs keep them alive via COM
    // refcounting, but must not draw with them again — the caller is
    // responsible for rebuilding every Renderer's per-window resources
    // after the next successful Initialize().
    void Discard();

    // Deliberately simple: true once Initialize() has produced a device,
    // false once Discard()'d. This is not a liveness check — callers detect
    // an actual device loss via their own Renderer/Present1 failures and
    // then drive Discard()+Initialize() explicitly.
    bool IsReady() const { return d3dDevice_ != nullptr; }

    ComPtr<ID3D11Device>        D3DDevice()   const { return d3dDevice_; }
    ComPtr<ID3D11DeviceContext> D3DContext()  const { return d3dContext_; }
    ComPtr<IDXGIDevice1>        DxgiDevice()  const { return dxgiDevice_; }
    ComPtr<IDXGIFactory2>       DxgiFactory() const { return dxgiFactory_; }
    ComPtr<ID2D1Factory1>       D2DFactory()  const { return d2dFactory_; }
    ComPtr<ID2D1Device>         D2DDevice()   const { return d2dDevice_; }
    ComPtr<IDCompositionDevice> DCompDevice() const { return dcompDevice_; }

private:
    ComPtr<ID3D11Device>        d3dDevice_;
    ComPtr<ID3D11DeviceContext> d3dContext_;
    ComPtr<IDXGIDevice1>        dxgiDevice_;
    ComPtr<IDXGIFactory2>       dxgiFactory_;
    ComPtr<ID2D1Factory1>       d2dFactory_;
    ComPtr<ID2D1Device>         d2dDevice_;
    ComPtr<IDCompositionDevice> dcompDevice_;
};

} // namespace desktopgrass
