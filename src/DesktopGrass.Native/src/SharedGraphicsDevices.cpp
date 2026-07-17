// SharedGraphicsDevices.cpp

#include "SharedGraphicsDevices.h"

#include <cstdio>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dcomp.lib")

namespace desktopgrass {

namespace {

void LogHR(const char* tag, HRESULT hr) {
    char buf[128];
    std::snprintf(buf, sizeof(buf), "[DesktopGrass] %s failed: 0x%08lX\n",
                  tag, static_cast<unsigned long>(hr));
    OutputDebugStringA(buf);
}

} // anonymous

bool SharedGraphicsDevices::Initialize() {
    Discard();

    UINT d3dFlags = D3D11_CREATE_DEVICE_BGRA_SUPPORT;

    static const D3D_FEATURE_LEVEL kFeatures[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1,
        D3D_FEATURE_LEVEL_10_0,
    };

    HRESULT hr = D3D11CreateDevice(
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
        if (FAILED(hr)) { LogHR("D3D11CreateDevice", hr); Discard(); return false; }
    }

    hr = d3dDevice_.As(&dxgiDevice_);
    if (FAILED(hr)) { LogHR("d3dDevice.As<IDXGIDevice1>", hr); Discard(); return false; }
    dxgiDevice_->SetMaximumFrameLatency(1);

    ComPtr<IDXGIAdapter> adapter;
    hr = dxgiDevice_->GetAdapter(&adapter);
    if (FAILED(hr)) { LogHR("GetAdapter", hr); Discard(); return false; }
    hr = adapter->GetParent(IID_PPV_ARGS(dxgiFactory_.ReleaseAndGetAddressOf()));
    if (FAILED(hr)) { LogHR("adapter.GetParent<IDXGIFactory2>", hr); Discard(); return false; }

    D2D1_FACTORY_OPTIONS opts{};
    hr = D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,
                           __uuidof(ID2D1Factory1), &opts,
                           reinterpret_cast<void**>(d2dFactory_.ReleaseAndGetAddressOf()));
    if (FAILED(hr)) { LogHR("D2D1CreateFactory", hr); Discard(); return false; }

    hr = d2dFactory_->CreateDevice(dxgiDevice_.Get(),
                                   d2dDevice_.ReleaseAndGetAddressOf());
    if (FAILED(hr)) { LogHR("CreateDevice(D2D)", hr); Discard(); return false; }

    // DComp device tied to the same DXGI device.
    hr = DCompositionCreateDevice(dxgiDevice_.Get(),
                                  __uuidof(IDCompositionDevice),
                                  reinterpret_cast<void**>(dcompDevice_.ReleaseAndGetAddressOf()));
    if (FAILED(hr)) { LogHR("DCompositionCreateDevice", hr); Discard(); return false; }

    return true;
}

void SharedGraphicsDevices::Discard() {
    dcompDevice_.Reset();
    d2dDevice_.Reset();
    d2dFactory_.Reset();
    dxgiFactory_.Reset();
    dxgiDevice_.Reset();
    d3dContext_.Reset();
    d3dDevice_.Reset();
}

} // namespace desktopgrass
