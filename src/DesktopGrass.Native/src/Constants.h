// Constants.h
//
// Single source of truth for all simulation constants.
// Mirrors docs/architecture.md §11. If a constant changes here it MUST change
// in the spec first.

#pragma once

#include <algorithm>
#include <cmath>
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

// Winter scene shrinks ordinary blade heights so pines and snow caps
// read as the dominant features; mushrooms are also suppressed below.
constexpr double   WINTER_GRASS_HEIGHT_SCALE     = 0.5;

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
    Sheep      = 3,
    Cat        = 4,
    Raindrop   = 5,
    Bunny      = 6,
    Butterfly  = 7,
    Firefly    = 8,
    Bird       = 9,
};
constexpr int MAX_ENTITIES_PER_MONITOR = 64;

// Critter subsystem — Grass-scene ambient critters plus legacy tray selectors.
// CritterKind discriminants are cross-impl-locked.
enum class CritterKind : uint8_t {
    None  = 0,
    Sheep = 1,
    Cat   = 2,
    Bunny = 3,
};
constexpr int         CRITTER_COUNT   = 4;
constexpr CritterKind CRITTER_DEFAULT = CritterKind::None;
constexpr uint64_t    CRITTER_PRNG_SALT = 0x5C8EE05C8EE05C8Eull;
constexpr int         PET_COUNT_OPTIONS[] = { 1, 2, 3, 4, 5, 6 };
constexpr int         PET_COUNT_DEFAULT_SHEEP = 2;
constexpr int         PET_COUNT_DEFAULT_CAT = 1;
constexpr int         PET_COUNT_MAX_PER_MONITOR = 6;
constexpr const wchar_t* SHEEP_NAME_POOL[] = {
    L"Bessie", L"Wooly", L"Clover", L"Daisy", L"Pippin", L"Buttercup", L"Mossy", L"Hazel"
};
constexpr const wchar_t* CAT_NAME_POOL[] = {
    L"Mittens", L"Whiskers", L"Shadow", L"Ginger", L"Smokey", L"Boots", L"Sage", L"Juno"
};
constexpr const wchar_t* BUNNY_NAME_POOL[] = {
    L"Clover", L"Hazel", L"Thumper", L"Mochi", L"Pip", L"Acorn",
    L"Biscuit", L"Willow", L"Pepper", L"Hopper", L"Juniper", L"Snowdrop"
};
constexpr double      PET_NAME_HOVER_RADIUS = 50.0;
constexpr double      PET_NAME_FADE_DURATION = 1.5;
constexpr double      PET_NAME_FONT_SIZE = 11.0;
constexpr double      PET_NAME_OFFSET_Y = -8.0;
constexpr uint32_t    PET_NAME_COLOR = 0xFFFFFFFFu;
constexpr uint32_t    PET_NAME_SHADOW_COLOR = 0xC0000000u;

// Sheep (§16). Procedurally drawn pet that walks, grazes, and idles along
// the bottom strip. Phase 2: state machine (Walking / Grazing / Idle) with
// animated leg cycle + head bob + grazing-head-down + idle-head-sweep.
// Counts/speeds/sizes are sampled per-monitor from the critter PRNG so
// different displays get different flocks. (Cursor-startle lands in §16.3.)
constexpr int      SHEEP_COUNT_MIN     = 2;
constexpr int      SHEEP_COUNT_MAX     = 3;
constexpr double   SHEEP_WALK_SPEED_MIN = 14.0;   // DIP/sec
constexpr double   SHEEP_WALK_SPEED_MAX = 26.0;

// Geometry (DIP). Round, slightly tall body so the silhouette reads as
// "cloud with legs" from a distance.
constexpr double   SHEEP_BODY_RADIUS   = 12.0;    // body x-radius
constexpr double   SHEEP_BODY_HEIGHT   = 9.5;     // body y-radius
constexpr double   SHEEP_HEAD_RADIUS   = 5.0;
constexpr double   SHEEP_LEG_LENGTH    = 5.5;
constexpr double   SHEEP_TAIL_RADIUS   = 3.2;     // rear puff

// Palette. Suffolk-style sheep: white wool, dark face, dark legs — that's
// the silhouette people instantly read as "sheep". Cream/pink faces look
// like a different creature entirely at this pixel scale.
constexpr uint32_t SHEEP_BODY_COLOR    = 0xFFF7F4EBu;   // off-white wool
constexpr uint32_t SHEEP_LEG_COLOR     = 0xFF1F1A16u;   // near-black
constexpr uint32_t SHEEP_FACE_COLOR    = 0xFF1F1A16u;   // dark Suffolk face
constexpr uint32_t SHEEP_EAR_COLOR     = 0xFF14110Eu;   // slightly darker than face
constexpr uint32_t SHEEP_INK_COLOR     = 0xFFF7F4EBu;   // eyes = light dots on dark face

// Animation cycle. WALK_PERIOD is one full leg cycle (one stride pair).
constexpr double   SHEEP_WALK_PERIOD     = 0.55;        // seconds
constexpr double   SHEEP_LEG_CYCLE_AMP   = 2.0;         // DIP vertical sway of leg-tip
constexpr double   SHEEP_HEAD_BOB_AMP    = 0.7;         // DIP head Y bob during walk
constexpr double   SHEEP_TAIL_WIGGLE_AMP = 0.6;         // DIP tail X wiggle

// State machine. State encodes 0=Walking, 1=Grazing, 2=Idle, 3=Sleeping,
// 4=Hopping, 5=Greeting in Entity.state. Walking → expires → Grazing /
// Idle / Hopping. Idle → expires → Walking or Sleeping. Other states →
// expires → Walking; Greeting flips vx on exit so paired sheep walk apart.
// Durations drawn from critter PRNG on every transition so behavior is
// deterministic per (seed, monitor). Click-near-sheep also forces Hopping.
constexpr uint8_t  SHEEP_STATE_WALKING  = 0;
constexpr uint8_t  SHEEP_STATE_GRAZING  = 1;
constexpr uint8_t  SHEEP_STATE_IDLE     = 2;
constexpr uint8_t  SHEEP_STATE_SLEEPING = 3;
constexpr uint8_t  SHEEP_STATE_HOPPING  = 4;
constexpr uint8_t  SHEEP_STATE_GREETING = 5;
constexpr double   SHEEP_WALK_DURATION_MIN   = 8.0;     // sec — average walk leg before pause
constexpr double   SHEEP_WALK_DURATION_MAX   = 14.0;
constexpr double   SHEEP_GRAZE_DURATION_MIN  = 3.0;     // sec — head down chewing grass
constexpr double   SHEEP_GRAZE_DURATION_MAX  = 5.0;
constexpr double   SHEEP_IDLE_DURATION_MIN   = 1.5;     // sec — looking around
constexpr double   SHEEP_IDLE_DURATION_MAX   = 3.0;
constexpr double   SHEEP_SLEEP_DURATION_MIN  = 8.0;     // sec — Zzz nap
constexpr double   SHEEP_SLEEP_DURATION_MAX  = 16.0;
constexpr double   SHEEP_HOP_DURATION        = 0.55;    // sec — one parabolic arc
constexpr double   SHEEP_GREET_RADIUS        = 50.0;    // DIP, center-to-center
constexpr double   SHEEP_GREET_DURATION_MIN  = 1.6;     // sec
constexpr double   SHEEP_GREET_DURATION_MAX  = 2.8;
constexpr double   SHEEP_GREET_MIN_AGE       = 1.5;     // sec, natural cooldown
constexpr double   SHEEP_CURIOUS_RADIUS      = 80.0;    // DIP, cursor proximity for noticing
constexpr double   SHEEP_CURIOUS_HEAD_TURN_MAX = 0.55;  // radians, max head rotation toward cursor

// Walking-expiry distribution. Cumulative: r<GRAZE → Grazing, else
// r<GRAZE+IDLE → Idle, else → Hopping. GRAZE + IDLE + HOP_PROB == 1.0.
constexpr double   SHEEP_GRAZE_PROBABILITY  = 0.60;
constexpr double   SHEEP_IDLE_PROBABILITY   = 0.25;
// Idle-expiry: chance of slipping into Sleeping vs returning to Walking.
constexpr double   SHEEP_SLEEP_PROB_MORNING = 0.10;  // 06:00-10:00 local
constexpr double   SHEEP_SLEEP_PROB_DEFAULT = 0.30;  // 10:00-22:00 local
constexpr double   SHEEP_SLEEP_PROB_NIGHT   = 0.70;  // 22:00-06:00 local
constexpr int      SHEEP_MORNING_START_HOUR = 6;     // inclusive
constexpr int      SHEEP_MORNING_END_HOUR   = 10;    // exclusive
constexpr int      SHEEP_NIGHT_START_HOUR   = 22;    // inclusive
constexpr int      SHEEP_NIGHT_END_HOUR     = 6;     // exclusive, wraps midnight
constexpr double   SHEEP_SLEEP_FROM_IDLE_PROB = SHEEP_SLEEP_PROB_DEFAULT;

// Idle / Grazing / Greeting tiny animations.
constexpr double   SHEEP_IDLE_SWEEP_FREQ      = 1.4;    // rad/sec for L/R head turn
constexpr double   SHEEP_GRAZE_MUNCH_FREQ     = 8.0;    // rad/sec for head nibble bob
constexpr double   SHEEP_GRAZE_MUNCH_AMP      = 0.6;    // DIP
constexpr double   SHEEP_GREET_HEAD_BOB_FREQ  = 4.5;    // rad/sec
constexpr double   SHEEP_GREET_HEAD_BOB_AMP   = 0.7;    // DIP, gentle nuzzle bob

// Hop arc + click-startle.
constexpr double   SHEEP_HOP_HEIGHT        = 11.0;      // DIP peak vertical offset
constexpr double   SHEEP_STARTLE_RADIUS    = 64.0;      // DIP — click within this hops the sheep
constexpr double   SHEEP_STARTLE_BOOST     = 1.6;       // walking speed multiplier post-startle

// Sleeping cosmetic — "Zzz" glyphs drift up from the sheep's head.
constexpr double   SHEEP_ZZZ_CYCLE_SEC     = 1.8;       // one Z lifespan
constexpr double   SHEEP_ZZZ_RISE          = 11.0;      // DIP rise over one cycle
constexpr double   SHEEP_ZZZ_SIZE_START    = 2.0;       // DIP starting Z side
constexpr double   SHEEP_ZZZ_SIZE_END      = 4.0;       // DIP ending Z side

// Cat (§17). Calm color-varied critter that reuses the sheep state byte values but
// only uses Walking, Idle, Sleeping, and Hopping (semantically Pouncing).
constexpr int      CAT_COUNT_MIN      = 1;
constexpr int      CAT_COUNT_MAX      = 2;
constexpr double   CAT_WALK_SPEED_MIN = 10.0;
constexpr double   CAT_WALK_SPEED_MAX = 22.0;
constexpr double   CAT_POUNCE_SPEED   = 60.0;

constexpr double   CAT_BODY_RADIUS    = 11.0;
constexpr double   CAT_BODY_HEIGHT    = 7.0;
constexpr double   CAT_HEAD_RADIUS    = 4.5;
constexpr double   CAT_LEG_LENGTH     = 5.0;
constexpr double   CAT_TAIL_LENGTH    = 13.0;
constexpr double   CAT_TAIL_THICKNESS = 1.6;
constexpr double   CAT_EAR_HEIGHT     = 4.5;

constexpr int CAT_COAT_VARIANT_COUNT = 6;

struct CatCoatPalette {
    uint32_t body;
    uint32_t leg;
    uint32_t face;
    uint32_t ear;
    uint32_t ink;
};

constexpr CatCoatPalette CAT_COAT_PALETTES[CAT_COAT_VARIANT_COUNT] = {
    { 0xFF6B6259u, 0xFF3D3733u, 0xFF6B6259u, 0xFF3D3733u, 0xFF1A1614u },  // 0 Gray tabby (existing)
    { 0xFFD89A6Fu, 0xFFA56B40u, 0xFFD89A6Fu, 0xFFA56B40u, 0xFF2B1A0Eu },  // 1 Orange
    { 0xFF2A2522u, 0xFF140F0Cu, 0xFF2A2522u, 0xFF140F0Cu, 0xFFD9B85Bu },  // 2 Black (yellow eyes)
    { 0xFFEDE9E1u, 0xFFBDB7ABu, 0xFFEDE9E1u, 0xFFBDB7ABu, 0xFF1F1817u },  // 3 White
    { 0xFF7A5F3Cu, 0xFF4E3F26u, 0xFF7A5F3Cu, 0xFF4E3F26u, 0xFF1A1108u },  // 4 Brown tabby
    { 0xFFC9B898u, 0xFF8E7F6Bu, 0xFFC9B898u, 0xFF8E7F6Bu, 0xFF2E251Du },  // 5 Cream
};

// Backward-compat aliases: variant 0 preserves the original muted gray tabby.
constexpr uint32_t CAT_BODY_COLOR = CAT_COAT_PALETTES[0].body;
constexpr uint32_t CAT_LEG_COLOR  = CAT_COAT_PALETTES[0].leg;
constexpr uint32_t CAT_FACE_COLOR = CAT_COAT_PALETTES[0].face;
constexpr uint32_t CAT_EAR_COLOR  = CAT_COAT_PALETTES[0].ear;
constexpr uint32_t CAT_INK_COLOR  = CAT_COAT_PALETTES[0].ink;

constexpr double   CAT_WALK_PERIOD    = 0.50;
constexpr double   CAT_LEG_CYCLE_AMP  = 1.6;
constexpr double   CAT_HEAD_BOB_AMP   = 0.4;
constexpr double   CAT_TAIL_SWAY_FREQ = 1.2;
constexpr double   CAT_TAIL_SWAY_AMP  = 0.35;

constexpr uint8_t  CAT_STATE_WALKING  = SHEEP_STATE_WALKING;   // 0
constexpr uint8_t  CAT_STATE_IDLE     = SHEEP_STATE_IDLE;      // 2, sit-and-watch
constexpr uint8_t  CAT_STATE_SLEEPING = SHEEP_STATE_SLEEPING;  // 3
constexpr uint8_t  CAT_STATE_POUNCING = SHEEP_STATE_HOPPING;   // 4, semantic alias

constexpr double   CAT_WALK_DURATION_MIN  = 6.0;
constexpr double   CAT_WALK_DURATION_MAX  = 10.0;
constexpr double   CAT_IDLE_DURATION_MIN  = 4.0;
constexpr double   CAT_IDLE_DURATION_MAX  = 8.0;
constexpr double   CAT_SLEEP_DURATION_MIN = 20.0;
constexpr double   CAT_SLEEP_DURATION_MAX = 40.0;
constexpr double   CAT_POUNCE_DURATION    = 0.45;

constexpr double   CAT_IDLE_PROBABILITY   = 0.65;
constexpr double   CAT_SLEEP_PROBABILITY  = 0.30;
constexpr double   CAT_SLEEP_FROM_IDLE_PROB_DEFAULT = 0.50;
constexpr double   CAT_SLEEP_FROM_IDLE_PROB_MORNING = 0.20;
constexpr double   CAT_SLEEP_FROM_IDLE_PROB_NIGHT   = 0.85;

constexpr double   CAT_POUNCE_RADIUS      = 80.0;
constexpr double   CAT_POUNCE_HEIGHT      = 9.0;
constexpr double   CAT_CURIOUS_RADIUS     = 100.0;
constexpr double   CAT_CURIOUS_HEAD_TURN_MAX = 0.7;

// Bunny (§18). Grass-only woodland critter: shy, passive, and always hopping
// when it moves. Generated after sheep and cats from the shared critter PRNG.
constexpr int      BUNNY_COUNT_MIN          = 1;
constexpr int      BUNNY_COUNT_MAX          = 2;
constexpr double   BUNNY_HOP_SPEED_MIN      = 22.0;
constexpr double   BUNNY_HOP_SPEED_MAX      = 38.0;
constexpr double   BUNNY_BODY_RADIUS        = 8.0;
constexpr double   BUNNY_BODY_HEIGHT        = 6.5;
constexpr double   BUNNY_HEAD_RADIUS        = 4.2;
constexpr double   BUNNY_EAR_HEIGHT         = 9.0;
constexpr double   BUNNY_EAR_WIDTH          = 2.2;
constexpr double   BUNNY_EAR_SPACING        = 3.0;
constexpr double   BUNNY_LEG_LENGTH         = 4.0;
constexpr double   BUNNY_TAIL_RADIUS        = 2.4;
constexpr uint32_t BUNNY_BODY_COLOR         = 0xFF8A6A4Au;
constexpr uint32_t BUNNY_BELLY_COLOR        = 0xFFC4A98Du;
constexpr uint32_t BUNNY_EAR_COLOR          = 0xFF8A6A4Au;
constexpr uint32_t BUNNY_EAR_INNER_COLOR    = 0xFFD9A0A0u;
constexpr uint32_t BUNNY_TAIL_COLOR         = 0xFFF7F4EBu;
constexpr uint32_t BUNNY_EYE_COLOR          = 0xFF1A1208u;
constexpr uint32_t BUNNY_NOSE_COLOR         = 0xFF8A4040u;

constexpr uint8_t  BUNNY_STATE_HOPPING      = 0;
constexpr uint8_t  BUNNY_STATE_GRAZING      = 1;
constexpr uint8_t  BUNNY_STATE_IDLE         = 2;
constexpr uint8_t  BUNNY_STATE_SLEEPING     = 3;
constexpr uint8_t  BUNNY_STATE_STARTLED     = 4;

constexpr double   BUNNY_HOP_DURATION       = 0.40;
constexpr double   BUNNY_HOP_HEIGHT         = 8.0;
constexpr double   BUNNY_HOP_GAP_MIN        = 0.05;
constexpr double   BUNNY_HOP_GAP_MAX        = 0.20;
constexpr double   BUNNY_GRAZE_DURATION_MIN = 2.5;
constexpr double   BUNNY_GRAZE_DURATION_MAX = 4.5;
constexpr double   BUNNY_IDLE_DURATION_MIN  = 2.0;
constexpr double   BUNNY_IDLE_DURATION_MAX  = 4.0;
constexpr double   BUNNY_SLEEP_DURATION_MIN = 6.0;
constexpr double   BUNNY_SLEEP_DURATION_MAX = 12.0;
constexpr double   BUNNY_GRAZE_PROBABILITY  = 0.55;
constexpr double   BUNNY_IDLE_PROBABILITY   = 0.30;
constexpr double   BUNNY_SLEEP_PROB_DAY     = 0.05;
constexpr double   BUNNY_SLEEP_PROB_NIGHT   = 0.40;

constexpr double   BUNNY_STARTLE_RADIUS     = 90.0;
constexpr double   BUNNY_STARTLE_BOOST      = 2.0;
constexpr double   BUNNY_STARTLE_HOP_HEIGHT = 14.0;
constexpr double   BUNNY_STARTLE_DURATION   = 3.0;

constexpr double   BUNNY_NOSE_TWITCH_FREQ   = 6.0;
constexpr double   BUNNY_NOSE_TWITCH_AMP    = 0.5;
constexpr double   BUNNY_EAR_WIGGLE_FREQ    = 1.2;
constexpr double   BUNNY_EAR_WIGGLE_AMP     = 0.20;

constexpr double   BUNNY_ZZZ_CYCLE_SEC      = SHEEP_ZZZ_CYCLE_SEC;
constexpr double   BUNNY_ZZZ_RISE           = SHEEP_ZZZ_RISE * 0.7;
constexpr double   BUNNY_ZZZ_SIZE_START     = SHEEP_ZZZ_SIZE_START * 0.7;
constexpr double   BUNNY_ZZZ_SIZE_END       = SHEEP_ZZZ_SIZE_END * 0.7;

// Butterflies (§17.6). Grass-only, passive daytime ambient flyers.
constexpr int      BUTTERFLY_COUNT_MIN          = 2;
constexpr int      BUTTERFLY_COUNT_MAX          = 4;
constexpr double   BUTTERFLY_SPEED_MIN          = 18.0;
constexpr double   BUTTERFLY_SPEED_MAX          = 32.0;
constexpr double   BUTTERFLY_BODY_LENGTH        = 2.4;
constexpr double   BUTTERFLY_WING_RADIUS        = 3.5;
constexpr double   BUTTERFLY_WING_OFFSET        = 2.2;
constexpr double   BUTTERFLY_FLUTTER_FREQ       = 16.0;
constexpr double   BUTTERFLY_FLUTTER_MIN_SCALE  = 0.20;
constexpr double   BUTTERFLY_MEANDER_FREQ_Y     = 0.8;
constexpr double   BUTTERFLY_MEANDER_AMP_Y      = 16.0;
constexpr double   BUTTERFLY_MEANDER_FREQ_X     = 0.5;
constexpr double   BUTTERFLY_MEANDER_AMP_X      = 0.4;
constexpr double   BUTTERFLY_ALTITUDE_MIN       = 18.0;
constexpr double   BUTTERFLY_ALTITUDE_MAX       = 70.0;
constexpr uint32_t BUTTERFLY_BODY_COLOR         = 0xFF2A2018u;
constexpr int      BUTTERFLY_COLOR_COUNT        = 5;
constexpr int      BUTTERFLY_HOUR_START         = 6;
constexpr int      BUTTERFLY_HOUR_END           = 19;
constexpr int      BUTTERFLY_FADE_DURATION_HOUR = 1;
constexpr uint64_t BUTTERFLY_PRNG_SALT          = 0xB07DEF1E0001ull;

struct ButterflyPalette {
    uint32_t wingColor;
    uint32_t accentColor;
};

constexpr ButterflyPalette BUTTERFLY_PALETTES[BUTTERFLY_COLOR_COUNT] = {
    { 0xFFFF9A2Eu, 0xFF1A130Cu }, // 0 Monarch: orange + black tips
    { 0xFFFFD34Du, 0xFF1A130Cu }, // 1 Swallowtail: yellow + black
    { 0xFFFFF8E8u, 0xFF3A3A3Au }, // 2 Cabbage: white + dark dots
    { 0xFF63C7FFu, 0xFF1B4D99u }, // 3 Morpho: sky blue + deeper blue
    { 0xFFFFA6C8u, 0xFFFF6EA8u }, // 4 Pink: soft pink + rose
};

// Fireflies (§17.7). Grass-only, passive nighttime ambient flyers.
constexpr int      FIREFLY_COUNT_MIN            = 3;
constexpr int      FIREFLY_COUNT_MAX            = 6;
constexpr double   FIREFLY_DRIFT_SPEED_MIN      = 4.0;
constexpr double   FIREFLY_DRIFT_SPEED_MAX      = 10.0;
constexpr double   FIREFLY_BODY_RADIUS          = 1.2;
constexpr double   FIREFLY_GLOW_RADIUS          = 5.0;
constexpr double   FIREFLY_BLINK_PERIOD_MIN     = 1.4;
constexpr double   FIREFLY_BLINK_PERIOD_MAX     = 2.6;
constexpr double   FIREFLY_BLINK_DUTY           = 0.55;
constexpr double   FIREFLY_BLINK_FADE           = 0.30;
constexpr double   FIREFLY_DRIFT_FREQ_X         = 0.4;
constexpr double   FIREFLY_DRIFT_FREQ_Y         = 0.6;
constexpr double   FIREFLY_DRIFT_AMP_X          = 0.6;
constexpr double   FIREFLY_DRIFT_AMP_Y          = 8.0;
constexpr double   FIREFLY_ALTITUDE_MIN         = 8.0;
constexpr double   FIREFLY_ALTITUDE_MAX         = 55.0;
constexpr uint32_t FIREFLY_BODY_COLOR           = 0xFFFFEE88u;
constexpr uint32_t FIREFLY_GLOW_COLOR_RGB       = 0xEEDD66u;
constexpr int      FIREFLY_GLOW_ALPHA_MAX       = 110;
constexpr int      FIREFLY_BODY_ALPHA_MAX       = 255;
constexpr int      FIREFLY_NIGHT_START_HOUR     = 20;
constexpr int      FIREFLY_NIGHT_END_HOUR       = 6;
constexpr int      FIREFLY_FADE_DURATION_HOUR   = 1;
constexpr uint64_t FIREFLY_PRNG_SALT            = 0xF13EF1E7777ull;

// Bird flybys (§17.8). Grass-only daytime transient flocks.
constexpr double   BIRD_FLYBY_SPAWN_RATE_PER_HOUR = 15.0;
constexpr int      BIRD_FLYBY_HOUR_START           = 7;
constexpr int      BIRD_FLYBY_HOUR_END             = 19;
constexpr int      BIRD_FLOCK_SIZE_MIN             = 3;
constexpr int      BIRD_FLOCK_SIZE_MAX             = 7;
constexpr double   BIRD_FLOCK_FORMATION_SPACING    = 9.0;
constexpr double   BIRD_FLOCK_V_ANGLE_DEG          = 22.0;
constexpr double   BIRD_SPEED_MIN                  = 65.0;
constexpr double   BIRD_SPEED_MAX                  = 95.0;
constexpr double   BIRD_ALTITUDE_MIN               = 78.0;
constexpr double   BIRD_ALTITUDE_MAX               = 96.0;
constexpr double   BIRD_BODY_LENGTH                = 3.6;
constexpr double   BIRD_WING_SPAN                  = 5.0;
constexpr double   BIRD_WING_FLAP_FREQ             = 7.0;
constexpr double   BIRD_WING_FLAP_PHASE_JITTER     = 0.6;
constexpr uint32_t BIRD_BODY_COLOR                 = 0xFF1A1610u;
constexpr double   BIRD_WING_OPEN_RATIO            = 1.0;
constexpr double   BIRD_WING_FOLD_RATIO            = 0.30;
constexpr double   BIRD_FADE_IN_FRAC               = 0.08;
constexpr double   BIRD_FADE_OUT_FRAC              = 0.08;
constexpr double   BIRD_DRIFT_AMP_Y                = 3.0;
constexpr double   BIRD_DRIFT_FREQ_Y               = 0.8;
constexpr uint64_t BIRD_FLYBY_PRNG_SALT            = 0xB12D1F1A1B12D1Aull;

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

// Snow accumulation (§15.2). Passive Winter-only layer on the strip baseline.
constexpr double   SNOW_ACCUMULATION_RATE           = 0.012;  // DIP/sec
constexpr double   SNOW_DEPTH_MAX                   = 30.0;   // DIP
constexpr double   SNOW_DEPTH_MIN_RENDER            = 0.3;    // DIP
constexpr uint32_t SNOW_LAYER_COLOR_TOP             = 0xFFFFFFFFu;
constexpr uint32_t SNOW_LAYER_COLOR_BOTTOM          = 0xFFE8E8F0u;
constexpr uint32_t SNOW_LAYER_HIGHLIGHT             = 0xFFFFFFFFu;
constexpr double   SNOW_TOP_UNDULATION_AMP          = 2.5;    // DIP
constexpr double   SNOW_TOP_UNDULATION_WAVELENGTH   = 90.0;   // DIP
constexpr uint64_t SNOW_TOP_UNDULATION_PHASE_SALT   = 0x5E0A1ull;

// Light rain (§20). Dedicated "rain drop" PRNG stream. Draw order per drop:
// size, x, fallSpeed, vx, seed, then the exponential next-spawn interval.
constexpr uint64_t RAINDROP_PRNG_SALT              = 0xD40F0A1DD40F0A1Dull;
constexpr double   RAINDROP_EMIT_RATE_PER_1920DIP  = 6.0;    // drops/sec
constexpr double   RAINDROP_LENGTH_MIN             = 4.0;    // DIP
constexpr double   RAINDROP_LENGTH_MAX             = 7.0;
constexpr double   RAINDROP_THICKNESS              = 0.9;    // DIP
constexpr double   RAINDROP_FALL_SPEED_MIN         = 240.0;  // DIP/sec
constexpr double   RAINDROP_FALL_SPEED_MAX         = 360.0;
constexpr double   RAINDROP_DRIFT_MIN              = -8.0;   // DIP/sec
constexpr double   RAINDROP_DRIFT_MAX              = 8.0;
constexpr uint32_t RAINDROP_COLOR                  = 0x88B0C4D0u;
constexpr double   RAINDROP_LIFETIME_PADDING_SEC   = 0.3;

// Snow-tipped blade caps (§15)
constexpr double   SNOW_TIP_RADIUS_FACTOR          = 1.25;
constexpr uint32_t SNOW_TIP_COLOR                  = 0xFFFFFFFFu;

// Pine trees (§15.1). Winter biome anchor — slot-bound, mirrors §14 cacti.
constexpr double   PINE_PROBABILITY                = 0.0075;
constexpr double   PINE_HEIGHT_MIN                 = 45.0;
constexpr double   PINE_HEIGHT_MAX                 = 90.0;
constexpr double   PINE_WIDTH_MIN                  = 28.0;
constexpr double   PINE_WIDTH_MAX                  = 48.0;
constexpr int      PINE_TIER_COUNT_MIN             = 2;
constexpr int      PINE_TIER_COUNT_MAX             = 4;
constexpr double   PINE_TIP_TAPER                  = 0.25;
constexpr double   PINE_TIER_OVERLAP               = 0.15;
constexpr double   PINE_SNOW_CAP_FRACTION          = 0.30;
constexpr uint32_t PINE_COLOR                      = 0xFF1B5E20u;
constexpr uint64_t PINE_PRNG_SALT                  = 0x50494E4550494E45ull;

// Birch tree variant (§15.1). Second tree style — vertical white trunk
// with dark bark marks and short bare branches. Selected per-slot via
// an additional PRNG draw on tree promotion.
constexpr double   BIRCH_VARIANT_PROBABILITY       = 0.30;
constexpr double   BIRCH_TRUNK_WIDTH_MIN           = 4.0;   // DIP
constexpr double   BIRCH_TRUNK_WIDTH_MAX           = 7.0;   // DIP
constexpr int      BIRCH_BARK_MARK_COUNT           = 5;     // short centered horizontal dashes
constexpr double   BIRCH_BARK_MARK_LENGTH_FRAC     = 0.50;  // max fraction of trunk width
constexpr int      BIRCH_BRANCH_COUNT              = 6;     // upward-angled branches with snow tips
constexpr double   BIRCH_SNOW_CAP_FRACTION         = 0.18;  // fraction of trunk height
constexpr uint32_t BIRCH_BARK_COLOR                = 0xFFEFEFE6u; // off-white trunk
constexpr uint32_t BIRCH_MARK_COLOR                = 0xFF2A2A28u; // dark bark stripes

// Day-night ambient tint (§19). Pure render overlay; no simulation state.
struct DayTintPhase {
    float   startHour;
    uint8_t r;
    uint8_t g;
    uint8_t b;
    uint8_t alpha;
};

constexpr bool    DAYTINT_ENABLED_DEFAULT = true;
constexpr uint8_t DAYTINT_MAX_ALPHA       = 36;

constexpr DayTintPhase DAYTINT_PHASES[] = {
    {  0.0f,  40,  50,  90, 36 },  // Night (wraps from prev night)
    {  4.0f,  60,  70, 110, 32 },  // Predawn
    {  6.0f, 255, 180, 140, 28 },  // Sunrise
    {  8.0f, 255, 220, 160, 16 },  // Morning
    { 10.0f, 255, 255, 255,  0 },  // Day - no tint
    { 17.0f, 240, 170, 110, 22 },  // Late afternoon
    { 19.0f, 220, 110,  90, 30 },  // Sunset
    { 20.0f,  90,  80, 130, 28 },  // Dusk
    { 22.0f,  40,  50,  90, 36 },  // Night
};
constexpr int DAYTINT_PHASE_COUNT = static_cast<int>(sizeof(DAYTINT_PHASES) / sizeof(DAYTINT_PHASES[0]));

inline double normalize_day_tint_hour(double hourFloat) noexcept {
    double hour = std::fmod(hourFloat, 24.0);
    if (hour < 0.0) hour += 24.0;
    return hour;
}

inline double ambient_clamp01(double value) noexcept {
    if (value <= 0.0) return 0.0;
    if (value >= 1.0) return 1.0;
    return value;
}

inline double ambient_smoothstep01(double value) noexcept {
    const double t = ambient_clamp01(value);
    return t * t * (3.0 - 2.0 * t);
}

inline double butterfly_fade(double hourFloat) noexcept {
    const double hour = normalize_day_tint_hour(hourFloat);
    const double fadeStart = static_cast<double>(BUTTERFLY_HOUR_START - BUTTERFLY_FADE_DURATION_HOUR);
    const double start = static_cast<double>(BUTTERFLY_HOUR_START);
    const double end = static_cast<double>(BUTTERFLY_HOUR_END);
    const double fadeEnd = static_cast<double>(BUTTERFLY_HOUR_END + BUTTERFLY_FADE_DURATION_HOUR);

    if (hour >= fadeStart && hour < start) return ambient_clamp01((hour - fadeStart) / BUTTERFLY_FADE_DURATION_HOUR);
    if (hour >= start && hour < end) return 1.0;
    if (hour >= end && hour < fadeEnd) return ambient_clamp01((fadeEnd - hour) / BUTTERFLY_FADE_DURATION_HOUR);
    return 0.0;
}

inline double firefly_fade(double hourFloat) noexcept {
    const double hour = normalize_day_tint_hour(hourFloat);
    const double nightStart = static_cast<double>(FIREFLY_NIGHT_START_HOUR);
    const double nightEnd = static_cast<double>(FIREFLY_NIGHT_END_HOUR);
    const double fadeInStart = nightStart - static_cast<double>(FIREFLY_FADE_DURATION_HOUR);
    const double fadeOutEnd = nightEnd + static_cast<double>(FIREFLY_FADE_DURATION_HOUR);

    if (hour >= nightStart || hour < nightEnd) return 1.0;
    if (hour >= fadeInStart && hour < nightStart) return ambient_clamp01((hour - fadeInStart) / FIREFLY_FADE_DURATION_HOUR);
    if (hour >= nightEnd && hour < fadeOutEnd) return ambient_clamp01((fadeOutEnd - hour) / FIREFLY_FADE_DURATION_HOUR);
    return 0.0;
}

inline double butterfly_wing_scale(double timeSeconds, double phaseY) noexcept {
    const double raw = std::cos(timeSeconds * BUTTERFLY_FLUTTER_FREQ + phaseY);
    if (raw < BUTTERFLY_FLUTTER_MIN_SCALE) return BUTTERFLY_FLUTTER_MIN_SCALE;
    if (raw > 1.0) return 1.0;
    return raw;
}

inline double bird_wing_scale(double timeSeconds, double wingPhaseOffset) noexcept {
    const double t = 0.5 + 0.5 * std::cos(timeSeconds * BIRD_WING_FLAP_FREQ + wingPhaseOffset);
    return BIRD_WING_FOLD_RATIO + (BIRD_WING_OPEN_RATIO - BIRD_WING_FOLD_RATIO) * t;
}

inline double bird_fade_alpha(double x, double vx, double monitorWidth) noexcept {
    if (monitorWidth <= 0.0) return 0.0;
    const double visibleSpan = monitorWidth;
    const double fadeInDist = BIRD_FADE_IN_FRAC * visibleSpan;
    const double fadeOutDist = BIRD_FADE_OUT_FRAC * visibleSpan;
    double alpha = 1.0;
    if (vx >= 0.0) {
        if (fadeInDist > 0.0 && x < fadeInDist) alpha = std::min(alpha, ambient_clamp01((x + 50.0) / fadeInDist));
        if (fadeOutDist > 0.0 && x > monitorWidth - fadeOutDist) alpha = std::min(alpha, ambient_clamp01((monitorWidth + 50.0 - x) / fadeOutDist));
    } else {
        if (fadeInDist > 0.0 && x > monitorWidth - fadeInDist) alpha = std::min(alpha, ambient_clamp01((monitorWidth + 50.0 - x) / fadeInDist));
        if (fadeOutDist > 0.0 && x < fadeOutDist) alpha = std::min(alpha, ambient_clamp01((x + 50.0) / fadeOutDist));
    }
    return ambient_clamp01(alpha);
}

inline double firefly_blink_brightness(double timeSeconds, double blinkPeriod, double blinkPhase) noexcept {
    if (blinkPeriod <= 0.0) return 0.0;
    double cycleT = std::fmod(timeSeconds / blinkPeriod + blinkPhase, 1.0);
    if (cycleT < 0.0) cycleT += 1.0;

    if (cycleT >= FIREFLY_BLINK_DUTY) return 0.0;

    const double fadeFrac = ambient_clamp01(FIREFLY_BLINK_FADE / blinkPeriod);
    double brightness = 1.0;
    if (fadeFrac > 0.0) {
        if (cycleT < fadeFrac) {
            brightness = ambient_smoothstep01(cycleT / fadeFrac);
        } else if (cycleT > FIREFLY_BLINK_DUTY - fadeFrac) {
            brightness = ambient_smoothstep01((FIREFLY_BLINK_DUTY - cycleT) / fadeFrac);
        }
    }
    return ambient_clamp01(brightness);
}

inline uint8_t day_tint_lerp_channel(uint8_t from, uint8_t to, double t) noexcept {
    const double value = static_cast<double>(from) +
        (static_cast<double>(to) - static_cast<double>(from)) * t;
    if (value <= 0.0) return 0;
    if (value >= 255.0) return 255;
    return static_cast<uint8_t>(value);
}

inline void compute_day_tint(double hourFloat,
                             uint8_t& r,
                             uint8_t& g,
                             uint8_t& b,
                             uint8_t& alpha) noexcept {
    const double hour = normalize_day_tint_hour(hourFloat);

    int currentIndex = DAYTINT_PHASE_COUNT - 1;
    for (int i = 0; i < DAYTINT_PHASE_COUNT; ++i) {
        if (hour >= static_cast<double>(DAYTINT_PHASES[i].startHour)) {
            currentIndex = i;
        } else {
            break;
        }
    }

    const DayTintPhase& current = DAYTINT_PHASES[currentIndex];
    const DayTintPhase& next = DAYTINT_PHASES[(currentIndex + 1) % DAYTINT_PHASE_COUNT];

    double currentStart = static_cast<double>(current.startHour);
    double nextStart = static_cast<double>(next.startHour);
    if (nextStart <= currentStart) nextStart += 24.0;

    double hourForLerp = hour;
    if (hourForLerp < currentStart) hourForLerp += 24.0;

    double t = 0.0;
    const double span = nextStart - currentStart;
    if (span > 0.0) t = (hourForLerp - currentStart) / span;
    if (t < 0.0) t = 0.0;
    if (t > 1.0) t = 1.0;
    if (span > 2.0) t = 0.0; // long Night/Day spans are plateaus; 1-2h bands blend.

    r = day_tint_lerp_channel(current.r, next.r, t);
    g = day_tint_lerp_channel(current.g, next.g, t);
    b = day_tint_lerp_channel(current.b, next.b, t);
    alpha = day_tint_lerp_channel(current.alpha, next.alpha, t);
    if (alpha > DAYTINT_MAX_ALPHA) alpha = DAYTINT_MAX_ALPHA;
}

} // namespace desktopgrass
