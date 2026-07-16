#pragma once

#include <string>

namespace autostart {

bool IsEnabled();
bool SetEnabled(bool on);
std::wstring GetRegistryValueName();
std::wstring GetCurrentExePath();
std::wstring GetRunCommand();
bool ReconcileWithState(bool autoStart);
void SetRegistryKeyOverride(const std::wstring& subkey);

} // namespace autostart
