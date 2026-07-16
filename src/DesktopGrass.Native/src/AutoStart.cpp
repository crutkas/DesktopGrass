#include "AutoStart.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <vector>

#pragma comment(lib, "Advapi32.lib")

namespace autostart {
namespace {

constexpr const wchar_t* kDefaultRunSubKey = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
std::wstring g_registryKeyOverride;

std::wstring GetRegistrySubKey() {
    return g_registryKeyOverride.empty() ? std::wstring(kDefaultRunSubKey) : g_registryKeyOverride;
}

} // namespace

std::wstring GetRegistryValueName() {
    return L"DesktopGrass.Native";
}

std::wstring GetCurrentExePath() {
    std::vector<wchar_t> buffer(MAX_PATH);
    while (true) {
        const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0) {
            return L"";
        }
        if (length < buffer.size()) {
            return std::wstring(buffer.data(), length);
        }
        buffer.resize(buffer.size() * 2);
    }
}

std::wstring GetRunCommand() {
    const std::wstring path = GetCurrentExePath();
    return path.empty() ? std::wstring{} : L"\"" + path + L"\"";
}

bool IsEnabled() {
    HKEY key = nullptr;
    const std::wstring subKey = GetRegistrySubKey();
    const LSTATUS openStatus = RegOpenKeyExW(
        HKEY_CURRENT_USER, subKey.c_str(), 0, KEY_QUERY_VALUE, &key);
    if (openStatus != ERROR_SUCCESS) {
        return false;
    }

    DWORD type = 0;
    DWORD byteCount = 0;
    const std::wstring valueName = GetRegistryValueName();
    LSTATUS queryStatus = RegQueryValueExW(
        key, valueName.c_str(), nullptr, &type, nullptr, &byteCount);
    if (queryStatus != ERROR_SUCCESS || (type != REG_SZ && type != REG_EXPAND_SZ)) {
        RegCloseKey(key);
        return false;
    }

    std::vector<wchar_t> buffer(byteCount / sizeof(wchar_t) + 1, L'\0');
    queryStatus = RegQueryValueExW(
        key, valueName.c_str(), nullptr, &type,
        reinterpret_cast<BYTE*>(buffer.data()), &byteCount);
    RegCloseKey(key);

    if (queryStatus != ERROR_SUCCESS) {
        return false;
    }

    const std::wstring expected = GetRunCommand();
    return !expected.empty()
        && CompareStringOrdinal(buffer.data(), -1, expected.c_str(), -1, TRUE) == CSTR_EQUAL;
}

bool SetEnabled(bool on) {
    const std::wstring subKey = GetRegistrySubKey();
    const std::wstring valueName = GetRegistryValueName();

    if (on) {
        const std::wstring command = GetRunCommand();
        if (command.empty()) {
            return false;
        }

        HKEY key = nullptr;
        const LSTATUS createStatus = RegCreateKeyExW(
            HKEY_CURRENT_USER, subKey.c_str(), 0, nullptr, REG_OPTION_NON_VOLATILE,
            KEY_SET_VALUE, nullptr, &key, nullptr);
        if (createStatus != ERROR_SUCCESS) {
            return false;
        }

        const DWORD byteCount = static_cast<DWORD>((command.size() + 1) * sizeof(wchar_t));
        const LSTATUS setStatus = RegSetValueExW(
            key, valueName.c_str(), 0, REG_SZ,
            reinterpret_cast<const BYTE*>(command.c_str()), byteCount);
        RegCloseKey(key);
        return setStatus == ERROR_SUCCESS;
    }

    HKEY key = nullptr;
    const LSTATUS openStatus = RegOpenKeyExW(
        HKEY_CURRENT_USER, subKey.c_str(), 0, KEY_SET_VALUE, &key);
    if (openStatus == ERROR_FILE_NOT_FOUND) {
        return true;
    }
    if (openStatus != ERROR_SUCCESS) {
        return false;
    }

    const LSTATUS deleteStatus = RegDeleteValueW(key, valueName.c_str());
    RegCloseKey(key);
    return deleteStatus == ERROR_SUCCESS || deleteStatus == ERROR_FILE_NOT_FOUND;
}

bool ReconcileWithState(bool autoStart) {
    return autoStart
        ? IsEnabled() || SetEnabled(true)
        : SetEnabled(false);
}

void SetRegistryKeyOverride(const std::wstring& subkey) {
    g_registryKeyOverride = subkey;
}

} // namespace autostart
