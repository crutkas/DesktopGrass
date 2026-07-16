#include "../third_party/catch2/catch.hpp"

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

TEST_CASE("Runtime policy uses the configured cadence on AC", "[runtime]") {
    const Decision decision = Evaluate(active_ac(), {}, 24);

    REQUIRE(decision == (Decision{PauseReason::None, 24, true, true}));
}

TEST_CASE("Runtime policy applies conservative power caps", "[runtime]") {
    GlobalState state = active_ac();

    SECTION("battery is capped at 12 fps") {
        state.powerSource = PowerSource::Battery;
        REQUIRE(Evaluate(state, {}, 24).targetFps == 12);
    }

    SECTION("short-term power is treated like battery") {
        state.powerSource = PowerSource::ShortTerm;
        REQUIRE(Evaluate(state, {}, 60).targetFps == 12);
    }

    SECTION("Battery Saver is capped at 5 fps on AC") {
        state.saverEnabled = true;
        REQUIRE(Evaluate(state, {}, 24).targetFps == 5);
    }

    SECTION("dimmed display is capped at 5 fps") {
        state.displayState = DisplayState::Dimmed;
        REQUIRE(Evaluate(state, {}, 24).targetFps == 5);
    }

    SECTION("a lower configured cadence is preserved") {
        state.powerSource = PowerSource::Battery;
        REQUIRE(Evaluate(state, {}, 8).targetFps == 8);
        state.saverEnabled = true;
        REQUIRE(Evaluate(state, {}, 4).targetFps == 4);
    }

    SECTION("unknown startup values fail open") {
        GlobalState unknown;
        REQUIRE(Evaluate(unknown, {}, 24)
                == (Decision{PauseReason::None, 24, true, true}));
    }
}

TEST_CASE("Runtime policy hard-pause precedence is deterministic", "[runtime]") {
    GlobalState state = active_ac();
    SurfaceState surface{true, true};

    state.displayState = DisplayState::Off;
    state.sessionState = SessionState::Locked;
    state.suspended = true;
    REQUIRE(Evaluate(state, surface, 24).pauseReason == PauseReason::Suspended);

    state.suspended = false;
    REQUIRE(Evaluate(state, surface, 24).pauseReason == PauseReason::SessionLocked);

    state.sessionState = SessionState::Disconnected;
    REQUIRE(Evaluate(state, surface, 24).pauseReason
            == PauseReason::SessionDisconnected);

    state.sessionState = SessionState::Active;
    REQUIRE(Evaluate(state, surface, 24).pauseReason == PauseReason::DisplayOff);
}

TEST_CASE("Runtime policy suppresses only the affected surface", "[runtime]") {
    const GlobalState state = active_ac();
    const Decision visible = Evaluate(state, {}, 24);
    const Decision fullscreen = Evaluate(state, SurfaceState{true, false}, 24);
    const Decision occluded = Evaluate(state, SurfaceState{false, true}, 24);

    REQUIRE(visible.render);
    REQUIRE(visible.show);

    REQUIRE_FALSE(fullscreen.render);
    REQUIRE_FALSE(fullscreen.show);
    REQUIRE(fullscreen.pauseReason == PauseReason::Fullscreen);

    REQUIRE_FALSE(occluded.render);
    REQUIRE_FALSE(occluded.show);
    REQUIRE(occluded.pauseReason == PauseReason::Occluded);
}

TEST_CASE("Runtime decisions are idempotent", "[runtime]") {
    GlobalState state = active_ac();
    state.powerSource = PowerSource::Battery;
    const SurfaceState surface{false, true};

    REQUIRE(Evaluate(state, surface, 24) == Evaluate(state, surface, 24));
}

TEST_CASE("Only process-wide reasons are global pauses", "[runtime]") {
    REQUIRE(IsGlobalPause(PauseReason::Suspended));
    REQUIRE(IsGlobalPause(PauseReason::SessionLocked));
    REQUIRE(IsGlobalPause(PauseReason::SessionDisconnected));
    REQUIRE(IsGlobalPause(PauseReason::DisplayOff));
    REQUIRE_FALSE(IsGlobalPause(PauseReason::Fullscreen));
    REQUIRE_FALSE(IsGlobalPause(PauseReason::Occluded));
    REQUIRE_FALSE(IsGlobalPause(PauseReason::None));
}
