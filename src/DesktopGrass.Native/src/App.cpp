// App.cpp

#include "App.h"

#include "AutoStart.h"
#include "Constants.h"
#include "DisplayTopologyWin32.h"
#include "Sim.h"
#include "../resource.h"

#include <dbt.h>
#include <algorithm>
#include <cstdio>
#include <string>
#include <utility>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "User32.lib")

namespace desktopgrass {

namespace {

constexpr const wchar_t* kMsgWindowClass = L"DesktopGrass.Native.MessageWindow";

runtime::Rect make_runtime_rect(const topology::PixelRect& bounds) {
    return runtime::Rect{
        bounds.left, bounds.top, bounds.right, bounds.bottom,
    };
}

runtime::Rect make_runtime_rect(const topology::SurfaceSpec& surface) {
    return runtime::Rect{
        surface.x,
        surface.y,
        surface.x + surface.widthPx,
        surface.y + surface.heightPx,
    };
}
} // anonymous

App::~App() {
    SetMouseObservationEnabled(false);
    ShutdownRuntimeNotifications();
    DestroyAllGrassWindows();
    RemoveTrayIcon();
    if (trayMenu_) { DestroyMenu(trayMenu_); trayMenu_ = nullptr; }
    DestroyMessageWindow();
}

bool App::Initialize(HINSTANCE hInst) {
    hInst_   = hInst;
    config_  = config::LoadConfig();

    QueryPerformanceFrequency(&qpcFreq_);
    QueryPerformanceCounter(&qpcLast_);

    hasPersistedState_ = persistence::LoadAppState(persistedState_);
    if (hasPersistedState_) {
        currentScene_ = persistedState_.scene;
        currentCritter_ = persistedState_.critter;
        currentCritterCount_ = persistedState_.critterCountOverride;
        autoStart_ = persistedState_.autoStart;
    }
    if (!autostart::ReconcileWithState(autoStart_)) {
        OutputDebugStringA("[DesktopGrass] unable to reconcile Start with Windows registry state\n");
    }
    lastPersistenceSaveMs_ = GetTickCount64();
    lastTopologyPollMs_ = lastPersistenceSaveMs_;
    taskbarCreatedMessage_ = RegisterWindowMessageW(L"TaskbarCreated");
    if (taskbarCreatedMessage_ == 0) return false;

    if (!GrassWindow::RegisterWindowClass(hInst_)) return false;
    if (!CreateMessageWindow())                    return false;
    if (!InitializeRuntimeNotifications())         return false;
    if (!CreateTrayIcon())                         return false;
    if (!ReconcileDisplayTopology())               return false;

    return true;
}

bool App::CreateMessageWindow() {
    WNDCLASSEXW wc{};
    wc.cbSize        = sizeof(wc);
    wc.lpfnWndProc   = App::MessageWindowProc;
    wc.hInstance     = hInst_;
    wc.lpszClassName = kMsgWindowClass;

    ATOM atom = RegisterClassExW(&wc);
    if (atom == 0 && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) {
        return false;
    }

    msgHwnd_ = CreateWindowExW(
        WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
        kMsgWindowClass, L"DesktopGrass.Msg",
        WS_POPUP, 0, 0, 0, 0,
        nullptr, nullptr, hInst_, this);
    return msgHwnd_ != nullptr;
}

void App::DestroyMessageWindow() {
    if (msgHwnd_) {
        if (!DestroyWindow(msgHwnd_)) {
            OutputDebugStringA(
                "[DesktopGrass] unable to destroy message window\n");
        }
    }
}

bool App::InitializeRuntimeNotifications() {
    if (!runtimeNotifications_.Start(msgHwnd_)) return false;

    if (!visibilityTracker_.Start(
            msgHwnd_, kVisibilityChangedMessage)) {
        OutputDebugStringA(
            "[DesktopGrass] visibility WinEvent registration failed\n");
        ShutdownRuntimeNotifications();
        return false;
    }

    runtimeStateDirty_ = true;
    visibilityStateDirty_ = true;
    return true;
}

void App::ShutdownRuntimeNotifications() noexcept {
    visibilityTracker_.Stop();
    runtimeNotifications_.Stop();
}

bool App::CreateTrayIcon() {
    // Build the menu: Scene ▸ (radio: Grass / Desert / Winter / Autumn) | Quit.
    // The Scene submenu is a child popup of trayMenu_; DestroyMenu is
    // recursive so destroying trayMenu_ cleans up the submenu too.
    trayMenu_ = CreatePopupMenu();
    if (!trayMenu_) return false;
    sceneSubmenu_ = CreatePopupMenu();
    if (!sceneSubmenu_) return false;
    AppendMenuW(sceneSubmenu_, MF_STRING, kMenuSceneGrass,  L"Grass");
    AppendMenuW(sceneSubmenu_, MF_STRING, kMenuSceneDesert, L"Desert");
    AppendMenuW(sceneSubmenu_, MF_STRING, kMenuSceneWinter, L"Winter");
    AppendMenuW(sceneSubmenu_, MF_STRING, kMenuSceneAutumn, L"Autumn");
    AppendMenuW(sceneSubmenu_, MF_STRING, kMenuSceneOcean,  L"Ocean");
    AppendMenuW(trayMenu_, MF_POPUP | MF_STRING,
                reinterpret_cast<UINT_PTR>(sceneSubmenu_), L"Scene");

    critterSubmenu_ = CreatePopupMenu();
    if (!critterSubmenu_) return false;
    AppendMenuW(critterSubmenu_, MF_STRING, kMenuCritterNone,  L"None");
    AppendMenuW(critterSubmenu_, MF_STRING, kMenuCritterSheep, L"Sheep");
    AppendMenuW(critterSubmenu_, MF_STRING, kMenuCritterCat,   L"Cat");
    AppendMenuW(critterSubmenu_, MF_STRING, kMenuCritterAll,   L"All");

    petCountSubmenu_ = CreatePopupMenu();
    if (!petCountSubmenu_) return false;
    AppendMenuW(petCountSubmenu_, MF_STRING, kMenuPetCountRandom, L"Random");
    for (int n : PET_COUNT_OPTIONS) {
        AppendMenuW(petCountSubmenu_, MF_STRING,
                    static_cast<UINT_PTR>(kMenuPetCount1 + (n - 1)),
                    std::to_wstring(n).c_str());
    }
    AppendMenuW(critterSubmenu_, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(critterSubmenu_, MF_POPUP | MF_STRING,
                reinterpret_cast<UINT_PTR>(petCountSubmenu_), L"Pet count");

    AppendMenuW(trayMenu_, MF_POPUP | MF_STRING,
                reinterpret_cast<UINT_PTR>(critterSubmenu_), L"Critter");

    AppendMenuW(trayMenu_, MF_STRING, kMenuAutoStart, L"Start with Windows");
    AppendMenuW(trayMenu_, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(trayMenu_, MF_STRING, kMenuQuit, L"Quit DesktopGrass");
    UpdateSceneMenuCheck();
    UpdateCritterMenuCheck();
    UpdatePetCountMenuCheck();
    UpdateAutoStartMenuCheck();

    nid_ = {};
    nid_.cbSize           = sizeof(nid_);
    nid_.hWnd             = msgHwnd_;
    nid_.uID              = kTrayIconId;
    nid_.uFlags           = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    nid_.uCallbackMessage = kTrayMessage;

    HICON icon = LoadIconW(hInst_, MAKEINTRESOURCEW(IDI_TRAYICON));
    if (!icon) icon = LoadIconW(nullptr, IDI_APPLICATION);
    nid_.hIcon = icon;
    wcsncpy_s(nid_.szTip, L"Desktop Grass", _TRUNCATE);

    AddTrayIcon();
    return true; // non-fatal
}

bool App::AddTrayIcon() {
    if (nid_.cbSize == 0 || !nid_.hWnd) {
        return false;
    }

    const BOOL ok = Shell_NotifyIconW(NIM_ADD, &nid_);
    trayAdded_ = ok == TRUE;
    if (!trayAdded_) {
        OutputDebugStringA("[DesktopGrass] Shell_NotifyIcon(NIM_ADD) failed\n");
        return false;
    }

    nid_.uVersion = NOTIFYICON_VERSION_4;
    if (!Shell_NotifyIconW(NIM_SETVERSION, &nid_)) {
        OutputDebugStringA(
            "[DesktopGrass] Shell_NotifyIcon(NIM_SETVERSION) failed\n");
    }
    return true;
}

void App::UpdateSceneMenuCheck() {
    if (!sceneSubmenu_) return;
    // Radio-style check: kMenuSceneGrass + Scene enum index.
    const int activeId = kMenuSceneGrass + static_cast<int>(currentScene_);
    CheckMenuRadioItem(sceneSubmenu_,
                       kMenuSceneGrass, kMenuSceneOcean,
                       activeId, MF_BYCOMMAND);
}

void App::SetScene(Scene s) {
    if (s == currentScene_) {
        UpdateSceneMenuCheck();
        return;
    }
    currentScene_ = s;
    for (auto& w : windows_) {
        sim_set_scene(w->GetRenderer().GetSim(), s);
    }
    UpdateSceneMenuCheck();
    SaveCurrentState();
}

void App::UpdateCritterMenuCheck() {
    if (!critterSubmenu_) return;
    const int activeId = kMenuCritterNone + static_cast<int>(currentCritter_);
    CheckMenuRadioItem(critterSubmenu_,
                       kMenuCritterNone, kMenuCritterAll,
                       activeId, MF_BYCOMMAND);
}

void App::UpdatePetCountMenuCheck() {
    if (!petCountSubmenu_) return;
    const int activeId = currentCritterCount_ > 0
        ? kMenuPetCount1 + (std::min(currentCritterCount_, PET_COUNT_MAX_PER_MONITOR) - 1)
        : kMenuPetCountRandom;
    CheckMenuRadioItem(petCountSubmenu_,
                       kMenuPetCountRandom, kMenuPetCount6,
                       activeId, MF_BYCOMMAND);
}

void App::UpdateAutoStartMenuCheck() {
    if (!trayMenu_) return;
    CheckMenuItem(trayMenu_, kMenuAutoStart,
                  MF_BYCOMMAND | (autoStart_ ? MF_CHECKED : MF_UNCHECKED));
}

void App::SetAutoStart(bool enabled) {
    if (enabled == autoStart_ && autostart::IsEnabled() == enabled) {
        UpdateAutoStartMenuCheck();
        return;
    }

    if (!autostart::SetEnabled(enabled)) {
        OutputDebugStringA("[DesktopGrass] unable to update Start with Windows registry state\n");
        UpdateAutoStartMenuCheck();
        return;
    }

    autoStart_ = enabled;
    UpdateAutoStartMenuCheck();
    SaveCurrentState();
}

void App::SetCritter(CritterKind c) {
    if (c == currentCritter_) {
        UpdateCritterMenuCheck();
        return;
    }
    currentCritter_ = c;
    for (auto& w : windows_) {
        sim_set_critter(w->GetRenderer().GetSim(), c);
    }
    UpdateCritterMenuCheck();
    SaveCurrentState();
}

void App::SetCritterCount(int n) {
    const int sanitized = n > 0 ? std::min(n, PET_COUNT_MAX_PER_MONITOR) : 0;
    if (sanitized == currentCritterCount_) {
        UpdatePetCountMenuCheck();
        return;
    }

    currentCritterCount_ = sanitized;
    for (auto& w : windows_) {
        sim_set_critter_count(w->GetRenderer().GetSim(), currentCritterCount_);
    }
    UpdatePetCountMenuCheck();
    SaveCurrentState();
}

void App::RemoveTrayIcon() {
    if (trayAdded_) {
        Shell_NotifyIconW(NIM_DELETE, &nid_);
        trayAdded_ = false;
    }
}

std::unique_ptr<GrassWindow> App::CreateGrassWindow(
    const topology::MonitorSnapshot& monitor,
    const topology::SurfaceSpec& surface,
    uint64_t layoutSeed,
    const std::vector<persistence::CutRecord>& cuts) {
    auto window = std::make_unique<GrassWindow>();
    if (!window->Create(
            hInst_, msgHwnd_, monitor, surface, layoutSeed,
            config_.bladeDensity, config_.swaySpeed, config_.swayAmplitude)) {
        OutputDebugStringA("[DesktopGrass] GrassWindow::Create failed\n");
        return nullptr;
    }
    ApplyStateToWindow(*window, cuts);
    return window;
}

uint64_t App::ResolveLayoutSeed(
    const topology::MonitorSnapshot& monitor,
    const persistence::MonitorState* state) const {
    if (state && state->layoutSeed) {
        return *state->layoutSeed;
    }
    if (state && state->workAreaBounds) {
        return topology::MakeLegacyLayoutSeed(monitor.workArea);
    }
    return topology::MakeLayoutSeed(monitor.identity);
}

bool App::ReconcileDisplayTopology() {
    std::vector<topology::MonitorSnapshot> desired;
    if (!topology::TryCaptureWin32Topology(desired)) {
        OutputDebugStringA(
            "[DesktopGrass] unable to capture a coherent display topology\n");
        return false;
    }

    std::vector<topology::CurrentSurface> current;
    current.reserve(windows_.size());
    for (const auto& window : windows_) {
        current.push_back(
            topology::CurrentSurface{
                window->GetMonitor(), window->GetSurface() });
    }

    const auto plan = topology::PlanReconciliation(
        current, desired, STRIP_HEIGHT + HEADROOM);
    if (!plan) {
        OutputDebugStringA(
            "[DesktopGrass] unable to plan display topology reconciliation\n");
        return false;
    }
    if (!plan->changed) {
        return true;
    }

    CacheCurrentState();

    std::vector<std::unique_ptr<GrassWindow>> oldWindows =
        std::move(windows_);
    std::vector<std::unique_ptr<GrassWindow>> reconciled;
    reconciled.reserve(plan->desired.size() + plan->removals.size());
    bool fullyApplied = true;

    for (const topology::PlannedSurface& planned : plan->desired) {
        if (planned.currentIndex == topology::kNoSurface) {
            const persistence::MonitorState* saved =
                hasPersistedState_
                    ? persistence::FindMonitorState(
                        persistedState_, planned.monitor)
                    : nullptr;
            const uint64_t seed = ResolveLayoutSeed(planned.monitor, saved);
            const std::vector<persistence::CutRecord> cuts =
                saved ? saved->cuts
                      : std::vector<persistence::CutRecord>{};
            auto created = CreateGrassWindow(
                planned.monitor, planned.surface, seed, cuts);
            if (created) {
                created->Show();
                reconciled.push_back(std::move(created));
            } else {
                fullyApplied = false;
            }
            continue;
        }

        auto& existing = oldWindows[planned.currentIndex];
        if (!existing) {
            fullyApplied = false;
            continue;
        }

        switch (planned.kind) {
        case topology::ReconcileKind::Keep:
            existing->UpdateTopology(planned.monitor, planned.surface);
            reconciled.push_back(std::move(existing));
            break;

        case topology::ReconcileKind::Move:
            if (!existing->MoveTo(planned.monitor, planned.surface)) {
                OutputDebugStringA(
                    "[DesktopGrass] unable to move a monitor surface\n");
                fullyApplied = false;
            }
            reconciled.push_back(std::move(existing));
            break;

        case topology::ReconcileKind::Replace: {
            const uint64_t seed = existing->GetLayoutSeed();
            const std::vector<persistence::CutRecord> cuts =
                sim_get_cuts(existing->GetRenderer().GetSim());
            auto replacement = CreateGrassWindow(
                planned.monitor, planned.surface, seed, cuts);
            if (!replacement) {
                fullyApplied = false;
                reconciled.push_back(std::move(existing));
                break;
            }

            existing->Destroy();
            if (existing->GetHwnd()) {
                OutputDebugStringA(
                    "[DesktopGrass] unable to retire replaced monitor surface\n");
                replacement->Destroy();
                fullyApplied = false;
                reconciled.push_back(std::move(existing));
                break;
            }
            existing.reset();
            replacement->Show();
            reconciled.push_back(std::move(replacement));
            break;
        }

        case topology::ReconcileKind::Create:
            fullyApplied = false;
            reconciled.push_back(std::move(existing));
            break;
        }
    }

    for (const std::size_t removedIndex : plan->removals) {
        auto& removed = oldWindows[removedIndex];
        if (!removed) continue;

        removed->Destroy();
        if (removed->GetHwnd()) {
            OutputDebugStringA(
                "[DesktopGrass] unable to destroy removed monitor surface\n");
            fullyApplied = false;
            reconciled.push_back(std::move(removed));
        }
    }

    windows_ = std::move(reconciled);
    SaveCurrentState();
    if (!fullyApplied) {
        OutputDebugStringA(
            "[DesktopGrass] display topology partially applied; will retry\n");
    }

    surfaceStates_.assign(windows_.size(), {});
    runtimeDecisions_.assign(windows_.size(), {});
    visibilityStateDirty_ = true;
    runtimeStateDirty_ = true;
    ApplyPendingRuntimeChanges();

    for (auto& window : windows_) {
        window->Show();
    }
    return !windows_.empty();
}

void App::DestroyAllGrassWindows() {
    windows_.clear();
    surfaceStates_.clear();
    runtimeDecisions_.clear();
    anySurfaceRendering_ = false;
    effectiveTargetFps_ = 0;
}

void App::ApplyPendingRuntimeChanges() {
    if (visibilityStateDirty_) {
        RefreshVisibilityState();
    }
    if (runtimeStateDirty_) {
        ApplyRuntimePolicy();
    }
}

void App::RefreshVisibilityState() {
    surfaceStates_.resize(windows_.size());

    runtime::Rect foregroundBounds;
    const bool hasForeground =
        visibilityTracker_.TryGetForegroundBounds(foregroundBounds);

    for (std::size_t i = 0; i < windows_.size(); ++i) {
        GrassWindow& window = *windows_[i];
        runtime::SurfaceState state;
        state.fullscreen = hasForeground
            && runtime::Covers(
                foregroundBounds,
                make_runtime_rect(window.GetMonitor().monitorBounds));
        state.occluded = !state.fullscreen
            && visibilityTracker_.IsFullyOccluded(
                window.GetHwnd(),
                make_runtime_rect(window.GetSurface()));
        surfaceStates_[i] = state;
    }

    visibilityStateDirty_ = false;
    runtimeStateDirty_ = true;
}

void App::ApplyRuntimePolicy() {
    const bool wasRendering = anySurfaceRendering_;
    anySurfaceRendering_ = false;
    effectiveTargetFps_ = 0;
    runtimeDecisions_.resize(windows_.size());

    const runtime::Decision globalDecision =
        runtime::Evaluate(
            runtimeNotifications_.State(), {}, config_.targetFps);
    const bool globallyPaused =
        runtime::IsGlobalPause(globalDecision.pauseReason);

    if (globallyPaused
        && !hardPauseStateSaved_
        && !windows_.empty()) {
        SaveCurrentState();
        hardPauseStateSaved_ = true;
    } else if (!globallyPaused) {
        hardPauseStateSaved_ = false;
    }

    for (std::size_t i = 0; i < windows_.size(); ++i) {
        const runtime::Decision decision = runtime::Evaluate(
            runtimeNotifications_.State(),
            surfaceStates_[i],
            config_.targetFps);
        runtimeDecisions_[i] = decision;

        const bool visibilitySuppressed =
            surfaceStates_[i].fullscreen || surfaceStates_[i].occluded;
        windows_[i]->SetSuppressed(!decision.show || visibilitySuppressed);

        if (decision.render) {
            anySurfaceRendering_ = true;
            effectiveTargetFps_ = effectiveTargetFps_ == 0
                ? decision.targetFps
                : std::min(effectiveTargetFps_, decision.targetFps);
        }
    }

    SetMouseObservationEnabled(anySurfaceRendering_);

    if (!wasRendering && anySurfaceRendering_) {
        QueryPerformanceCounter(&qpcLast_);
        resumeFramePending_ = true;
        lastTopologyPollMs_ = 0;
    } else if (wasRendering && !anySurfaceRendering_) {
        QueryPerformanceCounter(&qpcLast_);
    }

    runtimeStateDirty_ = false;
}

void App::SetMouseObservationEnabled(bool enabled) {
    if (enabled) {
        if (mouseHookInstalled_) return;
        queue_.clear();
        if (install_mouse_hook(&queue_)) {
            mouseHookInstalled_ = true;
        } else {
            OutputDebugStringA(
                "[DesktopGrass] install_mouse_hook failed; input effects are disabled\n");
        }
        return;
    }

    if (mouseHookInstalled_) {
        uninstall_mouse_hook();
        mouseHookInstalled_ = false;
    }
    queue_.clear();
}

void App::DispatchMouseEvents() {
    // Drain the lock-free queue once. Each event is then routed to whichever
    // GrassWindow's screen rect contains it.
    RawMouseEvent raw[256];
    while (true) {
        std::size_t n = queue_.drain(raw, 256);
        if (n == 0) break;

        for (std::size_t i = 0; i < n; ++i) {
            const RawMouseEvent& e = raw[i];
            for (std::size_t windowIndex = 0;
                 windowIndex < windows_.size();
                 ++windowIndex) {
                if (windowIndex >= runtimeDecisions_.size()
                    || !runtimeDecisions_[windowIndex].render) {
                    continue;
                }
                auto& w = windows_[windowIndex];
                const topology::SurfaceSpec& surface = w->GetSurface();
                const int right = surface.x + surface.widthPx;
                const int bottom = surface.y + surface.heightPx;
                // Move events fire across the gust band; click events only
                // when in the strip. The Sim's band-check (apply_move / click)
                // re-filters in window-local coords. Here we route any event
                // whose x is in the window's horizontal range — Move events
                // need to update the prevCursor baseline even outside the
                // band so the baseline stays accurate, and the spec already
                // handles the band rejection.
                if (e.screenX < surface.x || e.screenX > right) continue;

                // For move events we accept any y; for click events we only
                // accept y inside the band.
                if (e.type == EventType::Click) {
                    if (e.screenY < surface.y || e.screenY > bottom) continue;
                }

                // Convert to window-local DIPs.
                const UINT dpi = w->GetRenderer().GetDpi();
                const double scale = 96.0 / static_cast<double>(dpi);
                InputEvent ie{};
                ie.type = e.type;
                ie.x    = (e.screenX - surface.x) * scale;
                ie.y    = (e.screenY - surface.y) * scale;
                ie.time = e.timeSeconds;

                // Apply directly to the sim. Note that this happens BEFORE
                // sim_tick (which itself drains its events list), so we apply
                // events through the per-tick path — collect into a per-window
                // event vector instead.
                // To keep things simple, push to the Sim immediately:
                if (ie.type == EventType::Move) {
                    sim_apply_move(w->GetRenderer().GetSim(), ie);
                } else {
                    sim_apply_click(w->GetRenderer().GetSim(), ie);
                }
                break; // each event belongs to at most one window
            }
        }
        if (n < 256) break;
    }
}

void App::RenderAllWindows(double dt) {
    DispatchMouseEvents();
    for (std::size_t i = 0; i < windows_.size(); ++i) {
        if (i < runtimeDecisions_.size() && runtimeDecisions_[i].render) {
            windows_[i]->RenderFrame(dt, nullptr, 0);
        }
    }
}

void App::ApplyStateToWindow(
    GrassWindow& window,
    const std::vector<persistence::CutRecord>& cuts) {
    Sim& sim = window.GetRenderer().GetSim();
    sim_set_scene(sim, currentScene_);
    sim_set_critter_count(sim, currentCritterCount_);
    sim_set_critter(sim, currentCritter_);
    sim_apply_cuts(sim, cuts);
}

persistence::AppState App::BuildAppState() {
    persistence::AppState state =
        hasPersistedState_ ? persistedState_ : persistence::AppState{};
    state.version = 3;
    state.scene = currentScene_;
    state.critter = currentCritter_;
    state.critterCountOverride = currentCritterCount_;
    state.autoStart = autoStart_;

    state.monitors.reserve(state.monitors.size() + windows_.size());
    for (const auto& w : windows_) {
        const topology::MonitorSnapshot& snapshot = w->GetMonitor();
        const topology::PixelRect& bounds = snapshot.monitorBounds;
        persistence::MonitorState monitor;
        monitor.stableId = snapshot.identity.stableId;
        monitor.sourceId = snapshot.identity.sourceId;
        monitor.layoutSeed = w->GetLayoutSeed();
        monitor.width = bounds.Width();
        monitor.height = bounds.Height();
        monitor.left = bounds.left;
        monitor.top = bounds.top;
        monitor.workAreaBounds = false;
        const Sim& sim = w->GetRenderer().GetSim();
        monitor.cuts = sim_get_cuts(sim);
        persistence::UpsertMonitorState(
            state, std::move(monitor), snapshot);
    }

    return state;
}

void App::CacheCurrentState() {
    persistedState_ = BuildAppState();
    hasPersistedState_ = true;
}

void App::SaveCurrentState() {
    CacheCurrentState();
    if (!persistence::SaveAppState(persistedState_)) {
        OutputDebugStringA("[DesktopGrass] unable to save application state\n");
    }
    lastPersistenceSaveMs_ = GetTickCount64();
}

void App::HandleSessionEnding(bool ending) {
    if (!ending) {
        sessionEndStateSaved_ = false;
        return;
    }
    if (!sessionEndStateSaved_) {
        SaveCurrentState();
        sessionEndStateSaved_ = true;
    }
}

void App::HandleVisibilityNotification() {
    visibilityTracker_.AcknowledgeNotification();
    visibilityStateDirty_ = true;
}

int App::Run() {
    MSG msg{};

    while (!quitRequested_) {
        // Drain pending messages without blocking.
        while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
            if (msg.message == WM_QUIT) {
                quitRequested_ = true;
                break;
            }
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        if (quitRequested_) break;

        // Broadcasts can be missed or arrive while Windows is still converging
        // on a new topology. A low-frequency snapshot is the safety net while
        // rendering; equal snapshots produce no window or persistence work.
        ApplyPendingRuntimeChanges();
        if (!anySurfaceRendering_) {
            pacer_.WaitForMessage();
            continue;
        }
        const ULONGLONG tickMs = GetTickCount64();
        if (tickMs - lastTopologyPollMs_ >= 1000ull) {
            lastTopologyPollMs_ = tickMs;
            displayChangePending_ = true;
        }
        if (displayChangePending_) {
            displayChangePending_ = false;
            ReconcileDisplayTopology();
            ApplyPendingRuntimeChanges();
            if (!anySurfaceRendering_) continue;
        }

        // Compute dt.
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        double dt = static_cast<double>(now.QuadPart - qpcLast_.QuadPart) /
                    static_cast<double>(qpcFreq_.QuadPart);
        qpcLast_ = now;
        if (resumeFramePending_) {
            dt = std::min(dt, 1.0 / 30.0);
            resumeFramePending_ = false;
        }

        RenderAllWindows(dt);

        if (GetTickCount64() - lastPersistenceSaveMs_ >= 60000ull) {
            SaveCurrentState();
        }

        // Pace to the target frame interval, accounting for the time already
        // spent rendering/presenting this iteration so the cadence holds at
        // the target fps regardless of how long Present blocked. The pacer
        // uses a high-resolution waitable timer (Win 10 1803+) so the wait
        // honours sub-15.6 ms remainders instead of getting clamped to the
        // default system timer resolution. The wait returns early if input
        // arrives, keeping the app responsive.
        LARGE_INTEGER after;
        QueryPerformanceCounter(&after);
        const double elapsedSec = static_cast<double>(after.QuadPart - now.QuadPart) /
                                  static_cast<double>(qpcFreq_.QuadPart);
        const double targetFrameSec =
            1.0 / static_cast<double>(effectiveTargetFps_);
        const double remainingSec = targetFrameSec - elapsedSec;
        pacer_.WaitUntilNextFrame(remainingSec);
    }

    SaveCurrentState();
    return static_cast<int>(msg.wParam);
}

void App::RequestQuit() {
    quitRequested_ = true;
    PostQuitMessage(0);
}

LRESULT CALLBACK App::MessageWindowProc(HWND hwnd, UINT msg,
                                        WPARAM wp, LPARAM lp)
{
    App* self = nullptr;
    if (msg == WM_NCCREATE) {
        auto* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        self = reinterpret_cast<App*>(cs->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        if (self) self->msgHwnd_ = hwnd;
    } else {
        self = reinterpret_cast<App*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    }
    if (self) return self->HandleMessageWindowMessage(hwnd, msg, wp, lp);
    return DefWindowProcW(hwnd, msg, wp, lp);
}

LRESULT App::HandleMessageWindowMessage(
    HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (taskbarCreatedMessage_ != 0 && msg == taskbarCreatedMessage_) {
        trayAdded_ = false;
        AddTrayIcon();
        displayChangePending_ = true;
        return 0;
    }

    switch (msg) {
        case kTrayMessage:
            if (LOWORD(lp) == WM_RBUTTONUP || LOWORD(lp) == WM_CONTEXTMENU) {
                POINT pt;
                GetCursorPos(&pt);
                SetForegroundWindow(msgHwnd_);
                TrackPopupMenu(trayMenu_,
                               TPM_RIGHTBUTTON | TPM_BOTTOMALIGN,
                               pt.x, pt.y, 0, msgHwnd_, nullptr);
                PostMessageW(msgHwnd_, WM_NULL, 0, 0);
            }
            return 0;

        case WM_COMMAND: {
            const int id = LOWORD(wp);
            if (id == kMenuPetCountRandom) {
                SetCritterCount(0);
                return 0;
            }
            if (id >= kMenuPetCount1 && id <= kMenuPetCount6) {
                SetCritterCount(id - kMenuPetCount1 + 1);
                return 0;
            }
            switch (id) {
                case kMenuQuit:          RequestQuit();                       break;
                case kMenuAutoStart:     SetAutoStart(!autoStart_);           break;
                case kMenuSceneGrass:    SetScene(Scene::Grass);              break;
                case kMenuSceneDesert:   SetScene(Scene::Desert);             break;
                case kMenuSceneWinter:   SetScene(Scene::Winter);             break;
                case kMenuSceneAutumn:   SetScene(Scene::Autumn);             break;
    case kMenuSceneOcean:    SetScene(Scene::Ocean);              break;
                case kMenuCritterNone:   SetCritter(CritterKind::None);       break;
                case kMenuCritterSheep:  SetCritter(CritterKind::Sheep);      break;
                case kMenuCritterCat:    SetCritter(CritterKind::Cat);        break;
                case kMenuCritterAll:    SetCritter(CritterKind::Bunny);      break;
            }
            return 0;
        }

        case WM_DISPLAYCHANGE:
        case WM_SETTINGCHANGE:
        case WM_DEVICECHANGE:
        case GrassWindow::kWmAppDisplayChanged:
            displayChangePending_ = true;
            return 0;

        case WM_POWERBROADCAST:
        case WM_WTSSESSION_CHANGE:
        {
            const RuntimeNotificationResult notification =
                runtimeNotifications_.Dispatch(msg, wp, lp);
            if (!notification.handled) break;
            runtimeStateDirty_ =
                runtimeStateDirty_ || notification.stateChanged;
            visibilityStateDirty_ =
                visibilityStateDirty_ || notification.visibilityChanged;
            ApplyPendingRuntimeChanges();
            return notification.result;
        }

        case kVisibilityChangedMessage:
            HandleVisibilityNotification();
            ApplyPendingRuntimeChanges();
            return 0;

        case WM_QUERYENDSESSION:
            HandleSessionEnding(true);
            return TRUE;

        case WM_ENDSESSION:
            HandleSessionEnding(wp != FALSE);
            return 0;

        case GrassWindow::kWmAppSessionEnding:
            HandleSessionEnding(wp != FALSE);
            return 0;

        case WM_CLOSE:
            // The smoke harness sends WM_CLOSE to the *grass* window, which
            // PostQuitMessages from its WndProc. Also handle it here for
            // robustness.
            RequestQuit();
            return 0;

        case WM_NCDESTROY:
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
            msgHwnd_ = nullptr;
            return DefWindowProcW(hwnd, msg, wp, lp);

        default:
            return DefWindowProcW(hwnd, msg, wp, lp);
    }

    return DefWindowProcW(hwnd, msg, wp, lp);
}

} // namespace desktopgrass
