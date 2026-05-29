// Constants.h
//
// Single source of truth for all simulation constants.
// Mirrors docs/architecture.md §11. If a constant changes here it MUST change
// in the spec first.

#pragma once

#include <cstdint>

namespace desktopgrass {

// Geometry --------------------------------------------------------------------
constexpr double STRIP_HEIGHT          = 80.0;
constexpr double HEADROOM              = 30.0;

// Procedural generation -------------------------------------------------------
constexpr double DEFAULT_DENSITY        = 1.5;
constexpr double BLADE_SPACING_MIN     = 4.0;
constexpr double BLADE_SPACING_MAX     = 8.0;
constexpr double BLADE_HEIGHT_MIN      = 8.0;
constexpr double BLADE_HEIGHT_MAX      = 40.0;
constexpr double BLADE_THICKNESS_MIN   = 1.0;
constexpr double BLADE_THICKNESS_MAX   = 2.5;
constexpr double STIFFNESS_MIN         = 0.6;
constexpr double STIFFNESS_MAX         = 1.0;
constexpr int    PALETTE_SIZE          = 6;

// Sway / gust physics ---------------------------------------------------------
// π / 3 → 6-second sway period.
constexpr double BASE_SWAY_SPEED       = 1.0471975511965976;
constexpr double BASE_AMPLITUDE        = 6.0;
constexpr double DECAY_RATE            = 2.5;
constexpr double GUST_TO_LEAN_FACTOR   = 1.5;
constexpr double MAX_CURSOR_SPEED      = 4000.0;
constexpr double IMPULSE_SCALE         = 0.003;
constexpr double GUST_RADIUS           = 150.0;
constexpr double CURSOR_REINIT_GAP_SEC = 0.25;

// Cut ------------------------------------------------------------------------
constexpr double CUT_RADIUS            = 30.0;
constexpr double CUT_DURATION_SEC      = 0.2;
constexpr double CUT_STUMP_THRESHOLD   = 0.05;
constexpr double STUMP_HEIGHT          = 2.0;
constexpr double CTRL_OFFSET_FACTOR    = 0.6;

// Regrowth -------------------------------------------------------------------
// After a blade's cut animation finishes, it waits `regrowDelay` seconds (a
// per-blade jittered value in [MIN, MAX]) and then grows back from cutHeight=0
// to cutHeight=1 linearly over `regrowDuration` seconds (also per-blade
// jittered). The jitter is sampled from a second xorshift64 stream seeded with
// `seed XOR REGROW_PRNG_SALT` so it does NOT perturb blade positions/heights
// drawn from the main stream — conformance with seed 0x6B6173746F is preserved.
constexpr double REGROW_DELAY_MIN      = 30.0;
constexpr double REGROW_DELAY_MAX      = 90.0;
constexpr double REGROW_DURATION_MIN   = 2.0;
constexpr double REGROW_DURATION_MAX   = 4.0;
constexpr uint64_t REGROW_PRNG_SALT    = 0xDEADBEEFCAFEBABEull;

// Tests -----------------------------------------------------------------------
constexpr uint64_t CANONICAL_TEST_SEED = 0x6B6173746Full;

// ARGB palette. Alpha is always 0xFF; window-level transparency is at the
// compositor.
constexpr uint32_t PALETTE[PALETTE_SIZE] = {
    0xFF2C5E1Au,
    0xFF3A7A24u,
    0xFF4C9A2Eu,
    0xFF66B845u,
    0xFF7AC957u,
    0xFF8FD96Au,
};

} // namespace desktopgrass
