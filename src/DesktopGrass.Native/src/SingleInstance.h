// SingleInstance.h
//
// Process-lifetime ownership for the supported standalone application.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <string>
#include <string_view>

namespace desktopgrass {

enum class SingleInstanceResult {
    Acquired,
    AlreadyRunning,
    Failed,
};

class SingleInstanceGuard final {
public:
    static constexpr const wchar_t* kStandaloneApplicationId =
        L"DesktopGrass.Standalone.{2E424867-2D28-44CF-950B-819F264E8019}";

    SingleInstanceGuard() = default;
    ~SingleInstanceGuard();

    SingleInstanceGuard(const SingleInstanceGuard&) = delete;
    SingleInstanceGuard& operator=(const SingleInstanceGuard&) = delete;

    SingleInstanceGuard(SingleInstanceGuard&& other) noexcept;
    SingleInstanceGuard& operator=(SingleInstanceGuard&& other) noexcept;

    SingleInstanceResult TryAcquire(
        std::wstring_view applicationId = kStandaloneApplicationId);
    bool IsAcquired() const noexcept { return mutex_ != nullptr; }

    static std::wstring BuildMutexNameForUser(
        std::wstring_view applicationId,
        std::wstring_view userSid);

private:
    HANDLE mutex_ = nullptr;
};

} // namespace desktopgrass
