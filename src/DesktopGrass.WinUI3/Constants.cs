// Constants.cs
//
// Numerical constants for the DesktopGrass simulation. Mirrors §11 of
// docs/architecture.md verbatim. All three implementations (Native, Win2D,
// WinUI 3) reproduce this table independently; the values here are the
// source of truth for the WinUI 3 port.
//
// PURE: no UI / Windows / WinUI dependencies. Linked as source into
// DesktopGrass.WinUI3.Tests so unit tests can run on plain net10.0.

using System;

namespace DesktopGrass.WinUI3;

internal static class Constants
{
    // §2 / §8 / §9 — coordinate system + bands.
    public const double StripHeight = 80.0;     // DIP — visible grass strip height
    public const double Headroom    = 30.0;     // DIP — extra room above strip for sway/gust

    // §5 — generation.
    public const double BladeSpacingMin   = 4.0;
    public const double BladeSpacingMax   = 8.0;
    public const double BladeHeightMin    = 6.0;  // DIP — minimum blade height
    public const double BladeHeightMax    = 30.0; // DIP — maximum blade height
    public const double BladeThicknessMin = 1.0;
    public const double BladeThicknessMax = 2.5;
    public const double StiffnessMin      = 0.6;
    public const double StiffnessMax      = 1.0;

    public const int PaletteSize = 6;

    // §6 — sway physics.
    public static readonly double BaseSwaySpeed = Math.PI / 3.0; // rad/sec, 6s period
    public const double BaseAmplitude       = 3.0;   // DIP — base sway amplitude
    public const double DecayRate           = 2.5;   // /sec
    public const double GustToLeanFactor    = 0.75;  // DIP·sec/rad

    // §8 — gust impulse.
    public const double MaxCursorSpeed      = 4000.0; // DIP/sec
    public const double ImpulseScale        = 0.003;  // rad/DIP
    public const double GustRadius          = 150.0;  // DIP
    public const double CursorReinitGapSec  = 0.25;   // sec

    // §9 — cut.
    public const double CutRadius           = 30.0;   // DIP
    public const double CutDurationSec      = 0.2;    // sec

    // §9 "Regrowth" — per-blade jitter is sampled at generation from a
    // SECOND xorshift64 stream seeded `seed XOR RegrowPrngSalt`, so the main
    // stream remains bit-identical and the 10,787 conformance gate holds.
    public const double RegrowDelayMin     = 30.0;   // sec
    public const double RegrowDelayMax     = 90.0;   // sec
    public const double RegrowDurationMin  = 2.0;    // sec
    public const double RegrowDurationMax  = 4.0;    // sec
    public const ulong  RegrowPrngSalt     = 0xDEADBEEFCAFEBABEUL;

    // §7 — rendering geometry.
    public const double CutStumpThreshold   = 0.05;
    public const double StumpHeight         = 2.0;    // DIP
    public const double CtrlOffsetFactor    = 0.6;
    public const double MaxLeanFraction     = 0.95;   // fraction of blade length that the tip may horizontally displace; clamps gust impulses so the blade never folds completely flat.

    // §12 — canonical test seed.
    public const ulong CanonicalTestSeed    = 0x6B6173746FUL;

    // §4 — palette. ARGB hex; alpha is always 0xFF (window-level alpha is
    // handled by the compositor / layered window).
    public static readonly uint[] Palette = new uint[]
    {
        0xFF2C5E1A, // deep forest
        0xFF3A7A24, // dark green
        0xFF4C9A2E, // mid green
        0xFF66B845, // grass green
        0xFF7AC957, // bright green
        0xFF8FD96A, // light green
    };

    // Default density used by the runtime renderer. The spec says
    // "~400 blades per 1920 DIP" is met by density ≈ 1.25; WinUI 3 uses
    // 2.25 for a denser default while snapshot tests pass 1.0 explicitly.
    public const double DefaultDensity = 2.25; // density multiplier
}
