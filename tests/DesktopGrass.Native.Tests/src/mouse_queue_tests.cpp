#include "TestHelpers.h"

#include "MouseHook.h"

#include <vector>

using namespace desktopgrass;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
namespace {

int g_installCalls = 0;
int g_uninstallCalls = 0;

HHOOK install_success(HOOKPROC) noexcept {
    ++g_installCalls;
    return reinterpret_cast<HHOOK>(1);
}

HHOOK install_failure(HOOKPROC) noexcept {
    ++g_installCalls;
    return nullptr;
}

BOOL uninstall_success(HHOOK) noexcept {
    ++g_uninstallCalls;
    return TRUE;
}

MouseHookPlatform platform_with(
    HHOOK (*install)(HOOKPROC) noexcept) noexcept {
    return MouseHookPlatform{ install, uninstall_success };
}

void reset_hook_calls() noexcept {
    g_installCalls = 0;
    g_uninstallCalls = 0;
}

struct AppliedMouseEvent {
    std::size_t windowIndex;
    InputEvent event;
};

InputEvent move(double x, double y, double time) {
    return InputEvent{ EventType::Move, x, y, time };
}

InputEvent click(double x, double y, double time) {
    return InputEvent{ EventType::Click, x, y, time };
}

void assert_event(
    const AppliedMouseEvent& actual,
    std::size_t windowIndex,
    EventType type,
    double x,
    double y,
    double time) {
    Assert::IsTrue(actual.windowIndex == windowIndex);
    Assert::IsTrue(actual.event.type == type);
    Assert::IsTrue(actual.event.x == x);
    Assert::IsTrue(actual.event.y == y);
    Assert::IsTrue(actual.event.time == time);
}

} // namespace

TEST_CLASS(MouseQueueTests)
{
public:
TEST_METHOD(MouseQueueClearsEventsCollectedBeforeAPause) {
    MouseEventQueue queue;
    const RawMouseEvent stale{
        EventType::Move, 1.0, 100, 200,
    };
    const RawMouseEvent resumed{
        EventType::Click, 2.0, 300, 400,
    };

    Assert::IsTrue(queue.push(stale));
    queue.clear();

    RawMouseEvent drained[2]{};
    Assert::IsTrue(queue.drain(drained, 2) == 0);

    Assert::IsTrue(queue.push(resumed));
    Assert::IsTrue(queue.drain(drained, 2) == 1);
    Assert::IsTrue(drained[0].type == EventType::Click);
    Assert::IsTrue(drained[0].screenX == 300);
    Assert::IsTrue(drained[0].screenY == 400);
}

TEST_METHOD(FrameKeepsOnlyTheLatestMoveForEachWindow) {
    FrameMouseEventCoalescer coalescer(2);
    std::vector<AppliedMouseEvent> applied;
    auto apply = [&applied](
        std::size_t windowIndex,
        const InputEvent& event) {
            applied.push_back({ windowIndex, event });
        };

    coalescer.Push(0, move(10.0, -100.0, 1.0), apply);
    coalescer.Push(1, move(20.0, 200.0, 2.0), apply);
    coalescer.Push(0, move(30.0, 300.0, 3.0), apply);
    coalescer.Push(1, move(40.0, -400.0, 4.0), apply);

    Assert::IsTrue(applied.empty());
    coalescer.FlushAll(apply);

    Assert::IsTrue(applied.size() == 2);
    assert_event(applied[0], 0, EventType::Move, 30.0, 300.0, 3.0);
    assert_event(applied[1], 1, EventType::Move, 40.0, -400.0, 4.0);
}

TEST_METHOD(ClickFlushesTheLatestMoveBeforeIt) {
    FrameMouseEventCoalescer coalescer(1);
    std::vector<AppliedMouseEvent> applied;
    auto apply = [&applied](
        std::size_t windowIndex,
        const InputEvent& event) {
            applied.push_back({ windowIndex, event });
        };

    coalescer.Push(0, move(10.0, 100.0, 1.0), apply);
    coalescer.Push(0, move(20.0, 200.0, 2.0), apply);
    coalescer.Push(0, click(20.0, 200.0, 2.1), apply);
    coalescer.Push(0, move(30.0, 300.0, 3.0), apply);
    coalescer.FlushAll(apply);

    Assert::IsTrue(applied.size() == 3);
    assert_event(applied[0], 0, EventType::Move, 20.0, 200.0, 2.0);
    assert_event(applied[1], 0, EventType::Click, 20.0, 200.0, 2.1);
    assert_event(applied[2], 0, EventType::Move, 30.0, 300.0, 3.0);
}

TEST_METHOD(ClickFlushesOnlyItsOwnWindow) {
    FrameMouseEventCoalescer coalescer(2);
    std::vector<AppliedMouseEvent> applied;
    auto apply = [&applied](
        std::size_t windowIndex,
        const InputEvent& event) {
            applied.push_back({ windowIndex, event });
        };

    coalescer.Push(0, move(10.0, 100.0, 1.0), apply);
    coalescer.Push(1, move(20.0, 200.0, 2.0), apply);
    coalescer.Push(1, click(20.0, 200.0, 2.1), apply);

    Assert::IsTrue(applied.size() == 2);
    assert_event(applied[0], 1, EventType::Move, 20.0, 200.0, 2.0);
    assert_event(applied[1], 1, EventType::Click, 20.0, 200.0, 2.1);

    coalescer.FlushAll(apply);
    Assert::IsTrue(applied.size() == 3);
    assert_event(applied[2], 0, EventType::Move, 10.0, 100.0, 1.0);
}

TEST_METHOD(CoalescingSpansQueueDrainBatches) {
    FrameMouseEventCoalescer coalescer(1);
    std::vector<AppliedMouseEvent> applied;
    auto apply = [&applied](
        std::size_t windowIndex,
        const InputEvent& event) {
            applied.push_back({ windowIndex, event });
        };

    for (int i = 0; i < 256; ++i) {
        coalescer.Push(
            0,
            move(static_cast<double>(i), 100.0, i / 1000.0),
            apply);
    }
    Assert::IsTrue(applied.empty());

    for (int i = 256; i < 300; ++i) {
        coalescer.Push(
            0,
            move(static_cast<double>(i), 100.0, i / 1000.0),
            apply);
    }
    coalescer.FlushAll(apply);

    Assert::IsTrue(applied.size() == 1);
    assert_event(applied[0], 0, EventType::Move, 299.0, 100.0, 0.299);
}

TEST_METHOD(MouseHookIsRemovedDuringNormalShutdown) {
    reset_hook_calls();
    MouseEventQueue queue;
    MouseHookRegistration hook(platform_with(install_success));

    Assert::IsTrue(hook.Install(&queue));
    Assert::IsTrue(hook.IsInstalled());

    hook.Reset();

    Assert::IsFalse(hook.IsInstalled());
    Assert::IsTrue(g_installCalls == 1);
    Assert::IsTrue(g_uninstallCalls == 1);
}

TEST_METHOD(MouseHookIsRemovedWhenStartupFailsAfterInstallation) {
    reset_hook_calls();
    MouseEventQueue queue;

    {
        MouseHookRegistration hook(platform_with(install_success));
        Assert::IsTrue(hook.Install(&queue));
        // Leaving startup scope models a later initialization step failing.
    }

    Assert::IsTrue(g_installCalls == 1);
    Assert::IsTrue(g_uninstallCalls == 1);
}

TEST_METHOD(MouseHookInstallFailureReleasesProcessRegistration) {
    reset_hook_calls();
    MouseEventQueue queue;

    MouseHookRegistration failed(platform_with(install_failure));
    Assert::IsFalse(failed.Install(&queue));
    Assert::IsFalse(failed.IsInstalled());

    MouseHookRegistration retry(platform_with(install_success));
    Assert::IsTrue(retry.Install(&queue));
    retry.Reset();

    Assert::IsTrue(g_installCalls == 2);
    Assert::IsTrue(g_uninstallCalls == 1);
}

TEST_METHOD(FailedSecondRegistrationCannotDetachTheActiveHook) {
    reset_hook_calls();
    MouseEventQueue queue;
    MouseHookRegistration active(platform_with(install_success));
    MouseHookRegistration rejected(platform_with(install_success));

    Assert::IsTrue(active.Install(&queue));
    Assert::IsFalse(rejected.Install(&queue));

    rejected.Reset();
    Assert::IsTrue(active.IsInstalled());

    active.Reset();
    Assert::IsTrue(g_installCalls == 1);
    Assert::IsTrue(g_uninstallCalls == 1);
}
};
}
