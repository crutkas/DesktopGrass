#include "catch.hpp"

#include "Constants.h"

#include <cmath>
#include <cstdint>

using namespace desktopgrass;

namespace {
struct ExpectedPhase {
    float hour;
    uint8_t r;
    uint8_t g;
    uint8_t b;
    uint8_t alpha;
};

bool within_one(uint8_t actual, int expected) {
    return std::abs(static_cast<int>(actual) - expected) <= 1;
}

void require_tint(double hour, uint8_t er, uint8_t eg, uint8_t eb, uint8_t ea) {
    uint8_t r = 0;
    uint8_t g = 0;
    uint8_t b = 0;
    uint8_t a = 0;
    compute_day_tint(hour, r, g, b, a);
    REQUIRE(r == er);
    REQUIRE(g == eg);
    REQUIRE(b == eb);
    REQUIRE(a == ea);
}
}

TEST_CASE("day tint phases are pinned to the spec", "[day-tint]") {
    const ExpectedPhase expected[] = {
        {  0.0f,  40,  50,  90, 36 },
        {  4.0f,  60,  70, 110, 32 },
        {  6.0f, 255, 180, 140, 28 },
        {  8.0f, 255, 220, 160, 16 },
        { 10.0f, 255, 255, 255,  0 },
        { 17.0f, 240, 170, 110, 22 },
        { 19.0f, 220, 110,  90, 30 },
        { 20.0f,  90,  80, 130, 28 },
        { 22.0f,  40,  50,  90, 36 },
    };

    REQUIRE(DAYTINT_PHASE_COUNT == static_cast<int>(sizeof(expected) / sizeof(expected[0])));
    for (int i = 0; i < DAYTINT_PHASE_COUNT; ++i) {
        REQUIRE(DAYTINT_PHASES[i].startHour == Approx(expected[i].hour));
        REQUIRE(DAYTINT_PHASES[i].r == expected[i].r);
        REQUIRE(DAYTINT_PHASES[i].g == expected[i].g);
        REQUIRE(DAYTINT_PHASES[i].b == expected[i].b);
        REQUIRE(DAYTINT_PHASES[i].alpha == expected[i].alpha);
    }
}

TEST_CASE("day tint is transparent at noon", "[day-tint]") {
    uint8_t r = 0;
    uint8_t g = 0;
    uint8_t b = 0;
    uint8_t a = 255;
    compute_day_tint(12.0, r, g, b, a);
    REQUIRE(a == 0);
}

TEST_CASE("day tint starts night exactly at 22", "[day-tint]") {
    require_tint(22.0, 40, 50, 90, 36);
}

TEST_CASE("day tint interpolates sunrise to morning at 7", "[day-tint]") {
    uint8_t r = 0;
    uint8_t g = 0;
    uint8_t b = 0;
    uint8_t a = 0;
    compute_day_tint(7.0, r, g, b, a);

    REQUIRE(within_one(r, 255));
    REQUIRE(within_one(g, 200));
    REQUIRE(within_one(b, 150));
    REQUIRE(within_one(a, 22));
}

TEST_CASE("day tint remains night at 23", "[day-tint]") {
    require_tint(23.0, 40, 50, 90, 36);
}

TEST_CASE("day tint wraps 0 and 24 equivalently", "[day-tint]") {
    uint8_t r0 = 0, g0 = 0, b0 = 0, a0 = 0;
    uint8_t r24 = 0, g24 = 0, b24 = 0, a24 = 0;
    uint8_t rw = 0, gw = 0, bw = 0, aw = 0;

    compute_day_tint(0.0, r0, g0, b0, a0);
    compute_day_tint(24.0, r24, g24, b24, a24);
    compute_day_tint(-0.0001 + 24.0, rw, gw, bw, aw);

    REQUIRE(r0 == r24);
    REQUIRE(g0 == g24);
    REQUIRE(b0 == b24);
    REQUIRE(a0 == a24);
    REQUIRE(r0 == rw);
    REQUIRE(g0 == gw);
    REQUIRE(b0 == bw);
    REQUIRE(a0 == aw);
}

TEST_CASE("day tint alpha never exceeds maximum", "[day-tint]") {
    for (int i = 0; i <= 239; ++i) {
        uint8_t r = 0;
        uint8_t g = 0;
        uint8_t b = 0;
        uint8_t a = 0;
        compute_day_tint(static_cast<double>(i) / 10.0, r, g, b, a);
        REQUIRE(a <= DAYTINT_MAX_ALPHA);
    }

    uint8_t r = 0;
    uint8_t g = 0;
    uint8_t b = 0;
    uint8_t a = 0;
    compute_day_tint(23.99, r, g, b, a);
    REQUIRE(a <= DAYTINT_MAX_ALPHA);
}

TEST_CASE("day tint boundary at 8 is morning", "[day-tint]") {
    require_tint(8.0, 255, 220, 160, 16);
}
