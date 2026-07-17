#include "../third_party/catch2/catch.hpp"

#include "RuntimePolicy.h"

#include <vector>

using desktopgrass::runtime::Covers;
using desktopgrass::runtime::IsFullyCovered;
using desktopgrass::runtime::Rect;

TEST_CASE("Fullscreen coverage uses the complete physical monitor", "[visibility]") {
    const Rect monitor{0, 0, 1920, 1080};

    REQUIRE(Covers(Rect{0, 0, 1920, 1080}, monitor));
    REQUIRE_FALSE(Covers(Rect{0, 0, 1920, 1040}, monitor));
    REQUIRE_FALSE(Covers(Rect{0, 40, 1920, 1080}, monitor));
}

TEST_CASE("A single opaque rectangle can fully cover a grass strip", "[visibility]") {
    const Rect strip{0, 970, 1920, 1080};

    REQUIRE(IsFullyCovered(strip, {Rect{-10, 900, 1930, 1090}}));
    REQUIRE_FALSE(IsFullyCovered(strip, {Rect{0, 970, 1919, 1080}}));
}

TEST_CASE("Multiple opaque rectangles can jointly cover a surface", "[visibility]") {
    const Rect strip{0, 970, 1920, 1080};

    REQUIRE(IsFullyCovered(strip, {
        Rect{0, 970, 960, 1080},
        Rect{960, 970, 1920, 1080},
    }));

    REQUIRE_FALSE(IsFullyCovered(strip, {
        Rect{0, 970, 959, 1080},
        Rect{960, 970, 1920, 1080},
    }));
}

TEST_CASE("Partial and off-surface rectangles do not report full occlusion",
          "[visibility]") {
    const Rect strip{100, 500, 900, 600};

    REQUIRE_FALSE(IsFullyCovered(strip, {
        Rect{0, 0, 1000, 550},
        Rect{-100, 700, 1200, 900},
    }));
    REQUIRE_FALSE(IsFullyCovered(strip, {
        Rect{-100, -100, 0, 0},
        Rect{1000, 1000, 1100, 1100},
    }));
}

TEST_CASE("Occlusion remains independent across monitors", "[visibility]") {
    const Rect leftStrip{-1920, 970, 0, 1080};
    const Rect rightStrip{0, 970, 1920, 1080};
    const std::vector<Rect> rightOnly{Rect{0, 900, 1920, 1080}};

    REQUIRE_FALSE(IsFullyCovered(leftStrip, rightOnly));
    REQUIRE(IsFullyCovered(rightStrip, rightOnly));
}

TEST_CASE("Invalid rectangles never cause suppression", "[visibility]") {
    REQUIRE_FALSE(IsFullyCovered(Rect{0, 0, 0, 100}, {Rect{0, 0, 100, 100}}));
    REQUIRE_FALSE(IsFullyCovered(Rect{0, 0, 100, 100}, {
        Rect{10, 10, 10, 50},
        Rect{20, 30, 40, 30},
    }));
}
