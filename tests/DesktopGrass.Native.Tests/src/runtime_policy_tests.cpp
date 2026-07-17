#include "TestHelpers.h"

#include "RuntimePolicy.h"

using namespace desktopgrass::runtime;

namespace {

GlobalState active_ac() {
    GlobalState state;
    state.powerSource = PowerSource::Ac;
    state.displayState = DisplayState::On;
    state.sessionState = SessionState::Active;
    return state;
}

} // anonymous namespace

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace DesktopGrassNativeTests
{
TEST_CLASS(RuntimePolicyTests)
{
public:
TEST_METHOD(RuntimePolicyUsesTheConfiguredCadenceOnAC) {
    const Decision decision = Evaluate(active_ac(), {}, 24);

    Assert::IsTrue(decision == (Decision{PauseReason::None, 24, true, true}));
}

TEST_METHOD(RuntimePolicyAppliesConservativePowerCaps) {
    GlobalState state = active_ac();

    {
        state = active_ac();
        state.powerSource = PowerSource::Battery;
        Assert::IsTrue(Evaluate(state, {}, 24).targetFps == 12);
    }

    {
        state = active_ac();
        state.powerSource = PowerSource::ShortTerm;
        Assert::IsTrue(Evaluate(state, {}, 60).targetFps == 12);
    }

    {
        state = active_ac();
        state.saverEnabled = true;
        Assert::IsTrue(Evaluate(state, {}, 24).targetFps == 5);
    }

    {
        state = active_ac();
        state.displayState = DisplayState::Dimmed;
        Assert::IsTrue(Evaluate(state, {}, 24).targetFps == 5);
    }

    {
        state = active_ac();
        state.powerSource = PowerSource::Battery;
        Assert::IsTrue(Evaluate(state, {}, 8).targetFps == 8);
        state.saverEnabled = true;
        Assert::IsTrue(Evaluate(state, {}, 4).targetFps == 4);
    }

    {
        GlobalState unknown;
        Assert::IsTrue(Evaluate(unknown, {}, 24)
                == (Decision{PauseReason::None, 24, true, true}));
    }
}

TEST_METHOD(RuntimePolicyHardPausePrecedenceIsDeterministic) {
    GlobalState state = active_ac();
    SurfaceState surface{true, true};

    state.displayState = DisplayState::Off;
    state.sessionState = SessionState::Locked;
    state.suspended = true;
    Assert::IsTrue(Evaluate(state, surface, 24).pauseReason == PauseReason::Suspended);

    state.suspended = false;
    Assert::IsTrue(Evaluate(state, surface, 24).pauseReason == PauseReason::SessionLocked);

    state.sessionState = SessionState::Disconnected;
    Assert::IsTrue(Evaluate(state, surface, 24).pauseReason
            == PauseReason::SessionDisconnected);

    state.sessionState = SessionState::Active;
    Assert::IsTrue(Evaluate(state, surface, 24).pauseReason == PauseReason::DisplayOff);
}

TEST_METHOD(RuntimePolicySuppressesOnlyTheAffectedSurface) {
    const GlobalState state = active_ac();
    const Decision visible = Evaluate(state, {}, 24);
    const Decision fullscreen = Evaluate(state, SurfaceState{true, false}, 24);
    const Decision occluded = Evaluate(state, SurfaceState{false, true}, 24);

    Assert::IsTrue(visible.render);
    Assert::IsTrue(visible.show);

    Assert::IsFalse(fullscreen.render);
    Assert::IsFalse(fullscreen.show);
    Assert::IsTrue(fullscreen.pauseReason == PauseReason::Fullscreen);

    Assert::IsFalse(occluded.render);
    Assert::IsFalse(occluded.show);
    Assert::IsTrue(occluded.pauseReason == PauseReason::Occluded);
}

TEST_METHOD(RuntimeDecisionsAreIdempotent) {
    GlobalState state = active_ac();
    state.powerSource = PowerSource::Battery;
    const SurfaceState surface{false, true};

    Assert::IsTrue(Evaluate(state, surface, 24) == Evaluate(state, surface, 24));
}

TEST_METHOD(OnlyProcessWideReasonsAreGlobalPauses) {
    Assert::IsTrue(IsGlobalPause(PauseReason::Suspended));
    Assert::IsTrue(IsGlobalPause(PauseReason::SessionLocked));
    Assert::IsTrue(IsGlobalPause(PauseReason::SessionDisconnected));
    Assert::IsTrue(IsGlobalPause(PauseReason::DisplayOff));
    Assert::IsFalse(IsGlobalPause(PauseReason::Fullscreen));
    Assert::IsFalse(IsGlobalPause(PauseReason::Occluded));
    Assert::IsFalse(IsGlobalPause(PauseReason::None));
}
};
}
