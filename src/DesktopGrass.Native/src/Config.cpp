// Config.cpp

#include "Config.h"

#include "Json.h"

#include <Windows.h>

#include <algorithm>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <string>

namespace desktopgrass::config {
namespace {

// Annotated default config written verbatim on first run. JSONC comments are
// tolerated by the loader, so users can keep these notes while editing.
constexpr char kDefaultConfigTemplate[] =
    "{\n"
    "  // DesktopGrass settings. Edit and restart the app to apply.\n"
    "  // This file is created once and never overwritten, so your edits stick.\n"
    "  \"version\": 1,\n"
    "\n"
    "  // Animation frame rate. Lower = less CPU, choppier motion. Range 5-144.\n"
    "  \"targetFps\": 24,\n"
    "\n"
    "  // Grass blade density. Lower = fewer blades (less CPU). Range 0.2-5.0.\n"
    "  // Default 2.53125.\n"
    "  \"bladeDensity\": 2.53125\n"
    "}\n";

std::wstring DefaultConfigFilePath() {
    wchar_t* localAppData = nullptr;
    std::size_t length = 0;
    _wdupenv_s(&localAppData, &length, L"LOCALAPPDATA");

    std::filesystem::path path = localAppData && length > 0
        ? std::filesystem::path(localAppData)
        : std::filesystem::current_path();

    if (localAppData) {
        std::free(localAppData);
    }

    path /= L"DesktopGrass";
    path /= L"config.json";
    return path.wstring();
}

// Writes the annotated default config, but only if no file exists yet. Uses
// CREATE_NEW so a concurrent writer or an existing user file is never clobbered.
void TryWriteDefaultConfig(const std::filesystem::path& path) {
    const std::filesystem::path directory = path.parent_path();
    if (!directory.empty()) {
        std::error_code ec;
        std::filesystem::create_directories(directory, ec);
    }

    HANDLE handle = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr,
                                CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) {
        return; // Already exists (or unwritable): leave it untouched.
    }

    DWORD written = 0;
    WriteFile(handle, kDefaultConfigTemplate,
              static_cast<DWORD>(sizeof(kDefaultConfigTemplate) - 1), &written, nullptr);
    CloseHandle(handle);
}

template <typename T>
T Clamp(T value, T lo, T hi) {
    return value < lo ? lo : (value > hi ? hi : value);
}

Config ApplyAndClamp(const json::Value& root) {
    Config cfg;
    cfg.version = json::ReadInt(root, "version").value_or(kConfigVersion);

    const int fps = json::ReadInt(root, "targetFps").value_or(kTargetFpsDefault);
    cfg.targetFps = Clamp(fps, kTargetFpsMin, kTargetFpsMax);

    const double density = json::ReadDouble(root, "bladeDensity").value_or(kBladeDensityDefault);
    cfg.bladeDensity = Clamp(density, kBladeDensityMin, kBladeDensityMax);

    return cfg;
}

} // namespace

std::wstring GetConfigFilePath() {
    return DefaultConfigFilePath();
}

Config LoadConfig(const std::wstring& pathStr) {
    const std::filesystem::path path(pathStr);

    if (!std::filesystem::exists(path)) {
        TryWriteDefaultConfig(path);
        return Config{}; // Defaults match the template we just wrote.
    }

    std::ifstream file(path, std::ios::binary);
    if (!file) {
        return Config{};
    }

    std::ostringstream buffer;
    buffer << file.rdbuf();
    const std::string text = buffer.str();

    json::Value root;
    if (!json::Parse(text, root) || root.type != json::Value::Type::Object) {
        OutputDebugStringA("DesktopGrass config: malformed config.json; using defaults.\n");
        return Config{}; // Preserve the user's file; just fall back to defaults.
    }

    return ApplyAndClamp(root);
}

Config LoadConfig() {
    return LoadConfig(GetConfigFilePath());
}

} // namespace desktopgrass::config
