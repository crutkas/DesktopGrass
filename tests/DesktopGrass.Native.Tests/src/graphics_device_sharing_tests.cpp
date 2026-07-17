// Real-resource integration coverage for the shared process-wide device
// graph: multi-window sharing, ownership across renderer/window churn, and
// coordinated recovery across every dependent renderer at once.
//
// Reuses the capability probing and RendererTestAccess helpers shared with
// renderer_recovery_integration_tests.cpp (see
// RendererIntegrationTestSupport.h) so this file focuses purely on
// SharedGraphicsDevices/GraphicsDeviceRecovery behavior across ≥2 renderers.

#include "TestHelpers.h"

#include "GraphicsDeviceRecovery.h"
#include "Renderer.h"
#include "RendererIntegrationTestSupport.h"
#include "SharedGraphicsDevices.h"

#include <memory>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
using namespace desktopgrass;
using namespace desktopgrass::recoverytest;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{

TEST_CLASS(GraphicsDeviceSharingTests)
{
public:
    // Everything needed to skip cleanly (not fail) on a machine without a
    // usable GPU/DComp stack, mirroring
    // RendererRecoveryIntegrationTests::RendererRecreatesAndReleasesItsRealGraphicsResourceGraph.
    // Returns true if the environment is capable and the caller should
    // proceed; on false it has already logged the skip reason.
    static bool RequireRealGraphicsCapability(
        const ComApartment& apartment,
        const HiddenWindow& window)
    {
        if (FAILED(apartment.Result())) {
            const CapabilityResult capability =
                CapabilityFailure("CoInitializeEx", apartment.Result());
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return false;
        }
        if (!window.Get()) {
            const CapabilityResult capability =
                CapabilityFailure("CreateWindowExW", window.Result());
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return false;
        }
        const CapabilityResult capability = ProbeRendererGraphics(window.Get());
        if (!capability.available) {
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return false;
        }
        return true;
    }

    TEST_METHOD(TwoRenderersOnOneSharedGraphHaveIdenticalDeviceLevelObjectsAndDistinctPerWindowResources) {
        ComApartment apartment;
        HiddenWindow windowA;
        if (!RequireRealGraphicsCapability(apartment, windowA)) return;
        HiddenWindow windowB;
        if (!windowB.Get()) {
            const CapabilityResult capability =
                CapabilityFailure("CreateWindowExW", windowB.Result());
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return;
        }

        SharedGraphicsDevices sharedDevices;
        Assert::IsTrue(sharedDevices.Initialize());

        Renderer rendererA;
        Assert::IsTrue(rendererA.Initialize(
            sharedDevices, windowA.Get(), 320, 128, 96, CANONICAL_TEST_SEED, 0.5));
        Renderer rendererB;
        Assert::IsTrue(rendererB.Initialize(
            sharedDevices, windowB.Get(), 400, 200, 96, CANONICAL_TEST_SEED, 0.5));

        // Every device-level object came from the one shared graph, so both
        // renderers must observe the exact same pointer for each of them.
        Assert::IsTrue(
            RendererTestAccess::SharedD3DDevice(rendererA)
            == RendererTestAccess::SharedD3DDevice(rendererB));
        Assert::IsTrue(
            RendererTestAccess::SharedD2DDevice(rendererA)
            == RendererTestAccess::SharedD2DDevice(rendererB));
        Assert::IsTrue(
            RendererTestAccess::SharedDCompDevice(rendererA)
            == RendererTestAccess::SharedDCompDevice(rendererB));
        Assert::IsTrue(
            RendererTestAccess::SharedD3DDevice(rendererA)
            == sharedDevices.D3DDevice().Get());

        // Per-window resources must never be shared, even though both
        // windows draw from the same device graph.
        Assert::IsTrue(
            RendererTestAccess::PerWindowSwapChain(rendererA)
            != RendererTestAccess::PerWindowSwapChain(rendererB));
        Assert::IsTrue(
            RendererTestAccess::PerWindowD2DContext(rendererA)
            != RendererTestAccess::PerWindowD2DContext(rendererB));
        Assert::IsTrue(
            RendererTestAccess::PerWindowDCompVisual(rendererA)
            != RendererTestAccess::PerWindowDCompVisual(rendererB));

        // Each window keeps rendering its own, independently sized surface.
        rendererA.RenderFrame(0.01, nullptr, 0);
        rendererB.RenderFrame(0.01, nullptr, 0);
        Assert::IsTrue(rendererA.GetWidthPx() == 320);
        Assert::IsTrue(rendererB.GetWidthPx() == 400);
        Assert::IsTrue(RendererTestAccess::IsReady(rendererA));
        Assert::IsTrue(RendererTestAccess::IsReady(rendererB));

        RendererTestAccess::Cleanup(rendererA);
        RendererTestAccess::Cleanup(rendererB);
    }

    TEST_METHOD(TopologyAddAndRemoveReusesTheStillHealthySharedGraphWithoutRecreatingIt) {
        ComApartment apartment;
        HiddenWindow windowA;
        if (!RequireRealGraphicsCapability(apartment, windowA)) return;

        SharedGraphicsDevices sharedDevices;
        Assert::IsTrue(sharedDevices.Initialize());
        ID3D11Device* const originalSharedDevice = sharedDevices.D3DDevice().Get();

        {
            // A monitor is connected: create its renderer, render a frame,
            // then simulate it being unplugged by destroying the renderer —
            // exactly what topology reconciliation does when a monitor
            // disappears (see DisplayTopology.cpp).
            Renderer rendererA;
            Assert::IsTrue(rendererA.Initialize(
                sharedDevices, windowA.Get(), 320, 128, 96, CANONICAL_TEST_SEED, 0.5));
            rendererA.RenderFrame(0.01, nullptr, 0);
            Assert::IsTrue(RendererTestAccess::IsReady(rendererA));
            RendererTestAccess::Cleanup(rendererA);
        }

        // Losing a window must never touch the shared graph other windows
        // (or a subsequently reconnected monitor) still depend on.
        Assert::IsTrue(sharedDevices.IsReady());
        Assert::IsTrue(sharedDevices.D3DDevice().Get() == originalSharedDevice);

        HiddenWindow windowB;
        if (!windowB.Get()) {
            const CapabilityResult capability =
                CapabilityFailure("CreateWindowExW", windowB.Result());
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return;
        }

        // A monitor is reconnected: its new renderer must borrow the exact
        // same still-healthy shared graph rather than forcing a rebuild.
        Renderer rendererB;
        Assert::IsTrue(rendererB.Initialize(
            sharedDevices, windowB.Get(), 320, 128, 96, CANONICAL_TEST_SEED, 0.5));
        Assert::IsTrue(
            RendererTestAccess::SharedD3DDevice(rendererB) == originalSharedDevice);
        Assert::IsTrue(RendererTestAccess::IsReady(rendererB));

        RendererTestAccess::Cleanup(rendererB);
    }

    TEST_METHOD(SharedGraphicsDevicesSurvivesRendererDestructionAndStaysUsableForANewRenderer) {
        ComApartment apartment;
        HiddenWindow window;
        if (!RequireRealGraphicsCapability(apartment, window)) return;

        SharedGraphicsDevices sharedDevices;
        Assert::IsTrue(sharedDevices.Initialize());

        // sharedDevices outlives renderer by contract: the owner (App /
        // Benchmark / this test) always declares it before every window it
        // backs, so the renderer's non-owning pointer never dangles across
        // that renderer's own lifetime.
        {
            Renderer renderer;
            Assert::IsTrue(renderer.Initialize(
                sharedDevices, window.Get(), 320, 128, 96, CANONICAL_TEST_SEED, 0.5));
            renderer.RenderFrame(0.01, nullptr, 0);
            Assert::IsTrue(RendererTestAccess::IsReady(renderer));
            // Renderer destructor runs here; sharedDevices must be unaffected.
        }

        Assert::IsTrue(sharedDevices.IsReady());

        HiddenWindow secondWindow;
        if (!secondWindow.Get()) {
            const CapabilityResult capability =
                CapabilityFailure("CreateWindowExW", secondWindow.Result());
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return;
        }

        Renderer secondRenderer;
        Assert::IsTrue(secondRenderer.Initialize(
            sharedDevices, secondWindow.Get(), 320, 128, 96, CANONICAL_TEST_SEED, 0.5));
        Assert::IsTrue(RendererTestAccess::IsReady(secondRenderer));
        RendererTestAccess::Cleanup(secondRenderer);
    }

    TEST_METHOD(CoordinatedRecoveryReplacesTheSharedGraphOnceAndRebuildsEveryRendererWithoutStaleReferences) {
        ComApartment apartment;
        HiddenWindow windowA;
        if (!RequireRealGraphicsCapability(apartment, windowA)) return;
        HiddenWindow windowB;
        if (!windowB.Get()) {
            const CapabilityResult capability =
                CapabilityFailure("CreateWindowExW", windowB.Result());
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return;
        }

        SharedGraphicsDevices sharedDevices;
        Assert::IsTrue(sharedDevices.Initialize());

        Renderer rendererA;
        Assert::IsTrue(rendererA.Initialize(
            sharedDevices, windowA.Get(), 320, 128, 96, CANONICAL_TEST_SEED, 0.5));
        Renderer rendererB;
        Assert::IsTrue(rendererB.Initialize(
            sharedDevices, windowB.Get(), 400, 200, 96, CANONICAL_TEST_SEED, 0.5));

        rendererA.GetSim().currentScene = Scene::Grass;
        rendererA.GetSim().blades.front().cutHeight = 0.42;
        rendererB.GetSim().currentScene = Scene::Grass;
        rendererB.GetSim().blades.front().cutHeight = 0.73;

        rendererA.RenderFrame(0.01, nullptr, 0);
        rendererB.RenderFrame(0.01, nullptr, 0);
        Assert::IsTrue(rendererA.GetSim().globalTime == Near(0.01));
        Assert::IsTrue(rendererB.GetSim().globalTime == Near(0.01));

        const HeldRendererResources oldA =
            RendererTestAccess::HoldCoreResources(rendererA);
        const HeldRendererResources oldB =
            RendererTestAccess::HoldCoreResources(rendererB);
        ID3D11Device* const oldSharedDevice = sharedDevices.D3DDevice().Get();

        // A device-removed event has been detected by (say) rendererA's own
        // Present1 call. The process-wide coordinator is the only thing
        // that discards and replaces the shared graph, and it does so once
        // for every dependent renderer in a single pass — this is the
        // exact call App::RecoverSharedDeviceGraphIfNeeded and Benchmark's
        // frame loop make.
        std::vector<Renderer*> renderers{&rendererA, &rendererB};
        Assert::IsTrue(RecoverSharedGraphicsDevices(
            sharedDevices, renderers, true));

        // The shared graph itself was replaced exactly once, and both
        // renderers must be borrowing the identical new instance — never a
        // mix of old and new, and never two different "new" instances.
        Assert::IsTrue(sharedDevices.D3DDevice().Get() != oldSharedDevice);
        Assert::IsTrue(
            RendererTestAccess::SharedD3DDevice(rendererA)
            == sharedDevices.D3DDevice().Get());
        Assert::IsTrue(
            RendererTestAccess::SharedD3DDevice(rendererB)
            == sharedDevices.D3DDevice().Get());
        Assert::IsTrue(
            RendererTestAccess::SharedD2DDevice(rendererA)
            == RendererTestAccess::SharedD2DDevice(rendererB));
        Assert::IsTrue(
            RendererTestAccess::SharedDCompDevice(rendererA)
            == RendererTestAccess::SharedDCompDevice(rendererB));

        // Every per-window resource was rebuilt fresh — distinct from its
        // own pre-loss value and from the other window's, so neither window
        // is left drawing with a stale or borrowed COM reference.
        Assert::IsTrue(RendererTestAccess::HasDistinctCoreResources(rendererA, oldA));
        Assert::IsTrue(RendererTestAccess::HasDistinctCoreResources(rendererB, oldB));
        Assert::IsTrue(
            RendererTestAccess::PerWindowSwapChain(rendererA)
            != RendererTestAccess::PerWindowSwapChain(rendererB));
        Assert::IsTrue(
            RendererTestAccess::PerWindowD2DContext(rendererA)
            != RendererTestAccess::PerWindowD2DContext(rendererB));

        // Both windows are ready and immediately render again without
        // going through their own 1-second retry backoff, and each kept
        // its own simulation state throughout the outage.
        Assert::IsTrue(RendererTestAccess::IsReady(rendererA));
        Assert::IsTrue(RendererTestAccess::IsReady(rendererB));
        Assert::IsFalse(RendererTestAccess::IsRetryPending(rendererA));
        Assert::IsFalse(RendererTestAccess::IsRetryPending(rendererB));
        Assert::IsTrue(rendererA.GetSim().blades.front().cutHeight == Near(0.42));
        Assert::IsTrue(rendererB.GetSim().blades.front().cutHeight == Near(0.73));

        rendererA.RenderFrame(0.01, nullptr, 0);
        rendererB.RenderFrame(0.01, nullptr, 0);
        Assert::IsTrue(rendererA.GetSim().globalTime == Near(0.02));
        Assert::IsTrue(rendererB.GetSim().globalTime == Near(0.02));

        RendererTestAccess::Cleanup(rendererA);
        RendererTestAccess::Cleanup(rendererB);
    }

    TEST_METHOD(CoordinatedRecoveryRollsBackEveryRendererWhenOneWindowFailsToRebuild) {
        ComApartment apartment;
        HiddenWindow windowA;
        if (!RequireRealGraphicsCapability(apartment, windowA)) return;
        HiddenWindow windowB;
        if (!windowB.Get()) {
            const CapabilityResult capability =
                CapabilityFailure("CreateWindowExW", windowB.Result());
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return;
        }

        SharedGraphicsDevices sharedDevices;
        Assert::IsTrue(sharedDevices.Initialize());

        Renderer rendererA;
        Assert::IsTrue(rendererA.Initialize(
            sharedDevices, windowA.Get(), 320, 128, 96, CANONICAL_TEST_SEED, 0.5));
        Renderer rendererB;
        Assert::IsTrue(rendererB.Initialize(
            sharedDevices, windowB.Get(), 400, 200, 96, CANONICAL_TEST_SEED, 0.5));
        rendererA.RenderFrame(0.01, nullptr, 0);
        rendererB.RenderFrame(0.01, nullptr, 0);

        // Force only rendererB's rebuild to fail. RendererA rebuilds first,
        // so the coordinator must roll it back rather than expose a partial
        // success while rendererB remains broken.
        RendererTestAccess::ForceNextRecoveryToFail(rendererB);

        std::vector<Renderer*> renderers{&rendererA, &rendererB};
        Assert::IsFalse(RecoverSharedGraphicsDevices(
            sharedDevices, renderers, true));
        Assert::IsTrue(RendererTestAccess::HasNoGraphicsResources(rendererA));
        Assert::IsTrue(RendererTestAccess::HasNoGraphicsResources(rendererB));
        Assert::IsFalse(RendererTestAccess::IsReady(rendererA));
        Assert::IsFalse(RendererTestAccess::IsReady(rendererB));
        Assert::IsFalse(RendererTestAccess::IsRetryPending(rendererA));
        Assert::IsFalse(RendererTestAccess::IsRetryPending(rendererB));

        // The shared graph itself is healthy, but no dependent renderer is
        // allowed to resume until the next batch can rebuild all of them.
        Assert::IsTrue(sharedDevices.IsReady());

        RendererTestAccess::Cleanup(rendererA);
        RendererTestAccess::Cleanup(rendererB);
    }

    TEST_METHOD(IsolatedPerWindowRetryLeavesHealthyRendererAndSharedGraphUntouched) {
        ComApartment apartment;
        HiddenWindow windowA;
        if (!RequireRealGraphicsCapability(apartment, windowA)) return;
        HiddenWindow windowB;
        if (!windowB.Get()) {
            const CapabilityResult capability =
                CapabilityFailure("CreateWindowExW", windowB.Result());
            Logger::WriteMessage(CapabilityMessage(capability).c_str());
            return;
        }

        SharedGraphicsDevices sharedDevices;
        Assert::IsTrue(sharedDevices.Initialize());

        Renderer rendererA;
        Assert::IsTrue(rendererA.Initialize(
            sharedDevices, windowA.Get(), 320, 128, 96, CANONICAL_TEST_SEED, 0.5));
        Renderer rendererB;
        Assert::IsTrue(rendererB.Initialize(
            sharedDevices, windowB.Get(), 400, 200, 96, CANONICAL_TEST_SEED, 0.5));

        const HeldRendererResources healthyA =
            RendererTestAccess::HoldCoreResources(rendererA);
        ID3D11Device* const sharedDevice = sharedDevices.D3DDevice().Get();

        RendererTestAccess::ForceNextRecoveryToFail(rendererB);
        rendererB.DiscardPerWindowResources();
        const std::vector<Renderer*> renderers{&rendererA, &rendererB};
        Assert::IsFalse(RecoverSharedGraphicsDevices(
            sharedDevices, renderers, false));

        Assert::IsTrue(sharedDevices.D3DDevice().Get() == sharedDevice);
        Assert::IsTrue(RendererTestAccess::IsReady(rendererA));
        Assert::IsTrue(
            RendererTestAccess::PerWindowSwapChain(rendererA)
            == healthyA.swapChain.Get());
        Assert::IsTrue(RendererTestAccess::HasNoGraphicsResources(rendererB));
        Assert::IsFalse(RendererTestAccess::IsReady(rendererB));

        RendererTestAccess::Cleanup(rendererA);
        RendererTestAccess::Cleanup(rendererB);
    }
};

} // namespace DesktopGrassNativeTests
