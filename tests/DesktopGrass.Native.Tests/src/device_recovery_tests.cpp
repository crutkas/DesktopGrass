// Device-independent tests for the production graphics recovery policy.

#include "TestHelpers.h"

#include "DeviceRecovery.h"
#include "Sim.h"

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

using namespace desktopgrass;

namespace {

struct LogEntry {
    std::string tag;
    HRESULT result;
};

struct BladeLayout {
    double baseX;
    double height;
    double thickness;
    uint8_t hue;
};

struct SimContinuitySnapshot {
    Sim* address;
    const Blade* bladeStorage;
    std::vector<BladeLayout> layout;
    Scene scene;
    CritterKind critter;
    int critterCount;
    double cutHeight;
};

Sim MakeContinuitySim() {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 960.0, DEFAULT_DENSITY);
    Assert::IsFalse(sim.blades.empty());
    sim.currentScene = Scene::Grass;
    sim.currentCritter = CritterKind::Cat;
    sim.critterCountOverride = 4;
    sim.blades.front().cutHeight = 0.42;
    sim.blades.front().cutAnimStart = -1.0;
    sim.blades.front().regrowStart = -1.0;
    return sim;
}

SimContinuitySnapshot CaptureContinuity(Sim& sim) {
    SimContinuitySnapshot snapshot{
        &sim,
        sim.blades.data(),
        {},
        sim.currentScene,
        sim.currentCritter,
        sim.critterCountOverride,
        sim.blades.front().cutHeight,
    };
    snapshot.layout.reserve(sim.blades.size());
    for (const Blade& blade : sim.blades) {
        snapshot.layout.push_back(BladeLayout{
            blade.baseX,
            blade.height,
            blade.thickness,
            blade.hue,
        });
    }
    return snapshot;
}

void RequireContinuity(
    const SimContinuitySnapshot& expected,
    Sim& actual)
{
    Assert::IsTrue(&actual == expected.address);
    Assert::IsTrue(actual.blades.data() == expected.bladeStorage);
    Assert::IsTrue(actual.blades.size() == expected.layout.size());
    Assert::IsTrue(actual.currentScene == expected.scene);
    Assert::IsTrue(actual.currentCritter == expected.critter);
    Assert::IsTrue(actual.critterCountOverride == expected.critterCount);
    Assert::IsTrue(actual.blades.front().cutHeight == Near(expected.cutHeight));

    for (std::size_t i = 0; i < expected.layout.size(); ++i) {
        Assert::IsTrue(actual.blades[i].baseX == expected.layout[i].baseX);
        Assert::IsTrue(actual.blades[i].height == expected.layout[i].height);
        Assert::IsTrue(actual.blades[i].thickness == expected.layout[i].thickness);
        Assert::IsTrue(actual.blades[i].hue == expected.layout[i].hue);
    }
}

auto MakeReporter(std::vector<LogEntry>& log) {
    return [&log](const char* tag, HRESULT result) {
        log.push_back(LogEntry{tag, result});
    };
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(DeviceRecoveryTests)
{
public:
TEST_METHOD(DeviceRecoveryControllerProcessesAHealthyFrameOnce) {
    DeviceRecoveryController controller;
    controller.StartReady();

    Sim sim = MakeContinuitySim();
    int drawCalls = 0;
    int recreateCalls = 0;
    std::vector<LogEntry> log;

    controller.ProcessFrame(
        10,
        [&sim]() { sim_tick(sim, 0.01, nullptr, 0); },
        [&drawCalls]() { ++drawCalls; },
        [&recreateCalls]() {
            ++recreateCalls;
            return DeviceRecoveryAttempt{true, 10};
        });

    Assert::IsTrue(sim.globalTime == Near(0.01));
    Assert::IsTrue(drawCalls == 1);
    Assert::IsTrue(recreateCalls == 0);
    Assert::IsTrue(log.empty());
    Assert::IsTrue(controller.IsRunning());
    Assert::IsTrue(controller.IsReady());
    Assert::IsFalse(controller.IsRetryPending());
    Assert::IsTrue(controller.NextRetryMs() == 0);
}

TEST_METHOD(RenderFailuresClassifyDeviceLossBeforeResourcesAreDiscarded) {
    int removalReasonCalls = 0;
    auto getRemovalReason = [&removalReasonCalls]() {
        ++removalReasonCalls;
        return DXGI_ERROR_DEVICE_HUNG;
    };

    {
        const auto loss = ClassifyDeviceLoss(
            RenderOperation::EndDraw,
            D2DERR_RECREATE_TARGET,
            getRemovalReason);

        Assert::IsTrue(loss.has_value());
        Assert::IsTrue(loss->operation == RenderOperation::EndDraw);
        Assert::IsTrue(loss->result == D2DERR_RECREATE_TARGET);
        Assert::IsTrue(loss->deviceRemovalReason == DXGI_ERROR_DEVICE_HUNG);
        Assert::IsTrue(removalReasonCalls == 1);
    }

    {
        removalReasonCalls = 0;
        const auto loss = ClassifyDeviceLoss(
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_REMOVED,
            getRemovalReason);

        Assert::IsTrue(loss.has_value());
        Assert::IsTrue(loss->operation == RenderOperation::Present1);
        Assert::IsTrue(loss->result == DXGI_ERROR_DEVICE_REMOVED);
        Assert::IsTrue(loss->deviceRemovalReason == DXGI_ERROR_DEVICE_HUNG);
        Assert::IsTrue(removalReasonCalls == 1);
    }

    {
        removalReasonCalls = 0;
        const auto loss = ClassifyDeviceLoss(
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_RESET,
            getRemovalReason);

        Assert::IsTrue(loss.has_value());
        Assert::IsTrue(loss->operation == RenderOperation::Present1);
        Assert::IsTrue(loss->result == DXGI_ERROR_DEVICE_RESET);
        Assert::IsTrue(loss->deviceRemovalReason == DXGI_ERROR_DEVICE_HUNG);
        Assert::IsTrue(removalReasonCalls == 1);
    }

    {
        removalReasonCalls = 0;
        const auto endDrawLoss = ClassifyDeviceLoss(
            RenderOperation::EndDraw,
            E_FAIL,
            getRemovalReason);
        const auto presentLoss = ClassifyDeviceLoss(
            RenderOperation::Present1,
            DXGI_ERROR_INVALID_CALL,
            getRemovalReason);

        Assert::IsFalse(endDrawLoss.has_value());
        Assert::IsFalse(presentLoss.has_value());
        Assert::IsTrue(removalReasonCalls == 0);
    }
}

TEST_METHOD(D2DLossRestoresImmediatelyWithoutResettingSimulation) {
    DeviceRecoveryController controller;
    controller.StartReady();

    Sim sim = MakeContinuitySim();
    const SimContinuitySnapshot before = CaptureContinuity(sim);
    int drawCalls = 0;
    int recreateCalls = 0;
    std::vector<LogEntry> log;
    auto recreate = [&recreateCalls]() {
        ++recreateCalls;
        return DeviceRecoveryAttempt{true, 100};
    };
    auto reporter = MakeReporter(log);

    controller.ProcessFrame(
        100,
        [&sim]() { sim_tick(sim, 0.02, nullptr, 0); },
        [&controller, &drawCalls, &recreate, &reporter]() {
            ++drawCalls;
            controller.HandleDeviceLoss(
                DeviceLossInfo{
                    RenderOperation::EndDraw,
                    D2DERR_RECREATE_TARGET,
                    S_OK,
                },
                recreate,
                reporter);
        },
        recreate);

    Assert::IsTrue(sim.globalTime == Near(0.02));
    Assert::IsTrue(drawCalls == 1);
    Assert::IsTrue(recreateCalls == 1);
    Assert::IsTrue(controller.IsReady());
    Assert::IsFalse(controller.IsRetryPending());
    Assert::IsTrue(controller.NextRetryMs() == 0);
    Assert::IsTrue(log.size() == 1);
    Assert::IsTrue(log[0].tag == "EndDraw");
    Assert::IsTrue(log[0].result == D2DERR_RECREATE_TARGET);
    RequireContinuity(before, sim);
}

TEST_METHOD(DXGILossReportsItsRemovalReasonBeforeImmediateRecovery) {
    for (const HRESULT presentResult :
         {DXGI_ERROR_DEVICE_REMOVED, DXGI_ERROR_DEVICE_RESET}) {
        DeviceRecoveryController controller;
        controller.StartReady();
        std::vector<LogEntry> log;
        auto recreate = []() {
            return DeviceRecoveryAttempt{true, 200};
        };
        auto reporter = MakeReporter(log);

        controller.ProcessFrame(
            200,
            []() {},
            [&controller, presentResult, &recreate, &reporter]() {
                controller.HandleDeviceLoss(
                    DeviceLossInfo{
                        RenderOperation::Present1,
                        presentResult,
                        DXGI_ERROR_DEVICE_HUNG,
                    },
                    recreate,
                    reporter);
            },
            recreate);

        Assert::IsTrue(log.size() == 2);
        Assert::IsTrue(log[0].tag == "Present1");
        Assert::IsTrue(log[0].result == presentResult);
        Assert::IsTrue(log[1].tag == "ID3D11Device::GetDeviceRemovedReason");
        Assert::IsTrue(log[1].result == DXGI_ERROR_DEVICE_HUNG);
        Assert::IsTrue(controller.IsReady());
    }
}

TEST_METHOD(SuccessfulDeviceRemovalReasonIsNotLogged) {
    DeviceRecoveryController controller;
    controller.StartReady();
    std::vector<LogEntry> log;

    controller.HandleDeviceLoss(
        DeviceLossInfo{
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_REMOVED,
            S_OK,
        },
        []() { return DeviceRecoveryAttempt{true, 250}; },
        MakeReporter(log));

    Assert::IsTrue(log.size() == 1);
    Assert::IsTrue(log[0].tag == "Present1");
    Assert::IsTrue(log[0].result == DXGI_ERROR_DEVICE_REMOVED);
}

TEST_METHOD(FailedRecreationRetriesOncePerOneSecondDeadline) {
    enum class RecoveryStage {
        D3dCreate,
        DcompCommit,
        Success,
    };
    struct ScriptedAttempt {
        RecoveryStage stage;
        DeviceRecoveryAttempt result;
    };

    DeviceRecoveryController controller;
    controller.StartReady();
    Sim sim = MakeContinuitySim();
    const SimContinuitySnapshot before = CaptureContinuity(sim);
    std::vector<ScriptedAttempt> attempts{
        {RecoveryStage::D3dCreate, {false, 5100}},
        {RecoveryStage::DcompCommit, {false, 6250}},
        {RecoveryStage::Success, {true, 7255}},
    };
    std::size_t nextAttempt = 0;
    std::vector<RecoveryStage> observedStages;
    int drawCalls = 0;
    std::vector<LogEntry> log;

    auto tick = [&sim]() { sim_tick(sim, 0.01, nullptr, 0); };
    auto recreate = [&attempts, &nextAttempt, &observedStages]() {
        Assert::IsTrue(nextAttempt < attempts.size());
        const ScriptedAttempt& attempt = attempts[nextAttempt++];
        observedStages.push_back(attempt.stage);
        return attempt.result;
    };
    auto reporter = MakeReporter(log);
    auto draw = [&controller, &drawCalls, &recreate, &reporter]() {
        ++drawCalls;
        if (drawCalls == 1) {
            controller.HandleDeviceLoss(
                DeviceLossInfo{
                    RenderOperation::Present1,
                    DXGI_ERROR_DEVICE_REMOVED,
                    DXGI_ERROR_DEVICE_HUNG,
                },
                recreate,
                reporter);
        }
    };

    controller.ProcessFrame(5000, tick, draw, recreate);
    Assert::IsTrue(nextAttempt == 1);
    Assert::IsTrue(drawCalls == 1);
    Assert::IsFalse(controller.IsReady());
    Assert::IsTrue(controller.IsRetryPending());
    Assert::IsTrue(controller.NextRetryMs() == 6100);

    controller.ProcessFrame(6099, tick, draw, recreate);
    Assert::IsTrue(nextAttempt == 1);
    Assert::IsTrue(drawCalls == 1);
    Assert::IsTrue(controller.NextRetryMs() == 6100);

    controller.ProcessFrame(6100, tick, draw, recreate);
    Assert::IsTrue(nextAttempt == 2);
    Assert::IsTrue(drawCalls == 1);
    Assert::IsFalse(controller.IsReady());
    Assert::IsTrue(controller.NextRetryMs() == 7250);

    controller.ProcessFrame(7249, tick, draw, recreate);
    Assert::IsTrue(nextAttempt == 2);
    Assert::IsTrue(drawCalls == 1);
    Assert::IsTrue(controller.NextRetryMs() == 7250);

    controller.ProcessFrame(7250, tick, draw, recreate);
    Assert::IsTrue(nextAttempt == 3);
    Assert::IsTrue(drawCalls == 1);
    Assert::IsTrue(controller.IsReady());
    Assert::IsFalse(controller.IsRetryPending());
    Assert::IsTrue(controller.NextRetryMs() == 0);

    controller.ProcessFrame(7256, tick, draw, recreate);
    Assert::IsTrue(nextAttempt == 3);
    Assert::IsTrue(drawCalls == 2);
    Assert::IsTrue(sim.globalTime == Near(0.06));
    Assert::IsTrue(log.size() == 2);
    const std::vector<RecoveryStage> expectedStages{
        RecoveryStage::D3dCreate,
        RecoveryStage::DcompCommit,
        RecoveryStage::Success,
    };
    Assert::IsTrue(observedStages == expectedStages);
    RequireContinuity(before, sim);
}

TEST_METHOD(StoppingRecoveryCancelsRetriesAndLaterCallbacks) {
    DeviceRecoveryController controller;
    controller.StartReady();
    int tickCalls = 0;
    int drawCalls = 0;
    int recreateCalls = 0;
    int reportCalls = 0;

    controller.HandleDeviceLoss(
        DeviceLossInfo{
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_RESET,
            DXGI_ERROR_DEVICE_HUNG,
        },
        [&recreateCalls]() {
            ++recreateCalls;
            return DeviceRecoveryAttempt{false, 8050};
        },
        [&reportCalls](const char*, HRESULT) { ++reportCalls; });

    Assert::IsTrue(recreateCalls == 1);
    Assert::IsTrue(reportCalls == 2);
    Assert::IsTrue(controller.IsRetryPending());
    Assert::IsTrue(controller.NextRetryMs() == 9050);

    controller.Stop();
    controller.ProcessFrame(
        10000,
        [&tickCalls]() { ++tickCalls; },
        [&drawCalls]() { ++drawCalls; },
        [&recreateCalls]() {
            ++recreateCalls;
            return DeviceRecoveryAttempt{true, 10000};
        });
    controller.HandleDeviceLoss(
        DeviceLossInfo{
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_REMOVED,
            DXGI_ERROR_DEVICE_HUNG,
        },
        [&recreateCalls]() {
            ++recreateCalls;
            return DeviceRecoveryAttempt{true, 10000};
        },
        [&reportCalls](const char*, HRESULT) { ++reportCalls; });

    Assert::IsFalse(controller.IsRunning());
    Assert::IsFalse(controller.IsReady());
    Assert::IsFalse(controller.IsRetryPending());
    Assert::IsTrue(controller.NextRetryMs() == 0);
    Assert::IsTrue(tickCalls == 0);
    Assert::IsTrue(drawCalls == 0);
    Assert::IsTrue(recreateCalls == 1);
    Assert::IsTrue(reportCalls == 2);
}
};
}
