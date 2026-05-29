// Constants.cs - all defaults from docs/architecture.md §11.
// This file is referenced by the unit tests as the single source of truth
// for spec constants across all four implementations (Native, Win2D, WinUI 3, WPF).
// Keep field names and values in lock-step with the spec.
// This file is the source of truth for the WPF port and stays plain net10.0-compatible C#.

using System;

namespace DesktopGrass.WPF;

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

    // Flower palette (§4) — 6 ARGB colors.
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

    public static readonly uint[] MUSHROOM_PALETTE =
    {
        0xFFD32F2F, // red (amanita)
        0xFF8D6E63, // brown
        0xFFC9A66B, // tan
        0xFFFFF8E1, // ivory
        0xFFE57373, // dusty pink
        0xFF6D4C41, // dark brown
    };

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
}
