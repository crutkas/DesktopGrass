#include "../third_party/catch2/catch.hpp"

#include "MouseHook.h"

using namespace desktopgrass;

TEST_CASE("Mouse queue clears events collected before a pause", "[runtime][input]") {
    MouseEventQueue queue;
    const RawMouseEvent stale{
        EventType::Move, 1.0, 100, 200,
    };
    const RawMouseEvent resumed{
        EventType::Click, 2.0, 300, 400,
    };

    REQUIRE(queue.push(stale));
    queue.clear();

    RawMouseEvent drained[2]{};
    REQUIRE(queue.drain(drained, 2) == 0);

    REQUIRE(queue.push(resumed));
    REQUIRE(queue.drain(drained, 2) == 1);
    REQUIRE(drained[0].type == EventType::Click);
    REQUIRE(drained[0].screenX == 300);
    REQUIRE(drained[0].screenY == 400);
}
