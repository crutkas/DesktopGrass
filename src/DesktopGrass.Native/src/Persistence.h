#pragma once

#include "Constants.h"
#include "DisplayTopology.h"

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace desktopgrass::persistence {

struct MonitorState {
    std::string stableId;
    std::string sourceId;
    std::optional<std::uint64_t> layoutSeed;
    int width = 0;
    int height = 0;
    int left = 0;
    int top = 0;
    bool workAreaBounds = false;
};

struct AppState {
    int version = 3;
    Scene scene = Scene::Grass;
    CritterKind critter = CritterKind::None;
    int critterCountOverride = 0;
    bool autoStart = false;
    std::vector<MonitorState> monitors;
};

bool LoadAppState(AppState& out);
bool SaveAppState(const AppState& state);
std::wstring GetStateFilePath();
void SetStateFilePathForTest(const std::wstring& path);
std::string MonitorKey(int width, int height, int left, int top);
std::string MonitorKey(const MonitorState& monitor);
const MonitorState* FindMonitorState(
    const AppState& state,
    const topology::MonitorSnapshot& monitor) noexcept;
void UpsertMonitorState(
    AppState& state,
    MonitorState monitor,
    const topology::MonitorSnapshot& snapshot);

} // namespace desktopgrass::persistence
