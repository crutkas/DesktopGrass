#include "TestHelpers.h"

#include "TrayIcon.h"

#include <string>

using namespace desktopgrass::tray;
using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(TrayIconTests)
{
public:
TEST_METHOD(TrayMenuSupportsPointerAndKeyboardActivation) {
    Assert::IsTrue(IsMenuActivation(WM_RBUTTONUP));
    Assert::IsTrue(IsMenuActivation(WM_CONTEXTMENU));
    Assert::IsTrue(IsMenuActivation(NIN_SELECT));
    Assert::IsTrue(IsMenuActivation(NIN_KEYSELECT));
    Assert::IsFalse(IsMenuActivation(WM_MOUSEMOVE));
}

TEST_METHOD(TrayCallbackAnchorPreservesSignedVirtualScreenCoordinates) {
    const WPARAM coordinates = MAKELPARAM(
        static_cast<WORD>(static_cast<short>(-320)),
        static_cast<WORD>(static_cast<short>(1440)));

    const POINT anchor = CallbackAnchor(coordinates);

    Assert::AreEqual(-320L, anchor.x);
    Assert::AreEqual(1440L, anchor.y);
}

TEST_METHOD(KeyboardAndContextActivationUseTheIconBounds) {
    Assert::IsTrue(UsesIconRectAnchor(WM_CONTEXTMENU));
    Assert::IsTrue(UsesIconRectAnchor(NIN_KEYSELECT));
    Assert::IsFalse(UsesIconRectAnchor(NIN_SELECT));
    Assert::IsFalse(UsesIconRectAnchor(WM_RBUTTONUP));
}

TEST_METHOD(TrayAccessibleNameIdentifiesTheControlsEntryPoint) {
    Assert::AreEqual(
        std::wstring(L"Desktop Grass controls"),
        std::wstring(kAccessibleName));
}
};
}
