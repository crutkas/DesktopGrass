// DeviceRecovery.h
//
// Device-independent renderer recovery policy. Renderer owns the graphics
// resources and supplies the operations; this class owns only lifecycle and
// retry state.

#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

#include <d2d1.h>
#include <dxgi.h>

#include <optional>
#include <utility>

namespace desktopgrass {

enum class RenderOperation {
    EndDraw,
    Present1,
};

struct DeviceLossInfo {
    RenderOperation operation;
    HRESULT result;
    std::optional<HRESULT> deviceRemovalReason;
};

struct DeviceRecoveryAttempt {
    bool succeeded;
    ULONGLONG completedAtMs;
};

inline const char* RenderOperationTag(RenderOperation operation) noexcept {
    switch (operation) {
        case RenderOperation::EndDraw:  return "EndDraw";
        case RenderOperation::Present1: return "Present1";
    }
    return "Unknown";
}

template<typename GetDeviceRemovalReason>
std::optional<DeviceLossInfo> ClassifyDeviceLoss(
    RenderOperation operation,
    HRESULT result,
    GetDeviceRemovalReason&& getDeviceRemovalReason)
{
    if (operation == RenderOperation::EndDraw) {
        if (result == D2DERR_RECREATE_TARGET) {
            return DeviceLossInfo{
                operation,
                result,
                std::forward<GetDeviceRemovalReason>(getDeviceRemovalReason)(),
            };
        }
        return std::nullopt;
    }

    if (result == DXGI_ERROR_DEVICE_REMOVED || result == DXGI_ERROR_DEVICE_RESET) {
        return DeviceLossInfo{
            operation,
            result,
            std::forward<GetDeviceRemovalReason>(getDeviceRemovalReason)(),
        };
    }

    return std::nullopt;
}

class DeviceRecoveryController {
public:
    static constexpr ULONGLONG kRetryDelayMs = 1000;

    void StartReady() noexcept {
        running_ = true;
        ready_ = true;
        retryPending_ = false;
        nextRetryMs_ = 0;
    }

    void Stop() noexcept {
        running_ = false;
        ready_ = false;
        retryPending_ = false;
        nextRetryMs_ = 0;
    }

    bool IsRunning() const noexcept { return running_; }
    bool IsReady() const noexcept { return ready_; }
    bool IsRetryPending() const noexcept { return retryPending_; }
    ULONGLONG NextRetryMs() const noexcept { return nextRetryMs_; }

    // Marks externally owned dependencies unavailable without stopping
    // simulation updates. The process-wide device coordinator uses this
    // before releasing shared graphics resources so a failed shared rebuild
    // cannot leave a resource-less renderer reporting itself as ready.
    void Invalidate() noexcept {
        if (!running_) return;
        ready_ = false;
        retryPending_ = false;
        nextRetryMs_ = 0;
    }

    template<typename AdvanceSimulation,
             typename DrawAndPresent,
             typename RecreateResources>
    void ProcessFrame(
        ULONGLONG now,
        AdvanceSimulation&& advanceSimulation,
        DrawAndPresent&& drawAndPresent,
        RecreateResources&& recreateResources)
    {
        if (!running_) return;

        std::forward<AdvanceSimulation>(advanceSimulation)();

        if (!ready_) {
            if (retryPending_ && now >= nextRetryMs_) {
                AttemptRecovery(
                    std::forward<RecreateResources>(recreateResources));
            }
            return;
        }

        std::forward<DrawAndPresent>(drawAndPresent)();
    }

    template<typename RecreateResources, typename ReportHresult>
    void HandleDeviceLoss(
        const DeviceLossInfo& loss,
        RecreateResources&& recreateResources,
        ReportHresult&& reportHresult)
    {
        if (!running_ || !ready_) return;

        ReportDeviceLoss(
            loss,
            std::forward<ReportHresult>(reportHresult));

        ready_ = false;
        retryPending_ = false;
        nextRetryMs_ = 0;
        AttemptRecovery(
            std::forward<RecreateResources>(recreateResources));
    }

    // Forces an immediate recovery attempt regardless of the current
    // ready/retry state, bypassing the backoff timer. For callers that
    // already know it is time to retry — e.g. a process-wide coordinator
    // that just finished repairing a shared dependency this controller's
    // owner relies on, and now wants every dependent rebuilt synchronously
    // instead of waiting for its own 1-second backoff. Leaves `running_`
    // untouched either way, so a simulation kept alive through an outage
    // (via ProcessFrame's AdvanceSimulation callback) is unaffected.
    template<typename RecreateResources>
    void Recover(RecreateResources&& recreateResources)
    {
        AttemptRecovery(std::forward<RecreateResources>(recreateResources));
    }

private:
    template<typename RecreateResources>
    void AttemptRecovery(RecreateResources&& recreateResources)
    {
        const DeviceRecoveryAttempt attempt =
            std::forward<RecreateResources>(recreateResources)();
        if (attempt.succeeded) {
            ready_ = true;
            retryPending_ = false;
            nextRetryMs_ = 0;
            return;
        }

        ready_ = false;
        retryPending_ = true;
        nextRetryMs_ = attempt.completedAtMs + kRetryDelayMs;
    }

    template<typename ReportHresult>
    static void ReportDeviceLoss(
        const DeviceLossInfo& loss,
        ReportHresult&& reportHresult)
    {
        std::forward<ReportHresult>(reportHresult)(
            RenderOperationTag(loss.operation),
            loss.result);

        if (loss.deviceRemovalReason && FAILED(*loss.deviceRemovalReason)) {
            std::forward<ReportHresult>(reportHresult)(
                "ID3D11Device::GetDeviceRemovedReason",
                *loss.deviceRemovalReason);
        }
    }

    bool running_ = false;
    bool ready_ = false;
    bool retryPending_ = false;
    ULONGLONG nextRetryMs_ = 0;
};

} // namespace desktopgrass
