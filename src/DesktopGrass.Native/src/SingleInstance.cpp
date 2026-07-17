// SingleInstance.cpp

#include "SingleInstance.h"

#include <sddl.h>

#include <utility>
#include <vector>

#pragma comment(lib, "Advapi32.lib")

namespace desktopgrass {

namespace {

bool TryGetCurrentUserSid(std::wstring& userSid) {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
        return false;
    }

    DWORD byteCount = 0;
    GetTokenInformation(token, TokenUser, nullptr, 0, &byteCount);
    if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || byteCount == 0) {
        CloseHandle(token);
        return false;
    }

    std::vector<BYTE> buffer(byteCount);
    if (!GetTokenInformation(
            token, TokenUser, buffer.data(), byteCount, &byteCount)) {
        CloseHandle(token);
        return false;
    }
    CloseHandle(token);

    const auto tokenUser =
        reinterpret_cast<const TOKEN_USER*>(buffer.data());
    LPWSTR sidText = nullptr;
    if (!ConvertSidToStringSidW(tokenUser->User.Sid, &sidText)) {
        return false;
    }

    userSid.assign(sidText);
    LocalFree(sidText);
    return true;
}

} // anonymous namespace

SingleInstanceGuard::~SingleInstanceGuard() {
    if (mutex_) {
        CloseHandle(mutex_);
    }
}

SingleInstanceGuard::SingleInstanceGuard(
    SingleInstanceGuard&& other) noexcept
    : mutex_(std::exchange(other.mutex_, nullptr)) {
}

SingleInstanceGuard& SingleInstanceGuard::operator=(
    SingleInstanceGuard&& other) noexcept {
    if (this != &other) {
        if (mutex_) {
            CloseHandle(mutex_);
        }
        mutex_ = std::exchange(other.mutex_, nullptr);
    }
    return *this;
}

std::wstring SingleInstanceGuard::BuildMutexNameForUser(
    std::wstring_view applicationId,
    std::wstring_view userSid) {
    // Global spans logon sessions; the SID keeps ownership scoped to one user.
    std::wstring name = L"Global\\";
    name.append(applicationId);
    name.append(L".SingleInstance.");
    name.append(userSid);
    return name;
}

SingleInstanceResult SingleInstanceGuard::TryAcquire(
    std::wstring_view applicationId) {
    if (mutex_ || applicationId.empty()) {
        return mutex_ ? SingleInstanceResult::Acquired
                      : SingleInstanceResult::Failed;
    }

    std::wstring userSid;
    if (!TryGetCurrentUserSid(userSid)) {
        return SingleInstanceResult::Failed;
    }

    const std::wstring mutexName =
        BuildMutexNameForUser(applicationId, userSid);
    SetLastError(ERROR_SUCCESS);
    HANDLE mutex = CreateMutexW(nullptr, FALSE, mutexName.c_str());
    const DWORD error = GetLastError();
    if (!mutex) {
        return SingleInstanceResult::Failed;
    }
    if (error == ERROR_ALREADY_EXISTS) {
        CloseHandle(mutex);
        return SingleInstanceResult::AlreadyRunning;
    }

    mutex_ = mutex;
    return SingleInstanceResult::Acquired;
}

} // namespace desktopgrass
