// Constants.cs - all defaults from docs/architecture.md §11.
// This file is referenced by the unit tests as the single source of truth
// for spec constants. Keep field names and values in lock-step with the spec.

using System;
using System.Numerics;

namespace DesktopGrass.Win2D;

public enum Scene { Grass = 0, Desert = 1, Winter = 2 }

// Critter (§13.3). Grass-scene ambient critters plus legacy tray selectors.
// Cross-impl-locked discriminants.
public enum CritterKind : byte { None = 0, Sheep = 1, Cat = 2, Bunny = 3 }

internal static class Constants
{
    // Layout (§2, §8, §9)
    public const double STRIP_HEIGHT = 80.0;       // DIP - visible grass band height
    public const double HEADROOM = 30.0;           // DIP - extra above strip for sway/gust
    // Blade generation (§4, §5)
    public const double BLADE_SPACING_MIN = 4.0;
    public const double BLADE_SPACING_MAX = 8.0;
    public const double BLADE_HEIGHT_MIN = 6.0;    // DIP - minimum blade height
    public const double BLADE_HEIGHT_MAX = 30.0;   // DIP - maximum blade height
    public const double BLADE_THICKNESS_MIN = 1.0;
    public const double BLADE_THICKNESS_MAX = 2.5;
    public const double STIFFNESS_MIN = 0.6;
    public const double STIFFNESS_MAX = 1.0;
    public const int PALETTE_SIZE = 6;

    // Sway physics (§6)
    public static readonly double BASE_SWAY_SPEED = Math.PI / 3.0; // rad/sec → 6-sec period
    public const double BASE_AMPLITUDE = 3.3;          // DIP peak sway lean
    public const double DECAY_RATE = 2.5;              // /sec gust decay
    public const double GUST_TO_LEAN_FACTOR = 0.75;    // DIP*sec/rad

    // Gust (§8)
    public const double MAX_CURSOR_SPEED = 4000.0;     // DIP/sec
    public const double IMPULSE_SCALE = 0.003;         // rad/DIP
    public const double GUST_RADIUS = 150.0;           // DIP
    public const double CURSOR_REINIT_GAP_SEC = 0.25;  // sec

    // Cut (§9)
    public const double CUT_RADIUS = 30.0;             // DIP
    public const double CUT_DURATION_SEC = 0.2;        // sec

    // Regrowth (§9 "Regrowth"). Per-blade jitter values sampled at generation
    // from a separate xorshift64 stream seeded with `seed XOR REGROW_PRNG_SALT`
    // — this keeps the main PRNG stream unchanged so blade positions/heights
    // are bit-identical to the pre-regrowth implementation.
    public const double REGROW_DELAY_MIN     = 30.0;   // sec
    public const double REGROW_DELAY_MAX     = 90.0;   // sec
    public const double REGROW_DURATION_MIN  = 2.0;    // sec
    public const double REGROW_DURATION_MAX  = 4.0;    // sec
    public const ulong  REGROW_PRNG_SALT     = 0xDEADBEEFCAFEBABEUL;

    // Flower stream (§4, §5, §7). Independent PRNG stream salted with
    // FLOWER_PRNG_SALT so the main stream stays bit-identical.
    public const double FLOWER_PROBABILITY        = 0.04;
    public const double FLOWER_HEIGHT_BONUS_MIN   = 1.2;
    public const double FLOWER_HEIGHT_BONUS_MAX   = 1.5;
    public const double FLOWER_HEAD_RADIUS_MIN    = 1.8;  // DIP
    public const double FLOWER_HEAD_RADIUS_MAX    = 3.0;  // DIP
    public const int    FLOWER_PALETTE_SIZE       = 6;
    public const ulong  FLOWER_PRNG_SALT          = 0xC0FFEEFACE0FFE5UL;

    // Flower palette (§4) — 6 ARGB colors used for flower heads.
    public static readonly uint[] FLOWER_PALETTE =
    {
        0xFFFFEB3B, // 0 yellow (dandelion)
        0xFFFFA726, // 1 orange (marigold)
        0xFFFF80AB, // 2 pink (cosmos)
        0xFFE1BEE7, // 3 lavender
        0xFFFFFFFF, // 4 white (daisy)
        0xFFEF5350, // 5 red (poppy)
    };

    // Mushroom stream (§4, §5, §7). Independent PRNG stream salted with
    // MUSHROOM_PRNG_SALT so main / regrowth / flower stay bit-identical.
    public const double MUSHROOM_PROBABILITY        = 0.025;
    public const double MUSHROOM_CAP_WIDTH_MIN      = 4.0;
    public const double MUSHROOM_CAP_WIDTH_MAX      = 8.0;
    public const double MUSHROOM_CAP_HEIGHT_MIN     = 2.5;
    public const double MUSHROOM_CAP_HEIGHT_MAX     = 5.0;
    public const double MUSHROOM_STEM_HEIGHT_MIN    = 4.0;
    public const double MUSHROOM_STEM_HEIGHT_MAX    = 10.0;
    public const double MUSHROOM_STEM_THICKNESS_MIN = 2.0;
    public const double MUSHROOM_STEM_THICKNESS_MAX = 4.0;
    public const int    MUSHROOM_PALETTE_SIZE       = 6;
    public const ulong  MUSHROOM_PRNG_SALT          = 0xBADC0FFEE0FACE21UL;
    public const uint   MUSHROOM_STEM_COLOR         = 0xFFF5F5DC; // beige/ivory

    // Mushroom palette (§4) — 6 ARGB cap colors. Stems are always
    // MUSHROOM_STEM_COLOR.
    public static readonly uint[] MUSHROOM_PALETTE =
    {
        0xFFD32F2F, // 0 red (amanita)
        0xFF8D6E63, // 1 brown
        0xFFC9A66B, // 2 tan
        0xFFFFF8E1, // 3 ivory
        0xFFE57373, // 4 dusty pink
        0xFF6D4C41, // 5 dark brown
    };

    // Ambient gusts (§8.1). Fifth independent PRNG stream salted with
    // AMBIENT_GUST_PRNG_SALT so the static §12 blade snapshot is
    // untouched. Per-fire draw order is x, signDir, magFactor, interval —
    // exactly four draws per emitted puff.
    public const ulong  AMBIENT_GUST_PRNG_SALT       = 0xB7EE2EE2B7EE2EE2UL;
    public const double AMBIENT_GUST_INTERVAL_MIN    = 5.0;   // sec
    public const double AMBIENT_GUST_INTERVAL_MAX    = 15.0;  // sec
    public const double AMBIENT_GUST_MAG_FACTOR_MIN  = 0.3;   // unitless
    public const double AMBIENT_GUST_MAG_FACTOR_MAX  = 0.6;
    public const double AMBIENT_GUST_RADIUS_FACTOR   = 0.5;   // unitless

    // Desert scene shrinks non-cactus, non-mushroom blade heights at render
    // time so cacti read as the dominant biome feature.
    public const double DESERT_GRASS_HEIGHT_SCALE    = 0.5;
    public const double WINTER_GRASS_HEIGHT_SCALE    = 0.5;

    // Cacti (§14). Slot-bound Desert blade variants generated from an
    // independent stream so the static §12 blade snapshot is untouched.
    public const double CACTUS_PROBABILITY         = 0.005;
    public const double CACTUS_HEIGHT_MIN          = 30.0;
    public const double CACTUS_HEIGHT_MAX          = 70.0;
    public const double CACTUS_WIDTH_MIN           = 8.0;
    public const double CACTUS_WIDTH_MAX           = 14.0;
    public const double CACTUS_ARM_PROBABILITY     = 0.55;
    public const double CACTUS_TWO_ARM_PROBABILITY = 0.35;
    public const uint   CACTUS_COLOR               = 0xFF2D7A2D;
    public const ulong  CACTUS_PRNG_SALT           = 0xCAC75CAC75CAC75CUL;

    // Tumbleweeds (§14). Desert roaming entities generated and respawned from
    // a persistent stream seeded with seed XOR TUMBLEWEED_PRNG_SALT.
    public const int    TUMBLEWEED_COUNT_PER_1920DIP = 4;
    public const double TUMBLEWEED_SIZE_MIN          = 8.0;
    public const double TUMBLEWEED_SIZE_MAX          = 18.0;
    public const double TUMBLEWEED_SPEED_MIN         = 40.0;
    public const double TUMBLEWEED_SPEED_MAX         = 120.0;
    public const double TUMBLEWEED_Y_OFFSET_MIN      = 8.0;
    public const double TUMBLEWEED_Y_OFFSET_MAX      = 20.0;
    public const uint   TUMBLEWEED_COLOR             = 0xFF8A6A3D;
    public const ulong  TUMBLEWEED_PRNG_SALT         = 0x7B0117CA7B0117CAUL;

    // Bezier rendering (§7)
    public const double CUT_STUMP_THRESHOLD = 0.05;
    public const double STUMP_HEIGHT = 2.0;            // DIP
    public const double MUSHROOM_STUMP_HEIGHT = 4.0;   // DIP — slightly taller than the grass stub
    public const double CTRL_OFFSET_FACTOR = 0.6;
    public const double MAX_LEAN_FRACTION = 0.95;      // fraction of blade length that the tip may horizontally displace; clamps gust impulses so the blade never folds completely flat.

    // Canonical seed for snapshot tests (§12)
    public const ulong CANONICAL_TEST_SEED = 0x6B6173746FUL;

    // Density override the plan calls for ~600 blades / 1920 px monitor.
    public const double DEFAULT_DENSITY = 2.25;

    // Palette (§4) - exactly 6 ARGB colors, alpha always 0xFF.
    public static readonly uint[] PALETTE =
    {
        0xFF2C5E1A, // 0 deep forest
        0xFF3A7A24, // 1 dark green
        0xFF4C9A2E, // 2 mid green
        0xFF66B845, // 3 grass green
        0xFF7AC957, // 4 bright green
        0xFF8FD96A, // 5 light green
    };

    // Scenes (§13).
    public const int    SCENE_COUNT   = 3;
    public const Scene  SCENE_DEFAULT = Scene.Grass;

    // Desert palette (§13) — sandy/tan tones.
    public static readonly uint[] DESERT_PALETTE =
    {
        0xFFC9A26B, // 0 light sand
        0xFFB48A56, // 1 dark sand
        0xFFD9B57A, // 2 pale sand
        0xFF8F6E3F, // 3 deep tan
        0xFFE6C896, // 4 very pale sand
        0xFFA67843, // 5 medium tan
    };

    // Winter palette (§13) — frosty/icy tones.
    public static readonly uint[] WINTER_PALETTE =
    {
        0xFFE8EEF5, // 0 frost white
        0xFFB7C4D2, // 1 ice blue
        0xFFCBD8E5, // 2 pale ice
        0xFFD7E2EE, // 3 light frost
        0xFFA8B7C6, // 4 deep frost
        0xFFEEF3F8, // 5 snow white
    };

    // 2D lookup: SCENE_PALETTES[(int)scene, hue]. Row 0 must equal PALETTE.
    public static readonly uint[,] SCENE_PALETTES = new uint[SCENE_COUNT, PALETTE_SIZE]
    {
        { PALETTE[0],        PALETTE[1],        PALETTE[2],        PALETTE[3],        PALETTE[4],        PALETTE[5]        },
        { DESERT_PALETTE[0], DESERT_PALETTE[1], DESERT_PALETTE[2], DESERT_PALETTE[3], DESERT_PALETTE[4], DESERT_PALETTE[5] },
        { WINTER_PALETTE[0], WINTER_PALETTE[1], WINTER_PALETTE[2], WINTER_PALETTE[3], WINTER_PALETTE[4], WINTER_PALETTE[5] },
    };

    // Roaming-entity subsystem (§13.2). Caps Sim.Entities so the snowflake
    // emitter can't grow without bound; matches Native MAX_ENTITIES_PER_MONITOR.
    public const int MAX_ENTITIES_PER_MONITOR = 64;

    // Critter subsystem (§13.3). Grass-scene ambient critters plus legacy tray selectors.
    // CritterKind discriminants are cross-impl-locked with Native.
    public const int        CRITTER_COUNT      = 4;
    public const CritterKind CRITTER_DEFAULT   = CritterKind.None;
    public const ulong      CRITTER_PRNG_SALT  = 0x5C8EE05C8EE05C8Eul;
    public static readonly int[] PET_COUNT_OPTIONS = { 1, 2, 3, 4, 5, 6 };
    public const int        PET_COUNT_DEFAULT_SHEEP = 2;
    public const int        PET_COUNT_DEFAULT_CAT   = 1;
    public const int        PET_COUNT_MAX_PER_MONITOR = 6;
    public static readonly string[] SHEEP_NAME_POOL =
    {
        "Bessie", "Wooly", "Clover", "Daisy", "Pippin", "Buttercup", "Mossy", "Hazel",
    };
    public static readonly string[] CAT_NAME_POOL =
    {
        "Mittens", "Whiskers", "Shadow", "Ginger", "Smokey", "Boots", "Sage", "Juno",
    };
    public static readonly string[] BUNNY_NAME_POOL =
    {
        "Clover", "Hazel", "Thumper", "Mochi", "Pip", "Acorn",
        "Biscuit", "Willow", "Pepper", "Hopper", "Juniper", "Snowdrop",
    };
    public const double      PET_NAME_HOVER_RADIUS = 50.0;
    public const double      PET_NAME_FADE_DURATION = 1.5;
    public const double      PET_NAME_FONT_SIZE = 11.0;
    public const double      PET_NAME_OFFSET_Y = -8.0;
    public const uint        PET_NAME_COLOR = 0xFFFFFFFFu;
    public const uint        PET_NAME_SHADOW_COLOR = 0xC0000000u;

    // Sheep (§16). Procedurally drawn pet that walks, grazes, idles, sleeps,
    // and hops along the bottom strip. Counts/speeds/sizes are sampled per
    // monitor from the critter PRNG so different displays get different flocks.
    public const int    SHEEP_COUNT_MIN      = 2;
    public const int    SHEEP_COUNT_MAX      = 3;
    public const double SHEEP_WALK_SPEED_MIN = 14.0;
    public const double SHEEP_WALK_SPEED_MAX = 26.0;

    public const double SHEEP_BODY_RADIUS    = 12.0;
    public const double SHEEP_BODY_HEIGHT    = 9.5;
    public const double SHEEP_HEAD_RADIUS    = 5.0;
    public const double SHEEP_LEG_LENGTH     = 5.5;
    public const double SHEEP_TAIL_RADIUS    = 3.2;

    public const uint   SHEEP_BODY_COLOR     = 0xFFF7F4EBu;
    public const uint   SHEEP_LEG_COLOR      = 0xFF1F1A16u;
    public const uint   SHEEP_FACE_COLOR     = 0xFF1F1A16u;
    public const uint   SHEEP_EAR_COLOR      = 0xFF14110Eu;
    public const uint   SHEEP_INK_COLOR      = 0xFFF7F4EBu;

    public const double SHEEP_WALK_PERIOD     = 0.55;
    public const double SHEEP_LEG_CYCLE_AMP   = 2.0;
    public const double SHEEP_HEAD_BOB_AMP    = 0.7;
    public const double SHEEP_TAIL_WIGGLE_AMP = 0.6;

    public const byte   SHEEP_STATE_WALKING  = 0;
    public const byte   SHEEP_STATE_GRAZING  = 1;
    public const byte   SHEEP_STATE_IDLE     = 2;
    public const byte   SHEEP_STATE_SLEEPING = 3;
    public const byte   SHEEP_STATE_HOPPING  = 4;
    public const byte   SHEEP_STATE_GREETING = 5;

    public const double SHEEP_WALK_DURATION_MIN   = 8.0;
    public const double SHEEP_WALK_DURATION_MAX   = 14.0;
    public const double SHEEP_GRAZE_DURATION_MIN  = 3.0;
    public const double SHEEP_GRAZE_DURATION_MAX  = 5.0;
    public const double SHEEP_IDLE_DURATION_MIN   = 1.5;
    public const double SHEEP_IDLE_DURATION_MAX   = 3.0;
    public const double SHEEP_SLEEP_DURATION_MIN  = 8.0;
    public const double SHEEP_SLEEP_DURATION_MAX  = 16.0;
    public const double SHEEP_HOP_DURATION        = 0.55;
    public const double SHEEP_GREET_RADIUS        = 50.0;
    public const double SHEEP_GREET_DURATION_MIN  = 1.6;
    public const double SHEEP_GREET_DURATION_MAX  = 2.8;
    public const double SHEEP_GREET_MIN_AGE       = 1.5;
    public const double SHEEP_CURIOUS_RADIUS      = 80.0;
    public const double SHEEP_CURIOUS_HEAD_TURN_MAX = 0.55;

    public const double SHEEP_GRAZE_PROBABILITY    = 0.60;
    public const double SHEEP_IDLE_PROBABILITY     = 0.25;
    public const double SHEEP_SLEEP_PROB_MORNING   = 0.10;
    public const double SHEEP_SLEEP_PROB_DEFAULT   = 0.30;
    public const double SHEEP_SLEEP_PROB_NIGHT     = 0.70;
    public const int    SHEEP_MORNING_START_HOUR   = 6;
    public const int    SHEEP_MORNING_END_HOUR     = 10;
    public const int    SHEEP_NIGHT_START_HOUR     = 22;
    public const int    SHEEP_NIGHT_END_HOUR       = 6;
    public const double SHEEP_SLEEP_FROM_IDLE_PROB = SHEEP_SLEEP_PROB_DEFAULT;

    public const double SHEEP_IDLE_SWEEP_FREQ      = 1.4;
    public const double SHEEP_GRAZE_MUNCH_FREQ     = 8.0;
    public const double SHEEP_GRAZE_MUNCH_AMP      = 0.6;
    public const double SHEEP_GREET_HEAD_BOB_FREQ  = 4.5;
    public const double SHEEP_GREET_HEAD_BOB_AMP   = 0.7;

    public const double SHEEP_HOP_HEIGHT     = 11.0;
    public const double SHEEP_STARTLE_RADIUS = 64.0;
    public const double SHEEP_STARTLE_BOOST  = 1.6;

    public const double SHEEP_ZZZ_CYCLE_SEC  = 1.8;
    public const double SHEEP_ZZZ_RISE       = 11.0;
    public const double SHEEP_ZZZ_SIZE_START = 2.0;
    public const double SHEEP_ZZZ_SIZE_END   = 4.0;

    // Cat (§17). Calm color-varied critter that reuses the sheep state byte values but
    // only uses Walking, Idle, Sleeping, and Hopping (semantically Pouncing).
    public const int    CAT_COUNT_MIN      = 1;
    public const int    CAT_COUNT_MAX      = 2;
    public const double CAT_WALK_SPEED_MIN = 10.0;
    public const double CAT_WALK_SPEED_MAX = 22.0;
    public const double CAT_POUNCE_SPEED   = 60.0;

    public const double CAT_BODY_RADIUS    = 11.0;
    public const double CAT_BODY_HEIGHT    = 7.0;
    public const double CAT_HEAD_RADIUS    = 4.5;
    public const double CAT_LEG_LENGTH     = 5.0;
    public const double CAT_TAIL_LENGTH    = 13.0;
    public const double CAT_TAIL_THICKNESS = 1.6;
    public const double CAT_EAR_HEIGHT     = 4.5;

    public const int    CAT_COAT_VARIANT_COUNT = 6;

    public readonly record struct CatCoatPalette(uint Body, uint Leg, uint Face, uint Ear, uint Ink);

    public static readonly CatCoatPalette[] CAT_COAT_PALETTES = new[]
    {
        new CatCoatPalette(0xFF6B6259u, 0xFF3D3733u, 0xFF6B6259u, 0xFF3D3733u, 0xFF1A1614u), // 0 Gray
        new CatCoatPalette(0xFFD89A6Fu, 0xFFA56B40u, 0xFFD89A6Fu, 0xFFA56B40u, 0xFF2B1A0Eu), // 1 Orange
        new CatCoatPalette(0xFF2A2522u, 0xFF140F0Cu, 0xFF2A2522u, 0xFF140F0Cu, 0xFFD9B85Bu), // 2 Black
        new CatCoatPalette(0xFFEDE9E1u, 0xFFBDB7ABu, 0xFFEDE9E1u, 0xFFBDB7ABu, 0xFF1F1817u), // 3 White
        new CatCoatPalette(0xFF7A5F3Cu, 0xFF4E3F26u, 0xFF7A5F3Cu, 0xFF4E3F26u, 0xFF1A1108u), // 4 Brown
        new CatCoatPalette(0xFFC9B898u, 0xFF8E7F6Bu, 0xFFC9B898u, 0xFF8E7F6Bu, 0xFF2E251Du), // 5 Cream
    };

    // Backward-compat aliases: variant 0 preserves the original muted gray tabby.
    public const uint   CAT_BODY_COLOR     = 0xFF6B6259u;
    public const uint   CAT_LEG_COLOR      = 0xFF3D3733u;
    public const uint   CAT_FACE_COLOR     = 0xFF6B6259u;
    public const uint   CAT_EAR_COLOR      = 0xFF3D3733u;
    public const uint   CAT_INK_COLOR      = 0xFF1A1614u;

    public const double CAT_WALK_PERIOD    = 0.50;
    public const double CAT_LEG_CYCLE_AMP  = 1.6;
    public const double CAT_HEAD_BOB_AMP   = 0.4;
    public const double CAT_TAIL_SWAY_FREQ = 1.2;
    public const double CAT_TAIL_SWAY_AMP  = 0.35;

    public const byte   CAT_STATE_WALKING  = SHEEP_STATE_WALKING;   // 0
    public const byte   CAT_STATE_IDLE     = SHEEP_STATE_IDLE;      // 2, sit-and-watch
    public const byte   CAT_STATE_SLEEPING = SHEEP_STATE_SLEEPING;  // 3
    public const byte   CAT_STATE_POUNCING = SHEEP_STATE_HOPPING;   // 4, semantic alias

    public const double CAT_WALK_DURATION_MIN  = 6.0;
    public const double CAT_WALK_DURATION_MAX  = 10.0;
    public const double CAT_IDLE_DURATION_MIN  = 4.0;
    public const double CAT_IDLE_DURATION_MAX  = 8.0;
    public const double CAT_SLEEP_DURATION_MIN = 20.0;
    public const double CAT_SLEEP_DURATION_MAX = 40.0;
    public const double CAT_POUNCE_DURATION    = 0.45;

    public const double CAT_IDLE_PROBABILITY   = 0.65;
    public const double CAT_SLEEP_PROBABILITY  = 0.30;
    public const double CAT_SLEEP_FROM_IDLE_PROB_DEFAULT = 0.50;
    public const double CAT_SLEEP_FROM_IDLE_PROB_MORNING = 0.20;
    public const double CAT_SLEEP_FROM_IDLE_PROB_NIGHT   = 0.85;

    public const double CAT_POUNCE_RADIUS      = 80.0;
    public const double CAT_POUNCE_HEIGHT      = 9.0;
    public const double CAT_CURIOUS_RADIUS     = 100.0;
    public const double CAT_CURIOUS_HEAD_TURN_MAX = 0.7;

    // Bunny (§18). Grass-only woodland critter: shy, passive, and always hopping
    // when it moves. Generated after sheep and cats from the shared critter PRNG.
    public const int    BUNNY_COUNT_MIN          = 1;
    public const int    BUNNY_COUNT_MAX          = 2;
    public const double BUNNY_HOP_SPEED_MIN      = 22.0;
    public const double BUNNY_HOP_SPEED_MAX      = 38.0;
    public const double BUNNY_BODY_RADIUS        = 8.0;
    public const double BUNNY_BODY_HEIGHT        = 6.5;
    public const double BUNNY_HEAD_RADIUS        = 4.2;
    public const double BUNNY_EAR_HEIGHT         = 9.0;
    public const double BUNNY_EAR_WIDTH          = 2.2;
    public const double BUNNY_EAR_SPACING        = 3.0;
    public const double BUNNY_LEG_LENGTH         = 4.0;
    public const double BUNNY_TAIL_RADIUS        = 2.4;
    public const uint   BUNNY_BODY_COLOR         = 0xFF8A6A4Au;
    public const uint   BUNNY_BELLY_COLOR        = 0xFFC4A98Du;
    public const uint   BUNNY_EAR_COLOR          = 0xFF8A6A4Au;
    public const uint   BUNNY_EAR_INNER_COLOR    = 0xFFD9A0A0u;
    public const uint   BUNNY_TAIL_COLOR         = 0xFFF7F4EBu;
    public const uint   BUNNY_EYE_COLOR          = 0xFF1A1208u;
    public const uint   BUNNY_NOSE_COLOR         = 0xFF8A4040u;

    public const byte   BUNNY_STATE_HOPPING      = 0;
    public const byte   BUNNY_STATE_GRAZING      = 1;
    public const byte   BUNNY_STATE_IDLE         = 2;
    public const byte   BUNNY_STATE_SLEEPING     = 3;
    public const byte   BUNNY_STATE_STARTLED     = 4;

    public const double BUNNY_HOP_DURATION       = 0.40;
    public const double BUNNY_HOP_HEIGHT         = 8.0;
    public const double BUNNY_HOP_GAP_MIN        = 0.05;
    public const double BUNNY_HOP_GAP_MAX        = 0.20;
    public const double BUNNY_GRAZE_DURATION_MIN = 2.5;
    public const double BUNNY_GRAZE_DURATION_MAX = 4.5;
    public const double BUNNY_IDLE_DURATION_MIN  = 2.0;
    public const double BUNNY_IDLE_DURATION_MAX  = 4.0;
    public const double BUNNY_SLEEP_DURATION_MIN = 6.0;
    public const double BUNNY_SLEEP_DURATION_MAX = 12.0;
    public const double BUNNY_GRAZE_PROBABILITY  = 0.55;
    public const double BUNNY_IDLE_PROBABILITY   = 0.30;
    public const double BUNNY_SLEEP_PROB_DAY     = 0.05;
    public const double BUNNY_SLEEP_PROB_NIGHT   = 0.40;

    public const double BUNNY_STARTLE_RADIUS     = 90.0;
    public const double BUNNY_STARTLE_BOOST      = 2.0;
    public const double BUNNY_STARTLE_HOP_HEIGHT = 14.0;
    public const double BUNNY_STARTLE_DURATION   = 3.0;

    public const double BUNNY_NOSE_TWITCH_FREQ   = 6.0;
    public const double BUNNY_NOSE_TWITCH_AMP    = 0.5;
    public const double BUNNY_EAR_WIGGLE_FREQ    = 1.2;
    public const double BUNNY_EAR_WIGGLE_AMP     = 0.20;

    public const double BUNNY_ZZZ_CYCLE_SEC      = SHEEP_ZZZ_CYCLE_SEC;
    public const double BUNNY_ZZZ_RISE           = SHEEP_ZZZ_RISE * 0.7;
    public const double BUNNY_ZZZ_SIZE_START     = SHEEP_ZZZ_SIZE_START * 0.7;
    public const double BUNNY_ZZZ_SIZE_END       = SHEEP_ZZZ_SIZE_END * 0.7;

    // Butterflies (§17.6). Grass-only, passive daytime ambient flyers.
    public const int    BUTTERFLY_COUNT_MIN          = 2;
    public const int    BUTTERFLY_COUNT_MAX          = 4;
    public const double BUTTERFLY_SPEED_MIN          = 18.0;
    public const double BUTTERFLY_SPEED_MAX          = 32.0;
    public const double BUTTERFLY_BODY_LENGTH        = 2.4;
    public const double BUTTERFLY_WING_RADIUS        = 3.5;
    public const double BUTTERFLY_WING_OFFSET        = 2.2;
    public const double BUTTERFLY_FLUTTER_FREQ       = 16.0;
    public const double BUTTERFLY_FLUTTER_MIN_SCALE  = 0.20;
    public const double BUTTERFLY_MEANDER_FREQ_Y     = 0.8;
    public const double BUTTERFLY_MEANDER_AMP_Y      = 16.0;
    public const double BUTTERFLY_MEANDER_FREQ_X     = 0.5;
    public const double BUTTERFLY_MEANDER_AMP_X      = 0.4;
    public const double BUTTERFLY_ALTITUDE_MIN       = 18.0;
    public const double BUTTERFLY_ALTITUDE_MAX       = 70.0;
    public const uint   BUTTERFLY_BODY_COLOR         = 0xFF2A2018u;
    public const int    BUTTERFLY_COLOR_COUNT        = 5;
    public const int    BUTTERFLY_HOUR_START         = 6;
    public const int    BUTTERFLY_HOUR_END           = 19;
    public const int    BUTTERFLY_FADE_DURATION_HOUR = 1;
    public const ulong  BUTTERFLY_PRNG_SALT          = 0xB07DEF1E0001ul;

    public readonly record struct ButterflyPalette(uint WingColor, uint AccentColor);
    public static readonly ButterflyPalette[] BUTTERFLY_PALETTES =
    {
        new(0xFFFF9A2Eu, 0xFF1A130Cu), // 0 Monarch: orange + black tips
        new(0xFFFFD34Du, 0xFF1A130Cu), // 1 Swallowtail: yellow + black
        new(0xFFFFF8E8u, 0xFF3A3A3Au), // 2 Cabbage: white + dark dots
        new(0xFF63C7FFu, 0xFF1B4D99u), // 3 Morpho: sky blue + deeper blue
        new(0xFFFFA6C8u, 0xFFFF6EA8u), // 4 Pink: soft pink + rose
    };

    // Fireflies (§17.7). Grass-only, passive nighttime ambient flyers.
    public const int    FIREFLY_COUNT_MIN            = 3;
    public const int    FIREFLY_COUNT_MAX            = 6;
    public const double FIREFLY_DRIFT_SPEED_MIN      = 4.0;
    public const double FIREFLY_DRIFT_SPEED_MAX      = 10.0;
    public const double FIREFLY_BODY_RADIUS          = 1.2;
    public const double FIREFLY_GLOW_RADIUS          = 5.0;
    public const double FIREFLY_BLINK_PERIOD_MIN     = 1.4;
    public const double FIREFLY_BLINK_PERIOD_MAX     = 2.6;
    public const double FIREFLY_BLINK_DUTY           = 0.55;
    public const double FIREFLY_BLINK_FADE           = 0.30;
    public const double FIREFLY_DRIFT_FREQ_X         = 0.4;
    public const double FIREFLY_DRIFT_FREQ_Y         = 0.6;
    public const double FIREFLY_DRIFT_AMP_X          = 0.6;
    public const double FIREFLY_DRIFT_AMP_Y          = 8.0;
    public const double FIREFLY_ALTITUDE_MIN         = 8.0;
    public const double FIREFLY_ALTITUDE_MAX         = 55.0;
    public const uint   FIREFLY_BODY_COLOR           = 0xFFFFEE88u;
    public const uint   FIREFLY_GLOW_COLOR_RGB       = 0xEEDD66u;
    public const int    FIREFLY_GLOW_ALPHA_MAX       = 110;
    public const int    FIREFLY_BODY_ALPHA_MAX       = 255;
    public const int    FIREFLY_NIGHT_START_HOUR     = 20;
    public const int    FIREFLY_NIGHT_END_HOUR       = 6;
    public const int    FIREFLY_FADE_DURATION_HOUR   = 1;
    public const ulong  FIREFLY_PRNG_SALT            = 0xF13EF1E7777ul;

    // Bird flybys (§17.8). Grass-only daytime transient flocks.
    public const double BIRD_FLYBY_SPAWN_RATE_PER_HOUR = 15.0;
    public const int    BIRD_FLYBY_HOUR_START           = 7;
    public const int    BIRD_FLYBY_HOUR_END             = 19;
    public const int    BIRD_FLOCK_SIZE_MIN             = 3;
    public const int    BIRD_FLOCK_SIZE_MAX             = 7;
    public const double BIRD_FLOCK_FORMATION_SPACING    = 9.0;
    public const double BIRD_FLOCK_V_ANGLE_DEG          = 22.0;
    public const double BIRD_SPEED_MIN                  = 65.0;
    public const double BIRD_SPEED_MAX                  = 95.0;
    public const double BIRD_ALTITUDE_MIN               = 78.0;
    public const double BIRD_ALTITUDE_MAX               = 96.0;
    public const double BIRD_BODY_LENGTH                = 3.6;
    public const double BIRD_WING_SPAN                  = 5.0;
    public const double BIRD_WING_FLAP_FREQ             = 7.0;
    public const double BIRD_WING_FLAP_PHASE_JITTER     = 0.6;
    public const uint   BIRD_BODY_COLOR                 = 0xFF1A1610u;
    public const double BIRD_WING_OPEN_RATIO            = 1.0;
    public const double BIRD_WING_FOLD_RATIO            = 0.30;
    public const double BIRD_FADE_IN_FRAC               = 0.08;
    public const double BIRD_FADE_OUT_FRAC              = 0.08;
    public const double BIRD_DRIFT_AMP_Y                = 3.0;
    public const double BIRD_DRIFT_FREQ_Y               = 0.8;
    public const ulong  BIRD_FLYBY_PRNG_SALT            = 0xB12D1F1A1B12D1Aul;

    // Snowflakes (§15)
    public const double SNOWFLAKE_EMIT_RATE_PER_1920DIP = 8.0;
    public const double SNOWFLAKE_FALL_SPEED_MIN = 20.0;
    public const double SNOWFLAKE_FALL_SPEED_MAX = 40.0;
    public const double SNOWFLAKE_SIZE_MIN = 1.5;
    public const double SNOWFLAKE_SIZE_MAX = 3.0;
    public const double SNOWFLAKE_SWAY_AMPLITUDE = 10.0;
    public const double SNOWFLAKE_SWAY_FREQUENCY = 0.6;
    public const double SNOWFLAKE_LIFETIME_PADDING_SEC = 2.0;
    public const uint SNOWFLAKE_COLOR = 0xFFFFFFFFu;
    public const ulong SNOWFLAKE_PRNG_SALT = 0xC0FFEE1CECAFEBABul;

    // Snow accumulation (§15.2). Passive Winter-only layer on the strip baseline.
    public const double SNOW_ACCUMULATION_RATE = 0.012;
    public const double SNOW_DEPTH_MAX = 30.0;
    public const double SNOW_DEPTH_MIN_RENDER = 0.3;
    public const uint SNOW_LAYER_COLOR_TOP = 0xFFFFFFFFu;
    public const uint SNOW_LAYER_COLOR_BOTTOM = 0xFFE8E8F0u;
    public const uint SNOW_LAYER_HIGHLIGHT = 0xFFFFFFFFu;
    public const double SNOW_TOP_UNDULATION_AMP = 2.5;
    public const double SNOW_TOP_UNDULATION_WAVELENGTH = 90.0;
    public const ulong SNOW_TOP_UNDULATION_PHASE_SALT = 0x5E0A1ul;

    // Light rain (§20). Dedicated "rain drop" PRNG stream. Draw order per drop:
    // size, x, fallSpeed, vx, seed, then the exponential next-spawn interval.
    public const ulong RAINDROP_PRNG_SALT = 0xD40F0A1DD40F0A1Dul;
    public const double RAINDROP_EMIT_RATE_PER_1920DIP = 6.0;
    public const double RAINDROP_LENGTH_MIN = 4.0;
    public const double RAINDROP_LENGTH_MAX = 7.0;
    public const double RAINDROP_THICKNESS = 0.9;
    public const double RAINDROP_FALL_SPEED_MIN = 240.0;
    public const double RAINDROP_FALL_SPEED_MAX = 360.0;
    public const double RAINDROP_DRIFT_MIN = -8.0;
    public const double RAINDROP_DRIFT_MAX = 8.0;
    public const uint RAINDROP_COLOR = 0x88B0C4D0u;
    public const double RAINDROP_LIFETIME_PADDING_SEC = 0.3;

    // Snow-tipped blade caps (§15)
    public const double SNOW_TIP_RADIUS_FACTOR = 1.25;
    public const uint SNOW_TIP_COLOR = 0xFFFFFFFFu;

    // Pine trees (§15.1). Winter biome anchor — slot-bound, mirrors §14 cacti.
    public const double PINE_PROBABILITY        = 0.0075;
    public const double PINE_HEIGHT_MIN         = 45.0;
    public const double PINE_HEIGHT_MAX         = 90.0;
    public const double PINE_WIDTH_MIN          = 28.0;
    public const double PINE_WIDTH_MAX          = 48.0;
    public const int    PINE_TIER_COUNT_MIN     = 2;
    public const int    PINE_TIER_COUNT_MAX     = 4;
    public const double PINE_TIP_TAPER          = 0.25;
    public const double PINE_TIER_OVERLAP       = 0.15;
    public const double PINE_SNOW_CAP_FRACTION  = 0.30;
    public const uint   PINE_COLOR              = 0xFF1B5E20u;
    public const ulong  PINE_PRNG_SALT          = 0x50494E4550494E45ul;

    // Birch tree variant (§15.1). Second tree style — vertical white trunk
    // with dark bark marks and short bare branches.
    public const double BIRCH_VARIANT_PROBABILITY = 0.30;
    public const double BIRCH_TRUNK_WIDTH_MIN     = 4.0;
    public const double BIRCH_TRUNK_WIDTH_MAX     = 7.0;
    public const int    BIRCH_BARK_MARK_COUNT     = 5;
    public const double BIRCH_BARK_MARK_LENGTH_FRAC = 0.50;
    public const int    BIRCH_BRANCH_COUNT        = 6;
    public const double BIRCH_SNOW_CAP_FRACTION   = 0.18;
    public const uint   BIRCH_BARK_COLOR          = 0xFFEFEFE6u;
    public const uint   BIRCH_MARK_COLOR          = 0xFF2A2A28u;

    // Day-night ambient tint (§19). Pure render overlay; no simulation state.
    public readonly record struct DayTintPhase(float StartHour, byte R, byte G, byte B, byte Alpha);

    public const bool DAYTINT_ENABLED_DEFAULT = true;
    public const byte DAYTINT_MAX_ALPHA = 36;

    public static readonly DayTintPhase[] DAYTINT_PHASES =
    {
        new( 0.0f,  40,  50,  90, 36), // Night (wraps from prev night)
        new( 4.0f,  60,  70, 110, 32), // Predawn
        new( 6.0f, 255, 180, 140, 28), // Sunrise
        new( 8.0f, 255, 220, 160, 16), // Morning
        new(10.0f, 255, 255, 255,  0), // Day - no tint
        new(17.0f, 240, 170, 110, 22), // Late afternoon
        new(19.0f, 220, 110,  90, 30), // Sunset
        new(20.0f,  90,  80, 130, 28), // Dusk
        new(22.0f,  40,  50,  90, 36), // Night
    };

    public static Vector4 ComputeDayTint(double hourFloat)
    {
        double hour = NormalizeDayTintHour(hourFloat);

        int currentIndex = DAYTINT_PHASES.Length - 1;
        for (int i = 0; i < DAYTINT_PHASES.Length; i++)
        {
            if (hour >= DAYTINT_PHASES[i].StartHour)
            {
                currentIndex = i;
            }
            else
            {
                break;
            }
        }

        DayTintPhase current = DAYTINT_PHASES[currentIndex];
        DayTintPhase next = DAYTINT_PHASES[(currentIndex + 1) % DAYTINT_PHASES.Length];

        double currentStart = current.StartHour;
        double nextStart = next.StartHour;
        if (nextStart <= currentStart) nextStart += 24.0;

        double hourForLerp = hour;
        if (hourForLerp < currentStart) hourForLerp += 24.0;

        double t = 0.0;
        double span = nextStart - currentStart;
        if (span > 0.0) t = (hourForLerp - currentStart) / span;
        t = Math.Clamp(t, 0.0, 1.0);
        if (span > 2.0) t = 0.0; // long Night/Day spans are plateaus; 1-2h bands blend.

        byte r = LerpDayTintChannel(current.R, next.R, t);
        byte g = LerpDayTintChannel(current.G, next.G, t);
        byte b = LerpDayTintChannel(current.B, next.B, t);
        byte alpha = LerpDayTintChannel(current.Alpha, next.Alpha, t);
        if (alpha > DAYTINT_MAX_ALPHA) alpha = DAYTINT_MAX_ALPHA;

        return new Vector4(
            r / 255.0f,
            g / 255.0f,
            b / 255.0f,
            alpha / 255.0f);
    }

    private static double NormalizeDayTintHour(double hourFloat)
    {
        double hour = hourFloat % 24.0;
        if (hour < 0.0) hour += 24.0;
        return hour;
    }

    private static double AmbientClamp01(double value)
    {
        if (value <= 0.0) return 0.0;
        if (value >= 1.0) return 1.0;
        return value;
    }

    private static double AmbientSmoothstep01(double value)
    {
        double t = AmbientClamp01(value);
        return t * t * (3.0 - 2.0 * t);
    }

    public static double ButterflyFade(double hourFloat)
    {
        double hour = NormalizeDayTintHour(hourFloat);
        double fadeStart = BUTTERFLY_HOUR_START - BUTTERFLY_FADE_DURATION_HOUR;
        double start = BUTTERFLY_HOUR_START;
        double end = BUTTERFLY_HOUR_END;
        double fadeEnd = BUTTERFLY_HOUR_END + BUTTERFLY_FADE_DURATION_HOUR;

        if (hour >= fadeStart && hour < start) return AmbientClamp01((hour - fadeStart) / BUTTERFLY_FADE_DURATION_HOUR);
        if (hour >= start && hour < end) return 1.0;
        if (hour >= end && hour < fadeEnd) return AmbientClamp01((fadeEnd - hour) / BUTTERFLY_FADE_DURATION_HOUR);
        return 0.0;
    }

    public static double FireflyFade(double hourFloat)
    {
        double hour = NormalizeDayTintHour(hourFloat);
        double nightStart = FIREFLY_NIGHT_START_HOUR;
        double nightEnd = FIREFLY_NIGHT_END_HOUR;
        double fadeInStart = nightStart - FIREFLY_FADE_DURATION_HOUR;
        double fadeOutEnd = nightEnd + FIREFLY_FADE_DURATION_HOUR;

        if (hour >= nightStart || hour < nightEnd) return 1.0;
        if (hour >= fadeInStart && hour < nightStart) return AmbientClamp01((hour - fadeInStart) / FIREFLY_FADE_DURATION_HOUR);
        if (hour >= nightEnd && hour < fadeOutEnd) return AmbientClamp01((fadeOutEnd - hour) / FIREFLY_FADE_DURATION_HOUR);
        return 0.0;
    }

    public static double ButterflyWingScale(double timeSeconds, double phaseY)
    {
        double raw = Math.Cos(timeSeconds * BUTTERFLY_FLUTTER_FREQ + phaseY);
        if (raw < BUTTERFLY_FLUTTER_MIN_SCALE) return BUTTERFLY_FLUTTER_MIN_SCALE;
        if (raw > 1.0) return 1.0;
        return raw;
    }

    public static double BirdWingScale(double timeSeconds, double wingPhaseOffset)
    {
        double t = 0.5 + 0.5 * Math.Cos(timeSeconds * BIRD_WING_FLAP_FREQ + wingPhaseOffset);
        return BIRD_WING_FOLD_RATIO + (BIRD_WING_OPEN_RATIO - BIRD_WING_FOLD_RATIO) * t;
    }

    public static double BirdFadeAlpha(double x, double vx, double monitorWidth)
    {
        if (monitorWidth <= 0.0) return 0.0;
        double visibleSpan = monitorWidth;
        double fadeInDist = BIRD_FADE_IN_FRAC * visibleSpan;
        double fadeOutDist = BIRD_FADE_OUT_FRAC * visibleSpan;
        double alpha = 1.0;
        if (vx >= 0.0)
        {
            if (fadeInDist > 0.0 && x < fadeInDist) alpha = Math.Min(alpha, AmbientClamp01((x + 50.0) / fadeInDist));
            if (fadeOutDist > 0.0 && x > monitorWidth - fadeOutDist) alpha = Math.Min(alpha, AmbientClamp01((monitorWidth + 50.0 - x) / fadeOutDist));
        }
        else
        {
            if (fadeInDist > 0.0 && x > monitorWidth - fadeInDist) alpha = Math.Min(alpha, AmbientClamp01((monitorWidth + 50.0 - x) / fadeInDist));
            if (fadeOutDist > 0.0 && x < fadeOutDist) alpha = Math.Min(alpha, AmbientClamp01((x + 50.0) / fadeOutDist));
        }
        return AmbientClamp01(alpha);
    }

    public static double FireflyBlinkBrightness(double timeSeconds, double blinkPeriod, double blinkPhase)
    {
        if (blinkPeriod <= 0.0) return 0.0;
        double cycleT = (timeSeconds / blinkPeriod + blinkPhase) % 1.0;
        if (cycleT < 0.0) cycleT += 1.0;

        if (cycleT >= FIREFLY_BLINK_DUTY) return 0.0;

        double fadeFrac = AmbientClamp01(FIREFLY_BLINK_FADE / blinkPeriod);
        double brightness = 1.0;
        if (fadeFrac > 0.0)
        {
            if (cycleT < fadeFrac)
            {
                brightness = AmbientSmoothstep01(cycleT / fadeFrac);
            }
            else if (cycleT > FIREFLY_BLINK_DUTY - fadeFrac)
            {
                brightness = AmbientSmoothstep01((FIREFLY_BLINK_DUTY - cycleT) / fadeFrac);
            }
        }

        return AmbientClamp01(brightness);
    }

    private static byte LerpDayTintChannel(byte from, byte to, double t)
    {
        double value = from + (to - from) * t;
        if (value <= 0.0) return 0;
        if (value >= 255.0) return 255;
        return (byte)value;
    }
}
