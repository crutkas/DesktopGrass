#include "TestHelpers.h"

#include "SingleInstance.h"

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <atomic>
#include <string>

namespace {

std::wstring UniqueApplicationId(const wchar_t* testName) {
    static std::atomic<unsigned long> counter{0};
    return std::wstring(L"DesktopGrass.SingleInstanceTest.")
        + std::to_wstring(GetCurrentProcessId()) + L"."
        + std::to_wstring(GetTickCount64()) + L"."
        + std::to_wstring(counter.fetch_add(1)) + L"."
        + testName;
}

} // anonymous namespace

using namespace desktopgrass;
using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(SingleInstanceTests)
{
public:
TEST_METHOD(FirstLaunchAcquiresGuard) {
    SingleInstanceGuard guard;

    const SingleInstanceResult result =
        guard.TryAcquire(UniqueApplicationId(L"first"));

    Assert::IsTrue(result == SingleInstanceResult::Acquired);
    Assert::IsTrue(guard.IsAcquired());
}

TEST_METHOD(SecondLaunchIsRejected) {
    const std::wstring applicationId =
        UniqueApplicationId(L"second");
    SingleInstanceGuard first;
    SingleInstanceGuard second;

    Assert::IsTrue(
        first.TryAcquire(applicationId)
        == SingleInstanceResult::Acquired);
    Assert::IsTrue(
        second.TryAcquire(applicationId)
        == SingleInstanceResult::AlreadyRunning);
    Assert::IsFalse(second.IsAcquired());
}

TEST_METHOD(GuardRecoversAfterOwnerExits) {
    const std::wstring applicationId =
        UniqueApplicationId(L"recovery");
    {
        SingleInstanceGuard first;
        Assert::IsTrue(
            first.TryAcquire(applicationId)
            == SingleInstanceResult::Acquired);
    }

    SingleInstanceGuard recovered;
    Assert::IsTrue(
        recovered.TryAcquire(applicationId)
        == SingleInstanceResult::Acquired);
}

TEST_METHOD(StandaloneIdentityIsArchitectureNeutralAndPerUser) {
    Assert::AreEqual(
        L"DesktopGrass.Standalone.{2E424867-2D28-44CF-950B-819F264E8019}",
        SingleInstanceGuard::kStandaloneApplicationId);

    const std::wstring firstUser =
        SingleInstanceGuard::BuildMutexNameForUser(
            SingleInstanceGuard::kStandaloneApplicationId,
            L"S-1-5-21-100");
    const std::wstring secondUser =
        SingleInstanceGuard::BuildMutexNameForUser(
            SingleInstanceGuard::kStandaloneApplicationId,
            L"S-1-5-21-200");

    Assert::AreEqual(
        L"Global\\DesktopGrass.Standalone."
        L"{2E424867-2D28-44CF-950B-819F264E8019}."
        L"SingleInstance.S-1-5-21-100",
        firstUser.c_str());
    Assert::IsTrue(firstUser != secondUser);
    Assert::IsTrue(
        firstUser.find(L"x64") == std::wstring::npos);
    Assert::IsTrue(
        firstUser.find(L"ARM64") == std::wstring::npos);
}
};
}
