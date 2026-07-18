// sway_tests.cpp
//
// Sway physics tests (architecture.md §6).

#include "TestHelpers.h"
#include "Sim.h"

#include <array>
#include <cmath>
#include <limits>

using namespace desktopgrass;

namespace {

constexpr double kPi = 3.14159265358979323846;

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

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(SwayTests)
{
public:
TEST_METHOD(SwayPhaseAdvancesLinearlyWithGlobalTime) {
    Blade b = make_blade(0.0, 1.0);
    update_blade_dynamics(b, 0.0, 0.016);
    const double leanT0 = b.effectiveLean;

    // After one full BASE_SWAY_SPEED period (6 sec) the lean returns to ~same.
    update_blade_dynamics(b, (2.0 * kPi) / BASE_SWAY_SPEED, 0.016);
    Assert::IsTrue(b.effectiveLean == Near(leanT0).margin(1e-9));
}

TEST_METHOD(SwayLeanStaysBoundedByBASEAMPLITUDEStiffness) {
    Blade b = make_blade(0.0, 1.0);
    double maxAbs = 0.0;
    // Sample one full period at fine granularity.
    for (double t = 0.0; t < (2.0 * kPi) / BASE_SWAY_SPEED; t += 0.001) {
        update_blade_dynamics(b, t, 0.001);
        maxAbs = std::max(maxAbs, std::fabs(b.effectiveLean));
    }
    Assert::IsTrue(maxAbs <= BASE_AMPLITUDE + 1e-9);
    Assert::IsTrue(maxAbs >= BASE_AMPLITUDE * 0.99);
}

TEST_METHOD(StiffnessScalesSwayAmplitude) {
    Blade soft = make_blade(0.0, 0.6);
    Blade hard = make_blade(0.0, 1.0);

    double softMax = 0.0, hardMax = 0.0;
    for (double t = 0.0; t < (2.0 * kPi) / BASE_SWAY_SPEED; t += 0.001) {
        update_blade_dynamics(soft, t, 0.001);
        update_blade_dynamics(hard, t, 0.001);
        softMax = std::max(softMax, std::fabs(soft.effectiveLean));
        hardMax = std::max(hardMax, std::fabs(hard.effectiveLean));
    }

    Assert::IsTrue(softMax <  hardMax);
    Assert::IsTrue(softMax == Near(hardMax * 0.6).margin(1e-3));
}

TEST_METHOD(SwayAmplitudeScaleMultipliesTheLean) {
    // At the same time/phase, ampScale=2.0 doubles the lean; ampScale=0 zeroes it.
    Blade base = make_blade(0.3, 1.0);
    Blade dbl  = make_blade(0.3, 1.0);
    Blade zero = make_blade(0.3, 1.0);
    const double t = 1.234;
    update_blade_dynamics(base, t, 0.016, 1.0, 1.0);
    update_blade_dynamics(dbl,  t, 0.016, 1.0, 2.0);
    update_blade_dynamics(zero, t, 0.016, 1.0, 0.0);
    Assert::IsTrue(dbl.effectiveLean  == Near(2.0 * base.effectiveLean).margin(1e-12));
    Assert::IsTrue(zero.effectiveLean == Near(0.0).margin(1e-12));
}

TEST_METHOD(SwaySpeedScaleStretchesThePhaseAdvance) {
    // speedScale=2.0 at time t equals the default at time 2t (pure phase scaling).
    Blade fast = make_blade(0.1, 1.0);
    Blade slow = make_blade(0.1, 1.0);
    const double t = 0.9;
    update_blade_dynamics(fast, t,       0.016, 2.0, 1.0);
    update_blade_dynamics(slow, 2.0 * t, 0.016, 1.0, 1.0);
    Assert::IsTrue(fast.effectiveLean == Near(slow.effectiveLean).margin(1e-12));
}

TEST_METHOD(SimTickAppliesTheSimSwayScalesToBlades) {
    // Proves the knobs are actually wired through the per-frame tick, not just
    // the standalone helper: a sim with swayAmpScale=0 produces zero base lean.
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim.swayAmpScale   = 0.0;
    sim.swaySpeedScale = 1.0;
    sim_tick(sim, 0.5, nullptr, 0);
    for (const Blade& b : sim.blades) {
        // No ambient gust fired (gustVelocity stays 0), so effectiveLean is pure
        // base lean, which ampScale=0 must flatten to 0.
        Assert::IsTrue(b.gustVelocity == Near(0.0).margin(1e-12));
        Assert::IsTrue(b.effectiveLean == Near(0.0).margin(1e-12));
    }
}

TEST_METHOD(SimTickMatchesStandaloneDynamicsAcrossBlades) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim.swayAmpScale = 1.7;
    sim.swaySpeedScale = 0.8;
    sim.nextAmbientGustTime = 1000.0;

    constexpr double dt = 0.037;
    constexpr std::size_t bladeCount = 4;
    Blade expected[bladeCount]{};
    for (std::size_t i = 0; i < bladeCount; ++i) {
        sim.blades[i].gustVelocity = 1.25 + static_cast<double>(i);
        expected[i] = sim.blades[i];
        update_blade_dynamics(
            expected[i], dt, dt,
            sim.swaySpeedScale, sim.swayAmpScale);
    }

    sim_tick(sim, dt, nullptr, 0);

    for (std::size_t i = 0; i < bladeCount; ++i) {
        Assert::IsTrue(
            sim.blades[i].gustVelocity
            == Near(expected[i].gustVelocity).margin(1e-12));
        Assert::IsTrue(
            sim.blades[i].effectiveLean
            == Near(expected[i].effectiveLean).margin(1e-12));
    }
}

TEST_METHOD(GeneratedBladesInitializeStablePhaseTerms) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);

    for (const Blade& blade : sim.blades) {
        Assert::AreEqual(blade.swayPhaseOffset, blade.cachedSwayPhaseOffset);
        Assert::IsTrue(
            blade.swayPhaseSin
            == Near(std::sin(blade.swayPhaseOffset)).margin(1e-15));
        Assert::IsTrue(
            blade.swayPhaseCos
            == Near(std::cos(blade.swayPhaseOffset)).margin(1e-15));
    }
}

TEST_METHOD(SimTickInitializesHandBuiltBladePhaseCache) {
    Sim sim{};
    sim.nextAmbientGustTime = std::numeric_limits<double>::infinity();
    sim.blades.push_back(make_blade(kPi / 3.0, 0.75));
    Blade expected = sim.blades.front();

    constexpr double dt = 0.025;
    update_blade_dynamics(expected, dt, dt);
    sim_tick(sim, dt, nullptr, 0);

    const Blade& actual = sim.blades.front();
    Assert::AreEqual(actual.swayPhaseOffset, actual.cachedSwayPhaseOffset);
    Assert::IsTrue(
        actual.effectiveLean == Near(expected.effectiveLean).margin(1e-12));
}

TEST_METHOD(SimTickRefreshesCacheAfterDirectPhaseAssignment) {
    Sim sim = sim_init(CANONICAL_TEST_SEED, 1920.0, DEFAULT_DENSITY);
    sim.nextAmbientGustTime = std::numeric_limits<double>::infinity();
    sim_tick(sim, 0.01, nullptr, 0);

    Blade& blade = sim.blades.front();
    const double oldCachedPhase = blade.cachedSwayPhaseOffset;
    blade.swayPhaseOffset = oldCachedPhase + kPi / 2.0;
    Blade expected = blade;

    constexpr double dt = 0.037;
    update_blade_dynamics(expected, sim.globalTime + dt, dt);
    sim_tick(sim, dt, nullptr, 0);

    Assert::AreEqual(blade.swayPhaseOffset, blade.cachedSwayPhaseOffset);
    Assert::IsTrue(
        blade.effectiveLean == Near(expected.effectiveLean).margin(1e-12));
}

TEST_METHOD(SimTickCachedSwayMatchesStandaloneAcrossInputs) {
    constexpr std::array<double, 6> phases{
        0.0, 0.1, kPi / 2.0, kPi, 2.0 * kPi - 1e-12, 17.25
    };
    constexpr std::array<double, 5> times{
        0.0, 1e-9, 0.25, 9.75, 12345.678
    };
    constexpr std::array<double, 3> speedScales{ 0.0, 0.8, 2.5 };
    constexpr std::array<double, 3> ampScales{ 0.0, 1.0, 1.7 };
    constexpr std::array<double, 3> dts{ 0.001, 0.037, 0.1 };

    for (double phase : phases) {
        for (double time : times) {
            for (double speedScale : speedScales) {
                for (double ampScale : ampScales) {
                    for (double dt : dts) {
                        Sim sim{};
                        sim.globalTime = time;
                        sim.swaySpeedScale = speedScale;
                        sim.swayAmpScale = ampScale;
                        sim.nextAmbientGustTime =
                            std::numeric_limits<double>::infinity();
                        sim.blades.push_back(make_blade(phase, 0.73));
                        sim.blades.front().gustVelocity = -2.25;

                        Blade expected = sim.blades.front();
                        update_blade_dynamics(
                            expected, time + dt, dt, speedScale, ampScale);
                        sim_tick(sim, dt, nullptr, 0);

                        Assert::IsTrue(
                            sim.blades.front().gustVelocity
                            == Near(expected.gustVelocity).margin(1e-12));
                        Assert::IsTrue(
                            sim.blades.front().effectiveLean
                            == Near(expected.effectiveLean).margin(1e-10));
                    }
                }
            }
        }
    }
}

TEST_METHOD(StandaloneDynamicsUsesDirectlyAssignedPhase) {
    Blade blade = make_blade(0.25, 1.0);
    update_blade_dynamics(blade, 0.5, 0.016);

    blade.swayPhaseOffset = 1.75;
    update_blade_dynamics(blade, 0.5, 0.016);

    const double expected =
        std::sin(1.75 + 0.5 * BASE_SWAY_SPEED) * BASE_AMPLITUDE;
    Assert::IsTrue(blade.effectiveLean == Near(expected).margin(1e-12));
}

TEST_METHOD(PhaseOffsetShiftsTheSineWave) {
    Blade a = make_blade(0.0,        1.0);
    Blade b = make_blade(kPi / 2.0, 1.0);

    update_blade_dynamics(a, 0.0, 0.001);
    update_blade_dynamics(b, 0.0, 0.001);

    // At t=0 with stiffness=1: a -> sin(0)*6 = 0; b -> sin(π/2)*6 = 6.
    Assert::IsTrue(a.effectiveLean == Near(0.0).margin(1e-9));
    Assert::IsTrue(b.effectiveLean == Near(BASE_AMPLITUDE).margin(1e-9));
}

TEST_METHOD(GustVelocityDecaysExponentiallyWithDt) {
    Blade b = make_blade(0.0, 1.0);
    b.gustVelocity = 10.0;

    // After 1 second, expect gustVelocity ≈ 10 * exp(-2.5).
    update_blade_dynamics(b, 0.0, 1.0);
    Assert::IsTrue(b.gustVelocity == Near(10.0 * std::exp(-DECAY_RATE * 1.0)).margin(1e-9));
}

TEST_METHOD(GustVelocityContributesToEffectiveLean) {
    Blade b = make_blade(0.0, 1.0);
    b.gustVelocity = 2.0;

    // tiny dt so decay is negligible
    update_blade_dynamics(b, 0.0, 1e-6);
    const double expectedFromGust = 2.0 * GUST_TO_LEAN_FACTOR;
    // At t=0 sway contribution is sin(0)=0; only gust remains.
    Assert::IsTrue(b.effectiveLean == Near(expectedFromGust).margin(1e-3));
}
};
}
