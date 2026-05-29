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
#include "MouseHook.h"

namespace desktopgrass {

class App {
public:
    static constexpr UINT  kTrayMessage     = WM_APP + 100;
    static constexpr UINT  kTrayIconId      = 1;
    static constexpr int   kMenuQuit        = 1001;
    static constexpr int   kMenuSceneGrass  = 1010;
    static constexpr int   kMenuSceneDesert = 1011;
    static constexpr int   kMenuSceneWinter = 1012;

    App() = default;
    ~App();

    bool Initialize(HINSTANCE hInst);
    int  Run();
    void RequestQuit();
    void SetScene(Scene s);
    Scene GetScene() const { return currentScene_; }

private:
    bool CreateMessageWindow();
    bool CreateTrayIcon();
    void RemoveTrayIcon();
    void DestroyMessageWindow();
    bool EnumerateMonitorsAndCreateWindows();
    void DestroyAllGrassWindows();
    void OnDisplayChanged();
    void DispatchMouseEvents();
    void RenderAllWindows(double dt);
    void UpdateSceneMenuCheck();

    static LRESULT CALLBACK MessageWindowProc(HWND hwnd, UINT msg,
                                              WPARAM wp, LPARAM lp);
    LRESULT HandleMessageWindowMessage(UINT msg, WPARAM wp, LPARAM lp);

    HINSTANCE                                   hInst_   = nullptr;
    HWND                                        msgHwnd_ = nullptr;
    HMENU                                       trayMenu_ = nullptr;
    HMENU                                       sceneSubmenu_ = nullptr;
    NOTIFYICONDATAW                             nid_{};
    bool                                        trayAdded_ = false;
    MouseEventQueue                             queue_{};
    std::vector<std::unique_ptr<GrassWindow>>   windows_;
    uint64_t                                    seed_     = 0;
    Scene                                       currentScene_ = SCENE_DEFAULT;
    LARGE_INTEGER                               qpcFreq_{};
    LARGE_INTEGER                               qpcLast_{};
    bool                                        quitRequested_ = false;
};

} // namespace desktopgrass
