// pacing_tests.cpp
//
// FramePacer behaviour tests.
//
// Goal: lock in the contract that on supported Windows (10 1803+) the pacer
// requests a high-resolution waitable timer and short waits do not become
// accidental no-ops.
// A regression that drops the high-res flag would silently re-introduce the
// ~48 ms dt_p95 pacing bug; IsHighResolution catches that directly.
//
// Windows 11 may coalesce timers for an occluded test host even when the timer
// was created with the high-resolution flag, so timing assertions must tolerate
// scheduler and power-policy jitter rather than treating elapsed time as proof
// of the handle type.

#include "TestHelpers.h"
#include "Pacing.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <thread>

using namespace desktopgrass;

namespace {

double qpc_now_sec() {
    LARGE_INTEGER c{}, f{};
    QueryPerformanceCounter(&c);
    QueryPerformanceFrequency(&f);
    return static_cast<double>(c.QuadPart) / static_cast<double>(f.QuadPart);
}

} // namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(PacingTests)
{
public:
TEST_METHOD(FramePacerCreatesAHighResolutionWaitableTimerOnSupportedWindows) {
    FramePacer pacer;
    // DesktopGrass requires Windows 10 1809+, which is well past the
    // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION minimum (Win 10 1803). Build/CI
    // environments below that floor are not supported.
    Assert::IsTrue(pacer.IsHighResolution());
}

TEST_METHOD(FramePacerZeroOrNegativeWaitReturnsEssentiallyImmediately) {
    FramePacer pacer;
    const double t0 = qpc_now_sec();
    pacer.WaitUntilNextFrame(0.0);
    pacer.WaitUntilNextFrame(-1.0);
    const double dt = qpc_now_sec() - t0;
    // Two no-op calls should complete in well under a millisecond, but allow
    // 5 ms of slop for loaded CI machines.
    Assert::IsTrue(dt < 0.005);
}

TEST_METHOD(FramePacerShortWaitsDoNotReturnImmediately) {
    FramePacer pacer;
    Assert::IsTrue(pacer.IsHighResolution());

    // Use the median so an isolated scheduling delay cannot fail the test.
    constexpr int    kIterations = 7;
    constexpr double kWaitSec    = 0.001;

    std::array<double, kIterations> waits{};
    for (int i = 0; i < kIterations; ++i) {
        const double t0 = qpc_now_sec();
        pacer.WaitUntilNextFrame(kWaitSec);
        waits[i] = qpc_now_sec() - t0;
    }
    std::sort(waits.begin(), waits.end());
    const double median = waits[kIterations / 2];

    Assert::IsTrue(median >= 0.0005);
}

TEST_METHOD(FramePacerPausedWaitWakesForQueuedMessages) {
    MSG msg{};
    while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
    }
    PeekMessageW(&msg, nullptr, 0, 0, PM_NOREMOVE);

    constexpr UINT kWakeMessage = WM_APP + 77;
    const DWORD waitingThreadId = GetCurrentThreadId();
    std::atomic<bool> posted{false};
    std::thread poster([&]() {
        Sleep(20);
        posted.store(
            PostThreadMessageW(
                waitingThreadId, kWakeMessage, 0, 0) != FALSE,
            std::memory_order_release);
    });

    FramePacer pacer;
    const double t0 = qpc_now_sec();
    pacer.WaitForMessage();
    const double dt = qpc_now_sec() - t0;
    poster.join();

    Assert::IsTrue(posted.load(std::memory_order_acquire));
    Assert::IsTrue(dt >= 0.010);
    Assert::IsTrue(dt < 0.500);
    Assert::IsTrue(PeekMessageW(
        &msg, nullptr, kWakeMessage, kWakeMessage, PM_REMOVE));
    Assert::IsTrue(msg.message == kWakeMessage);
}
};
}
