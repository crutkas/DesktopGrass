#include "../third_party/catch2/catch.hpp"

#include "RuntimeNotifications.h"

#include <powersetting.h>
#include <wtsapi32.h>

#include <cstddef>
#include <cstdint>
#include <vector>

using namespace desktopgrass;

namespace {

struct FakeApiState {
    int failAt = 0;
    int nextRegistration = 0;
    DWORD sessionId = 42;
    runtime::SessionState queriedSessionState =
        runtime::SessionState::Active;
    std::vector<int> calls;
};

FakeApiState* fakeState = nullptr;

HPOWERNOTIFY FakeRegisterHandle() {
    const int id = ++fakeState->nextRegistration;
    fakeState->calls.push_back(id);
    if (fakeState->failAt == id) return nullptr;
    return reinterpret_cast<HPOWERNOTIFY>(
        static_cast<std::uintptr_t>(id));
}

HPOWERNOTIFY FakeRegisterPowerSetting(HWND, const GUID&) {
    return FakeRegisterHandle();
}

void FakeUnregisterPowerSetting(HPOWERNOTIFY notification) {
    const int id = static_cast<int>(
        reinterpret_cast<std::uintptr_t>(notification));
    fakeState->calls.push_back(-id);
}

HPOWERNOTIFY FakeRegisterSuspendResume(HWND) {
    return FakeRegisterHandle();
}

void FakeUnregisterSuspendResume(HPOWERNOTIFY notification) {
    FakeUnregisterPowerSetting(notification);
}

bool FakeRegisterSession(HWND) {
    const int id = ++fakeState->nextRegistration;
    fakeState->calls.push_back(id);
    return fakeState->failAt != id;
}

void FakeUnregisterSession(HWND) {
    fakeState->calls.push_back(-5);
}

void FakeSeedState(runtime::GlobalState& state, DWORD& sessionId) {
    state = {};
    state.powerSource = runtime::PowerSource::Ac;
    state.displayState = runtime::DisplayState::On;
    state.sessionState = runtime::SessionState::Active;
    sessionId = fakeState->sessionId;
}

runtime::SessionState FakeQuerySessionState(DWORD) {
    return fakeState->queriedSessionState;
}

RuntimeNotificationApi MakeFakeApi() {
    return RuntimeNotificationApi{
        FakeRegisterPowerSetting,
        FakeUnregisterPowerSetting,
        FakeRegisterSuspendResume,
        FakeUnregisterSuspendResume,
        FakeRegisterSession,
        FakeUnregisterSession,
        FakeSeedState,
        FakeQuerySessionState,
    };
}

struct PowerSettingPayload {
    GUID setting;
    DWORD dataLength;
    DWORD value;
};

static_assert(
    offsetof(PowerSettingPayload, value)
        == offsetof(POWERBROADCAST_SETTING, Data),
    "Test power payload must match POWERBROADCAST_SETTING");

PowerSettingPayload MakePowerSetting(const GUID& setting, DWORD value) {
    return PowerSettingPayload{setting, sizeof(value), value};
}

} // anonymous namespace

TEST_CASE("Runtime notification startup rolls back partial registrations",
          "[runtime][notifications]") {
    const HWND receiver =
        reinterpret_cast<HWND>(static_cast<std::uintptr_t>(0x1234));

    for (int failAt = 1; failAt <= 5; ++failAt) {
        CAPTURE(failAt);
        FakeApiState state;
        state.failAt = failAt;
        fakeState = &state;
        const RuntimeNotificationApi api = MakeFakeApi();

        {
            RuntimeNotifications notifications(api);
            REQUIRE_FALSE(notifications.Start(receiver));
            REQUIRE_FALSE(notifications.IsStarted());

            std::vector<int> expected;
            for (int id = 1; id <= failAt; ++id) {
                expected.push_back(id);
            }
            for (int id = failAt - 1; id >= 1; --id) {
                expected.push_back(-id);
            }
            REQUIRE(state.calls == expected);

            notifications.Stop();
            REQUIRE(state.calls == expected);
        }
    }
    fakeState = nullptr;
}

TEST_CASE("Runtime notification teardown is reverse-order and idempotent",
          "[runtime][notifications]") {
    FakeApiState state;
    fakeState = &state;
    const RuntimeNotificationApi api = MakeFakeApi();
    const HWND receiver =
        reinterpret_cast<HWND>(static_cast<std::uintptr_t>(0x1234));
    const HWND otherReceiver =
        reinterpret_cast<HWND>(static_cast<std::uintptr_t>(0x5678));

    {
        RuntimeNotifications notifications(api);
        REQUIRE(notifications.Start(receiver));
        REQUIRE(notifications.IsStarted());
        REQUIRE(notifications.Start(receiver));
        REQUIRE_FALSE(notifications.Start(otherReceiver));
        REQUIRE(state.calls == (std::vector<int>{1, 2, 3, 4, 5}));

        notifications.Stop();
        REQUIRE_FALSE(notifications.IsStarted());
        REQUIRE(state.calls
                == (std::vector<int>{
                    1, 2, 3, 4, 5, -5, -4, -3, -2, -1,
                }));

        notifications.Stop();
        REQUIRE(state.calls
                == (std::vector<int>{
                    1, 2, 3, 4, 5, -5, -4, -3, -2, -1,
                }));
    }
    fakeState = nullptr;
}

TEST_CASE("Runtime notification dispatch is receiver-neutral",
          "[runtime][notifications]") {
    FakeApiState state;
    fakeState = &state;
    const RuntimeNotificationApi api = MakeFakeApi();
    const HWND receiver =
        reinterpret_cast<HWND>(static_cast<std::uintptr_t>(0x1234));

    {
        RuntimeNotifications notifications(api);
        REQUIRE(notifications.Start(receiver));

        PowerSettingPayload battery =
            MakePowerSetting(GUID_ACDC_POWER_SOURCE, 1);
        const RuntimeNotificationResult powerResult = notifications.Dispatch(
            WM_POWERBROADCAST,
            PBT_POWERSETTINGCHANGE,
            reinterpret_cast<LPARAM>(&battery));
        REQUIRE(powerResult.handled);
        REQUIRE(powerResult.stateChanged);
        REQUIRE_FALSE(powerResult.visibilityChanged);
        REQUIRE(
            notifications.State().powerSource
            == runtime::PowerSource::Battery);

        const RuntimeNotificationResult otherSession =
            notifications.Dispatch(
                WM_WTSSESSION_CHANGE, WTS_SESSION_LOCK, 99);
        REQUIRE(otherSession.handled);
        REQUIRE_FALSE(otherSession.stateChanged);
        REQUIRE(
            notifications.State().sessionState
            == runtime::SessionState::Active);

        const RuntimeNotificationResult lockResult =
            notifications.Dispatch(
                WM_WTSSESSION_CHANGE, WTS_SESSION_LOCK, 42);
        REQUIRE(lockResult.handled);
        REQUIRE(lockResult.stateChanged);
        REQUIRE(
            notifications.State().sessionState
            == runtime::SessionState::Locked);

        REQUIRE(notifications.Dispatch(
                    WM_POWERBROADCAST, PBT_APMSUSPEND, 0)
                    .stateChanged);
        REQUIRE(notifications.State().suspended);

        const RuntimeNotificationResult resumeResult =
            notifications.Dispatch(
                WM_POWERBROADCAST, PBT_APMRESUMEAUTOMATIC, 0);
        REQUIRE(resumeResult.handled);
        REQUIRE(resumeResult.stateChanged);
        REQUIRE(resumeResult.visibilityChanged);
        REQUIRE_FALSE(notifications.State().suspended);

        REQUIRE_FALSE(notifications.Dispatch(WM_NULL, 0, 0).handled);
    }
    fakeState = nullptr;
}
