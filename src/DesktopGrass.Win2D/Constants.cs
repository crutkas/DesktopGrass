// Constants.cs - all defaults from docs/architecture.md §11.
// This file is referenced by the unit tests as the single source of truth
// for spec constants. Keep field names and values in lock-step with the spec.

using System;
using System.Numerics;

namespace DesktopGrass.Win2D;

public enum Scene { Grass = 0, Desert = 1, Winter = 2, Autumn = 3, Ocean = 4 }

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
    // Render-only stroke-width bonus added to each blade so grass reads thicker
    // on screen without perturbing the generation PRNG / blade snapshots.
    public const double BLADE_THICKNESS_RENDER_BONUS = 1.5;
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
    public const double CUT_RADIUS = 15.0;             // DIP
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
    public const double CACTUS_ARM_MIN_HEIGHT      = 50.0; // only tall cacti grow arms (range is 30-70)
    public const double CACTUS_ARM_MIN_CUT_HEIGHT  = 0.85; // render-only: hide arms once a cactus is cut
    public const uint   CACTUS_COLOR               = 0xFF2D7A2D;
    public const ulong  CACTUS_PRNG_SALT           = 0xCAC75CAC75CAC75CUL;

    // Tumbleweeds (§14). Desert roaming entities generated and respawned from
    // a persistent stream seeded with seed XOR TUMBLEWEED_PRNG_SALT.
    public const int    TUMBLEWEED_COUNT_PER_1920DIP = 4;
    public const double TUMBLEWEED_SIZE_MIN          = 8.0;
    public const double TUMBLEWEED_SIZE_MAX          = 18.0;
    public const double TUMBLEWEED_SPEED_MIN         = 24.0;
    public const double TUMBLEWEED_SPEED_MAX         = 72.0;
    public const double TUMBLEWEED_Y_OFFSET_MIN      = 8.0;
    public const double TUMBLEWEED_Y_OFFSET_MAX      = 20.0;
    public const uint   TUMBLEWEED_COLOR             = 0xFF8A6A3D;
    public const ulong  TUMBLEWEED_PRNG_SALT         = 0x7B0117CA7B0117CAUL;
    // Gentle, staggered vertical hop (§14). Heights are a fraction of the
    // tumbleweed radius so the bounce stays subtle; period is the rough gap
    // between hops, jittered per-hop. Gravity sets the arc/airtime.
    public const double TUMBLEWEED_BOUNCE_GRAVITY        = 300.0;
    public const double TUMBLEWEED_BOUNCE_HEIGHT_MIN_FRAC = 0.35;
    public const double TUMBLEWEED_BOUNCE_HEIGHT_MAX_FRAC = 0.75;
    public const double TUMBLEWEED_BOUNCE_PERIOD_MIN     = 2.5;
    public const double TUMBLEWEED_BOUNCE_PERIOD_MAX     = 6.0;

    // Bezier rendering (§7)
    public const double CUT_STUMP_THRESHOLD = 0.05;
    public const double STUMP_HEIGHT = 2.0;            // DIP
    public const double MUSHROOM_STUMP_HEIGHT = 4.0;   // DIP — slightly taller than the grass stub
    public const double CTRL_OFFSET_FACTOR = 0.6;
    public const double MAX_LEAN_FRACTION = 0.95;      // fraction of blade length that the tip may horizontally displace; clamps gust impulses so the blade never folds completely flat.

    // Cut residual height (stubble). A mowed blade settles at a small per-blade
    // normalized height in [CUT_FLOOR_MIN, CUT_FLOOR_MAX] (both above
    // CUT_STUMP_THRESHOLD) so the cut line reads with gentle, natural variation
    // instead of a perfectly even edge. Sampled from an independent stream
    // salted with CUT_FLOOR_PRNG_SALT so it does NOT perturb generation.
    public const double CUT_FLOOR_MIN = 0.06;
    public const double CUT_FLOOR_MAX = 0.16;
    public const ulong  CUT_FLOOR_PRNG_SALT = 0xC07F100DC07F100DUL;

    // Canonical seed for snapshot tests (§12)
    public const ulong CANONICAL_TEST_SEED = 0x6B6173746FUL;

    // Density override the plan calls for ~600 blades / 1920 px monitor.
    public const double DEFAULT_DENSITY = 2.53125;

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
    public const int    SCENE_COUNT   = 5;
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

    // Autumn palette (§16.5) — warm orange/red/yellow/gold tones.
    public static readonly uint[] AUTUMN_PALETTE =
    {
        0xFFD96B0C, // 0 burnt orange
        0xFFB54D1E, // 1 deep rust
        0xFFE89A3C, // 2 warm amber
        0xFFC23E12, // 3 vibrant red-orange
        0xFFD9A65C, // 4 honey-gold
        0xFF8C2E0F, // 5 dark maroon
    };

    // Ocean palette — seafloor sand / silt / pebble tones used for blades on
    // the Ocean scene (so non-coral grass slots read as wisps of seagrass on
    // a sandy bottom rather than green lawn).
    public static readonly uint[] OCEAN_PALETTE =
    {
        0xFF3FA9A6, // 0 teal
        0xFF2E8C8A, // 1 deep teal
        0xFF6FC6C2, // 2 pale aqua
        0xFF1F6F75, // 3 deep sea green
        0xFF8FD7CC, // 4 light sea foam
        0xFF257D7B, // 5 mid teal
    };

    // 2D lookup: SCENE_PALETTES[(int)scene, hue]. Row 0 must equal PALETTE.
    public static readonly uint[,] SCENE_PALETTES = new uint[SCENE_COUNT, PALETTE_SIZE]
    {
        { PALETTE[0],        PALETTE[1],        PALETTE[2],        PALETTE[3],        PALETTE[4],        PALETTE[5]        },
        { DESERT_PALETTE[0], DESERT_PALETTE[1], DESERT_PALETTE[2], DESERT_PALETTE[3], DESERT_PALETTE[4], DESERT_PALETTE[5] },
        { WINTER_PALETTE[0], WINTER_PALETTE[1], WINTER_PALETTE[2], WINTER_PALETTE[3], WINTER_PALETTE[4], WINTER_PALETTE[5] },
        { AUTUMN_PALETTE[0], AUTUMN_PALETTE[1], AUTUMN_PALETTE[2], AUTUMN_PALETTE[3], AUTUMN_PALETTE[4], AUTUMN_PALETTE[5] },
        { OCEAN_PALETTE[0],  OCEAN_PALETTE[1],  OCEAN_PALETTE[2],  OCEAN_PALETTE[3],  OCEAN_PALETTE[4],  OCEAN_PALETTE[5]  },
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
    public static readonly string[] HEDGEHOG_NAME_POOL =
    {
        "Bristle", "Quill", "Mossy", "Truffle", "Prickles", "Snuffles",
        "Pinecone", "Hazel", "Bramble", "Pip", "Sage", "Burdock",
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
    public const double SHEEP_SLEEP_FROM_IDLE_PROB = 0.30;

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
    public const double CAT_SLEEP_FROM_IDLE_PROB = 0.50;

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
    public const double BUNNY_SLEEP_PROB         = 0.05;

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
    // Hedgehog (§17.9). Grass-only, solitary nocturnal critter. Generated after
    // bunnies from the shared critter PRNG; passive defense curls into a ball.
    public const int    HEDGEHOG_COUNT_MIN             = 0;
    public const int    HEDGEHOG_COUNT_MAX             = 1;
    public const double HEDGEHOG_COUNT_PROBABILITY     = 0.55;
    public const double HEDGEHOG_WALK_SPEED_MIN        = 4.0;
    public const double HEDGEHOG_WALK_SPEED_MAX        = 8.0;
    public const double HEDGEHOG_BODY_RADIUS           = 9.0;
    public const double HEDGEHOG_BODY_HEIGHT           = 5.5;
    public const double HEDGEHOG_HEAD_RADIUS           = 3.6;
    public const double HEDGEHOG_NOSE_RADIUS           = 0.8;
    public const double HEDGEHOG_LEG_LENGTH            = 2.5;
    public const int    HEDGEHOG_SPIKE_COUNT           = 14;
    public const double HEDGEHOG_SPIKE_LENGTH          = 3.0;
    public const double HEDGEHOG_SPIKE_WIDTH           = 1.4;
    public const double HEDGEHOG_SPIKE_ARC_START_DEG   = -20.0;
    public const double HEDGEHOG_SPIKE_ARC_END_DEG     = 200.0;
    public const uint   HEDGEHOG_BODY_COLOR            = 0xFF5C4633u;
    public const uint   HEDGEHOG_SPIKE_COLOR           = 0xFF3A2A1Fu;
    public const uint   HEDGEHOG_SPIKE_TIP_COLOR       = 0xFF1E150Eu;
    public const uint   HEDGEHOG_NOSE_COLOR            = 0xFF1A1208u;
    public const uint   HEDGEHOG_EYE_COLOR             = 0xFF1A1208u;

    public const byte   HEDGEHOG_STATE_WALKING         = 0;
    public const byte   HEDGEHOG_STATE_SNUFFLING       = 1;
    public const byte   HEDGEHOG_STATE_IDLE            = 2;
    public const byte   HEDGEHOG_STATE_SLEEPING        = 3;
    public const byte   HEDGEHOG_STATE_CURLED          = 4;

    public const double HEDGEHOG_WALK_DURATION_MIN     = 6.0;
    public const double HEDGEHOG_WALK_DURATION_MAX     = 12.0;
    public const double HEDGEHOG_SNUFFLE_DURATION_MIN  = 3.0;
    public const double HEDGEHOG_SNUFFLE_DURATION_MAX  = 6.0;
    public const double HEDGEHOG_IDLE_DURATION_MIN     = 1.5;
    public const double HEDGEHOG_IDLE_DURATION_MAX     = 3.0;
    public const double HEDGEHOG_SLEEP_DURATION_MIN    = 10.0;
    public const double HEDGEHOG_SLEEP_DURATION_MAX    = 25.0;
    public const double HEDGEHOG_CURL_DURATION_MIN     = 3.0;
    public const double HEDGEHOG_CURL_DURATION_MAX     = 5.5;
    public const double HEDGEHOG_SNUFFLE_PROBABILITY   = 0.55;
    public const double HEDGEHOG_IDLE_PROBABILITY      = 0.30;
    public const double HEDGEHOG_SLEEP_PROB            = 0.50;
    public const double HEDGEHOG_STARTLE_RADIUS        = 70.0;
    public const double HEDGEHOG_SNUFFLE_HEAD_FREQ     = 5.0;
    public const double HEDGEHOG_SNUFFLE_HEAD_AMP      = 0.7;
    public const double HEDGEHOG_WADDLE_FREQ           = 4.0;
    public const double HEDGEHOG_WADDLE_AMP            = 0.8;
    public const double HEDGEHOG_ZZZ_CYCLE_SEC         = SHEEP_ZZZ_CYCLE_SEC;
    public const double HEDGEHOG_ZZZ_RISE              = SHEEP_ZZZ_RISE * 0.5;
    public const double HEDGEHOG_ZZZ_SIZE_START        = SHEEP_ZZZ_SIZE_START * 0.6;
    public const double HEDGEHOG_ZZZ_SIZE_END          = SHEEP_ZZZ_SIZE_END * 0.6;
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
    public const ulong  FIREFLY_PRNG_SALT            = 0xF13EF1E7777ul;

    // Bird flybys (§17.8). Grass-only transient flocks.
    public const double BIRD_FLYBY_SPAWN_RATE_PER_HOUR = 15.0;
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

    // Snow puff (§21). A click in the Winter scene kicks up a short-lived
    // burst of powder. Dedicated PRNG stream (salted) so the burst never
    // perturbs the snowflake emitter; it only fires on click input. y is
    // screen-down, so an upward launch is negative vy and SNOW_PUFF_GRAVITY
    // pulls back toward the ground.
    public const int    SNOW_PUFF_COUNT_MIN       = 9;
    public const int    SNOW_PUFF_COUNT_MAX       = 16;
    public const double SNOW_PUFF_SIZE_MIN        = 3.5;
    public const double SNOW_PUFF_SIZE_MAX        = 8.0;
    public const double SNOW_PUFF_BURST_SPEED_MIN = 70.0;
    public const double SNOW_PUFF_BURST_SPEED_MAX = 150.0;
    public const double SNOW_PUFF_SPREAD_RAD      = 1.25;
    public const double SNOW_PUFF_GRAVITY         = 150.0;
    public const double SNOW_PUFF_DRAG            = 1.6;
    public const double SNOW_PUFF_START_RADIUS    = 7.0;
    public const double SNOW_PUFF_LIFETIME_MIN    = 1.0;
    public const double SNOW_PUFF_LIFETIME_MAX    = 1.8;
    // Puffs are white, so on the white bank they need an edge: the cool bank
    // shadow brush is reused to draw a larger disc offset down behind the core.
    public const double SNOW_PUFF_SHADOW_SCALE    = 1.35;
    public const double SNOW_PUFF_SHADOW_OFFSET   = 0.45;
    public const double SNOW_PUFF_SHADOW_OPACITY  = 0.55;
    public const ulong  SNOW_PUFF_PRNG_SALT       = 0x5503FF1E5503FF1Eul;

    // §21.1 Snow drift (Winter cursor-move spindrift). Brushing the cursor low
    // and fast across the snowbank kicks up a small, gentle wisp of powder — the
    // Winter analogue of the autumn leaf-puff hover, giving the scene a calm
    // move-driven interaction to match grass/desert/fall. Reuses the snow-puff
    // particle but with fewer, smaller, slower grains, gated by a global cooldown.
    public const int    SNOW_DRIFT_COUNT_MIN      = 4;
    public const int    SNOW_DRIFT_COUNT_MAX      = 8;
    public const double SNOW_DRIFT_REACH_DIP      = 70.0;
    public const double SNOW_DRIFT_MIN_SPEED      = 90.0;
    public const double SNOW_DRIFT_COOLDOWN_SEC   = 0.12;
    public const double SNOW_DRIFT_SIZE_SCALE     = 0.9;
    public const double SNOW_DRIFT_SPEED_SCALE    = 0.85;
    public const ulong  SNOW_DRIFT_PRNG_SALT      = 0x5D81F77D5D81F77Dul;

    // Cool shadow tint reused by the live snow-puff to give white powder an edge
    // against light backgrounds (despite the legacy "bank" name).
    public const uint   SNOW_BANK_SHADOW_COLOR       = 0xFFBFCDE4u;

    // Falling leaves (§16.5). Autumn-only transient particles.
    public const double LEAF_SPAWN_RATE_PER_SEC_1920DIP = 1.4;
    public const double LEAF_FALL_SPEED_MIN = 14.0;
    public const double LEAF_FALL_SPEED_MAX = 26.0;
    public const double LEAF_HORIZONTAL_DRIFT_AMP = 32.0;
    public const double LEAF_HORIZONTAL_DRIFT_FREQ = 1.4;
    public const double LEAF_ROTATION_SPEED_MIN = 0.8;
    public const double LEAF_ROTATION_SPEED_MAX = 2.4;
    public const double LEAF_SIZE_MIN = 4.0;
    public const double LEAF_SIZE_MAX = 7.0;
    public const double LEAF_SPAWN_Y_OFFSET = -10.0;
    public const int    LEAF_COLOR_COUNT = 6;
    public const uint   LEAF_COLOR_0 = 0xFFD96B0Cu;
    public const uint   LEAF_COLOR_1 = 0xFFB54D1Eu;
    public const uint   LEAF_COLOR_2 = 0xFFE89A3Cu;
    public const uint   LEAF_COLOR_3 = 0xFFC23E12u;
    public const uint   LEAF_COLOR_4 = 0xFFE6C849u;
    public const uint   LEAF_COLOR_5 = 0xFF8C2E0Fu;
    public const ulong  LEAF_PRNG_SALT = 0x1EA1DEC1D1EA1D05ul;
    public static readonly uint[] LEAF_COLORS =
    {
        LEAF_COLOR_0, LEAF_COLOR_1, LEAF_COLOR_2,
        LEAF_COLOR_3, LEAF_COLOR_4, LEAF_COLOR_5,
    };

    // Snow-tipped blade caps (§15)
    public const double SNOW_TIP_RADIUS_FACTOR = 1.25;
    public const uint SNOW_TIP_COLOR = 0xFFFFFFFFu;

    // §CPU: Winter draws a snow cap on every plain grass blade, the scene's
    // dominant render cost (~2,500 extra fills/frame). Deterministically cull a
    // fixed fraction of plain blades (and their caps) in Winter only, keyed on the
    // blade's stable array index — identical across frames and the Native/Win2D
    // renderers, and survives cuts. (hash & 3)==0 drops ~25% of blades.
    public const uint WINTER_CULL_MASK = 3u;

    public static bool WinterBladeCulled(uint bladeIndex)
    {
        unchecked
        {
            uint h = bladeIndex * 2654435761u;
            h ^= h >> 13;
            h *= 0x85ebca6bu;
            h ^= h >> 16;
            return (h & WINTER_CULL_MASK) == 0u;
        }
    }

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
    // Dimensional shading for pine boughs: a darker green dropped down-right as a
    // self-shadow and a lighter green dabbed on the upper-left lit face, so each
    // tier reads as a rounded bough instead of a flat triangle.
    public const uint   PINE_SHADOW_COLOR       = 0xFF103D16u;
    public const uint   PINE_HIGHLIGHT_COLOR    = 0xFF43A047u;
    public const double PINE_SHADOW_OFFSET_X_FRAC    = 0.14;
    public const double PINE_SHADOW_OFFSET_Y_FRAC    = 0.07;
    public const double PINE_HIGHLIGHT_OFFSET_X_FRAC = 0.20;
    public const double PINE_HIGHLIGHT_WIDTH_FRAC    = 0.50;
    public const float  PINE_HIGHLIGHT_OPACITY       = 0.5f;
    public const ulong  PINE_PRNG_SALT          = 0x50494E4550494E45ul;

    // Tree sway (§15.2): render-only shear that leans fall/winter trees about
    // their trunk base by a damped, clamped fraction of the blade's
    // effectiveLean, so canopies drift slightly with the wind and the cursor.
    public const double TREE_SWAY_LEAN_FACTOR         = 0.6;
    public const double TREE_SWAY_MAX_HEIGHT_FRACTION = 0.05;

    // Tree depth layering (§15.4). Winter pines/birches split into a foreground
    // layer (full size, in front of the snowbank) and a background layer (scaled
    // down, hazier, behind the snowbank) for real fore/background depth. Depth is
    // chosen by one locked PRNG draw per tree at generation. Render-only scale/opacity.
    public const double TREE_BACKGROUND_PROBABILITY   = 0.45;
    public const double TREE_BG_SCALE                 = 0.62;
    public const float  TREE_BG_OPACITY               = 0.78f;

    // Min spacing between adjacent props (cacti, pines, maples, coral) so
    // they never render directly on top of one another. Each generator
    // tracks the last-placed prop's right edge and rejects a candidate
    // whose left edge isn't at least PROP_MIN_GAP_DIP further along. The
    // candidate's PRNG draws still happen (so determinism + canonical
    // first-prop snapshots are unchanged); only the commit-vs-revert step
    // is gated. Foreground and background pines are tracked independently,
    // since the bg layer is parallax and is expected to overlap the fg.
    public const double PROP_MIN_GAP_DIP              = 4.0;

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

    // Maple trees (§16.5). Autumn slot-bound biome anchor.
    public const double MAPLE_PROBABILITY = 0.0070;
    public const double MAPLE_HEIGHT_MIN = 50.0;
    public const double MAPLE_HEIGHT_MAX = 85.0;
    public const double MAPLE_TRUNK_WIDTH_MIN = 6.0;
    public const double MAPLE_TRUNK_WIDTH_MAX = 10.0;
    public const double MAPLE_CANOPY_RADIUS_MIN = 14.0;
    public const double MAPLE_CANOPY_RADIUS_MAX = 24.0;
    public const uint   MAPLE_TRUNK_COLOR = 0xFF4A2C18u;
    public const uint   MAPLE_TRUNK_DARK = 0xFF2F1B0Eu;
    public const int    MAPLE_CANOPY_COLOR_COUNT = 4;
    public const uint   MAPLE_CANOPY_COLOR_0 = 0xFFD96B0Cu;
    public const uint   MAPLE_CANOPY_COLOR_1 = 0xFFE89A3Cu;
    public const uint   MAPLE_CANOPY_COLOR_2 = 0xFFC23E12u;
    public const uint   MAPLE_CANOPY_COLOR_3 = 0xFFE6C849u;
    public const double MAPLE_BARE_FRACTION = 0.20;
    public const ulong  MAPLE_PRNG_SALT = 0xC1AA51EC1AA51Eul;
    public static readonly uint[] MAPLE_CANOPY_COLORS =
    {
        MAPLE_CANOPY_COLOR_0, MAPLE_CANOPY_COLOR_1,
        MAPLE_CANOPY_COLOR_2, MAPLE_CANOPY_COLOR_3,
    };

    // Leaf puff (§16.6). Hovering the cursor over a leafy maple canopy in
    // Autumn shakes a small flurry of leaves loose, like a gust caught the
    // crown. Each puff draws from an independent salted PRNG stream so it never
    // perturbs the ambient leaf emitter. A per-tree cooldown keeps re-hovers
    // calm. Puff leaves reuse the ordinary Leaf entity but carry an outward
    // burst velocity (Vx) that decays via LEAF_PUFF_DRAG before they settle.
    public const int    LEAF_PUFF_COUNT_MIN         = 4;
    public const int    LEAF_PUFF_COUNT_MAX         = 7;
    public const double LEAF_PUFF_BURST_SPEED_MIN   = 18.0;   // DIP/s outward
    public const double LEAF_PUFF_BURST_SPEED_MAX   = 42.0;   // DIP/s outward
    public const double LEAF_PUFF_DRAG              = 2.2;     // exp decay (1/s) on burst Vx
    public const double LEAF_PUFF_COOLDOWN_SEC      = 1.5;     // per-tree re-puff gate
    public const double LEAF_PUFF_HOVER_RADIUS_MUL  = 1.15;    // × canopy radius
    public const double LEAF_PUFF_MIN_CUT_HEIGHT    = 0.5;     // tree must be reasonably leafy
    public const double LEAF_PUFF_START_OFFSET_FRAC = 0.4;     // spawn spread within canopy
    public const ulong  LEAF_PUFF_PRNG_SALT         = 0x9E3779B97F4A7C15ul;

    // Ocean scene — coral (blade variant), bubbles (rising entity), fish
    // (horizontal swimmer). Coral probability is intentionally lower than
    // pines/maples because each piece is wider (multi-DIP fan/brain).
    public const double CORAL_PROBABILITY     = 0.018;
    public const double CORAL_HEIGHT_MIN      = 22.0;
    public const double CORAL_HEIGHT_MAX      = 48.0;
    public const double CORAL_WIDTH_MIN       = 10.0;
    public const double CORAL_WIDTH_MAX       = 20.0;
    public const int    CORAL_TYPE_COUNT      = 3;   // 0 = fan, 1 = branching, 2 = brain
    public const int    CORAL_COLOR_COUNT     = 5;
    public const uint   CORAL_COLOR_0         = 0xFFFF6FA8u; // pink
    public const uint   CORAL_COLOR_1         = 0xFFFF8A3Du; // orange
    public const uint   CORAL_COLOR_2         = 0xFFB155D9u; // purple
    public const uint   CORAL_COLOR_3         = 0xFFE53935u; // red
    public const uint   CORAL_COLOR_4         = 0xFFFFE6D0u; // bone-white
    public static readonly uint[] CORAL_COLORS =
    {
        CORAL_COLOR_0, CORAL_COLOR_1, CORAL_COLOR_2, CORAL_COLOR_3, CORAL_COLOR_4,
    };
    public const ulong  CORAL_PRNG_SALT       = 0xC04A1C04A1C04A1Cul;

    // Bubbles — rise from the seafloor with horizontal wobble, pop at the top
    // of the canvas. Emit rate mirrors snowflake but at a calmer cadence.
    public const double BUBBLE_EMIT_RATE_PER_1920DIP = 1.8;
    public const double BUBBLE_RISE_SPEED_MIN   = 18.0;
    public const double BUBBLE_RISE_SPEED_MAX   = 38.0;
    public const double BUBBLE_SIZE_MIN         = 2.0;
    public const double BUBBLE_SIZE_MAX         = 4.5;
    public const double BUBBLE_WOBBLE_AMPLITUDE = 6.0;
    public const double BUBBLE_WOBBLE_FREQUENCY = 0.7;
    public const double BUBBLE_LIFETIME_PADDING_SEC = 1.5;
    public const uint   BUBBLE_STROKE_COLOR     = 0xCCB0E4FFu;
    public const uint   BUBBLE_HIGHLIGHT_COLOR  = 0xFFFFFFFFu;
    public const ulong  BUBBLE_PRNG_SALT        = 0xB0BB1EB0BB1EB0BBul;

    // Fish — small swimmers confined to the visible strip so they stay on
    // canvas. The overlay is only STRIP_HEIGHT + HEADROOM (≈110 DIP) tall,
    // so altitudes are tight: 25..75 DIP above the ground line.
    public const double FISH_COUNT_PER_1920DIP = 2.5;
    public const int    FISH_COUNT_MIN         = 2;
    public const int    FISH_COUNT_MAX         = 8;
    public const double FISH_SPEED_MIN         = 18.0;
    public const double FISH_SPEED_MAX         = 38.0;
    public const double FISH_SIZE_MIN          = 5.0;   // body half-length DIP
    public const double FISH_SIZE_MAX          = 8.5;
    public const double FISH_ALTITUDE_MIN      = 25.0;  // DIP above ground
    public const double FISH_ALTITUDE_MAX      = 75.0;
    public const double FISH_TAIL_WOBBLE_FREQ  = 6.0;
    public const double FISH_TAIL_WOBBLE_AMP   = 0.45;  // radians
    public const int    FISH_COLOR_COUNT       = 4;
    public const uint   FISH_COLOR_0           = 0xFFFFA844u; // clownfish orange
    public const uint   FISH_COLOR_1           = 0xFFFFD54Fu; // yellow
    public const uint   FISH_COLOR_2           = 0xFF42A5F5u; // bright blue
    public const uint   FISH_COLOR_3           = 0xFFE57373u; // coral pink
    public static readonly uint[] FISH_COLORS =
    {
        FISH_COLOR_0, FISH_COLOR_1, FISH_COLOR_2, FISH_COLOR_3,
    };
    public const uint   FISH_FIN_COLOR         = 0xFF222222u;
    public const ulong  FISH_PRNG_SALT         = 0xF15F15F15F15F15Ful;

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
}
