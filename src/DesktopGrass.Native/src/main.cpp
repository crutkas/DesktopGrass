// main.cpp
//
// Entry point: set up DPI awareness, COM, the App, run the message loop.
//
// When the command line contains `--benchmark`, dispatch into the benchmark
// runner instead of the normal App lifecycle. Production tray/persistence
// code remains untouched in that path.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <combaseapi.h>
#include <shellapi.h>

#include <cwchar>

#include "App.h"
#include "Benchmark.h"
#include "SingleInstance.h"

namespace {

bool HasBenchmarkFlag(int argc, wchar_t** argv) {
    for (int i = 1; i < argc; ++i) {
        if (argv[i] && _wcsicmp(argv[i], L"--benchmark") == 0) return true;
    }
    return false;
}

bool EnsurePerMonitorV2DpiAwareness() {
    if (!SetProcessDpiAwarenessContext(
            DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)
        && GetLastError() != ERROR_ACCESS_DENIED) {
        return false;
    }

    return AreDpiAwarenessContextsEqual(
        GetThreadDpiAwarenessContext(),
        DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2) == TRUE;
}

} // anonymous

int APIENTRY wWinMain(HINSTANCE hInst, HINSTANCE, LPWSTR, int) {
    // Per-Monitor V2 DPI awareness. Also declared in the manifest so OSes that
    // honour the manifest pick it up before WinMain runs.
    if (!EnsurePerMonitorV2DpiAwareness()) {
        OutputDebugStringA(
            "[DesktopGrass] Per-Monitor V2 DPI awareness is unavailable\n");
        return -4;
    }

    int argc = 0;
    wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);

    const bool benchmark = argv && HasBenchmarkFlag(argc, argv);
    desktopgrass::SingleInstanceGuard singleInstance;
    if (!benchmark) {
        const desktopgrass::SingleInstanceResult result =
            singleInstance.TryAcquire();
        if (result == desktopgrass::SingleInstanceResult::AlreadyRunning) {
            MessageBoxW(
                nullptr,
                L"DesktopGrass is already running. Use its system tray icon "
                L"to manage or quit it.",
                L"DesktopGrass",
                MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND);
            if (argv) LocalFree(argv);
            return 0;
        }
        if (result == desktopgrass::SingleInstanceResult::Failed) {
            OutputDebugStringA(
                "[DesktopGrass] unable to acquire the single-instance guard\n");
            MessageBoxW(
                nullptr,
                L"DesktopGrass could not verify that another instance is not "
                L"already running.",
                L"DesktopGrass",
                MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
            if (argv) LocalFree(argv);
            return -5;
        }
    }

    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) {
        if (argv) LocalFree(argv);
        return -1;
    }

    int exitCode = 0;
    if (benchmark) {
        desktopgrass::benchmark::Options opts;
        if (!desktopgrass::benchmark::ParseOptions(argc, argv, opts)) {
            if (argv) LocalFree(argv);
            CoUninitialize();
            return -3;
        }
        exitCode = desktopgrass::benchmark::Run(hInst, opts);
    } else {
        desktopgrass::App app;
        if (!app.Initialize(hInst)) {
            if (argv) LocalFree(argv);
            CoUninitialize();
            return -2;
        }
        exitCode = app.Run();
    }

    if (argv) LocalFree(argv);
    CoUninitialize();
    return exitCode;
}
