// RendererIntegrationTestSupport.h
//
// Shared real-resource test support for Renderer/SharedGraphicsDevices
// integration coverage. Every test in this header assumes it may be run on
// a machine without a usable GPU/DComp stack (e.g. some CI agents), so
// `ProbeRendererGraphics` is used to gracefully skip (not fail) when the
// underlying capability genuinely isn't there — real hardware/driver
// failures are not this test suite's concern, only the production
// recovery/sharing logic is.
//
// RendererTestAccess is the one seam production code grants test code into
// Renderer's private members (see the `friend struct RendererTestAccess;`
// declaration in Renderer.h). Keeping it here lets every integration test
// file that needs it share one definition instead of redeclaring it.

#pragma once

#include "Renderer.h"

#include <combaseapi.h>

#include <cstdio>
#include <string>

namespace desktopgrass {

struct HeldRendererResources {
    Microsoft::WRL::ComPtr<ID3D11Device> d3dDevice;
    Microsoft::WRL::ComPtr<IDXGISwapChain1> swapChain;
    Microsoft::WRL::ComPtr<ID2D1Device> d2dDevice;
    Microsoft::WRL::ComPtr<ID2D1DeviceContext> d2dContext;
    Microsoft::WRL::ComPtr<IDCompositionDevice> dcompDevice;
    Microsoft::WRL::ComPtr<IDCompositionVisual> dcompVisual;
};

struct RendererTestAccess {
    static HeldRendererResources HoldCoreResources(const Renderer& renderer) {
        return HeldRendererResources{
            renderer.d3dDevice_,
            renderer.swapChain_,
            renderer.d2dDevice_,
            renderer.d2dContext_,
            renderer.dcompDevice_,
            renderer.dcompVisual_,
        };
    }

    static void ForceDeviceLoss(
        Renderer& renderer,
        const DeviceLossInfo& loss)
    {
        renderer.HandleDeviceLoss(loss);
    }

    static void Cleanup(Renderer& renderer) {
        renderer.Cleanup();
    }

    static bool IsReady(const Renderer& renderer) {
        return renderer.recovery_.IsReady();
    }

    static bool IsRetryPending(const Renderer& renderer) {
        return renderer.recovery_.IsRetryPending();
    }

    static void ForceNextRecoveryToFail(Renderer& renderer) {
        renderer.widthPx_ = 0;
    }

    static SharedGraphicsDevices* SharedDevices(const Renderer& renderer) {
        return renderer.shared_;
    }

    static ID3D11Device* SharedD3DDevice(const Renderer& renderer) {
        return renderer.d3dDevice_.Get();
    }

    static ID2D1Device* SharedD2DDevice(const Renderer& renderer) {
        return renderer.d2dDevice_.Get();
    }

    static IDCompositionDevice* SharedDCompDevice(const Renderer& renderer) {
        return renderer.dcompDevice_.Get();
    }

    static IDXGISwapChain1* PerWindowSwapChain(const Renderer& renderer) {
        return renderer.swapChain_.Get();
    }

    static ID2D1DeviceContext* PerWindowD2DContext(const Renderer& renderer) {
        return renderer.d2dContext_.Get();
    }

    static IDCompositionVisual* PerWindowDCompVisual(const Renderer& renderer) {
        return renderer.dcompVisual_.Get();
    }

    static bool HasDistinctCoreResources(
        const Renderer& renderer,
        const HeldRendererResources& old)
    {
        return renderer.d3dDevice_
            && renderer.d3dDevice_.Get() != old.d3dDevice.Get()
            && renderer.swapChain_
            && renderer.swapChain_.Get() != old.swapChain.Get()
            && renderer.d2dDevice_
            && renderer.d2dDevice_.Get() != old.d2dDevice.Get()
            && renderer.d2dContext_
            && renderer.d2dContext_.Get() != old.d2dContext.Get()
            && renderer.dcompDevice_
            && renderer.dcompDevice_.Get() != old.dcompDevice.Get()
            && renderer.dcompVisual_
            && renderer.dcompVisual_.Get() != old.dcompVisual.Get()
            && renderer.dcompTarget_
            && renderer.d2dTarget_;
    }

    static bool HasNoGraphicsResources(const Renderer& renderer) {
        if (renderer.d3dDevice_ || renderer.d3dContext_
            || renderer.dxgiDevice_ || renderer.dxgiFactory_
            || renderer.swapChain_ || renderer.d2dFactory_
            || renderer.d2dDevice_ || renderer.d2dContext_
            || renderer.d2dTarget_ || renderer.roundStrokeStyle_
            || renderer.mushroomStemBrush_ || renderer.cactusBrush_
            || renderer.tumbleweedBrush_ || renderer.snowflakeBrush_
            || renderer.snowTipBrush_ || renderer.snowBankShadowBrush_
            || renderer.pineBrush_ || renderer.pineShadowBrush_
            || renderer.pineHighlightBrush_ || renderer.birchBarkBrush_
            || renderer.birchMarkBrush_ || renderer.mapleTrunkBrush_
            || renderer.mapleTrunkDarkBrush_ || renderer.bubbleStrokeBrush_
            || renderer.bubbleHighlightBrush_ || renderer.fishFinBrush_
            || renderer.sheepBodyBrush_ || renderer.sheepLegBrush_
            || renderer.sheepFaceBrush_ || renderer.sheepEarBrush_
            || renderer.sheepInkBrush_ || renderer.bunnyBodyBrush_
            || renderer.bunnyBellyBrush_ || renderer.bunnyEarBrush_
            || renderer.bunnyEarInnerBrush_ || renderer.bunnyTailBrush_
            || renderer.bunnyEyeBrush_ || renderer.bunnyNoseBrush_
            || renderer.hedgehogBodyBrush_ || renderer.hedgehogSpikeBrush_
            || renderer.hedgehogSpikeTipBrush_ || renderer.hedgehogNoseBrush_
            || renderer.hedgehogEyeBrush_ || renderer.butterflyBodyBrush_
            || renderer.fireflyBodyBrush_ || renderer.fireflyGlowBrush_
            || renderer.birdBrush_ || renderer.petNameBrush_
            || renderer.petNameShadowBrush_ || renderer.dwriteFactory_
            || renderer.petNameTextFormat_ || renderer.dcompDevice_
            || renderer.dcompTarget_ || renderer.dcompVisual_) {
            return false;
        }

        for (const auto& row : renderer.brushes_) {
            for (const auto& brush : row) {
                if (brush) return false;
            }
        }
        for (const auto& brush : renderer.flowerHeadBrushes_) {
            if (brush) return false;
        }
        for (const auto& brush : renderer.mushroomCapBrushes_) {
            if (brush) return false;
        }
        for (const auto& brush : renderer.leafBrushes_) {
            if (brush) return false;
        }
        for (const auto& brush : renderer.mapleCanopyBrushes_) {
            if (brush) return false;
        }
        for (const auto& brush : renderer.coralBrushes_) {
            if (brush) return false;
        }
        for (const auto& brush : renderer.fishBrushes_) {
            if (brush) return false;
        }
        for (const auto& brushes : renderer.catCoatBrushes_) {
            if (brushes.body || brushes.leg || brushes.face
                || brushes.ear || brushes.ink) {
                return false;
            }
        }
        for (const auto& brush : renderer.butterflyWingBrushes_) {
            if (brush) return false;
        }
        for (const auto& brush : renderer.butterflyAccentBrushes_) {
            if (brush) return false;
        }
        return true;
    }
};

namespace recoverytest {

struct CapabilityResult {
    bool available;
    const char* stage;
    HRESULT result;
};

inline CapabilityResult CapabilityFailure(const char* stage, HRESULT result) {
    return CapabilityResult{false, stage, result};
}

inline std::string CapabilityMessage(const CapabilityResult& capability) {
    char buffer[256];
    std::snprintf(
        buffer,
        sizeof(buffer),
        "Skipping real renderer recovery integration: %s failed with 0x%08lX",
        capability.stage,
        static_cast<unsigned long>(capability.result));
    return buffer;
}

class ComApartment {
public:
    ComApartment()
        : result_(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED)),
          uninitialize_(SUCCEEDED(result_)) {
    }

    ~ComApartment() {
        if (uninitialize_) {
            CoUninitialize();
        }
    }

    HRESULT Result() const { return result_; }

private:
    HRESULT result_;
    bool uninitialize_;
};

inline LRESULT CALLBACK RecoveryTestWndProc(
    HWND hwnd,
    UINT message,
    WPARAM wparam,
    LPARAM lparam)
{
    return DefWindowProcW(hwnd, message, wparam, lparam);
}

class HiddenWindow {
public:
    HiddenWindow()
        : instance_(GetModuleHandleW(nullptr)),
          className_(L"DesktopGrass.Native.Recovery.")
    {
        className_ += std::to_wstring(GetCurrentProcessId());
        className_ += L".";
        className_ += std::to_wstring(
            reinterpret_cast<std::uintptr_t>(this));

        WNDCLASSEXW windowClass{};
        windowClass.cbSize = sizeof(windowClass);
        windowClass.lpfnWndProc = RecoveryTestWndProc;
        windowClass.hInstance = instance_;
        windowClass.lpszClassName = className_.c_str();
        if (!RegisterClassExW(&windowClass)) {
            result_ = HRESULT_FROM_WIN32(GetLastError());
            return;
        }
        registered_ = true;

        hwnd_ = CreateWindowExW(
            WS_EX_TOOLWINDOW,
            className_.c_str(),
            L"DesktopGrass recovery test",
            WS_POPUP,
            0,
            0,
            320,
            128,
            nullptr,
            nullptr,
            instance_,
            nullptr);
        if (!hwnd_) {
            result_ = HRESULT_FROM_WIN32(GetLastError());
        }
    }

    ~HiddenWindow() {
        if (hwnd_) {
            DestroyWindow(hwnd_);
        }
        if (registered_) {
            UnregisterClassW(className_.c_str(), instance_);
        }
    }

    HWND Get() const { return hwnd_; }
    HRESULT Result() const { return result_; }

private:
    HINSTANCE instance_ = nullptr;
    std::wstring className_;
    HWND hwnd_ = nullptr;
    bool registered_ = false;
    HRESULT result_ = S_OK;
};

// Probes the same D3D11 → DXGI → D2D → DWrite → DComp → swap-chain →
// present chain Renderer/SharedGraphicsDevices exercise, independently of
// both, so a genuine capability failure on this machine (no GPU, no WARP,
// remote desktop session without a compositor, etc.) is diagnosed
// precisely and its caller can skip instead of failing.
inline CapabilityResult ProbeRendererGraphics(HWND hwnd) {
    using Microsoft::WRL::ComPtr;

    static const D3D_FEATURE_LEVEL featureLevels[] = {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0,
        D3D_FEATURE_LEVEL_10_1,
        D3D_FEATURE_LEVEL_10_0,
    };

    ComPtr<ID3D11Device> d3dDevice;
    ComPtr<ID3D11DeviceContext> d3dContext;
    HRESULT hr = D3D11CreateDevice(
        nullptr,
        D3D_DRIVER_TYPE_HARDWARE,
        nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        featureLevels,
        ARRAYSIZE(featureLevels),
        D3D11_SDK_VERSION,
        d3dDevice.GetAddressOf(),
        nullptr,
        d3dContext.GetAddressOf());
    if (FAILED(hr)) {
        hr = D3D11CreateDevice(
            nullptr,
            D3D_DRIVER_TYPE_WARP,
            nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            featureLevels,
            ARRAYSIZE(featureLevels),
            D3D11_SDK_VERSION,
            d3dDevice.ReleaseAndGetAddressOf(),
            nullptr,
            d3dContext.ReleaseAndGetAddressOf());
    }
    if (FAILED(hr)) return CapabilityFailure("D3D11CreateDevice", hr);

    ComPtr<IDXGIDevice1> dxgiDevice;
    hr = d3dDevice.As(&dxgiDevice);
    if (FAILED(hr)) return CapabilityFailure("ID3D11Device::QueryInterface", hr);

    ComPtr<IDXGIAdapter> adapter;
    hr = dxgiDevice->GetAdapter(&adapter);
    if (FAILED(hr)) return CapabilityFailure("IDXGIDevice::GetAdapter", hr);

    ComPtr<IDXGIFactory2> dxgiFactory;
    hr = adapter->GetParent(IID_PPV_ARGS(dxgiFactory.GetAddressOf()));
    if (FAILED(hr)) return CapabilityFailure("IDXGIAdapter::GetParent", hr);

    D2D1_FACTORY_OPTIONS options{};
    ComPtr<ID2D1Factory1> d2dFactory;
    hr = D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED,
        __uuidof(ID2D1Factory1),
        &options,
        reinterpret_cast<void**>(d2dFactory.GetAddressOf()));
    if (FAILED(hr)) return CapabilityFailure("D2D1CreateFactory", hr);

    ComPtr<ID2D1Device> d2dDevice;
    hr = d2dFactory->CreateDevice(dxgiDevice.Get(), &d2dDevice);
    if (FAILED(hr)) return CapabilityFailure("ID2D1Factory1::CreateDevice", hr);

    ComPtr<ID2D1DeviceContext> d2dContext;
    hr = d2dDevice->CreateDeviceContext(
        D2D1_DEVICE_CONTEXT_OPTIONS_NONE,
        &d2dContext);
    if (FAILED(hr)) return CapabilityFailure("ID2D1Device::CreateDeviceContext", hr);

    ComPtr<IDWriteFactory> dwriteFactory;
    hr = DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED,
        __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(dwriteFactory.GetAddressOf()));
    if (FAILED(hr)) return CapabilityFailure("DWriteCreateFactory", hr);

    ComPtr<IDWriteTextFormat> textFormat;
    hr = dwriteFactory->CreateTextFormat(
        L"Segoe UI",
        nullptr,
        DWRITE_FONT_WEIGHT_REGULAR,
        DWRITE_FONT_STYLE_NORMAL,
        DWRITE_FONT_STRETCH_NORMAL,
        10.0f,
        L"",
        &textFormat);
    if (FAILED(hr)) return CapabilityFailure("IDWriteFactory::CreateTextFormat", hr);

    ComPtr<IDCompositionDevice> dcompDevice;
    hr = DCompositionCreateDevice(
        dxgiDevice.Get(),
        __uuidof(IDCompositionDevice),
        reinterpret_cast<void**>(dcompDevice.GetAddressOf()));
    if (FAILED(hr)) return CapabilityFailure("DCompositionCreateDevice", hr);

    ComPtr<IDCompositionTarget> dcompTarget;
    hr = dcompDevice->CreateTargetForHwnd(
        hwnd,
        TRUE,
        &dcompTarget);
    if (FAILED(hr)) return CapabilityFailure("IDCompositionDevice::CreateTargetForHwnd", hr);

    ComPtr<IDCompositionVisual> dcompVisual;
    hr = dcompDevice->CreateVisual(&dcompVisual);
    if (FAILED(hr)) return CapabilityFailure("IDCompositionDevice::CreateVisual", hr);

    DXGI_SWAP_CHAIN_DESC1 swapChainDescription{};
    swapChainDescription.Width = 320;
    swapChainDescription.Height = 128;
    swapChainDescription.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    swapChainDescription.SampleDesc.Count = 1;
    swapChainDescription.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swapChainDescription.BufferCount = 2;
    swapChainDescription.Scaling = DXGI_SCALING_STRETCH;
    swapChainDescription.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    swapChainDescription.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;

    ComPtr<IDXGISwapChain1> swapChain;
    hr = dxgiFactory->CreateSwapChainForComposition(
        d3dDevice.Get(),
        &swapChainDescription,
        nullptr,
        &swapChain);
    if (FAILED(hr)) return CapabilityFailure("IDXGIFactory2::CreateSwapChainForComposition", hr);

    ComPtr<IDXGISurface2> surface;
    hr = swapChain->GetBuffer(0, IID_PPV_ARGS(surface.GetAddressOf()));
    if (FAILED(hr)) return CapabilityFailure("IDXGISwapChain1::GetBuffer", hr);

    const D2D1_BITMAP_PROPERTIES1 bitmapProperties =
        D2D1::BitmapProperties1(
            D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
            D2D1::PixelFormat(
                DXGI_FORMAT_B8G8R8A8_UNORM,
                D2D1_ALPHA_MODE_PREMULTIPLIED),
            96.0f,
            96.0f);
    ComPtr<ID2D1Bitmap1> targetBitmap;
    hr = d2dContext->CreateBitmapFromDxgiSurface(
        surface.Get(),
        &bitmapProperties,
        &targetBitmap);
    if (FAILED(hr)) return CapabilityFailure("ID2D1DeviceContext::CreateBitmapFromDxgiSurface", hr);
    d2dContext->SetTarget(targetBitmap.Get());

    hr = dcompVisual->SetContent(swapChain.Get());
    if (FAILED(hr)) return CapabilityFailure("IDCompositionVisual::SetContent", hr);

    hr = dcompTarget->SetRoot(dcompVisual.Get());
    if (FAILED(hr)) return CapabilityFailure("IDCompositionTarget::SetRoot", hr);

    hr = dcompDevice->Commit();
    if (FAILED(hr)) return CapabilityFailure("IDCompositionDevice::Commit", hr);

    d2dContext->BeginDraw();
    d2dContext->Clear(D2D1::ColorF(0.0f, 0.0f, 0.0f, 0.0f));
    hr = d2dContext->EndDraw();
    if (FAILED(hr)) return CapabilityFailure("ID2D1DeviceContext::EndDraw", hr);

    DXGI_PRESENT_PARAMETERS presentParameters{};
    hr = swapChain->Present1(0, 0, &presentParameters);
    if (FAILED(hr)) return CapabilityFailure("IDXGISwapChain1::Present1", hr);

    return CapabilityResult{true, "", S_OK};
}

} // namespace recoverytest
} // namespace desktopgrass
