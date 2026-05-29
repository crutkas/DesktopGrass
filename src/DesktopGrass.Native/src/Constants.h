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
constexpr double MUSHROOM_STUMP_HEIGHT = 4.0;  // §7 — sits a touch above the grass stub line
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

// Ambient gusts (architecture.md §8.1). Small, randomly scheduled puffs of
// wind that fire independently of cursor input. Implemented via a fifth
// independent PRNG stream salted with AMBIENT_GUST_PRNG_SALT so adding
// ambient gusts does NOT perturb the main / regrowth / flower / mushroom
// streams — the §12 static blade snapshot is unchanged.
//
// Per-fire draw order (locked in §8.1): x, signDir, magFactor, interval.
// Four draws per emitted puff, zero draws on idle ticks.
constexpr uint64_t AMBIENT_GUST_PRNG_SALT       = 0xB7EE2EE2B7EE2EE2ull;
constexpr double   AMBIENT_GUST_INTERVAL_MIN    = 5.0;   // sec
constexpr double   AMBIENT_GUST_INTERVAL_MAX    = 15.0;  // sec
constexpr double   AMBIENT_GUST_MAG_FACTOR_MIN  = 0.3;   // unitless, fraction of MAX_CURSOR_SPEED
constexpr double   AMBIENT_GUST_MAG_FACTOR_MAX  = 0.6;
constexpr double   AMBIENT_GUST_RADIUS_FACTOR   = 0.5;   // unitless, fraction of GUST_RADIUS

// Desert scene shrinks non-cactus, non-mushroom blade heights at render
// time so cacti read as the dominant biome feature.
constexpr double   DESERT_GRASS_HEIGHT_SCALE     = 0.5;

// Cacti (§14). Slot-bound Desert blade variants generated from an independent
// PRNG stream so the §12 static blade snapshot remains unchanged.
constexpr double   CACTUS_PROBABILITY            = 0.005;
constexpr double   CACTUS_HEIGHT_MIN             = 30.0;
constexpr double   CACTUS_HEIGHT_MAX             = 70.0;
constexpr double   CACTUS_WIDTH_MIN              = 8.0;
constexpr double   CACTUS_WIDTH_MAX              = 14.0;
constexpr double   CACTUS_ARM_PROBABILITY        = 0.55;
constexpr double   CACTUS_TWO_ARM_PROBABILITY    = 0.35;
constexpr uint32_t CACTUS_COLOR                  = 0xFF2D7A2Du;
constexpr uint64_t CACTUS_PRNG_SALT              = 0xCAC75CAC75CAC75Cull;

// Tumbleweeds (§14). Desert roaming entities generated and respawned from a
// persistent stream seeded with seed XOR TUMBLEWEED_PRNG_SALT.
constexpr int      TUMBLEWEED_COUNT_PER_1920DIP  = 4;
constexpr double   TUMBLEWEED_SIZE_MIN           = 8.0;
constexpr double   TUMBLEWEED_SIZE_MAX           = 18.0;
constexpr double   TUMBLEWEED_SPEED_MIN          = 40.0;
constexpr double   TUMBLEWEED_SPEED_MAX          = 120.0;
constexpr double   TUMBLEWEED_Y_OFFSET_MIN       = 8.0;
constexpr double   TUMBLEWEED_Y_OFFSET_MAX       = 20.0;
constexpr uint32_t TUMBLEWEED_COLOR              = 0xFF8A6A3Du;
constexpr uint64_t TUMBLEWEED_PRNG_SALT          = 0x7B0117CA7B0117CAull;

// Scenes (architecture.md §13). Render-time presentation modes that share
// generation, sway, gust, cut, and ambient-gust logic. The infrastructure
// pass swaps only the blade palette; per-scene entity content (cacti,
// tumbleweeds, snowflakes, frost) ships in §14/§15.
enum class Scene : uint8_t {
    Grass  = 0,   // default
    Desert = 1,
    Winter = 2,
};
constexpr int    SCENE_COUNT   = 3;
constexpr Scene  SCENE_DEFAULT = Scene::Grass;

// Per-scene blade palettes (§13). Each is six ARGB colors indexed by
// blade.hue (drawn from the §5 main PRNG stream — generation is
// scene-agnostic). The Grass palette is the original §4 PALETTE; the
// Desert and Winter palettes are listed below.
constexpr uint32_t DESERT_PALETTE[PALETTE_SIZE] = {
    0xFFC9A26Bu,  // 0 dried-grass tan
    0xFFB48A56u,  // 1 warm sand
    0xFFD9B57Au,  // 2 light dune
    0xFF8F6E3Fu,  // 3 dust brown
    0xFFE6C896u,  // 4 pale beige
    0xFFA67843u,  // 5 burnt sienna
};

constexpr uint32_t WINTER_PALETTE[PALETTE_SIZE] = {
    0xFFE8EEF5u,  // 0 frost white
    0xFFB7C4D2u,  // 1 cool silver
    0xFFCBD8E5u,  // 2 pale ice
    0xFFD7E2EEu,  // 3 light snow
    0xFFA8B7C6u,  // 4 winter slate
    0xFFEEF3F8u,  // 5 hoarfrost
};

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

// (§13) Per-scene blade palettes indexed by `[scene][hue]`. The Grass row
// is the original §4 PALETTE, repeated for uniform indexing.
constexpr uint32_t SCENE_PALETTES[SCENE_COUNT][PALETTE_SIZE] = {
    { PALETTE[0],        PALETTE[1],        PALETTE[2],        PALETTE[3],        PALETTE[4],        PALETTE[5]        },
    { DESERT_PALETTE[0], DESERT_PALETTE[1], DESERT_PALETTE[2], DESERT_PALETTE[3], DESERT_PALETTE[4], DESERT_PALETTE[5] },
    { WINTER_PALETTE[0], WINTER_PALETTE[1], WINTER_PALETTE[2], WINTER_PALETTE[3], WINTER_PALETTE[4], WINTER_PALETTE[5] },
};

// Roaming-entity subsystem (§13.2). EntityKind discriminants are
// cross-impl-locked. MAX_ENTITIES_PER_MONITOR caps the snowflake emitter
// so the entities vector cannot grow without bound; the Sim pre-reserves
// to this size at construction to avoid grow churn during the tick.
enum class EntityKind : uint8_t {
    None       = 0,
    Tumbleweed = 1,
    Snowflake  = 2,
};
constexpr int MAX_ENTITIES_PER_MONITOR = 64;

// Snowflakes (§15)
constexpr double   SNOWFLAKE_EMIT_RATE_PER_1920DIP = 8.0;    // flakes/sec
constexpr double   SNOWFLAKE_FALL_SPEED_MIN        = 20.0;   // DIP/sec
constexpr double   SNOWFLAKE_FALL_SPEED_MAX        = 40.0;
constexpr double   SNOWFLAKE_SIZE_MIN              = 1.5;    // DIP
constexpr double   SNOWFLAKE_SIZE_MAX              = 3.0;
constexpr double   SNOWFLAKE_SWAY_AMPLITUDE        = 10.0;   // DIP
constexpr double   SNOWFLAKE_SWAY_FREQUENCY        = 0.6;    // Hz
constexpr double   SNOWFLAKE_LIFETIME_PADDING_SEC  = 2.0;
constexpr uint32_t SNOWFLAKE_COLOR                 = 0xFFFFFFFFu;
constexpr uint64_t SNOWFLAKE_PRNG_SALT             = 0xC0FFEE1CECAFEBABull;

// Snow-tipped blade caps (§15)
constexpr double   SNOW_TIP_RADIUS_FACTOR          = 1.25;
constexpr uint32_t SNOW_TIP_COLOR                  = 0xFFFFFFFFu;

// Pine trees (§15.1). Winter biome anchor — slot-bound, mirrors §14 cacti.
constexpr double   PINE_PROBABILITY                = 0.006;
constexpr double   PINE_HEIGHT_MIN                 = 36.0;
constexpr double   PINE_HEIGHT_MAX                 = 72.0;
constexpr double   PINE_WIDTH_MIN                  = 16.0;
constexpr double   PINE_WIDTH_MAX                  = 28.0;
constexpr int      PINE_TIER_COUNT_MIN             = 2;
constexpr int      PINE_TIER_COUNT_MAX             = 4;
constexpr double   PINE_TIP_TAPER                  = 0.25;
constexpr double   PINE_TIER_OVERLAP               = 0.15;
constexpr double   PINE_SNOW_CAP_FRACTION          = 0.30;
constexpr uint32_t PINE_COLOR                      = 0xFF1B5E20u;
constexpr uint64_t PINE_PRNG_SALT                  = 0x50494E4550494E45ull;

} // namespace desktopgrass
