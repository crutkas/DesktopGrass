// Renderer.cpp

#include "Renderer.h"

#include <algorithm>
#include <cmath>
#include <cstdio>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dcomp.lib")

namespace desktopgrass {

namespace {

inline D2D1::ColorF FromArgb(uint32_t argb) {
    const float a = ((argb >> 24) & 0xFF) / 255.0f;
    const float r = ((argb >> 16) & 0xFF) / 255.0f;
    const float g = ((argb >>  8) & 0xFF) / 255.0f;
    const float b = ( argb        & 0xFF) / 255.0f;
    return D2D1::ColorF(r, g, b, a);
}

void LogHR(const char* tag, HRESULT hr) {
    char buf[128];
    std::snprintf(buf, sizeof(buf), "[DesktopGrass] %s failed: 0x%08lX\n",
                  tag, static_cast<unsigned long>(hr));
    OutputDebugStringA(buf);
}

} // anonymous

Renderer::~Renderer() {
    Cleanup();
}

void Renderer::Cleanup() {
    DiscardDeviceResources();
    dcompVisual_.Reset();
    dcompTarget_.Reset();
    dcompDevice_.Reset();
    initialized_ = false;
}

bool Renderer::CreateDeviceResources() {
    HRESULT hr = S_OK;

    UINT d3dFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;
#ifdef _DEBUG
    // d3dFlags |= D3D11_CREATE_DEVICE_DEBUG; // skip — requires SDK debug layer
#endif

    static const D3D_FEATURE_LEVEL kFeatures[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1,
        D3D_FEATURE_LEVEL_10_0,
    };

    hr = D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, d3dFlags,
        kFeatures, ARRAYSIZE(kFeatures), D3D11_SDK_VERSION,
        d3dDevice_.ReleaseAndGetAddressOf(), nullptr,
        d3dContext_.ReleaseAndGetAddressOf());

    if (FAILED(hr)) {
        // Fall back to WARP (software).
        hr = D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_WARP, nullptr, d3dFlags,
            kFeatures, ARRAYSIZE(kFeatures), D3D11_SDK_VERSION,
            d3dDevice_.ReleaseAndGetAddressOf(), nullptr,
            d3dContext_.ReleaseAndGetAddressOf());
        if (FAILED(hr)) { LogHR("D3D11CreateDevice", hr); return false; }
    }

    hr = d3dDevice_.As(&dxgiDevice_);
    if (FAILED(hr)) { LogHR("d3dDevice.As<IDXGIDevice1>", hr); return false; }
    dxgiDevice_->SetMaximumFrameLatency(1);

    ComPtr<IDXGIAdapter> adapter;
    hr = dxgiDevice_->GetAdapter(&adapter);
    if (FAILED(hr)) { LogHR("GetAdapter", hr); return false; }
    hr = adapter->GetParent(IID_PPV_ARGS(dxgiFactory_.ReleaseAndGetAddressOf()));
    if (FAILED(hr)) { LogHR("adapter.GetParent<IDXGIFactory2>", hr); return false; }

    D2D1_FACTORY_OPTIONS opts{};
    hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,
                           __uuidof(ID2D1Factory1), &opts,
                           reinterpret_cast<void**>(d2dFactory_.ReleaseAndGetAddressOf()));
    if (FAILED(hr)) { LogHR("D2D1CreateFactory", hr); return false; }

    hr = d2dFactory_->CreateDevice(dxgiDevice_.Get(),
                                   d2dDevice_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateDevice(D2D)", hr); return false; }

    hr = d2dDevice_->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,
                                         d2dContext_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateDeviceContext", hr); return false; }

    d2dContext_->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    d2dContext_->SetDpi(static_cast<float>(dpi_), static_cast<float>(dpi_));

    // DComp device tied to the same DXGI device.
    hr = DCompositionCreateDevice(dxgiDevice_.Get(),
                                  __uuidof(IDCompositionDevice),
                                  reinterpret_cast<void**>(dcompDevice_.ReleaseAndGetAddressOf()));
    if (FAILED(hr)) { LogHR("DCompositionCreateDevice", hr); return false; }

    hr = dcompDevice_->CreateTargetForHwnd(hwnd_, TRUE, dcompTarget_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateTargetForHwnd", hr); return false; }

    hr = dcompDevice_->CreateVisual(dcompVisual_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateVisual", hr); return false; }

    // Pre-create palette brushes.
    for (int i = 0; i < PALETTE_SIZE; ++i) {
        brushes_[i].Reset();
        hr = d2dContext_->CreateSolidColorBrush(FromArgb(PALETTE[i]),
                                                brushes_[i].ReleaseAndGetAddressOf());
        if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }
    }

    return true;
}

bool Renderer::CreateSwapChainResources(int widthPx, int heightPx) {
    if (widthPx <= 0 || heightPx <= 0) return false;

    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width            = static_cast<UINT>(widthPx);
    desc.Height           = static_cast<UINT>(heightPx);
    desc.Format           = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.Stereo           = FALSE;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage      = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount      = 2;
    desc.Scaling          = DXGI_SCALING_STRETCH;
    desc.SwapEffect       = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.AlphaMode        = DXGI_ALPHA_MODE_PREMULTIPLIED;
    desc.Flags            = 0;

    HRESULT hr = dxgiFactory_->CreateSwapChainForComposition(
        d3dDevice_.Get(), &desc, nullptr,
        swapChain_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSwapChainForComposition", hr); return false; }

    ComPtr<IDXGISurface2> surface;
    hr = swapChain_->GetBuffer(0, IID_PPV_ARGS(&surface));
    if (FAILED(hr)) { LogHR("swapChain.GetBuffer", hr); return false; }

    D2D1_BITMAP_PROPERTIES1 bp = D2D1::BitmapProperties1(
        D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED),
        static_cast<float>(dpi_), static_cast<float>(dpi_));

    hr = d2dContext_->CreateBitmapFromDxgiSurface(surface.Get(), &bp,
                                                  d2dTarget_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateBitmapFromDxgiSurface", hr); return false; }

    d2dContext_->SetTarget(d2dTarget_.Get());

    hr = dcompVisual_->SetContent(swapChain_.Get());
    if (FAILED(hr)) { LogHR("Visual.SetContent", hr); return false; }

    hr = dcompTarget_->SetRoot(dcompVisual_.Get());
    if (FAILED(hr)) { LogHR("Target.SetRoot", hr); return false; }

    hr = dcompDevice_->Commit();
    if (FAILED(hr)) { LogHR("DComp Commit", hr); return false; }

    return true;
}

void Renderer::DiscardDeviceResources() {
    for (auto& b : brushes_) b.Reset();
    d2dTarget_.Reset();
    if (d2dContext_) d2dContext_->SetTarget(nullptr);
    d2dContext_.Reset();
    d2dDevice_.Reset();
    d2dFactory_.Reset();
    swapChain_.Reset();
    dxgiFactory_.Reset();
    dxgiDevice_.Reset();
    d3dContext_.Reset();
    d3dDevice_.Reset();
}

bool Renderer::Initialize(HWND hwnd, int widthPx, int heightPx,
                          UINT dpi, uint64_t seed, double density)
{
    hwnd_     = hwnd;
    widthPx_  = widthPx;
    heightPx_ = heightPx;
    dpi_      = dpi == 0 ? 96 : dpi;

    if (!CreateDeviceResources())   return false;
    if (!CreateSwapChainResources(widthPx, heightPx)) return false;

    const double widthDip  = static_cast<double>(widthPx)  * 96.0 / static_cast<double>(dpi_);
    const double heightDip = static_cast<double>(heightPx) * 96.0 / static_cast<double>(dpi_);
    sim_ = sim_init(seed, widthDip, density);
    sim_.windowHeight = heightDip;
    initialized_ = true;
    return true;
}

bool Renderer::Resize(int widthPx, int heightPx, UINT dpi) {
    if (!initialized_) return false;
    if (widthPx <= 0 || heightPx <= 0) return false;

    widthPx_  = widthPx;
    heightPx_ = heightPx;
    dpi_      = dpi == 0 ? 96 : dpi;

    // Discard render-target view.
    d2dTarget_.Reset();
    if (d2dContext_) d2dContext_->SetTarget(nullptr);

    HRESULT hr = swapChain_->ResizeBuffers(
        0, static_cast<UINT>(widthPx), static_cast<UINT>(heightPx),
        DXGI_FORMAT_UNKNOWN, 0);
    if (FAILED(hr)) {
        LogHR("ResizeBuffers", hr);
        return false;
    }

    ComPtr<IDXGISurface2> surface;
    hr = swapChain_->GetBuffer(0, IID_PPV_ARGS(&surface));
    if (FAILED(hr)) { LogHR("swapChain.GetBuffer(resize)", hr); return false; }

    D2D1_BITMAP_PROPERTIES1 bp = D2D1::BitmapProperties1(
        D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED),
        static_cast<float>(dpi_), static_cast<float>(dpi_));

    hr = d2dContext_->CreateBitmapFromDxgiSurface(surface.Get(), &bp,
                                                  d2dTarget_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateBitmapFromDxgiSurface(resize)", hr); return false; }

    d2dContext_->SetTarget(d2dTarget_.Get());
    d2dContext_->SetDpi(static_cast<float>(dpi_), static_cast<float>(dpi_));

    const double heightDip = static_cast<double>(heightPx) * 96.0 / static_cast<double>(dpi_);
    sim_.windowHeight = heightDip;
    return true;
}

void Renderer::Tick(double dt,
                    const InputEvent* events,
                    std::size_t numEvents)
{
    sim_tick(sim_, clamp_dt(dt), events, numEvents);
}

void Renderer::RenderFrame(double dt,
                           const InputEvent* events,
                           std::size_t numEvents)
{
    if (!initialized_) return;

    Tick(dt, events, numEvents);

    d2dContext_->BeginDraw();
    // Fully transparent background so the layered window stays click-through.
    d2dContext_->Clear(D2D1::ColorF(0.0f, 0.0f, 0.0f, 0.0f));

    DrawGrass();

    HRESULT hr = d2dContext_->EndDraw();
    if (hr == D2DERR_RECREATE_TARGET) {
        DiscardDeviceResources();
        dcompVisual_.Reset();
        dcompTarget_.Reset();
        dcompDevice_.Reset();
        initialized_ = false;
        if (CreateDeviceResources() && CreateSwapChainResources(widthPx_, heightPx_)) {
            initialized_ = true;
        }
        return;
    }
    if (FAILED(hr)) { LogHR("EndDraw", hr); }

    DXGI_PRESENT_PARAMETERS pp{};
    hr = swapChain_->Present1(1, 0, &pp);
    if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET) {
        DiscardDeviceResources();
        dcompVisual_.Reset();
        dcompTarget_.Reset();
        dcompDevice_.Reset();
        initialized_ = false;
        if (CreateDeviceResources() && CreateSwapChainResources(widthPx_, heightPx_)) {
            initialized_ = true;
        }
    } else if (FAILED(hr)) {
        LogHR("Present1", hr);
    }
}

void Renderer::DrawGrass() {
    const double groundY = sim_.windowHeight;

    ComPtr<ID2D1Factory> factoryGeneric;
    d2dFactory_.As(&factoryGeneric);

    for (const Blade& b : sim_.blades) {
        const Stroke s = compute_blade_stroke(b, groundY);

        // Path: line from base, quadratic Bezier to tip via control.
        ComPtr<ID2D1PathGeometry> path;
        if (FAILED(d2dFactory_->CreatePathGeometry(&path))) continue;

        ComPtr<ID2D1GeometrySink> sink;
        if (FAILED(path->Open(&sink))) continue;

        sink->BeginFigure(
            D2D1::Point2F(static_cast<float>(s.base.x),
                          static_cast<float>(s.base.y)),
            D2D1_FIGURE_BEGIN_HOLLOW);

        D2D1_QUADRATIC_BEZIER_SEGMENT seg{};
        seg.point1 = D2D1::Point2F(static_cast<float>(s.control.x),
                                   static_cast<float>(s.control.y));
        seg.point2 = D2D1::Point2F(static_cast<float>(s.tip.x),
                                   static_cast<float>(s.tip.y));
        sink->AddQuadraticBezier(seg);

        sink->EndFigure(D2D1_FIGURE_END_OPEN);
        if (FAILED(sink->Close())) continue;

        ID2D1SolidColorBrush* brush = brushes_[b.hue].Get();
        d2dContext_->DrawGeometry(path.Get(), brush,
                                  static_cast<float>(s.thickness));
    }
}

} // namespace desktopgrass
