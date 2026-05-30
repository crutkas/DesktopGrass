// Renderer.cpp

#include "Renderer.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cwchar>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dcomp.lib")
#pragma comment(lib, "dwrite.lib")

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

constexpr float SHEEP_CURIOUS_VERTICAL_RADIUS_DIP = 120.0f;

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

    dwriteFactory_.Reset();
    hr = DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,
                             __uuidof(IDWriteFactory),
                             reinterpret_cast<IUnknown**>(dwriteFactory_.ReleaseAndGetAddressOf()));
    if (FAILED(hr)) { LogHR("DWriteCreateFactory", hr); return false; }

    petNameTextFormat_.Reset();
    hr = dwriteFactory_->CreateTextFormat(
        L"Segoe UI", nullptr,
        DWRITE_FONT_WEIGHT_REGULAR,
        DWRITE_FONT_STYLE_NORMAL,
        DWRITE_FONT_STRETCH_NORMAL,
        static_cast<FLOAT>(PET_NAME_FONT_SIZE),
        L"",
        petNameTextFormat_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateTextFormat", hr); return false; }
    petNameTextFormat_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
    petNameTextFormat_->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_NEAR);

    // DComp device tied to the same DXGI device.
    hr = DCompositionCreateDevice(dxgiDevice_.Get(),
                                  __uuidof(IDCompositionDevice),
                                  reinterpret_cast<void**>(dcompDevice_.ReleaseAndGetAddressOf()));
    if (FAILED(hr)) { LogHR("DCompositionCreateDevice", hr); return false; }

    hr = dcompDevice_->CreateTargetForHwnd(hwnd_, TRUE, dcompTarget_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateTargetForHwnd", hr); return false; }

    hr = dcompDevice_->CreateVisual(dcompVisual_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateVisual", hr); return false; }

    // Pre-create palette brushes for every scene (§13). Brushes are tiny
    // (a few floats each) and only ever read at draw time, so we cache
    // SCENE_COUNT × PALETTE_SIZE instead of recreating on scene change.
    for (int s = 0; s < SCENE_COUNT; ++s) {
        for (int i = 0; i < PALETTE_SIZE; ++i) {
            brushes_[s][i].Reset();
            hr = d2dContext_->CreateSolidColorBrush(FromArgb(SCENE_PALETTES[s][i]),
                                                    brushes_[s][i].ReleaseAndGetAddressOf());
            if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }
        }
    }

    for (int i = 0; i < FLOWER_PALETTE_SIZE; ++i) {
        flowerHeadBrushes_[i].Reset();
        hr = d2dContext_->CreateSolidColorBrush(FromArgb(FLOWER_PALETTE[i]),
                                                flowerHeadBrushes_[i].ReleaseAndGetAddressOf());
        if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }
    }

    for (int i = 0; i < MUSHROOM_PALETTE_SIZE; ++i) {
        mushroomCapBrushes_[i].Reset();
        hr = d2dContext_->CreateSolidColorBrush(FromArgb(MUSHROOM_PALETTE[i]),
                                                mushroomCapBrushes_[i].ReleaseAndGetAddressOf());
        if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }
    }

    mushroomStemBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(MUSHROOM_STEM_COLOR),
                                            mushroomStemBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    cactusBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(CACTUS_COLOR),
                                            cactusBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    tumbleweedBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(TUMBLEWEED_COLOR),
                                            tumbleweedBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    snowflakeBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(SNOWFLAKE_COLOR),
                                            snowflakeBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    snowTipBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(SNOW_TIP_COLOR),
                                            snowTipBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    pineBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(PINE_COLOR),
                                            pineBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    birchBarkBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(BIRCH_BARK_COLOR),
                                            birchBarkBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    birchMarkBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(BIRCH_MARK_COLOR),
                                            birchMarkBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    sheepBodyBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(SHEEP_BODY_COLOR),
                                            sheepBodyBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    sheepLegBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(SHEEP_LEG_COLOR),
                                            sheepLegBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    sheepFaceBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(SHEEP_FACE_COLOR),
                                            sheepFaceBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    sheepEarBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(SHEEP_EAR_COLOR),
                                            sheepEarBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    sheepInkBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(SHEEP_INK_COLOR),
                                            sheepInkBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    catBodyBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(CAT_BODY_COLOR),
                                            catBodyBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    catLegBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(CAT_LEG_COLOR),
                                            catLegBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    catFaceBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(CAT_FACE_COLOR),
                                            catFaceBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    catEarBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(CAT_EAR_COLOR),
                                            catEarBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    catInkBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(CAT_INK_COLOR),
                                            catInkBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    petNameBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(PET_NAME_COLOR),
                                            petNameBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

    petNameShadowBrush_.Reset();
    hr = d2dContext_->CreateSolidColorBrush(FromArgb(PET_NAME_SHADOW_COLOR),
                                            petNameShadowBrush_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateSolidColorBrush", hr); return false; }

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
    for (auto& row : brushes_) for (auto& b : row) b.Reset();
    for (auto& b : flowerHeadBrushes_) b.Reset();
    for (auto& b : mushroomCapBrushes_) b.Reset();
    mushroomStemBrush_.Reset();
    cactusBrush_.Reset();
    tumbleweedBrush_.Reset();
    snowflakeBrush_.Reset();
    snowTipBrush_.Reset();
    pineBrush_.Reset();
    birchBarkBrush_.Reset();
    birchMarkBrush_.Reset();
    sheepBodyBrush_.Reset();
    sheepLegBrush_.Reset();
    sheepFaceBrush_.Reset();
    sheepEarBrush_.Reset();
    sheepInkBrush_.Reset();
    catBodyBrush_.Reset();
    catLegBrush_.Reset();
    catFaceBrush_.Reset();
    catEarBrush_.Reset();
    catInkBrush_.Reset();
    petNameBrush_.Reset();
    petNameShadowBrush_.Reset();
    petNameTextFormat_.Reset();
    dwriteFactory_.Reset();
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

bool Renderer::TryGetCursorPositionDip(D2D1_POINT_2F& cursorPosition) const {
    POINT pt{};
    if (!GetCursorPos(&pt)) return false;

    const double scale = 96.0 / static_cast<double>(dpi_ == 0 ? 96 : dpi_);
    cursorPosition.x = static_cast<float>((pt.x - windowOriginScreenX_) * scale);
    cursorPosition.y = static_cast<float>((pt.y - windowOriginScreenY_) * scale);
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

    D2D1_POINT_2F cursorPosition{};
    const D2D1_POINT_2F* cursorForRender = TryGetCursorPositionDip(cursorPosition)
        ? &cursorPosition
        : nullptr;

    DrawGrass();
    DrawEntities(cursorForRender);

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
    const int sceneIdx = static_cast<int>(sim_.currentScene);
    ComPtr<ID2D1Factory> factoryGeneric;
    d2dFactory_.As(&factoryGeneric);

    auto drawCactusArm = [&](float baseX, float gy, float h, float width, int side) {
        const float sx = baseX;
        const float sy = gy - h * 0.4f;
        const float ex = baseX + static_cast<float>(side) * width * 1.5f;
        const float ey = gy - h * 0.7f;
        const float cx = ex;
        const float cy = sy;
        const float armWidth = width * 0.7f;

        constexpr int N = 4;
        float prevX = sx;
        float prevY = sy;
        for (int i = 1; i <= N; ++i) {
            const float t = static_cast<float>(i) / static_cast<float>(N);
            const float u = 1.0f - t;
            const float px = u * u * sx + 2.0f * u * t * cx + t * t * ex;
            const float py = u * u * sy + 2.0f * u * t * cy + t * t * ey;
            d2dContext_->DrawLine(D2D1::Point2F(prevX, prevY), D2D1::Point2F(px, py),
                                  cactusBrush_.Get(), armWidth);
            prevX = px;
            prevY = py;
        }
        d2dContext_->DrawLine(D2D1::Point2F(ex, ey), D2D1::Point2F(ex, ey - h * 0.15f),
                              cactusBrush_.Get(), armWidth);
    };

    for (const Blade& b : sim_.blades) {
        if (b.isCactus) {
            const float baseX = static_cast<float>(b.baseX);
            const float gy = static_cast<float>(groundY);
            const float width = static_cast<float>(b.cactusWidth);

            if (b.cutHeight < CUT_STUMP_THRESHOLD) {
                d2dContext_->DrawLine(
                    D2D1::Point2F(baseX, gy),
                    D2D1::Point2F(baseX, gy - static_cast<float>(STUMP_HEIGHT)),
                    cactusBrush_.Get(), width);
                continue;
            }

            const float h = static_cast<float>(b.cactusHeight * b.cutHeight);
            const float topY = gy - h;
            d2dContext_->DrawLine(D2D1::Point2F(baseX, gy), D2D1::Point2F(baseX, topY),
                                  cactusBrush_.Get(), width);
            d2dContext_->FillEllipse(
                D2D1::Ellipse(D2D1::Point2F(baseX, topY), width * 0.5f, width * 0.5f),
                cactusBrush_.Get());

            if (b.cactusType == 1) {
                drawCactusArm(baseX, gy, h, width, b.cactusArmSide < 0 ? -1 : 1);
            } else if (b.cactusType == 2) {
                drawCactusArm(baseX, gy, h, width, -1);
                drawCactusArm(baseX, gy, h, width, +1);
            }
            continue;
        }

        // Tree (§15.1). Slot-bound Winter variant. Two styles selected
        // by treeVariant: 0 = classic tiered pine, 1 = bare birch with
        // dark bark marks and short branch stubs. Below CUT_STUMP_THRESHOLD
        // both styles reduce to a short brown stump.
        if (b.isPine) {
            const float baseX = static_cast<float>(b.baseX);
            const float gy    = static_cast<float>(groundY);

            if (b.cutHeight < CUT_STUMP_THRESHOLD) {
                d2dContext_->DrawLine(
                    D2D1::Point2F(baseX, gy),
                    D2D1::Point2F(baseX, gy - static_cast<float>(STUMP_HEIGHT)),
                    pineBrush_.Get(),
                    static_cast<float>(std::max(2.0, b.pineWidth * 0.25)));
                continue;
            }

            auto drawFilledTri = [&](float cx, float baseY, float topY, float halfW,
                                     ID2D1SolidColorBrush* brush) {
                const float h = baseY - topY;
                if (h <= 0.0f || halfW <= 0.0f) return;
                constexpr float kStep = 0.5f;
                for (float y = baseY; y >= topY; y -= kStep) {
                    const float t  = (baseY - y) / h;
                    const float hw = halfW * (1.0f - t);
                    if (hw <= 0.0f) continue;
                    d2dContext_->DrawLine(
                        D2D1::Point2F(cx - hw, y),
                        D2D1::Point2F(cx + hw, y),
                        brush, kStep * 1.5f);
                }
            };

            if (b.treeVariant == 1) {
                // ---- Birch: vertical trunk + short bark dashes + upward branch fan ----
                const float totalH    = static_cast<float>(b.pineHeight * b.cutHeight);
                const float trunkW    = static_cast<float>(b.pineWidth);
                const float trunkTopY = gy - totalH;

                d2dContext_->DrawLine(
                    D2D1::Point2F(baseX, gy),
                    D2D1::Point2F(baseX, trunkTopY),
                    birchBarkBrush_.Get(),
                    trunkW);

                // Short bark dashes — centered on trunk, varied lengths so the
                // pattern reads as broken bark "eyes" instead of full ribs.
                static const float kDashLenFrac[BIRCH_BARK_MARK_COUNT] = {
                    0.50f, 0.30f, 0.45f, 0.25f, 0.40f
                };
                for (int m = 0; m < BIRCH_BARK_MARK_COUNT; ++m) {
                    const float tM = (m + 1.0f) / (BIRCH_BARK_MARK_COUNT + 1.0f);
                    const float yM = gy - totalH * tM;
                    const float dashLen = trunkW * kDashLenFrac[m];
                    d2dContext_->DrawLine(
                        D2D1::Point2F(baseX - dashLen * 0.5f, yM),
                        D2D1::Point2F(baseX + dashLen * 0.5f, yM),
                        birchMarkBrush_.Get(),
                        std::max(1.0f, trunkW * 0.22f));
                }

                // Branch fan — hand-tuned for deciduous tree silhouette.
                // Each branch is angled UPWARD (never horizontal) and ends in
                // a small white snow puff, so the shape never reads as a cross.
                struct Branch { float trunkFrac; float angleDeg; float side; float lenMul; };
                static const Branch kBranches[BIRCH_BRANCH_COUNT] = {
                    {0.45f, 35.0f, +1.0f, 1.20f},
                    {0.55f, 50.0f, -1.0f, 1.40f},
                    {0.65f, 25.0f, +1.0f, 1.60f},
                    {0.72f, 60.0f, -1.0f, 1.00f},
                    {0.80f, 20.0f, +1.0f, 1.10f},
                    {0.85f, 45.0f, -1.0f, 0.80f},
                };
                const float branchBaseLen = trunkW * 3.0f;
                const float branchW = std::max(1.0f, trunkW * 0.35f);
                const float snowR   = std::max(1.5f, trunkW * 0.65f);
                for (const auto& br : kBranches) {
                    const float sy   = gy - totalH * br.trunkFrac;
                    const float blen = branchBaseLen * br.lenMul;
                    const float ang  = br.angleDeg * 3.14159265f / 180.0f;
                    const float ex   = baseX + br.side * blen * std::sin(ang);
                    const float ey   = sy - blen * std::cos(ang);
                    d2dContext_->DrawLine(
                        D2D1::Point2F(baseX, sy), D2D1::Point2F(ex, ey),
                        birchBarkBrush_.Get(), branchW);
                    d2dContext_->FillEllipse(
                        D2D1::Ellipse(D2D1::Point2F(ex, ey), snowR, snowR),
                        snowTipBrush_.Get());
                }

                // Small snow puff right at the top of the trunk.
                const float capR = std::max(2.0f, trunkW * 0.9f);
                d2dContext_->FillEllipse(
                    D2D1::Ellipse(D2D1::Point2F(baseX, trunkTopY), capR, capR * 0.6f),
                    snowTipBrush_.Get());
                continue;
            }

            // ---- Pine: stacked snow-capped triangle tiers ----
            const int    tierCount  = b.pineTierCount > 0 ? b.pineTierCount : PINE_TIER_COUNT_MIN;
            const double totalH     = b.pineHeight * b.cutHeight;
            const double tierH      = totalH / tierCount;

            for (int i = 0; i < tierCount; ++i) {
                const double tFrac    = (tierCount == 1) ? 0.0
                                       : static_cast<double>(i) / static_cast<double>(tierCount - 1);
                const double widthAt  = b.pineWidth * (1.0 - tFrac * (1.0 - PINE_TIP_TAPER));
                const double baseY    = gy - i * tierH * (1.0 - PINE_TIER_OVERLAP);
                const double topY     = baseY - tierH;
                drawFilledTri(baseX,
                              static_cast<float>(baseY),
                              static_cast<float>(topY),
                              static_cast<float>(widthAt * 0.5),
                              pineBrush_.Get());

                // Snow cap: smaller triangle covering top PINE_SNOW_CAP_FRACTION
                // of the tier. Inherits the tier's apex; base is at the cap's
                // bottom (PINE_SNOW_CAP_FRACTION up from the tier apex).
                const double capH      = tierH * PINE_SNOW_CAP_FRACTION;
                const double capBaseY  = topY + capH;
                const double capHalfW  = widthAt * 0.5 * PINE_SNOW_CAP_FRACTION * 1.4;
                drawFilledTri(baseX,
                              static_cast<float>(capBaseY),
                              static_cast<float>(topY),
                              static_cast<float>(capHalfW),
                              snowTipBrush_.Get());
            }
            continue;
        }

        // Mushroom slots preempt grass + flower rendering at this position.
        // Cap + stem scale linearly with cutHeight so the cut animation
        // visibly shrinks them; below CUT_STUMP_THRESHOLD the mushroom is
        // invisible (matches the grass-stump short-circuit in spirit).
        if (b.isMushroom) {
            const float baseX  = static_cast<float>(b.baseX);
            const float gy     = static_cast<float>(groundY);
            const float stemT  = static_cast<float>(b.mushroomStemThickness);

            // Stump-stub short-circuit: when a mushroom is cut below the
            // CUT_STUMP_THRESHOLD, draw a short ivory stem stub. We use
            // MUSHROOM_STUMP_HEIGHT (slightly taller than STUMP_HEIGHT)
            // so the mushroom nub reads as distinct from cut grass.
            if (b.cutHeight < CUT_STUMP_THRESHOLD) {
                d2dContext_->DrawLine(
                    D2D1::Point2F(baseX, gy),
                    D2D1::Point2F(baseX, gy - static_cast<float>(MUSHROOM_STUMP_HEIGHT)),
                    mushroomStemBrush_.Get(),
                    stemT);
                continue;
            }

            const float scale  = static_cast<float>(b.cutHeight);
            const float stemH  = static_cast<float>(b.mushroomStemHeight) * scale;
            const float capRX  = static_cast<float>(b.mushroomCapWidth)  * scale;
            const float capRY  = static_cast<float>(b.mushroomCapHeight) * scale;
            const float capCY  = gy - stemH;

            // Stem: short vertical line, ivory.
            d2dContext_->DrawLine(
                D2D1::Point2F(baseX, gy),
                D2D1::Point2F(baseX, capCY),
                mushroomStemBrush_.Get(),
                stemT);

            // Cap: filled ellipse sitting on top of the stem.
            uint8_t cIdx = b.mushroomCapColorIdx;
            if (cIdx >= MUSHROOM_PALETTE_SIZE) cIdx = 0;
            const D2D1_ELLIPSE cap = D2D1::Ellipse(
                D2D1::Point2F(baseX, capCY), capRX, capRY);
            d2dContext_->FillEllipse(cap, mushroomCapBrushes_[cIdx].Get());
            continue;
        }

        const Stroke s = compute_blade_stroke(b, groundY, sim_.currentScene);

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

        ID2D1SolidColorBrush* brush = brushes_[sceneIdx][b.hue].Get();
        d2dContext_->DrawGeometry(path.Get(), brush,
                                  static_cast<float>(s.thickness));

        if (b.isFlower && b.cutHeight >= CUT_STUMP_THRESHOLD) {
            const D2D1_ELLIPSE ellipse = D2D1::Ellipse(
                D2D1::Point2F(static_cast<float>(s.tip.x),
                              static_cast<float>(s.tip.y)),
                static_cast<float>(b.flowerHeadRadius),
                static_cast<float>(b.flowerHeadRadius));
            uint8_t idx = b.flowerHeadColorIdx;
            if (idx >= FLOWER_PALETTE_SIZE) idx = 0;
            d2dContext_->FillEllipse(ellipse, flowerHeadBrushes_[idx].Get());
        }

        if (sim_.currentScene == Scene::Winter && !b.isCactus && !b.isPine && b.cutHeight >= CUT_STUMP_THRESHOLD) {
            const float r = static_cast<float>(b.thickness * SNOW_TIP_RADIUS_FACTOR);
            const D2D1_ELLIPSE cap = D2D1::Ellipse(
                D2D1::Point2F(static_cast<float>(s.tip.x), static_cast<float>(s.tip.y)),
                r,
                r);
            d2dContext_->FillEllipse(cap, snowTipBrush_.Get());
        }
    }
}

void Renderer::DrawCat(const Entity& e, const D2D1_POINT_2F* cursorPosition) {
    if (!catBodyBrush_ || !catLegBrush_ || !catFaceBrush_ || !catEarBrush_ || !catInkBrush_) return;

    constexpr double TWO_PI_LOCAL = 6.28318530717958647692;
    const float cx = static_cast<float>(e.x);
    const float br = static_cast<float>(CAT_BODY_RADIUS);
    const float bh = static_cast<float>(CAT_BODY_HEIGHT);
    const float legLen = static_cast<float>(CAT_LEG_LENGTH);
    const float headR = static_cast<float>(CAT_HEAD_RADIUS);
    const float facing = (e.vx >= 0.0) ? 1.0f : -1.0f;

    const bool isWalking = (e.state == CAT_STATE_WALKING);
    const bool isIdle = (e.state == CAT_STATE_IDLE);
    const bool isSleeping = (e.state == CAT_STATE_SLEEPING);
    const bool isPouncing = (e.state == CAT_STATE_POUNCING);

    float pounceOffsetY = 0.0f;
    if (isPouncing) {
        const float t = std::max(0.0f,
            std::min(1.0f, static_cast<float>(e.age / CAT_POUNCE_DURATION)));
        pounceOffsetY = -4.0f * static_cast<float>(CAT_POUNCE_HEIGHT) * t * (1.0f - t);
    }
    const float sleepOffsetY = isSleeping ? legLen : 0.0f;
    const float cy = static_cast<float>(e.y) + pounceOffsetY + sleepOffsetY;

    const float walkPhase = static_cast<float>(e.age * (TWO_PI_LOCAL / CAT_WALK_PERIOD));
    const float legAmp = isWalking ? static_cast<float>(CAT_LEG_CYCLE_AMP) : 0.0f;
    const float headBob = isWalking
        ? std::sin(walkPhase * 2.0f) * static_cast<float>(CAT_HEAD_BOB_AMP)
        : 0.0f;
    const float tailSway = (isWalking || isIdle)
        ? std::sin(static_cast<float>(e.age * CAT_TAIL_SWAY_FREQ)) * static_cast<float>(CAT_TAIL_SWAY_AMP)
        : 0.0f;

    auto fillTriangle = [&](D2D1_POINT_2F a, D2D1_POINT_2F b, D2D1_POINT_2F c,
                            ID2D1SolidColorBrush* brush) {
        ComPtr<ID2D1PathGeometry> path;
        if (FAILED(d2dFactory_->CreatePathGeometry(&path))) return;
        ComPtr<ID2D1GeometrySink> sink;
        if (FAILED(path->Open(&sink))) return;
        sink->BeginFigure(a, D2D1_FIGURE_BEGIN_FILLED);
        sink->AddLine(b);
        sink->AddLine(c);
        sink->EndFigure(D2D1_FIGURE_END_CLOSED);
        if (FAILED(sink->Close())) return;
        d2dContext_->FillGeometry(path.Get(), brush);
    };

    auto drawBezier = [&](D2D1_POINT_2F p0, D2D1_POINT_2F c1, D2D1_POINT_2F c2,
                          D2D1_POINT_2F p1, ID2D1SolidColorBrush* brush, float thickness) {
        ComPtr<ID2D1PathGeometry> path;
        if (FAILED(d2dFactory_->CreatePathGeometry(&path))) return;
        ComPtr<ID2D1GeometrySink> sink;
        if (FAILED(path->Open(&sink))) return;
        sink->BeginFigure(p0, D2D1_FIGURE_BEGIN_HOLLOW);
        D2D1_BEZIER_SEGMENT seg{};
        seg.point1 = c1;
        seg.point2 = c2;
        seg.point3 = p1;
        sink->AddBezier(seg);
        sink->EndFigure(D2D1_FIGURE_END_OPEN);
        if (FAILED(sink->Close())) return;
        d2dContext_->DrawGeometry(path.Get(), brush, thickness);
    };

    auto drawZ = [&](float zX, float zY, float zSize, float alpha) {
        catInkBrush_->SetOpacity(alpha);
        d2dContext_->DrawLine(D2D1::Point2F(zX, zY),
                              D2D1::Point2F(zX + zSize, zY),
                              catInkBrush_.Get(), 0.9f);
        d2dContext_->DrawLine(D2D1::Point2F(zX + zSize, zY),
                              D2D1::Point2F(zX, zY + zSize),
                              catInkBrush_.Get(), 0.9f);
        d2dContext_->DrawLine(D2D1::Point2F(zX, zY + zSize),
                              D2D1::Point2F(zX + zSize, zY + zSize),
                              catInkBrush_.Get(), 0.9f);
        catInkBrush_->SetOpacity(1.0f);
    };

    const float tailBaseX = cx - facing * br * 0.92f;
    const float tailBaseY = cy - bh * 0.10f;
    if (isSleeping) {
        drawBezier(
            D2D1::Point2F(tailBaseX, tailBaseY + bh * 0.15f),
            D2D1::Point2F(cx - facing * br * 0.55f, cy + bh * 0.95f),
            D2D1::Point2F(cx + facing * br * 0.10f, cy + bh * 0.95f),
            D2D1::Point2F(cx + facing * br * 0.72f, cy + bh * 0.45f),
            catLegBrush_.Get(), static_cast<float>(CAT_TAIL_THICKNESS));
    } else {
        const float tailLen = static_cast<float>(CAT_TAIL_LENGTH);
        const float tipX = tailBaseX - facing * tailLen * (0.78f + 0.08f * std::sin(tailSway));
        const float tipY = tailBaseY - tailLen * (0.42f + 0.18f * std::cos(tailSway));
        drawBezier(
            D2D1::Point2F(tailBaseX, tailBaseY),
            D2D1::Point2F(tailBaseX - facing * tailLen * 0.18f, tailBaseY - tailLen * 0.08f),
            D2D1::Point2F(tailBaseX - facing * tailLen * 0.60f, tailBaseY - tailLen * (0.70f + 0.20f * std::sin(tailSway))),
            D2D1::Point2F(tipX, tipY),
            catLegBrush_.Get(), static_cast<float>(CAT_TAIL_THICKNESS));
    }

    if (!isSleeping) {
        const float legY0 = cy + bh * 0.35f;
        const float legXs[4] = { -br * 0.58f, -br * 0.20f, br * 0.20f, br * 0.58f };
        const float swingA = std::sin(walkPhase) * legAmp;
        const float swingB = std::sin(walkPhase + 3.14159265f) * legAmp;
        const float legSwings[4] = { swingA, swingB, swingA, swingB };
        for (int li = 0; li < 4; ++li) {
            const float lx = cx + legXs[li];
            const float ly1 = cy + bh + legLen + legSwings[li];
            d2dContext_->DrawLine(D2D1::Point2F(lx, legY0),
                                  D2D1::Point2F(lx, ly1),
                                  catLegBrush_.Get(), 1.2f);
        }
    }

    d2dContext_->FillEllipse(D2D1::Ellipse(D2D1::Point2F(cx, cy), br, bh),
                             catBodyBrush_.Get());

    float headDirX = facing;
    float headDx = facing * br * 0.82f;
    float headDy = -bh * 0.78f + headBob;
    if (isIdle) {
        const float stripTop = static_cast<float>(sim_.windowHeight - STRIP_HEIGHT);
        const bool curious = cursorPosition != nullptr
            && std::fabs(cursorPosition->y - stripTop) <= SHEEP_CURIOUS_VERTICAL_RADIUS_DIP
            && std::fabs(cursorPosition->x - cx) <= static_cast<float>(CAT_CURIOUS_RADIUS);
        if (curious) {
            const float cursorDx = cursorPosition->x - cx;
            const float maxHeadDx = static_cast<float>(CAT_CURIOUS_HEAD_TURN_MAX * CAT_HEAD_RADIUS);
            headDirX = cursorDx >= 0.0f ? 1.0f : -1.0f;
            headDx = facing * br * 0.55f + std::clamp(cursorDx, -maxHeadDx, maxHeadDx);
        } else {
            const float sweep = std::sin(static_cast<float>(e.age * CAT_TAIL_SWAY_FREQ * 0.7));
            headDirX = sweep >= 0.0f ? 1.0f : -1.0f;
            headDx = facing * br * 0.60f + sweep * headR * 0.70f;
        }
        headDy = -bh * 0.82f;
    } else if (isSleeping) {
        headDx = facing * br * 0.62f;
        headDy = -bh * 0.20f;
    }

    const float headCx = cx + headDx;
    const float headCy = cy + headDy;
    d2dContext_->FillEllipse(D2D1::Ellipse(D2D1::Point2F(headCx, headCy), headR, headR),
                             catFaceBrush_.Get());

    const float earBaseY = headCy - headR * 0.62f;
    const float earH = static_cast<float>(CAT_EAR_HEIGHT);
    fillTriangle(
        D2D1::Point2F(headCx - headR * 0.60f, earBaseY),
        D2D1::Point2F(headCx - headR * 0.18f, earBaseY),
        D2D1::Point2F(headCx - headR * 0.47f - headDirX * 0.65f, earBaseY - earH),
        catEarBrush_.Get());
    fillTriangle(
        D2D1::Point2F(headCx + headR * 0.18f, earBaseY),
        D2D1::Point2F(headCx + headR * 0.60f, earBaseY),
        D2D1::Point2F(headCx + headR * 0.47f + headDirX * 0.65f, earBaseY - earH),
        catEarBrush_.Get());

    if (isSleeping) {
        const float eyeY = headCy - headR * 0.05f;
        for (float ex : { -headR * 0.25f, headR * 0.32f }) {
            const float x0 = headCx + ex - 1.1f;
            const float x1 = headCx + ex + 1.1f;
            drawBezier(D2D1::Point2F(x0, eyeY),
                       D2D1::Point2F(x0 + 0.45f, eyeY + 0.8f),
                       D2D1::Point2F(x1 - 0.45f, eyeY + 0.8f),
                       D2D1::Point2F(x1, eyeY),
                       catInkBrush_.Get(), 0.9f);
        }
    } else {
        const float eyeR = headR * 0.16f;
        d2dContext_->FillEllipse(
            D2D1::Ellipse(D2D1::Point2F(headCx + headDirX * headR * 0.22f,
                                        headCy - headR * 0.18f), eyeR, eyeR * 0.75f),
            catInkBrush_.Get());
        d2dContext_->FillEllipse(
            D2D1::Ellipse(D2D1::Point2F(headCx - headDirX * headR * 0.18f,
                                        headCy - headR * 0.18f), eyeR, eyeR * 0.75f),
            catInkBrush_.Get());
    }

    const float noseTipX = headCx + headDirX * headR * 0.63f;
    const float noseTipY = headCy + headR * 0.12f;
    fillTriangle(
        D2D1::Point2F(noseTipX, noseTipY),
        D2D1::Point2F(noseTipX - headDirX * 1.5f, noseTipY - 1.1f),
        D2D1::Point2F(noseTipX - headDirX * 1.5f, noseTipY + 1.1f),
        catInkBrush_.Get());

    if (isSleeping) {
        const float zBaseX = headCx + headDirX * headR * 0.55f;
        const float zBaseY = headCy - headR * 1.25f;
        for (int zi = 0; zi < 2; ++zi) {
            const float phaseOffset = 0.5f * static_cast<float>(zi);
            const float t = static_cast<float>(std::fmod(e.age / SHEEP_ZZZ_CYCLE_SEC + phaseOffset, 1.0));
            const float zSize = static_cast<float>((SHEEP_ZZZ_SIZE_START * 0.65) +
                t * ((SHEEP_ZZZ_SIZE_END * 0.70) - (SHEEP_ZZZ_SIZE_START * 0.65)));
            drawZ(zBaseX + t * 3.0f * headDirX, zBaseY - t * static_cast<float>(SHEEP_ZZZ_RISE * 0.75),
                  zSize, 1.0f - t);
        }
    }
}

void Renderer::DrawPetName(const Entity& e, const D2D1_POINT_2F* cursorPosition) {
    if (!petNameTextFormat_ || !petNameBrush_ || !petNameShadowBrush_) return;
    if (e.kind != EntityKind::Sheep && e.kind != EntityKind::Cat) return;

    const wchar_t* const* pool = (e.kind == EntityKind::Sheep) ? SHEEP_NAME_POOL : CAT_NAME_POOL;
    const std::size_t poolSize = (e.kind == EntityKind::Sheep)
        ? (sizeof(SHEEP_NAME_POOL) / sizeof(SHEEP_NAME_POOL[0]))
        : (sizeof(CAT_NAME_POOL) / sizeof(CAT_NAME_POOL[0]));
    if (poolSize == 0) return;

    const uint64_t key = (static_cast<uint64_t>(static_cast<uint8_t>(e.kind)) << 32)
                       ^ static_cast<uint64_t>(e.seed);
    bool hovering = false;
    if (cursorPosition != nullptr) {
        const double dx = static_cast<double>(cursorPosition->x) - e.x;
        const double dy = static_cast<double>(cursorPosition->y) - e.y;
        hovering = (dx * dx + dy * dy) <= (PET_NAME_HOVER_RADIUS * PET_NAME_HOVER_RADIUS);
    }

    float opacity = 0.0f;
    if (hovering) {
        petNameLastHover_[key] = sim_.globalTime;
        opacity = 1.0f;
    } else {
        auto it = petNameLastHover_.find(key);
        if (it == petNameLastHover_.end()) return;
        const double elapsed = sim_.globalTime - it->second;
        if (elapsed >= PET_NAME_FADE_DURATION) {
            petNameLastHover_.erase(it);
            return;
        }
        opacity = static_cast<float>(1.0 - (elapsed / PET_NAME_FADE_DURATION));
    }

    const wchar_t* name = pool[e.nameIndex % poolSize];
    const UINT32 length = static_cast<UINT32>(std::wcslen(name));
    const float centerX = static_cast<float>(e.x);
    const float top = static_cast<float>(e.y - e.size + PET_NAME_OFFSET_Y - PET_NAME_FONT_SIZE);
    const float halfWidth = 60.0f;
    const float height = static_cast<float>(PET_NAME_FONT_SIZE + 4.0);
    const D2D1_RECT_F rect = D2D1::RectF(centerX - halfWidth, top,
                                        centerX + halfWidth, top + height);
    const D2D1_RECT_F shadowRect = D2D1::RectF(rect.left + 1.0f, rect.top + 1.0f,
                                              rect.right + 1.0f, rect.bottom + 1.0f);

    petNameShadowBrush_->SetOpacity(opacity);
    petNameBrush_->SetOpacity(opacity);
    d2dContext_->DrawTextW(name, length, petNameTextFormat_.Get(), shadowRect,
                           petNameShadowBrush_.Get());
    d2dContext_->DrawTextW(name, length, petNameTextFormat_.Get(), rect,
                           petNameBrush_.Get());
    petNameShadowBrush_->SetOpacity(1.0f);
    petNameBrush_->SetOpacity(1.0f);
}

void Renderer::DrawEntities(const D2D1_POINT_2F* cursorPosition) {
    if (sim_.entities.empty()) return;

    constexpr double TWO_PI_LOCAL = 6.28318530717958647692;

    for (const Entity& e : sim_.entities) {
        if (e.kind == EntityKind::Tumbleweed) {
            const float cx = static_cast<float>(e.x);
            const float cy = static_cast<float>(e.y);
            const float size = static_cast<float>(e.size);
            for (int k = 0; k < 5; ++k) {
                const double angle = e.rotation + static_cast<double>(k) * (TWO_PI_LOCAL / 5.0);
                const float dx = static_cast<float>(std::cos(angle));
                const float dy = static_cast<float>(std::sin(angle));
                const float px = -dy;
                const float py = dx;
                const D2D1_POINT_2F p0 = D2D1::Point2F(cx - dx * size * 0.95f + px * size * 0.18f,
                                                       cy - dy * size * 0.95f + py * size * 0.18f);
                const D2D1_POINT_2F p1 = D2D1::Point2F(cx - dx * size * 0.20f - px * size * 0.14f,
                                                       cy - dy * size * 0.20f - py * size * 0.14f);
                const D2D1_POINT_2F p2 = D2D1::Point2F(cx + dx * size * 0.95f + px * size * 0.18f,
                                                       cy + dy * size * 0.95f + py * size * 0.18f);
                d2dContext_->DrawLine(p0, p1, tumbleweedBrush_.Get(), 1.0f);
                d2dContext_->DrawLine(p1, p2, tumbleweedBrush_.Get(), 1.0f);
            }
            continue;
        }

        if (e.kind == EntityKind::Cat) {
            DrawCat(e, cursorPosition);
            DrawPetName(e, cursorPosition);
            continue;
        }

        if (e.kind != EntityKind::Snowflake) {
            if (e.kind == EntityKind::Sheep) {
                // Suffolk-style vector sheep: white wool cloud + dark head
                // and legs. State drives the pose:
                //   WALKING  : leg cycle + head bob + tail wiggle.
                //   GRAZING  : frozen, head pivoted down to the grass line.
                //   IDLE     : frozen, head turns side-to-side.
                //   GREETING : frozen, head gently bobs while facing partner.
                //   SLEEPING : tucked on the ground, legs hidden, eyes
                //              closed (horizontal slits), Z's drift up.
                //   HOPPING  : sheep arcs upward (parabola) — entire pose
                //              translated by a hopY offset; horizontal vx
                //              still applies so the sheep covers ground.
                const float cx = static_cast<float>(e.x);
                const float br = static_cast<float>(SHEEP_BODY_RADIUS);
                const float bh = static_cast<float>(SHEEP_BODY_HEIGHT);
                const float legLen = static_cast<float>(SHEEP_LEG_LENGTH);
                const float headR  = static_cast<float>(SHEEP_HEAD_RADIUS);
                const float tailR  = static_cast<float>(SHEEP_TAIL_RADIUS);
                const float facing = (e.vx >= 0.0) ? 1.0f : -1.0f;

                const bool isWalking  = (e.state == SHEEP_STATE_WALKING);
                const bool isGrazing  = (e.state == SHEEP_STATE_GRAZING);
                const bool isIdle     = (e.state == SHEEP_STATE_IDLE);
                const bool isGreeting = (e.state == SHEEP_STATE_GREETING);
                const bool isSleeping = (e.state == SHEEP_STATE_SLEEPING);
                const bool isHopping  = (e.state == SHEEP_STATE_HOPPING);

                // Hop parabola y-offset (negative = up). t = age / DURATION.
                float hopOffsetY = 0.0f;
                if (isHopping) {
                    const float t = std::max(0.0f,
                        std::min(1.0f, static_cast<float>(e.age / SHEEP_HOP_DURATION)));
                    hopOffsetY = -4.0f * static_cast<float>(SHEEP_HOP_HEIGHT) * t * (1.0f - t);
                }
                // Sleep pose: body drops by leg-length so it sits on the
                // ground; legs are hidden because they're tucked underneath.
                const float sleepOffsetY = isSleeping ? legLen : 0.0f;
                const float cy = static_cast<float>(e.y) + hopOffsetY + sleepOffsetY;

                const float walkPhase = static_cast<float>(e.age * (TWO_PI_LOCAL / SHEEP_WALK_PERIOD));
                const float legAmp   = isWalking ? static_cast<float>(SHEEP_LEG_CYCLE_AMP) : 0.0f;
                const float headBob  = isWalking
                    ? std::sin(walkPhase * 2.0f) * static_cast<float>(SHEEP_HEAD_BOB_AMP)
                    : 0.0f;
                const float tailWig  = isWalking
                    ? std::sin(walkPhase * 2.0f) * static_cast<float>(SHEEP_TAIL_WIGGLE_AMP)
                    : 0.0f;

                // Legs — hidden while sleeping (tucked). Hopping draws them
                // straight (no swing) so the sheep looks suspended.
                if (!isSleeping) {
                    const float legY0 = cy + bh * 0.30f;
                    const float legXs[4] = { -br * 0.62f, -br * 0.22f,
                                             +br * 0.22f, +br * 0.62f };
                    const float swingA = std::sin(walkPhase) * legAmp;
                    const float swingB = std::sin(walkPhase + 3.14159265f) * legAmp;
                    const float legSwings[4] = { swingA, swingB, swingA, swingB };
                    for (int li = 0; li < 4; ++li) {
                        const float lx = cx + legXs[li];
                        const float ly1 = cy + bh + legLen + legSwings[li];
                        d2dContext_->DrawLine(
                            D2D1::Point2F(lx, legY0),
                            D2D1::Point2F(lx, ly1),
                            sheepLegBrush_.Get(),
                            1.8f);
                    }
                }

                // Tail puff — rear of the body (opposite of facing).
                const float tailCx = cx - facing * br * 0.95f + tailWig;
                const float tailCy = cy - bh * 0.05f;
                d2dContext_->FillEllipse(
                    D2D1::Ellipse(D2D1::Point2F(tailCx, tailCy), tailR, tailR * 0.95f),
                    sheepBodyBrush_.Get());

                // Body — one large ellipse + 3 evenly-spaced top puffs.
                d2dContext_->FillEllipse(
                    D2D1::Ellipse(D2D1::Point2F(cx, cy), br, bh),
                    sheepBodyBrush_.Get());
                const float puffY = cy - bh * 0.55f;
                const float puffRx = br * 0.40f;
                const float puffRy = bh * 0.48f;
                const float puffXs[3] = { -br * 0.50f, 0.0f, +br * 0.50f };
                for (float pdx : puffXs) {
                    d2dContext_->FillEllipse(
                        D2D1::Ellipse(D2D1::Point2F(cx + pdx, puffY), puffRx, puffRy),
                        sheepBodyBrush_.Get());
                }

                // Head position. WALKING/HOPPING: forward + slight bob.
                // GRAZING: pivoted down to the grass. IDLE: sweeps L/R.
                // GREETING: faces partner via vx and gently nuzzles.
                // SLEEPING: rests low on the front edge of the body.
                float headDirX = facing;
                float headDx = headDirX * (br * 1.08f);
                float headDy = -bh * 0.05f + headBob;
                if (isGrazing) {
                    const float munch = std::sin(
                        static_cast<float>(e.age * SHEEP_GRAZE_MUNCH_FREQ))
                        * static_cast<float>(SHEEP_GRAZE_MUNCH_AMP);
                    headDx = headDirX * br * 0.85f;
                    headDy = bh * 0.85f + munch;
                } else if (isIdle) {
                    const float stripTop = static_cast<float>(sim_.windowHeight - STRIP_HEIGHT);
                    const bool curious = cursorPosition != nullptr
                        && std::fabs(cursorPosition->y - stripTop) <= SHEEP_CURIOUS_VERTICAL_RADIUS_DIP
                        && std::fabs(cursorPosition->x - cx) <= static_cast<float>(SHEEP_CURIOUS_RADIUS);
                    if (curious) {
                        const float cursorDx = cursorPosition->x - cx;
                        const float maxHeadDx = static_cast<float>(
                            SHEEP_CURIOUS_HEAD_TURN_MAX * SHEEP_HEAD_RADIUS);
                        headDirX = cursorDx >= 0.0f ? 1.0f : -1.0f;
                        headDx = std::clamp(cursorDx, -maxHeadDx, maxHeadDx);
                    } else {
                        const float sweep = std::sin(
                            static_cast<float>(e.age * SHEEP_IDLE_SWEEP_FREQ));
                        headDirX = sweep >= 0.0f ? 1.0f : -1.0f;
                        headDx = headDirX * (br * 1.08f) * (0.6f + 0.4f * std::fabs(sweep));
                    }
                    headDy = -bh * 0.05f;
                } else if (isGreeting) {
                    headDy -= std::sin(static_cast<float>(e.age * SHEEP_GREET_HEAD_BOB_FREQ))
                        * static_cast<float>(SHEEP_GREET_HEAD_BOB_AMP);
                } else if (isSleeping) {
                    headDx = headDirX * br * 0.95f;
                    headDy = bh * 0.10f;
                }
                const float headCx = cx + headDx;
                const float headCy = cy + headDy;

                d2dContext_->FillEllipse(
                    D2D1::Ellipse(D2D1::Point2F(headCx, headCy), headR, headR * 1.05f),
                    sheepFaceBrush_.Get());

                // Two ear blobs at the top of the head.
                const float earRx = headR * 0.32f;
                const float earRy = headR * 0.55f;
                d2dContext_->FillEllipse(
                    D2D1::Ellipse(D2D1::Point2F(headCx - headR * 0.55f,
                                                headCy - headR * 0.65f),
                                  earRx, earRy),
                    sheepEarBrush_.Get());
                d2dContext_->FillEllipse(
                    D2D1::Ellipse(D2D1::Point2F(headCx + headR * 0.55f,
                                                headCy - headR * 0.65f),
                                  earRx, earRy),
                    sheepEarBrush_.Get());

                // Eye — open dot in most states, closed slit while sleeping.
                if (isSleeping) {
                    const float slitY = headCy - headR * 0.05f;
                    const float slitX = headCx + headDirX * headR * 0.42f;
                    d2dContext_->DrawLine(
                        D2D1::Point2F(slitX - 1.4f, slitY),
                        D2D1::Point2F(slitX + 1.4f, slitY),
                        sheepInkBrush_.Get(),
                        1.0f);
                } else {
                    const float eyeR = headR * 0.22f;
                    d2dContext_->FillEllipse(
                        D2D1::Ellipse(D2D1::Point2F(headCx + headDirX * headR * 0.42f,
                                                    headCy - headR * 0.05f),
                                      eyeR, eyeR),
                        sheepInkBrush_.Get());
                }

                // Sleeping "Z" glyphs — two staggered Z's drifting up and
                // growing then fading, so the user reads the sleep state
                // instantly even from across the desktop. Drawn as 3-line
                // glyphs in body color (white) so they read on any biome.
                if (isSleeping) {
                    const float zBaseX = headCx + headDirX * headR * 0.7f;
                    const float zBaseY = headCy - headR * 1.4f;
                    for (int zi = 0; zi < 2; ++zi) {
                        const float phaseOffset = 0.5f * static_cast<float>(zi);
                        float t = static_cast<float>(
                            std::fmod(e.age / SHEEP_ZZZ_CYCLE_SEC + phaseOffset, 1.0));
                        // Skip the leading half-cycle of the offset Z so it
                        // doesn't pop in at full size.
                        const float zSize = static_cast<float>(
                            SHEEP_ZZZ_SIZE_START + t * (SHEEP_ZZZ_SIZE_END - SHEEP_ZZZ_SIZE_START));
                        const float zY = zBaseY - t * static_cast<float>(SHEEP_ZZZ_RISE);
                        const float zX = zBaseX + t * 4.0f * headDirX;
                        const float alpha = 1.0f - t;
                        sheepBodyBrush_->SetOpacity(alpha);
                        // Top horizontal
                        d2dContext_->DrawLine(
                            D2D1::Point2F(zX,         zY),
                            D2D1::Point2F(zX + zSize, zY),
                            sheepBodyBrush_.Get(),
                            1.1f);
                        // Diagonal
                        d2dContext_->DrawLine(
                            D2D1::Point2F(zX + zSize, zY),
                            D2D1::Point2F(zX,         zY + zSize),
                            sheepBodyBrush_.Get(),
                            1.1f);
                        // Bottom horizontal
                        d2dContext_->DrawLine(
                            D2D1::Point2F(zX,         zY + zSize),
                            D2D1::Point2F(zX + zSize, zY + zSize),
                            sheepBodyBrush_.Get(),
                            1.1f);
                    }
                    sheepBodyBrush_->SetOpacity(1.0f);
                }

                DrawPetName(e, cursorPosition);
                continue;
            }
            continue;
        }
        const float r = static_cast<float>(e.size);
        const D2D1_ELLIPSE flake = D2D1::Ellipse(
            D2D1::Point2F(static_cast<float>(e.x), static_cast<float>(e.y)),
            r,
            r);
        d2dContext_->FillEllipse(flake, snowflakeBrush_.Get());
    }
}

} // namespace desktopgrass
