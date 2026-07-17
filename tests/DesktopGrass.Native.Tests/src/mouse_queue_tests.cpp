#include "TestHelpers.h"

#include "MouseHook.h"

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
