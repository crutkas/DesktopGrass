// RuntimeNotifications.cpp

#include "RuntimeNotifications.h"

#include <powersetting.h>
#include <wtsapi32.h>

#include <cstring>

#pragma comment(lib, "User32.lib")
#pragma comment(lib, "Wtsapi32.lib")

namespace desktopgrass {

namespace {

constexpr DWORD kUnknownSessionId = 0xFFFFFFFFu;
constexpr DWORD kTermSrvReadyTimeoutMs = 10'000;

runtime::SessionState QuerySystemSessionState(DWORD sessionId) {
    LPWSTR buffer = nullptr;
    DWORD bytesReturned = 0;
    const DWORD querySession = sessionId == kUnknownSessionId
        ? WTS_CURRENT_SESSION
        : sessionId;

    if (!WTSQuerySessionInformationW(
            WTS_CURRENT_SERVER_HANDLE,
            querySession,
            WTSSessionInfoEx,
            &buffer,
            &bytesReturned)) {
        OutputDebugStringA(
            "[DesktopGrass] WTSQuerySessionInformation failed; session state is unknown\n");
        return runtime::SessionState::Unknown;
    }

    runtime::SessionState result = runtime::SessionState::Unknown;
    if (buffer && bytesReturned >= sizeof(WTSINFOEXW)) {
        const auto* info = reinterpret_cast<const WTSINFOEXW*>(buffer);
        if (info->Level == 1) {
            const WTSINFOEX_LEVEL1_W& level = info->Data.WTSInfoExLevel1;
            if (level.SessionState == WTSDisconnected) {
                result = runtime::SessionState::Disconnected;
            } else if (level.SessionFlags == WTS_SESSIONSTATE_LOCK) {
                result = runtime::SessionState::Locked;
            } else if (level.SessionFlags == WTS_SESSIONSTATE_UNLOCK
                       || level.SessionState == WTSActive) {
                result = runtime::SessionState::Active;
            }
        }
    }
    if (buffer) WTSFreeMemory(buffer);
    return result;
}

void SeedSystemState(runtime::GlobalState& state, DWORD& sessionId) {
    state = {};
    sessionId = kUnknownSessionId;

    SYSTEM_POWER_STATUS power{};
    if (GetSystemPowerStatus(&power)) {
        switch (power.ACLineStatus) {
            case 0:
                state.powerSource = runtime::PowerSource::Battery;
                break;
            case 1:
                state.powerSource = runtime::PowerSource::Ac;
                break;
            default:
                state.powerSource = runtime::PowerSource::Unknown;
                break;
        }
        state.saverEnabled = power.SystemStatusFlag != 0;
    } else {
        OutputDebugStringA(
            "[DesktopGrass] GetSystemPowerStatus failed; power source is unknown\n");
    }

    if (!ProcessIdToSessionId(GetCurrentProcessId(), &sessionId)) {
        sessionId = kUnknownSessionId;
        OutputDebugStringA(
            "[DesktopGrass] ProcessIdToSessionId failed; session state is unknown\n");
    }
    state.sessionState = QuerySystemSessionState(sessionId);
}

HPOWERNOTIFY RegisterSystemPowerSetting(
    HWND receiver, const GUID& setting)
{
    return RegisterPowerSettingNotification(
        receiver, &setting, DEVICE_NOTIFY_WINDOW_HANDLE);
}

void UnregisterSystemPowerSetting(HPOWERNOTIFY notification) {
    UnregisterPowerSettingNotification(notification);
}

HPOWERNOTIFY RegisterSystemSuspendResume(HWND receiver) {
    return RegisterSuspendResumeNotification(
        receiver, DEVICE_NOTIFY_WINDOW_HANDLE);
}

void UnregisterSystemSuspendResume(HPOWERNOTIFY notification) {
    UnregisterSuspendResumeNotification(notification);
}

bool RegisterSystemSession(HWND receiver) {
    if (WTSRegisterSessionNotification(
            receiver, NOTIFY_FOR_THIS_SESSION)) {
        return true;
    }
    if (GetLastError() != RPC_S_INVALID_BINDING) {
        return false;
    }

    HANDLE readyEvent = OpenEventW(
        SYNCHRONIZE, FALSE, L"Global\\TermSrvReadyEvent");
    if (!readyEvent) return false;

    const DWORD waitResult =
        WaitForSingleObject(readyEvent, kTermSrvReadyTimeoutMs);
    CloseHandle(readyEvent);
    if (waitResult != WAIT_OBJECT_0) {
        SetLastError(
            waitResult == WAIT_TIMEOUT ? ERROR_TIMEOUT : ERROR_GEN_FAILURE);
        return false;
    }

    return WTSRegisterSessionNotification(
               receiver, NOTIFY_FOR_THIS_SESSION) != FALSE;
}

void UnregisterSystemSession(HWND receiver) {
    WTSUnRegisterSessionNotification(receiver);
}

} // anonymous namespace

const RuntimeNotificationApi& GetSystemRuntimeNotificationApi() noexcept {
    static const RuntimeNotificationApi api{
        RegisterSystemPowerSetting,
        UnregisterSystemPowerSetting,
        RegisterSystemSuspendResume,
        UnregisterSystemSuspendResume,
        RegisterSystemSession,
        UnregisterSystemSession,
        SeedSystemState,
        QuerySystemSessionState,
    };
    return api;
}

RuntimeNotifications::RuntimeNotifications() noexcept
    : RuntimeNotifications(GetSystemRuntimeNotificationApi())
{
}

RuntimeNotifications::RuntimeNotifications(
    const RuntimeNotificationApi& api) noexcept
    : api_(api)
{
}

RuntimeNotifications::~RuntimeNotifications() {
    Stop();
}

bool RuntimeNotifications::Start(HWND receiver) noexcept {
    if (!receiver) return false;
    if (started_) return receiver_ == receiver;

    receiver_ = receiver;
    api_.seedState(state_, sessionId_);

    acdcPowerNotification_ =
        api_.registerPowerSetting(receiver_, GUID_ACDC_POWER_SOURCE);
    if (!acdcPowerNotification_) {
        OutputDebugStringA(
            "[DesktopGrass] RegisterPowerSettingNotification(AC/DC) failed\n");
        Stop();
        return false;
    }

    saverNotification_ =
        api_.registerPowerSetting(receiver_, GUID_POWER_SAVING_STATUS);
    if (!saverNotification_) {
        OutputDebugStringA(
            "[DesktopGrass] RegisterPowerSettingNotification(Battery Saver) failed\n");
        Stop();
        return false;
    }

    displayNotification_ =
        api_.registerPowerSetting(receiver_, GUID_SESSION_DISPLAY_STATUS);
    if (!displayNotification_) {
        OutputDebugStringA(
            "[DesktopGrass] RegisterPowerSettingNotification(display) failed\n");
        Stop();
        return false;
    }

    suspendResumeNotification_ = api_.registerSuspendResume(receiver_);
    if (!suspendResumeNotification_) {
        OutputDebugStringA(
            "[DesktopGrass] RegisterSuspendResumeNotification failed\n");
        Stop();
        return false;
    }

    if (!api_.registerSession(receiver_)) {
        OutputDebugStringA(
            "[DesktopGrass] WTSRegisterSessionNotification failed\n");
        Stop();
        return false;
    }
    wtsNotificationRegistered_ = true;

    // Reconcile events that raced the initial query and registration.
    state_.sessionState = api_.querySessionState(sessionId_);
    started_ = true;
    return true;
}

void RuntimeNotifications::Stop() noexcept {
    started_ = false;

    if (wtsNotificationRegistered_) {
        api_.unregisterSession(receiver_);
        wtsNotificationRegistered_ = false;
    }
    if (suspendResumeNotification_) {
        api_.unregisterSuspendResume(suspendResumeNotification_);
        suspendResumeNotification_ = nullptr;
    }
    if (displayNotification_) {
        api_.unregisterPowerSetting(displayNotification_);
        displayNotification_ = nullptr;
    }
    if (saverNotification_) {
        api_.unregisterPowerSetting(saverNotification_);
        saverNotification_ = nullptr;
    }
    if (acdcPowerNotification_) {
        api_.unregisterPowerSetting(acdcPowerNotification_);
        acdcPowerNotification_ = nullptr;
    }

    receiver_ = nullptr;
}

RuntimeNotificationResult RuntimeNotifications::Dispatch(
    UINT message, WPARAM wParam, LPARAM lParam) noexcept
{
    if (!started_) return {};

    switch (message) {
        case WM_POWERBROADCAST:
            return HandlePowerBroadcast(wParam, lParam);
        case WM_WTSSESSION_CHANGE:
            return HandleSessionChange(wParam, lParam);
        default:
            return {};
    }
}

RuntimeNotificationResult RuntimeNotifications::HandlePowerBroadcast(
    WPARAM event, LPARAM data) noexcept
{
    RuntimeNotificationResult result;
    result.result = TRUE;

    if (event == PBT_APMSUSPEND) {
        result.handled = true;
        result.stateChanged = !state_.suspended;
        state_.suspended = true;
        return result;
    }

    if (event == PBT_APMRESUMEAUTOMATIC
        || event == PBT_APMRESUMESUSPEND) {
        result.handled = true;
        result.stateChanged = state_.suspended;
        result.visibilityChanged = true;
        state_.suspended = false;
        return result;
    }

    if (event != PBT_POWERSETTINGCHANGE) {
        return {};
    }
    result.handled = true;

    const auto* setting =
        reinterpret_cast<const POWERBROADCAST_SETTING*>(data);
    if (!setting || setting->DataLength < sizeof(DWORD)) {
        OutputDebugStringA(
            "[DesktopGrass] Ignoring malformed power-setting notification\n");
        return result;
    }

    DWORD value = 0;
    std::memcpy(&value, setting->Data, sizeof(value));
    if (IsEqualGUID(setting->PowerSetting, GUID_ACDC_POWER_SOURCE)) {
        runtime::PowerSource next = runtime::PowerSource::Unknown;
        switch (value) {
            case 0:
                next = runtime::PowerSource::Ac;
                break;
            case 1:
                next = runtime::PowerSource::Battery;
                break;
            case 2:
                next = runtime::PowerSource::ShortTerm;
                break;
        }
        result.stateChanged = state_.powerSource != next;
        state_.powerSource = next;
    } else if (IsEqualGUID(
                   setting->PowerSetting,
                   GUID_POWER_SAVING_STATUS)) {
        const bool next = value != 0;
        result.stateChanged = state_.saverEnabled != next;
        state_.saverEnabled = next;
    } else if (IsEqualGUID(
                   setting->PowerSetting,
                   GUID_SESSION_DISPLAY_STATUS)) {
        runtime::DisplayState next = runtime::DisplayState::Unknown;
        switch (value) {
            case 0:
                next = runtime::DisplayState::Off;
                break;
            case 1:
                next = runtime::DisplayState::On;
                break;
            case 2:
                next = runtime::DisplayState::Dimmed;
                break;
        }
        result.stateChanged = state_.displayState != next;
        state_.displayState = next;
    }

    return result;
}

RuntimeNotificationResult RuntimeNotifications::HandleSessionChange(
    WPARAM event, LPARAM sessionId) noexcept
{
    RuntimeNotificationResult result;
    result.handled = true;

    if (sessionId_ != kUnknownSessionId
        && static_cast<DWORD>(sessionId) != sessionId_) {
        return result;
    }

    runtime::SessionState next = state_.sessionState;
    switch (event) {
        case WTS_SESSION_LOCK:
            next = runtime::SessionState::Locked;
            break;
        case WTS_CONSOLE_DISCONNECT:
        case WTS_REMOTE_DISCONNECT:
        case WTS_SESSION_LOGOFF:
            next = runtime::SessionState::Disconnected;
            break;
        case WTS_SESSION_UNLOCK:
            next = runtime::SessionState::Active;
            result.visibilityChanged = true;
            break;
        case WTS_CONSOLE_CONNECT:
        case WTS_REMOTE_CONNECT:
        case WTS_SESSION_LOGON:
            next = api_.querySessionState(sessionId_);
            result.visibilityChanged = true;
            break;
        default:
            return result;
    }

    result.stateChanged = state_.sessionState != next;
    state_.sessionState = next;
    return result;
}

} // namespace desktopgrass
