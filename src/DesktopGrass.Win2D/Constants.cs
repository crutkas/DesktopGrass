// Constants.cs - all defaults from docs/architecture.md §11.
// This file is referenced by the unit tests as the single source of truth
// for spec constants. Keep field names and values in lock-step with the spec.

using System;

namespace DesktopGrass.Win2D;

public enum Scene { Grass = 0, Desert = 1, Winter = 2 }

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

    // Snow-tipped blade caps (§15)
    public const double SNOW_TIP_RADIUS_FACTOR = 1.25;
    public const uint SNOW_TIP_COLOR = 0xFFFFFFFFu;

    // Pine trees (§15.1). Winter biome anchor — slot-bound, mirrors §14 cacti.
    public const double PINE_PROBABILITY        = 0.006;
    public const double PINE_HEIGHT_MIN         = 36.0;
    public const double PINE_HEIGHT_MAX         = 72.0;
    public const double PINE_WIDTH_MIN          = 16.0;
    public const double PINE_WIDTH_MAX          = 28.0;
    public const int    PINE_TIER_COUNT_MIN     = 2;
    public const int    PINE_TIER_COUNT_MAX     = 4;
    public const double PINE_TIP_TAPER          = 0.25;
    public const double PINE_TIER_OVERLAP       = 0.15;
    public const double PINE_SNOW_CAP_FRACTION  = 0.30;
    public const uint   PINE_COLOR              = 0xFF1B5E20u;
    public const ulong  PINE_PRNG_SALT          = 0x50494E4550494E45ul;
}
