#include "TestHelpers.h"
#include "AutoStart.h"
#include "Persistence.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <atomic>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

namespace {

std::wstring unique_subkey(const wchar_t* name) {
    static std::atomic<int> counter{0};
    return std::wstring(L"Software\\DesktopGrass.Test.")
        + std::to_wstring(GetCurrentProcessId()) + L"."
        + std::to_wstring(GetTickCount64()) + L"."
        + std::to_wstring(counter.fetch_add(1)) + L"."
        + name;
}

class AutoStartRegistrySandbox {
public:
    explicit AutoStartRegistrySandbox(const wchar_t* name) : subkey_(unique_subkey(name)) {
        RegDeleteTreeW(HKEY_CURRENT_USER, subkey_.c_str());
        autostart::SetRegistryKeyOverride(subkey_);
    }

    ~AutoStartRegistrySandbox() {
        autostart::SetRegistryKeyOverride(subkey_);
        autostart::SetEnabled(false);
        RegDeleteTreeW(HKEY_CURRENT_USER, subkey_.c_str());
        autostart::SetRegistryKeyOverride(L"");
        desktopgrass::persistence::SetStateFilePathForTest(L"");
    }

    const std::wstring& subkey() const { return subkey_; }

private:
    std::wstring subkey_;
};

std::wstring read_registry_value(const std::wstring& subkey) {
    HKEY key = nullptr;
    Assert::IsTrue(RegOpenKeyExW(HKEY_CURRENT_USER, subkey.c_str(), 0, KEY_QUERY_VALUE, &key) == ERROR_SUCCESS);

    DWORD type = 0;
    DWORD byteCount = 0;
    const std::wstring valueName = autostart::GetRegistryValueName();
    Assert::IsTrue(RegQueryValueExW(key, valueName.c_str(), nullptr, &type, nullptr, &byteCount) == ERROR_SUCCESS);
    Assert::IsTrue(type == REG_SZ);

    std::vector<wchar_t> buffer(byteCount / sizeof(wchar_t) + 1);
    Assert::IsTrue(RegQueryValueExW(
        key, valueName.c_str(), nullptr, &type,
        reinterpret_cast<BYTE*>(buffer.data()), &byteCount) == ERROR_SUCCESS);
    RegCloseKey(key);
    return std::wstring(buffer.data());
}

bool registry_value_exists(const std::wstring& subkey) {
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, subkey.c_str(), 0, KEY_QUERY_VALUE, &key) != ERROR_SUCCESS) {
        return false;
    }

    const std::wstring valueName = autostart::GetRegistryValueName();
    const LSTATUS queryStatus = RegQueryValueExW(
        key, valueName.c_str(), nullptr, nullptr, nullptr, nullptr);
    RegCloseKey(key);
    return queryStatus == ERROR_SUCCESS;
}

void write_registry_value(const std::wstring& subkey, const std::wstring& value) {
    HKEY key = nullptr;
    Assert::IsTrue(RegCreateKeyExW(
        HKEY_CURRENT_USER, subkey.c_str(), 0, nullptr, REG_OPTION_NON_VOLATILE,
        KEY_SET_VALUE, nullptr, &key, nullptr) == ERROR_SUCCESS);
    const DWORD byteCount = static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t));
    Assert::IsTrue(RegSetValueExW(
        key, autostart::GetRegistryValueName().c_str(), 0, REG_SZ,
        reinterpret_cast<const BYTE*>(value.c_str()), byteCount) == ERROR_SUCCESS);
    RegCloseKey(key);
}

std::filesystem::path test_state_path(const char* name) {
    std::filesystem::path dir = std::filesystem::current_path()
        / ".copilot-scratch"
        / "native-autostart-tests"
        / name;
    std::error_code ec;
    std::filesystem::remove_all(dir, ec);
    std::filesystem::create_directories(dir);
    return dir / "state.json";
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(AutostartTests)
{
public:
TEST_METHOD(AutostartIsDisabledWhenRegistryValueIsMissing) {
    AutoStartRegistrySandbox sandbox(L"missing");

    Assert::IsFalse(autostart::IsEnabled());
}

TEST_METHOD(AutostartEnableCreatesRegistryValue) {
    AutoStartRegistrySandbox sandbox(L"enable");

    Assert::IsTrue(autostart::SetEnabled(true));

    Assert::IsTrue(autostart::IsEnabled());
}

TEST_METHOD(AutostartDisableDeletesRegistryValue) {
    AutoStartRegistrySandbox sandbox(L"disable");

    Assert::IsTrue(autostart::SetEnabled(true));
    Assert::IsTrue(autostart::SetEnabled(false));

    Assert::IsFalse(autostart::IsEnabled());
}

TEST_METHOD(AutostartRegistryValueContainsQuotedCurrentExePath) {
    AutoStartRegistrySandbox sandbox(L"path");

    Assert::IsTrue(autostart::SetEnabled(true));

    Assert::IsTrue(read_registry_value(sandbox.subkey()) == autostart::GetRunCommand());
}

TEST_METHOD(AutostartReconciliationRepairsAStaleCommand) {
    AutoStartRegistrySandbox sandbox(L"stale-command");
    write_registry_value(sandbox.subkey(), L"\"C:\\Old\\DesktopGrass.Native.exe\"");

    Assert::IsFalse(autostart::IsEnabled());
    Assert::IsTrue(autostart::ReconcileWithState(true));
    Assert::IsTrue(read_registry_value(sandbox.subkey()) == autostart::GetRunCommand());
}

TEST_METHOD(AutostartReconciliationRemovesAStaleCommandWhenDisabled) {
    AutoStartRegistrySandbox sandbox(L"stale-command-disabled");
    write_registry_value(sandbox.subkey(), L"\"C:\\Old\\DesktopGrass.Native.exe\"");

    Assert::IsTrue(registry_value_exists(sandbox.subkey()));
    Assert::IsTrue(autostart::ReconcileWithState(false));
    Assert::IsFalse(registry_value_exists(sandbox.subkey()));
}

TEST_METHOD(AutostartEnableIsIdempotent) {
    AutoStartRegistrySandbox sandbox(L"enable-idempotent");

    Assert::IsTrue(autostart::SetEnabled(true));
    Assert::IsTrue(autostart::SetEnabled(true));

    Assert::IsTrue(autostart::IsEnabled());
}

TEST_METHOD(AutostartDisableMissingValueIsNoOp) {
    AutoStartRegistrySandbox sandbox(L"disable-missing");

    Assert::IsTrue(autostart::SetEnabled(false));

    Assert::IsFalse(autostart::IsEnabled());
}

TEST_METHOD(AutostartPersistedTrueReconcilesRegistryOnStartup) {
    AutoStartRegistrySandbox sandbox(L"persisted-true");
    const auto path = test_state_path("persisted-true");
    desktopgrass::persistence::SetStateFilePathForTest(path.wstring());

    desktopgrass::persistence::AppState state;
    state.autoStart = true;
    Assert::IsTrue(desktopgrass::persistence::SaveAppState(state));

    desktopgrass::persistence::AppState loaded;
    Assert::IsTrue(desktopgrass::persistence::LoadAppState(loaded));
    Assert::IsTrue(autostart::ReconcileWithState(loaded.autoStart));

    Assert::IsTrue(autostart::IsEnabled());
}

TEST_METHOD(AutostartPersistedFalseReconcilesRegistryOnStartup) {
    AutoStartRegistrySandbox sandbox(L"persisted-false");
    const auto path = test_state_path("persisted-false");
    desktopgrass::persistence::SetStateFilePathForTest(path.wstring());

    Assert::IsTrue(autostart::SetEnabled(true));
    desktopgrass::persistence::AppState state;
    state.autoStart = false;
    Assert::IsTrue(desktopgrass::persistence::SaveAppState(state));

    desktopgrass::persistence::AppState loaded;
    Assert::IsTrue(desktopgrass::persistence::LoadAppState(loaded));
    Assert::IsTrue(autostart::ReconcileWithState(loaded.autoStart));

    Assert::IsFalse(autostart::IsEnabled());
}
};
}
