// Real-resource integration coverage for Renderer device-loss recovery.

#include "TestHelpers.h"

#include "GraphicsDeviceRecovery.h"
#include "Renderer.h"
#include "RendererIntegrationTestSupport.h"
#include "SharedGraphicsDevices.h"

#include <combaseapi.h>

#include <cstdint>
#include <cstdio>
#include <optional>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
using namespace desktopgrass;
using namespace desktopgrass::recoverytest;

namespace {

bool WriteQualificationMarker() {
    wchar_t markerPath[32768]{};
    const DWORD length = GetEnvironmentVariableW(
        L"DESKTOPGRASS_DEVICE_LOSS_MARKER",
        markerPath,
        static_cast<DWORD>(ARRAYSIZE(markerPath)));
    if (length == 0) {
        return GetLastError() == ERROR_ENVVAR_NOT_FOUND;
    }
    if (length >= ARRAYSIZE(markerPath)) {
        return false;
    }

    HANDLE marker = CreateFileW(
        markerPath,
        GENERIC_WRITE,
        0,
        nullptr,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (marker == INVALID_HANDLE_VALUE) {
        return false;
    }

    constexpr char payload[] = "renderer-recovery-integration:pass\n";
    DWORD written = 0;
    const BOOL ok = WriteFile(
        marker,
        payload,
        static_cast<DWORD>(sizeof(payload) - 1),
        &written,
        nullptr);
    const BOOL closed = CloseHandle(marker);
    return ok && closed && written == sizeof(payload) - 1;
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(RendererRecoveryIntegrationTests)
{
public:
TEST_METHOD(RendererRecreatesAndReleasesItsRealGraphicsResourceGraph) {
    ComApartment apartment;
    if (FAILED(apartment.Result())) {
        const CapabilityResult capability =
            CapabilityFailure("CoInitializeEx", apartment.Result());
        const std::string message = CapabilityMessage(capability);
        Logger::WriteMessage(message.c_str());
        return;
    }

    HiddenWindow window;
    if (!window.Get()) {
        const CapabilityResult capability =
            CapabilityFailure("CreateWindowExW", window.Result());
        const std::string message = CapabilityMessage(capability);
        Logger::WriteMessage(message.c_str());
        return;
    }

    const CapabilityResult capability = ProbeRendererGraphics(window.Get());
    if (!capability.available) {
        const std::string message = CapabilityMessage(capability);
        Logger::WriteMessage(message.c_str());
        return;
    }

    SharedGraphicsDevices sharedDevices;
    Assert::IsTrue(sharedDevices.Initialize());

    Renderer renderer;
    Assert::IsTrue(renderer.Initialize(
        sharedDevices,
        window.Get(),
        320,
        128,
        96,
        CANONICAL_TEST_SEED,
        0.5));

    Sim* const simAddress = &renderer.GetSim();
    const Blade* const bladeStorage = renderer.GetSim().blades.data();
    const std::size_t bladeCount = renderer.GetSim().blades.size();
    renderer.GetSim().currentScene = Scene::Grass;
    renderer.GetSim().currentCritter = CritterKind::Cat;
    renderer.GetSim().critterCountOverride = 3;
    renderer.GetSim().blades.front().cutHeight = 0.42;
    renderer.GetSim().blades.front().cutAnimStart = -1.0;
    renderer.GetSim().blades.front().regrowStart = -1.0;

    renderer.RenderFrame(0.01, nullptr, 0);
    Assert::IsTrue(renderer.GetSim().globalTime == Near(0.01));
    Assert::IsTrue(RendererTestAccess::IsReady(renderer));

    const HeldRendererResources old =
        RendererTestAccess::HoldCoreResources(renderer);

    RendererTestAccess::ForceDeviceLoss(
        renderer,
        DeviceLossInfo{
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_REMOVED,
            DXGI_ERROR_DEVICE_HUNG,
        });

    Assert::IsFalse(RendererTestAccess::IsReady(renderer));
    Assert::IsTrue(renderer.NeedsSharedDeviceRecovery());
    const std::vector<Renderer*> renderers{&renderer};
    Assert::IsTrue(RecoverSharedGraphicsDevices(
        sharedDevices, renderers, true));

    Assert::IsTrue(RendererTestAccess::IsReady(renderer));
    Assert::IsFalse(RendererTestAccess::IsRetryPending(renderer));
    Assert::IsTrue(RendererTestAccess::HasDistinctCoreResources(renderer, old));
    Assert::IsTrue(&renderer.GetSim() == simAddress);
    Assert::IsTrue(renderer.GetSim().blades.data() == bladeStorage);
    Assert::IsTrue(renderer.GetSim().blades.size() == bladeCount);
    Assert::IsTrue(renderer.GetSim().currentScene == Scene::Grass);
    Assert::IsTrue(renderer.GetSim().currentCritter == CritterKind::Cat);
    Assert::IsTrue(renderer.GetSim().critterCountOverride == 3);
    Assert::IsTrue(renderer.GetSim().blades.front().cutHeight == Near(0.42));

    renderer.RenderFrame(0.01, nullptr, 0);
    Assert::IsTrue(renderer.GetSim().globalTime == Near(0.02));
    Assert::IsTrue(RendererTestAccess::IsReady(renderer));

    const HeldRendererResources recovered =
        RendererTestAccess::HoldCoreResources(renderer);
    Assert::IsTrue(recovered.d3dDevice);
    RendererTestAccess::ForceNextRecoveryToFail(renderer);
    RendererTestAccess::ForceDeviceLoss(
        renderer,
        DeviceLossInfo{
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_RESET,
            DXGI_ERROR_DEVICE_HUNG,
        });

    Assert::IsFalse(RendererTestAccess::IsReady(renderer));
    Assert::IsTrue(RendererTestAccess::IsRetryPending(renderer));
    Assert::IsTrue(RendererTestAccess::HasNoGraphicsResources(renderer));

    renderer.RenderFrame(0.01, nullptr, 0);
    Assert::IsTrue(renderer.GetSim().globalTime == Near(0.03));
    Assert::IsTrue(RendererTestAccess::HasNoGraphicsResources(renderer));

    RendererTestAccess::Cleanup(renderer);
    Assert::IsTrue(RendererTestAccess::HasNoGraphicsResources(renderer));
    Assert::IsFalse(RendererTestAccess::IsReady(renderer));
    Assert::IsFalse(RendererTestAccess::IsRetryPending(renderer));

    renderer.RenderFrame(0.01, nullptr, 0);
    Assert::IsTrue(renderer.GetSim().globalTime == Near(0.03));
    Assert::IsTrue(RendererTestAccess::HasNoGraphicsResources(renderer));
    Assert::IsTrue(WriteQualificationMarker());
}
};
}
