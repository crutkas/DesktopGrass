// main.cpp
//
// Entry point: set up DPI awareness, COM, the App, run the message loop.

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <combaseapi.h>

#include "App.h"

int APIENTRY wWinMain(HINSTANCE hInst, HINSTANCE, LPWSTR, int) {
    // Per-Monitor V2 DPI awareness. Also declared in the manifest so OSes that
    // honour the manifest pick it up before WinMain runs.
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) {
        return -1;
    }

    int exitCode = 0;
    {
        desktopgrass::App app;
        if (!app.Initialize(hInst)) {
            CoUninitialize();
            return -2;
        }
        exitCode = app.Run();
    }

    CoUninitialize();
    return exitCode;
}
