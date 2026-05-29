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
constexpr double DEFAULT_DENSITY        = 2.25;
constexpr double BLADE_SPACING_MIN     = 4.0;
constexpr double BLADE_SPACING_MAX     = 8.0;
constexpr double BLADE_HEIGHT_MIN      = 6.0;
constexpr double BLADE_HEIGHT_MAX      = 30.0;
constexpr double BLADE_THICKNESS_MIN   = 1.0;
constexpr double BLADE_THICKNESS_MAX   = 2.5;
constexpr double STIFFNESS_MIN         = 0.6;
constexpr double STIFFNESS_MAX         = 1.0;
constexpr int    PALETTE_SIZE          = 6;

// Sway / gust physics ---------------------------------------------------------
// π / 3 → 6-second sway period.
constexpr double BASE_SWAY_SPEED       = 1.0471975511965976;
constexpr double BASE_AMPLITUDE        = 3.3;
constexpr double DECAY_RATE            = 2.5;
constexpr double GUST_TO_LEAN_FACTOR   = 0.75;
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
// fraction of blade length that the tip may horizontally displace; clamps gust impulses so the blade never folds completely flat.
constexpr double MAX_LEAN_FRACTION     = 0.95;

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

// Flowers (§4, §5, §7). Sampled from a third independent PRNG stream
// (seed XOR FLOWER_PRNG_SALT) so the main stream stays bit-identical
// to the pre-flower implementation. 4% of blades become flowers; each
// flower has a head color (6-entry palette), head radius, and a stem
// height bonus of 1.2x–1.5x. Non-flower blades carry heightBonus=1.0.
constexpr double   FLOWER_PROBABILITY        = 0.04;
constexpr double   FLOWER_HEIGHT_BONUS_MIN   = 1.2;
constexpr double   FLOWER_HEIGHT_BONUS_MAX   = 1.5;
constexpr double   FLOWER_HEAD_RADIUS_MIN    = 1.8;   // DIP
constexpr double   FLOWER_HEAD_RADIUS_MAX    = 3.0;   // DIP
constexpr int      FLOWER_PALETTE_SIZE       = 6;
constexpr uint64_t FLOWER_PRNG_SALT          = 0xC0FFEEFACE0FFE5ull;

constexpr uint32_t FLOWER_PALETTE[FLOWER_PALETTE_SIZE] = {
    0xFFFFEB3Bu, // 0 yellow (dandelion)
    0xFFFFA726u, // 1 orange (marigold)
    0xFFFF80ABu, // 2 pink (cosmos)
    0xFFE1BEE7u, // 3 lavender
    0xFFFFFFFFu, // 4 white (daisy)
    0xFFEF5350u, // 5 red (poppy)
};

// Mushrooms (PROTOTYPE — Native-only for now). 2.5% of blade slots become
// mushrooms (filled-ellipse cap on a short stem). Sampled from a fourth
// independent PRNG stream so adding mushrooms does NOT perturb the existing
// flower / regrowth / main streams. Mushrooms preempt grass rendering at a
// slot: the renderer draws the mushroom geometry and skips the grass blade
// + flower head for that slot.
constexpr double   MUSHROOM_PROBABILITY        = 0.025;
constexpr double   MUSHROOM_CAP_WIDTH_MIN      = 4.0;   // DIP, radius X
constexpr double   MUSHROOM_CAP_WIDTH_MAX      = 8.0;
constexpr double   MUSHROOM_CAP_HEIGHT_MIN     = 2.5;   // DIP, radius Y (flatter than width)
constexpr double   MUSHROOM_CAP_HEIGHT_MAX     = 5.0;
constexpr double   MUSHROOM_STEM_HEIGHT_MIN    = 4.0;   // DIP
constexpr double   MUSHROOM_STEM_HEIGHT_MAX    = 10.0;
constexpr double   MUSHROOM_STEM_THICKNESS_MIN = 2.0;   // DIP
constexpr double   MUSHROOM_STEM_THICKNESS_MAX = 4.0;
constexpr int      MUSHROOM_PALETTE_SIZE       = 6;
constexpr uint64_t MUSHROOM_PRNG_SALT          = 0xBADC0FFEE0FACE21ull;
constexpr uint32_t MUSHROOM_STEM_COLOR         = 0xFFF5F5DCu; // beige/ivory

constexpr uint32_t MUSHROOM_PALETTE[MUSHROOM_PALETTE_SIZE] = {
    0xFFD32F2Fu, // 0 red (amanita)
    0xFF8D6E63u, // 1 brown
    0xFFC9A66Bu, // 2 tan
    0xFFFFF8E1u, // 3 ivory
    0xFFE57373u, // 4 dusty pink
    0xFF6D4C41u, // 5 dark brown
};

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
