// sway_tests.cpp
//
// Sway physics tests (architecture.md §6).

#include "../third_party/catch2/catch.hpp"
#include "Sim.h"

#include <cmath>

using namespace desktopgrass;

namespace {

Blade make_blade(double phase, double stiffness) {
    Blade b{};
    b.baseX            = 0.0;
    b.height           = 20.0;
    b.thickness        = 1.5;
    b.hue              = 0;
    b.swayPhaseOffset  = phase;
    b.stiffness        = stiffness;
    b.cutHeight        = 1.0;
    b.gustVelocity     = 0.0;
    b.cutAnimStart     = -1.0;
    b.cutInitialHeight = 1.0;
    return b;
}

} // anonymous

TEST_CASE("sway phase advances linearly with globalTime", "[sway]") {
    Blade b = make_blade(0.0, 1.0);
    update_blade_dynamics(b, 0.0, 0.016);
    const double leanT0 = b.effectiveLean;

    // After one full BASE_SWAY_SPEED period (3 sec) the lean returns to ~same.
    update_blade_dynamics(b, 3.0, 0.016);
    REQUIRE(b.effectiveLean == Approx(leanT0).margin(1e-9));
}

TEST_CASE("sway lean stays bounded by BASE_AMPLITUDE * stiffness", "[sway]") {
    Blade b = make_blade(0.0, 1.0);
    double maxAbs = 0.0;
    // Sample one full period at fine granularity.
    for (double t = 0.0; t < 3.0; t += 0.001) {
        update_blade_dynamics(b, t, 0.001);
        maxAbs = std::max(maxAbs, std::fabs(b.effectiveLean));
    }
    REQUIRE(maxAbs <= BASE_AMPLITUDE + 1e-9);
    REQUIRE(maxAbs >= BASE_AMPLITUDE * 0.99);
}

TEST_CASE("stiffness scales sway amplitude", "[sway]") {
    Blade soft = make_blade(0.0, 0.6);
    Blade hard = make_blade(0.0, 1.0);

    double softMax = 0.0, hardMax = 0.0;
    for (double t = 0.0; t < 3.0; t += 0.001) {
        update_blade_dynamics(soft, t, 0.001);
        update_blade_dynamics(hard, t, 0.001);
        softMax = std::max(softMax, std::fabs(soft.effectiveLean));
        hardMax = std::max(hardMax, std::fabs(hard.effectiveLean));
    }

    REQUIRE(softMax <  hardMax);
    REQUIRE(softMax == Approx(hardMax * 0.6).margin(1e-3));
}

TEST_CASE("phase offset shifts the sine wave", "[sway]") {
    Blade a = make_blade(0.0,                          1.0);
    Blade b = make_blade(3.14159265358979323846 / 2.0, 1.0);

    update_blade_dynamics(a, 0.0, 0.001);
    update_blade_dynamics(b, 0.0, 0.001);

    // At t=0 with stiffness=1: a -> sin(0)*6 = 0; b -> sin(π/2)*6 = 6.
    REQUIRE(a.effectiveLean == Approx(0.0).margin(1e-9));
    REQUIRE(b.effectiveLean == Approx(BASE_AMPLITUDE).margin(1e-9));
}

TEST_CASE("gust velocity decays exponentially with dt", "[sway]") {
    Blade b = make_blade(0.0, 1.0);
    b.gustVelocity = 10.0;

    // After 1 second, expect gustVelocity ≈ 10 * exp(-2.5).
    update_blade_dynamics(b, 0.0, 1.0);
    REQUIRE(b.gustVelocity == Approx(10.0 * std::exp(-DECAY_RATE * 1.0)).margin(1e-9));
}

TEST_CASE("gust velocity contributes to effective lean", "[sway]") {
    Blade b = make_blade(0.0, 1.0);
    b.gustVelocity = 2.0;

    // tiny dt so decay is negligible
    update_blade_dynamics(b, 0.0, 1e-6);
    const double expectedFromGust = 2.0 * GUST_TO_LEAN_FACTOR;
    // At t=0 sway contribution is sin(0)=0; only gust remains.
    REQUIRE(b.effectiveLean == Approx(expectedFromGust).margin(1e-3));
}
