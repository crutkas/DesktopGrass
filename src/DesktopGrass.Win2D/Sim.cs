// Sim.cs - pure C# port of docs/architecture.md.
//
// This file is intentionally free of Win32, Win2D, threading, and rendering
// concerns. Everything here is deterministic given (seed, monitorWidth,
// density, event stream). The unit tests in DesktopGrass.Win2D.Tests
// exercise this file directly.

using System;
using System.Collections.Generic;

namespace DesktopGrass.Win2D;

// PRNG: xorshift64 seeded via SplitMix64 (§3). Conformance requires identical
// uint64 sequences across all three impls.
internal struct Prng
{
    public ulong State;

    public static Prng Init(ulong seed)
    {
        var p = new Prng { State = SplitMix64(seed) };
        if (p.State == 0UL) p.State = 0x9E3779B97F4A7C15UL;
        return p;
    }

    private static ulong SplitMix64(ulong z)
    {
        unchecked
        {
            z = z + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public ulong NextU64()
    {
        unchecked
        {
            ulong x = State;
            x ^= x << 13;
            x ^= x >> 7;
            x ^= x << 17;
            State = x;
            return x;
        }
    }

    // Uniform double in [0,1) using top 53 bits, per §3.
    public double NextUnit() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

    public double Uniform(double lo, double hi) => lo + NextUnit() * (hi - lo);

    public uint Index(uint n) => (uint)(NextUnit() * n);
}

// Blade record (§4). Field order matches generation order: any reordering of
// PRNG draws in GenerateBlades breaks the snapshot tests.
internal struct Blade
{
    public double BaseX;
    public double Height;
    public double Thickness;
    public byte Hue;
    public double SwayPhaseOffset;
    public double Stiffness;

    public double CutHeight;
    public double GustVelocity;
    public double CutAnimStart;
    public double CutInitialHeight;

    // Regrowth (§9 "Regrowth"). RegrowDelay / RegrowDuration are assigned
    // once at generation from an independent PRNG stream. RegrowStart is the
    // absolute GlobalTime at which the regrow animation begins; -1 = not
    // scheduled. AdvanceCut only schedules regrowth when both fields are
    // positive, so default-constructed Blade instances (used by tests) stay
    // dormant.
    public double RegrowDelay;
    public double RegrowDuration;
    public double RegrowStart;

    // Flower (§4, §5, §7). Static, set once at generation.
    public bool   IsFlower;
    public byte   FlowerHeadColorIdx;
    public double FlowerHeadRadius;
    public double HeightBonus;          // 1.0 for non-flowers

    // Mushroom (§4, §5, §7). Static, set once at generation.
    public bool   IsMushroom;
    public byte   MushroomCapColorIdx;
    public double MushroomCapWidth;        // DIP, radius X
    public double MushroomCapHeight;       // DIP, radius Y (cap is wider than tall)
    public double MushroomStemHeight;      // DIP
    public double MushroomStemThickness;   // DIP

    public double EffectiveLean;
}

internal enum EventType { Move, Click }

internal readonly struct InputEvent
{
    public readonly EventType Type;
    public readonly double X;
    public readonly double Y;
    public readonly double Time;

    public InputEvent(EventType type, double x, double y, double time)
    {
        Type = type; X = x; Y = y; Time = time;
    }
}

internal sealed class Sim
{
    public Blade[] Blades = Array.Empty<Blade>();
    public double GlobalTime;
    public double PrevCursorX;
    public double PrevCursorTime = -1.0; // -1 = uninitialized

    public double GroundY;        // y coordinate of the ground line in window-local space
    public double WindowHeight;   // for diagnostics

    // §5 procedural generation.
    public static Blade[] GenerateBlades(ulong seed, double monitorWidth, double density)
    {
        var rng = Prng.Init(seed);
        // Independent PRNG stream for regrowth jitter — seeded with seed XOR
        // salt so the main stream stays bit-identical to the pre-regrowth
        // implementation (preserves the 10,787 cross-impl conformance).
        var rngRegrow = Prng.Init(seed ^ Constants.REGROW_PRNG_SALT);
        // Flower stream — independent of main and regrowth. Every blade
        // consumes exactly one unconditional draw (probability check);
        // flowers additionally consume 3 more (head color, radius, bonus).
        var rngFlower = Prng.Init(seed ^ Constants.FLOWER_PRNG_SALT);
        // Mushroom stream — fourth independent stream salted with
        // MUSHROOM_PRNG_SALT. Order: probability, then (if mushroom)
        // cap-color, cap-width, cap-height, stem-height, stem-thickness.
        var rngMushroom = Prng.Init(seed ^ Constants.MUSHROOM_PRNG_SALT);
        var list = new List<Blade>(capacity: (int)(monitorWidth / 4.0));
        double x = 0.0;

        while (x < monitorWidth)
        {
            double step = rng.Uniform(Constants.BLADE_SPACING_MIN, Constants.BLADE_SPACING_MAX) / density;
            x += step;
            if (x >= monitorWidth) break;

            Blade b = default;
            b.BaseX = x;
            b.Height = rng.Uniform(Constants.BLADE_HEIGHT_MIN, Constants.BLADE_HEIGHT_MAX);
            b.Thickness = rng.Uniform(Constants.BLADE_THICKNESS_MIN, Constants.BLADE_THICKNESS_MAX);
            b.Hue = (byte)rng.Index(Constants.PALETTE_SIZE);
            b.SwayPhaseOffset = rng.Uniform(0.0, 2.0 * Math.PI);
            b.Stiffness = rng.Uniform(Constants.STIFFNESS_MIN, Constants.STIFFNESS_MAX);

            b.CutHeight = 1.0;
            b.GustVelocity = 0.0;
            b.CutAnimStart = -1.0;
            b.CutInitialHeight = 1.0;

            // Regrowth jitter — independent stream, draw delay then duration
            // (order MUST match across impls).
            b.RegrowDelay    = rngRegrow.Uniform(Constants.REGROW_DELAY_MIN, Constants.REGROW_DELAY_MAX);
            b.RegrowDuration = rngRegrow.Uniform(Constants.REGROW_DURATION_MIN, Constants.REGROW_DURATION_MAX);
            b.RegrowStart    = -1.0;

            // Flower stream — draw order MUST be probability, then
            // (if flower) head-color, head-radius, height-bonus.
            bool isFlower = rngFlower.Uniform(0.0, 1.0) < Constants.FLOWER_PROBABILITY;
            b.IsFlower = isFlower;
            if (isFlower)
            {
                b.FlowerHeadColorIdx = (byte)rngFlower.Index((uint)Constants.FLOWER_PALETTE_SIZE);
                b.FlowerHeadRadius   = rngFlower.Uniform(Constants.FLOWER_HEAD_RADIUS_MIN, Constants.FLOWER_HEAD_RADIUS_MAX);
                b.HeightBonus        = rngFlower.Uniform(Constants.FLOWER_HEIGHT_BONUS_MIN, Constants.FLOWER_HEIGHT_BONUS_MAX);
            }
            else
            {
                b.FlowerHeadColorIdx = 0;
                b.FlowerHeadRadius   = 0.0;
                b.HeightBonus        = 1.0;
            }

            bool isMushroom = rngMushroom.Uniform(0.0, 1.0) < Constants.MUSHROOM_PROBABILITY;
            b.IsMushroom = isMushroom;
            if (isMushroom)
            {
                b.MushroomCapColorIdx     = (byte)rngMushroom.Index((uint)Constants.MUSHROOM_PALETTE_SIZE);
                b.MushroomCapWidth        = rngMushroom.Uniform(Constants.MUSHROOM_CAP_WIDTH_MIN,      Constants.MUSHROOM_CAP_WIDTH_MAX);
                b.MushroomCapHeight       = rngMushroom.Uniform(Constants.MUSHROOM_CAP_HEIGHT_MIN,     Constants.MUSHROOM_CAP_HEIGHT_MAX);
                b.MushroomStemHeight      = rngMushroom.Uniform(Constants.MUSHROOM_STEM_HEIGHT_MIN,    Constants.MUSHROOM_STEM_HEIGHT_MAX);
                b.MushroomStemThickness   = rngMushroom.Uniform(Constants.MUSHROOM_STEM_THICKNESS_MIN, Constants.MUSHROOM_STEM_THICKNESS_MAX);
            }
            else
            {
                b.MushroomCapColorIdx     = 0;
                b.MushroomCapWidth        = 0.0;
                b.MushroomCapHeight       = 0.0;
                b.MushroomStemHeight      = 0.0;
                b.MushroomStemThickness   = 0.0;
            }

            list.Add(b);
        }

        return list.ToArray();
    }

    // §6 sway physics, applied per blade per frame.
    public static void UpdateBladeDynamics(ref Blade b, double globalTime, double dt)
    {
        b.GustVelocity *= Math.Exp(-Constants.DECAY_RATE * dt);

        double swayPhase = b.SwayPhaseOffset + globalTime * Constants.BASE_SWAY_SPEED;
        double baseLean = Math.Sin(swayPhase) * Constants.BASE_AMPLITUDE * b.Stiffness;

        b.EffectiveLean = baseLean + b.GustVelocity * Constants.GUST_TO_LEAN_FACTOR;
    }

    // §8 cursor-move impulse.
    public void ApplyCursorMove(in InputEvent e)
    {
        double gustBandTop = GroundY - Constants.STRIP_HEIGHT - Constants.HEADROOM;
        double gustBandBottom = GroundY;
        if (e.Y < gustBandTop || e.Y > gustBandBottom)
            return;

        // First event after init / long idle: just prime the baseline.
        if (PrevCursorTime < 0.0 || (e.Time - PrevCursorTime) > Constants.CURSOR_REINIT_GAP_SEC)
        {
            PrevCursorX = e.X;
            PrevCursorTime = e.Time;
            return;
        }

        double dtEv = Math.Max(e.Time - PrevCursorTime, 1.0 / 1000.0);
        double velX = (e.X - PrevCursorX) / dtEv;
        double capped = Math.Clamp(velX, -Constants.MAX_CURSOR_SPEED, Constants.MAX_CURSOR_SPEED);

        PrevCursorX = e.X;
        PrevCursorTime = e.Time;

        double impulseMagnitude = Math.Abs(capped) * Constants.IMPULSE_SCALE;
        double signDir = capped > 0.0 ? 1.0 : capped < 0.0 ? -1.0 : 0.0;

        for (int i = 0; i < Blades.Length; i++)
        {
            ref Blade b = ref Blades[i];
            double dxAbs = Math.Abs(b.BaseX - e.X);
            if (dxAbs >= Constants.GUST_RADIUS) continue;

            double t = 1.0 - dxAbs / Constants.GUST_RADIUS;
            double s = Math.Clamp(t, 0.0, 1.0);
            double smooth = s * s * (3.0 - 2.0 * s);

            double delta = impulseMagnitude * smooth * signDir;
            b.GustVelocity += delta;
        }
    }

    // §9 click → cut.
    public void ApplyClick(double clickX, double clickY, double time)
    {
        double cutBandTop = GroundY - Constants.STRIP_HEIGHT;
        double cutBandBottom = GroundY;
        if (clickY < cutBandTop || clickY > cutBandBottom) return;

        for (int i = 0; i < Blades.Length; i++)
        {
            ref Blade b = ref Blades[i];
            if (Math.Abs(b.BaseX - clickX) >= Constants.CUT_RADIUS) continue;
            if (b.CutHeight <= 0.0) continue;
            if (b.CutAnimStart >= 0.0) continue;

            b.CutAnimStart = GlobalTime;
            b.CutInitialHeight = b.CutHeight;
            // Cancel any pending or in-progress regrowth: we're going back down.
            b.RegrowStart = -1.0;
        }
    }

    public static void AdvanceCut(ref Blade b, double globalTime)
    {
        // Phase 1: cut animation is running.
        if (b.CutAnimStart >= 0.0)
        {
            double elapsed = globalTime - b.CutAnimStart;
            double t = elapsed / Constants.CUT_DURATION_SEC;
            if (t >= 1.0)
            {
                b.CutHeight = 0.0;
                b.CutAnimStart = -1.0;
                // Schedule regrowth only if the per-blade jitter is
                // well-defined. Production blades from GenerateBlades always
                // satisfy this; test fixtures with default-constructed Blade
                // stay cut (matches the pre-regrowth contract).
                if (b.RegrowDelay > 0.0 && b.RegrowDuration > 0.0)
                {
                    b.RegrowStart = globalTime + b.RegrowDelay;
                }
            }
            else
            {
                b.CutHeight = b.CutInitialHeight * (1.0 - t);
            }
            return;
        }

        // Phase 2: regrowth scheduled / running.
        if (b.RegrowStart < 0.0 || globalTime < b.RegrowStart) return;
        if (b.RegrowDuration <= 0.0)
        {
            b.CutHeight = 1.0;
            b.RegrowStart = -1.0;
            return;
        }

        double regrowElapsed = globalTime - b.RegrowStart;
        double regrowT = regrowElapsed / b.RegrowDuration;
        if (regrowT >= 1.0)
        {
            b.CutHeight = 1.0;
            b.RegrowStart = -1.0;
        }
        else
        {
            // Linear 0 -> 1, same easing as the cut animation in reverse.
            b.CutHeight = regrowT;
        }
    }

    // §10 single per-frame entry point.
    public void Tick(double dt, ReadOnlySpan<InputEvent> events)
    {
        GlobalTime += dt;

        for (int i = 0; i < events.Length; i++)
        {
            ref readonly InputEvent e = ref events[i];
            switch (e.Type)
            {
                case EventType.Move: ApplyCursorMove(e); break;
                case EventType.Click: ApplyClick(e.X, e.Y, e.Time); break;
            }
        }

        for (int i = 0; i < Blades.Length; i++)
        {
            ref Blade b = ref Blades[i];
            UpdateBladeDynamics(ref b, GlobalTime, dt);
            AdvanceCut(ref b, GlobalTime);
        }
    }

    // §7 stroke geometry, returned to the renderer.
    public readonly struct Stroke
    {
        public readonly double BaseX, BaseY;
        public readonly double CtrlX, CtrlY;
        public readonly double TipX, TipY;
        public readonly double Thickness;
        public readonly uint Argb;

        public Stroke(double bx, double by, double cx, double cy, double tx, double ty, double thickness, uint argb)
        {
            BaseX = bx; BaseY = by; CtrlX = cx; CtrlY = cy; TipX = tx; TipY = ty;
            Thickness = thickness; Argb = argb;
        }
    }

    public static Stroke ComputeBladeStroke(in Blade b, double groundY)
    {
        uint argb = Constants.PALETTE[b.Hue];

        if (b.CutHeight < Constants.CUT_STUMP_THRESHOLD)
        {
            return new Stroke(
                b.BaseX, groundY,
                b.BaseX, groundY - 1.0,
                b.BaseX, groundY - Constants.STUMP_HEIGHT,
                b.Thickness, argb);
        }

        double L = b.Height * b.HeightBonus * b.CutHeight;

        // Chord preservation: blades have a fixed length L. As EffectiveLean
        // grows, the tip arcs over (Y drops) rather than the blade stretching
        // diagonally. Clamp to MAX_LEAN_FRACTION * L so the sqrt is always
        // positive even under strong gust impulses.
        double lean = b.EffectiveLean;
        double maxLean = Constants.MAX_LEAN_FRACTION * L;
        if (lean >  maxLean) lean =  maxLean;
        if (lean < -maxLean) lean = -maxLean;

        double dropFactor = Math.Sqrt(1.0 - (lean / L) * (lean / L));

        double tipX = b.BaseX + lean;
        double tipY = groundY - L * dropFactor;

        // Rooted-bend control point: directly above the base, at a fraction
        // CTRL_OFFSET_FACTOR of the (current, foreshortened) blade height.
        return new Stroke(
            b.BaseX, groundY,
            b.BaseX, groundY - L * Constants.CTRL_OFFSET_FACTOR * dropFactor,
            tipX, tipY,
            b.Thickness, argb);
    }
}
