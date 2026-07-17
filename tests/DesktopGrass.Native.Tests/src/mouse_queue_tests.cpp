#include "TestHelpers.h"

#include "MouseHook.h"

using namespace desktopgrass;

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
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
};
}
