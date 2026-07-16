#include "DisplayTopologyWin32.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellscalingapi.h>

#include <algorithm>
#include <map>
#include <string>
#include <string_view>
#include <vector>

#pragma comment(lib, "Shcore.lib")
#pragma comment(lib, "User32.lib")

namespace desktopgrass::topology {
namespace {

constexpr int kCaptureAttempts = 4;

using TargetPathMap = std::map<std::string, std::vector<std::string>>;

std::string WideToUtf8(std::wstring_view value) {
    if (value.empty()) return {};

    const int length = WideCharToMultiByte(
        CP_UTF8, WC_ERR_INVALID_CHARS,
        value.data(), static_cast<int>(value.size()),
        nullptr, 0, nullptr, nullptr);
    if (length <= 0) return {};

    std::string result(static_cast<std::size_t>(length), '\0');
    if (WideCharToMultiByte(
            CP_UTF8, WC_ERR_INVALID_CHARS,
            value.data(), static_cast<int>(value.size()),
            result.data(), length, nullptr, nullptr) != length) {
        return {};
    }
    return result;
}

std::string CanonicalWideIdentity(const wchar_t* value) {
    if (!value) return {};
    return CanonicalizeIdentityComponent(WideToUtf8(value));
}

bool TryCaptureTargetPaths(TargetPathMap& targetPaths) {
    targetPaths.clear();

    for (int attempt = 0; attempt < kCaptureAttempts; ++attempt) {
        UINT32 pathCount = 0;
        UINT32 modeCount = 0;
        LONG result = GetDisplayConfigBufferSizes(
            QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount);
        if (result != ERROR_SUCCESS) {
            return false;
        }

        std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
        std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
        result = QueryDisplayConfig(
            QDC_ONLY_ACTIVE_PATHS,
            &pathCount, paths.data(),
            &modeCount, modes.data(),
            nullptr);
        if (result == ERROR_INSUFFICIENT_BUFFER) {
            continue;
        }
        if (result != ERROR_SUCCESS) {
            return false;
        }

        paths.resize(pathCount);
        TargetPathMap captured;
        for (const DISPLAYCONFIG_PATH_INFO& path : paths) {
            DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};
            source.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = sizeof(source);
            source.header.adapterId = path.sourceInfo.adapterId;
            source.header.id = path.sourceInfo.id;
            if (DisplayConfigGetDeviceInfo(&source.header) != ERROR_SUCCESS) {
                continue;
            }

            const std::string sourceId =
                CanonicalWideIdentity(source.viewGdiDeviceName);
            if (sourceId.empty()) {
                continue;
            }

            std::vector<std::string>& targets = captured[sourceId];
            DISPLAYCONFIG_TARGET_DEVICE_NAME target{};
            target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = sizeof(target);
            target.header.adapterId = path.targetInfo.adapterId;
            target.header.id = path.targetInfo.id;
            if (DisplayConfigGetDeviceInfo(&target.header) != ERROR_SUCCESS) {
                continue;
            }

            const std::string targetPath =
                CanonicalWideIdentity(target.monitorDevicePath);
            if (!targetPath.empty()) {
                targets.push_back(targetPath);
            }
        }

        for (auto& [source, targets] : captured) {
            std::sort(targets.begin(), targets.end());
            targets.erase(std::unique(targets.begin(), targets.end()),
                          targets.end());
        }
        targetPaths = std::move(captured);
        return true;
    }
    return false;
}

struct MonitorEnumContext {
    const TargetPathMap* targetPaths = nullptr;
    std::vector<MonitorSnapshot> monitors;
    bool valid = true;
    bool correlationComplete = true;
};

PixelRect ToPixelRect(const RECT& rect) noexcept {
    return PixelRect{ rect.left, rect.top, rect.right, rect.bottom };
}

BOOL CALLBACK MonitorEnumProc(HMONITOR monitor,
                              HDC,
                              LPRECT,
                              LPARAM contextValue) {
    auto* context = reinterpret_cast<MonitorEnumContext*>(contextValue);

    MONITORINFOEXW info{};
    info.cbSize = sizeof(info);
    if (!GetMonitorInfoW(monitor, &info)) {
        context->valid = false;
        return FALSE;
    }

    UINT dpiX = 0;
    UINT dpiY = 0;
    if (FAILED(GetDpiForMonitor(
            monitor, MDT_EFFECTIVE_DPI, &dpiX, &dpiY))
        || dpiX == 0 || dpiY == 0) {
        context->valid = false;
        return FALSE;
    }

    MonitorSnapshot snapshot;
    snapshot.identity.sourceId = CanonicalWideIdentity(info.szDevice);
    if (snapshot.identity.sourceId.empty()) {
        context->valid = false;
        return FALSE;
    }

    if (context->targetPaths) {
        const auto found =
            context->targetPaths->find(snapshot.identity.sourceId);
        if (found != context->targetPaths->end()) {
            snapshot.identity.stableId =
                MakeStableMonitorId(found->second);
        } else {
            context->correlationComplete = false;
        }
    }

    snapshot.monitorBounds = ToPixelRect(info.rcMonitor);
    snapshot.workArea = ToPixelRect(info.rcWork);
    snapshot.dpi = dpiX;
    snapshot.primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
    context->monitors.push_back(std::move(snapshot));
    return TRUE;
}

} // namespace

bool TryCaptureWin32Topology(
    std::vector<MonitorSnapshot>& monitors) noexcept {
    monitors.clear();

    for (int attempt = 0; attempt < kCaptureAttempts; ++attempt) {
        TargetPathMap targetPaths;
        const bool hasDisplayConfig = TryCaptureTargetPaths(targetPaths);

        MonitorEnumContext context;
        context.targetPaths = hasDisplayConfig ? &targetPaths : nullptr;
        const BOOL enumerated = EnumDisplayMonitors(
            nullptr, nullptr, MonitorEnumProc,
            reinterpret_cast<LPARAM>(&context));
        if (!enumerated || !context.valid
            || !IsValidTopology(context.monitors)) {
            continue;
        }
        if (hasDisplayConfig && !context.correlationComplete
            && attempt + 1 < kCaptureAttempts) {
            continue;
        }

        monitors = SortTopology(context.monitors);
        return true;
    }
    return false;
}

} // namespace desktopgrass::topology
