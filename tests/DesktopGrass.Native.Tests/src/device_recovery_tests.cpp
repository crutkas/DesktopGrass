// Device-independent tests for the production graphics recovery policy.

#include "../third_party/catch2/catch.hpp"

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
    REQUIRE_FALSE(sim.blades.empty());
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
    REQUIRE(&actual == expected.address);
    REQUIRE(actual.blades.data() == expected.bladeStorage);
    REQUIRE(actual.blades.size() == expected.layout.size());
    REQUIRE(actual.currentScene == expected.scene);
    REQUIRE(actual.currentCritter == expected.critter);
    REQUIRE(actual.critterCountOverride == expected.critterCount);
    REQUIRE(actual.blades.front().cutHeight == Approx(expected.cutHeight));

    for (std::size_t i = 0; i < expected.layout.size(); ++i) {
        REQUIRE(actual.blades[i].baseX == expected.layout[i].baseX);
        REQUIRE(actual.blades[i].height == expected.layout[i].height);
        REQUIRE(actual.blades[i].thickness == expected.layout[i].thickness);
        REQUIRE(actual.blades[i].hue == expected.layout[i].hue);
    }
}

auto MakeReporter(std::vector<LogEntry>& log) {
    return [&log](const char* tag, HRESULT result) {
        log.push_back(LogEntry{tag, result});
    };
}

} // namespace

TEST_CASE("Device recovery controller processes a healthy frame once",
          "[device-recovery][unit]") {
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

    REQUIRE(sim.globalTime == Approx(0.01));
    REQUIRE(drawCalls == 1);
    REQUIRE(recreateCalls == 0);
    REQUIRE(log.empty());
    REQUIRE(controller.IsRunning());
    REQUIRE(controller.IsReady());
    REQUIRE_FALSE(controller.IsRetryPending());
    REQUIRE(controller.NextRetryMs() == 0);
}

TEST_CASE("Render failures classify device loss before resources are discarded",
          "[device-recovery][unit][classification]") {
    int removalReasonCalls = 0;
    auto getRemovalReason = [&removalReasonCalls]() {
        ++removalReasonCalls;
        return DXGI_ERROR_DEVICE_HUNG;
    };

    SECTION("D2D recreate target") {
        const auto loss = ClassifyDeviceLoss(
            RenderOperation::EndDraw,
            D2DERR_RECREATE_TARGET,
            getRemovalReason);

        REQUIRE(loss.has_value());
        REQUIRE(loss->operation == RenderOperation::EndDraw);
        REQUIRE(loss->result == D2DERR_RECREATE_TARGET);
        REQUIRE(loss->deviceRemovalReason == DXGI_ERROR_DEVICE_HUNG);
        REQUIRE(removalReasonCalls == 1);
    }

    SECTION("DXGI device removed") {
        const auto loss = ClassifyDeviceLoss(
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_REMOVED,
            getRemovalReason);

        REQUIRE(loss.has_value());
        REQUIRE(loss->operation == RenderOperation::Present1);
        REQUIRE(loss->result == DXGI_ERROR_DEVICE_REMOVED);
        REQUIRE(loss->deviceRemovalReason == DXGI_ERROR_DEVICE_HUNG);
        REQUIRE(removalReasonCalls == 1);
    }

    SECTION("DXGI device reset") {
        const auto loss = ClassifyDeviceLoss(
            RenderOperation::Present1,
            DXGI_ERROR_DEVICE_RESET,
            getRemovalReason);

        REQUIRE(loss.has_value());
        REQUIRE(loss->operation == RenderOperation::Present1);
        REQUIRE(loss->result == DXGI_ERROR_DEVICE_RESET);
        REQUIRE(loss->deviceRemovalReason == DXGI_ERROR_DEVICE_HUNG);
        REQUIRE(removalReasonCalls == 1);
    }

    SECTION("unrelated failures are not device loss") {
        const auto endDrawLoss = ClassifyDeviceLoss(
            RenderOperation::EndDraw,
            E_FAIL,
            getRemovalReason);
        const auto presentLoss = ClassifyDeviceLoss(
            RenderOperation::Present1,
            DXGI_ERROR_INVALID_CALL,
            getRemovalReason);

        REQUIRE_FALSE(endDrawLoss.has_value());
        REQUIRE_FALSE(presentLoss.has_value());
        REQUIRE(removalReasonCalls == 0);
    }
}

TEST_CASE("D2D loss restores immediately without resetting simulation",
          "[device-recovery][unit]") {
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

    REQUIRE(sim.globalTime == Approx(0.02));
    REQUIRE(drawCalls == 1);
    REQUIRE(recreateCalls == 1);
    REQUIRE(controller.IsReady());
    REQUIRE_FALSE(controller.IsRetryPending());
    REQUIRE(controller.NextRetryMs() == 0);
    REQUIRE(log.size() == 1);
    REQUIRE(log[0].tag == "EndDraw");
    REQUIRE(log[0].result == D2DERR_RECREATE_TARGET);
    RequireContinuity(before, sim);
}

TEST_CASE("DXGI loss reports its removal reason before immediate recovery",
          "[device-recovery][unit][logging]") {
    HRESULT presentResult = DXGI_ERROR_DEVICE_REMOVED;

    SECTION("removed") {
        presentResult = DXGI_ERROR_DEVICE_REMOVED;
    }
    SECTION("reset") {
        presentResult = DXGI_ERROR_DEVICE_RESET;
    }

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

    REQUIRE(log.size() == 2);
    REQUIRE(log[0].tag == "Present1");
    REQUIRE(log[0].result == presentResult);
    REQUIRE(log[1].tag == "ID3D11Device::GetDeviceRemovedReason");
    REQUIRE(log[1].result == DXGI_ERROR_DEVICE_HUNG);
    REQUIRE(controller.IsReady());
}

TEST_CASE("Successful device-removal reason is not logged",
          "[device-recovery][unit][logging]") {
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

    REQUIRE(log.size() == 1);
    REQUIRE(log[0].tag == "Present1");
    REQUIRE(log[0].result == DXGI_ERROR_DEVICE_REMOVED);
}

TEST_CASE("Failed recreation retries once per one-second deadline",
          "[device-recovery][unit][timing]") {
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
        REQUIRE(nextAttempt < attempts.size());
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
    REQUIRE(nextAttempt == 1);
    REQUIRE(drawCalls == 1);
    REQUIRE_FALSE(controller.IsReady());
    REQUIRE(controller.IsRetryPending());
    REQUIRE(controller.NextRetryMs() == 6100);

    controller.ProcessFrame(6099, tick, draw, recreate);
    REQUIRE(nextAttempt == 1);
    REQUIRE(drawCalls == 1);
    REQUIRE(controller.NextRetryMs() == 6100);

    controller.ProcessFrame(6100, tick, draw, recreate);
    REQUIRE(nextAttempt == 2);
    REQUIRE(drawCalls == 1);
    REQUIRE_FALSE(controller.IsReady());
    REQUIRE(controller.NextRetryMs() == 7250);

    controller.ProcessFrame(7249, tick, draw, recreate);
    REQUIRE(nextAttempt == 2);
    REQUIRE(drawCalls == 1);
    REQUIRE(controller.NextRetryMs() == 7250);

    controller.ProcessFrame(7250, tick, draw, recreate);
    REQUIRE(nextAttempt == 3);
    REQUIRE(drawCalls == 1);
    REQUIRE(controller.IsReady());
    REQUIRE_FALSE(controller.IsRetryPending());
    REQUIRE(controller.NextRetryMs() == 0);

    controller.ProcessFrame(7256, tick, draw, recreate);
    REQUIRE(nextAttempt == 3);
    REQUIRE(drawCalls == 2);
    REQUIRE(sim.globalTime == Approx(0.06));
    REQUIRE(log.size() == 2);
    const std::vector<RecoveryStage> expectedStages{
        RecoveryStage::D3dCreate,
        RecoveryStage::DcompCommit,
        RecoveryStage::Success,
    };
    REQUIRE(observedStages == expectedStages);
    RequireContinuity(before, sim);
}

TEST_CASE("Stopping recovery cancels retries and later callbacks",
          "[device-recovery][unit][lifecycle]") {
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

    REQUIRE(recreateCalls == 1);
    REQUIRE(reportCalls == 2);
    REQUIRE(controller.IsRetryPending());
    REQUIRE(controller.NextRetryMs() == 9050);

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

    REQUIRE_FALSE(controller.IsRunning());
    REQUIRE_FALSE(controller.IsReady());
    REQUIRE_FALSE(controller.IsRetryPending());
    REQUIRE(controller.NextRetryMs() == 0);
    REQUIRE(tickCalls == 0);
    REQUIRE(drawCalls == 0);
    REQUIRE(recreateCalls == 1);
    REQUIRE(reportCalls == 2);
}
