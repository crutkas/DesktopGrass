// App.h
//
// Application lifecycle. Owns the tray icon, the mouse hook, the per-monitor
// GrassWindow list, and the message loop.

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>

#include <memory>
#include <vector>

#include "GrassWindow.h"
#include "DisplayTopology.h"
#include "MouseHook.h"
#include "Pacing.h"
#include "Persistence.h"
#include "Config.h"
#include "RuntimePolicy.h"
#include "VisibilityTracker.h"

namespace desktopgrass {

class App {
public:
    static constexpr UINT  kTrayMessage     = WM_APP + 100;
    static constexpr UINT  kVisibilityChangedMessage = WM_APP + 110;
    static constexpr UINT  kTrayIconId      = 1;
    static constexpr int   kMenuQuit          = 1001;
    static constexpr int   kMenuSceneGrass    = 1010;
    static constexpr int   kMenuSceneDesert   = 1011;
    static constexpr int   kMenuSceneWinter   = 1012;
    static constexpr int   kMenuSceneAutumn   = 1013;
    static constexpr int   kMenuSceneOcean    = 1014;
    static constexpr int   kMenuCritterNone     = 1020;
    static constexpr int   kMenuCritterSheep    = 1021;
    static constexpr int   kMenuCritterCat      = 1022;
    static constexpr int   kMenuCritterAll      = 1023;
    static constexpr int   kMenuPetCountRandom  = 1030;
    static constexpr int   kMenuPetCount1       = 1031;
    static constexpr int   kMenuPetCount6       = 1036;
    static constexpr int   kMenuAutoStart       = 1040;

    App() = default;
    ~App();

    bool Initialize(HINSTANCE hInst);
    int  Run();
    void RequestQuit();
    void SetScene(Scene s);
    Scene GetScene() const { return currentScene_; }
    void SetCritter(CritterKind c);
    CritterKind GetCritter() const { return currentCritter_; }
    void SetCritterCount(int n);
    int  GetCritterCount() const { return currentCritterCount_; }

private:
    bool CreateMessageWindow();
    bool InitializeRuntimeNotifications();
    void ShutdownRuntimeNotifications() noexcept;
    void SeedRuntimeState();
    runtime::SessionState QueryCurrentSessionState() const;
    bool CreateTrayIcon();
    bool AddTrayIcon();
    void RemoveTrayIcon();
    void DestroyMessageWindow();
    bool ReconcileDisplayTopology();
    void DestroyAllGrassWindows();
    std::unique_ptr<GrassWindow> CreateGrassWindow(
        const topology::MonitorSnapshot& monitor,
        const topology::SurfaceSpec& surface,
        uint64_t layoutSeed,
        const std::vector<persistence::CutRecord>& cuts);
    uint64_t ResolveLayoutSeed(
        const topology::MonitorSnapshot& monitor,
        const persistence::MonitorState* state) const;
    void ApplyPendingRuntimeChanges();
    void RefreshVisibilityState();
    void ApplyRuntimePolicy();
    void SetMouseObservationEnabled(bool enabled);
    LRESULT HandlePowerBroadcast(WPARAM wp, LPARAM lp);
    void HandleWtsSessionChange(WPARAM wp, LPARAM lp);
    void HandleVisibilityNotification();
    void DispatchMouseEvents();
    void RenderAllWindows(double dt);
    void ApplyStateToWindow(
        GrassWindow& window,
        const std::vector<persistence::CutRecord>& cuts);
    persistence::AppState BuildAppState();
    void CacheCurrentState();
    void SaveCurrentState();
    void HandleSessionEnding(bool ending);
    void SetAutoStart(bool enabled);
    void UpdateSceneMenuCheck();
    void UpdateCritterMenuCheck();
    void UpdatePetCountMenuCheck();
    void UpdateAutoStartMenuCheck();

    static LRESULT CALLBACK MessageWindowProc(HWND hwnd, UINT msg,
                                              WPARAM wp, LPARAM lp);
    LRESULT HandleMessageWindowMessage(HWND hwnd, UINT msg,
                                       WPARAM wp, LPARAM lp);

    HINSTANCE                                   hInst_   = nullptr;
    HWND                                        msgHwnd_ = nullptr;
    HMENU                                       trayMenu_ = nullptr;
    HMENU                                       sceneSubmenu_ = nullptr;
    HMENU                                       critterSubmenu_ = nullptr;
    HMENU                                       petCountSubmenu_ = nullptr;
    NOTIFYICONDATAW                             nid_{};
    bool                                        trayAdded_ = false;
    MouseEventQueue                             queue_{};
    std::vector<std::unique_ptr<GrassWindow>>   windows_;
    std::vector<runtime::SurfaceState>           surfaceStates_;
    std::vector<runtime::Decision>               runtimeDecisions_;
    config::Config                              config_{};
    Scene                                       currentScene_ = SCENE_DEFAULT;
    CritterKind                                 currentCritter_ = CRITTER_DEFAULT;
    int                                         currentCritterCount_ = 0;
    bool                                        autoStart_ = false;
    bool                                        hasPersistedState_ = false;
    persistence::AppState                       persistedState_{};
    ULONGLONG                                   lastPersistenceSaveMs_ = 0;
    ULONGLONG                                   lastTopologyPollMs_ = 0;
    LARGE_INTEGER                               qpcFreq_{};
    LARGE_INTEGER                               qpcLast_{};
    FramePacer                                  pacer_{};
    VisibilityTracker                           visibilityTracker_{};
    runtime::GlobalState                        runtimeState_{};
    HPOWERNOTIFY                                acdcPowerNotification_ = nullptr;
    HPOWERNOTIFY                                saverNotification_ = nullptr;
    HPOWERNOTIFY                                displayNotification_ = nullptr;
    HPOWERNOTIFY                                suspendResumeNotification_ = nullptr;
    DWORD                                       sessionId_ = 0xFFFFFFFFu;
    int                                         effectiveTargetFps_ = 0;
    bool                                        quitRequested_ = false;
    bool                                        displayChangePending_ = false;
    bool                                        runtimeStateDirty_ = true;
    bool                                        visibilityStateDirty_ = true;
    bool                                        anySurfaceRendering_ = false;
    bool                                        resumeFramePending_ = true;
    bool                                        mouseHookInstalled_ = false;
    bool                                        hardPauseStateSaved_ = false;
    bool                                        wtsNotificationRegistered_ = false;
    bool                                        sessionEndStateSaved_ = false;
    UINT                                        taskbarCreatedMessage_ = 0;
};

} // namespace desktopgrass
