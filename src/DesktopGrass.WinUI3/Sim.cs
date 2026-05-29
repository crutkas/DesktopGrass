// Sim.cs
//
// Pure-logic core of the DesktopGrass simulation. Ports docs/architecture.md
// §3–§10 verbatim into C#. Contains:
//
//   * Prng                — xorshift64 PRNG seeded via SplitMix64 (§3)
//   * Blade               — POD record with static + runtime state (§4)
//   * InputEvent          — move / click event from the mouse-hook side (§10)
//   * Stroke              — base / control / tip points + thickness + color (§7)
//   * Sim                 — owns the blade list + globalTime + cursor history (§10)
//
// PURE: no UI / Windows / WinUI dependencies. The test project links this
// file directly so unit tests can run on plain net8.0 without WinAppSDK.

using System;
using System.Collections.Generic;

namespace DesktopGrass.WinUI3;

/// <summary>Deterministic PRNG — xorshift64 seeded via SplitMix64. §3.</summary>
internal sealed class Prng
{
    private ulong _state;

    public Prng(ulong seed)
    {
        _state = SplitMix64(seed);
        if (_state == 0UL)
        {
            _state = 0x9E3779B97F4A7C15UL; // belt-and-suspenders, matches spec
        }
    }

    private static ulong SplitMix64(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextU64()
    {
        ulong x = _state;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        _state = x;
        return x;
    }

    // Uses top 53 bits — matches IEEE-754 mantissa precision (§3).
    public double NextUnit() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

    public double Uniform(double lo, double hi) => lo + NextUnit() * (hi - lo);

    public uint Index(uint n) => (uint)(NextUnit() * n);
}

/// <summary>Per-blade state. §4.</summary>
internal struct Blade
{
    // Static (set at generation, never mutated by the sim).
    public double BaseX;
    public double Height;
    public double Thickness;
    public byte   Hue;
    public double SwayPhaseOffset;
    public double Stiffness;

    // Runtime.
    public double CutHeight;
    public double GustVelocity;
    public double CutAnimStart;
    public double CutInitialHeight;

    // Regrowth (§9 "Regrowth"). RegrowDelay / RegrowDuration are assigned
    // once at generation from an independent PRNG stream. RegrowStart is the
    // absolute GlobalTime at which the regrow animation begins; -1 = not
    // scheduled. AdvanceCut only schedules regrowth when both fields are
    // positive so default-constructed Blade instances (used by tests) stay
    // dormant.
    public double RegrowDelay;
    public double RegrowDuration;
    public double RegrowStart;

    // Derived per frame by update_blade_dynamics (§6).
    public double EffectiveLean;
}

internal enum InputEventType
{
    Move,
    Click,
}

internal readonly struct InputEvent
{
    public readonly InputEventType Type;
    public readonly double X;
    public readonly double Y;
    public readonly double Time;

    public InputEvent(InputEventType type, double x, double y, double time)
    {
        Type = type;
        X = x;
        Y = y;
        Time = time;
    }
}

/// <summary>Geometry for one quadratic Bezier blade. §7.</summary>
internal struct Stroke
{
    public double BaseX, BaseY;
    public double ControlX, ControlY;
    public double TipX, TipY;
    public double Thickness;
    public uint   Argb;
}

/// <summary>
/// The pure simulation: blade list + global time + cursor history.
/// Owns no GPU resources. Drives the renderer via <see cref="GetStroke"/>.
/// </summary>
internal sealed class Sim
{
    public Blade[] Blades { get; private set; } = Array.Empty<Blade>();
    public double GlobalTime { get; private set; }
    public double WindowHeight { get; }
    public double GroundY => WindowHeight;

    private double _prevCursorX;
    private double _prevCursorTime = -1.0;

    public Sim(double windowHeight)
    {
        WindowHeight = windowHeight;
    }

    // §5 — procedural generation.
    public void Generate(ulong seed, double monitorWidth, double density)
    {
        var p = new Prng(seed);
        // Independent stream for regrowth jitter — see Constants.RegrowPrngSalt.
        var pRegrow = new Prng(seed ^ Constants.RegrowPrngSalt);
        var list = new List<Blade>(capacity: (int)(monitorWidth / 6.0));

        double x = 0.0;
        while (x < monitorWidth)
        {
            double step = p.Uniform(Constants.BladeSpacingMin, Constants.BladeSpacingMax) / density;
            x += step;
            if (x >= monitorWidth) break;

            // Field-draw order is FIXED across impls (height, thickness,
            // hue, swayPhaseOffset, stiffness). Don't reorder.
            var b = new Blade
            {
                BaseX            = x,
                Height           = p.Uniform(Constants.BladeHeightMin, Constants.BladeHeightMax),
                Thickness        = p.Uniform(Constants.BladeThicknessMin, Constants.BladeThicknessMax),
                Hue              = (byte)p.Index((uint)Constants.PaletteSize),
                SwayPhaseOffset  = p.Uniform(0.0, 2.0 * Math.PI),
                Stiffness        = p.Uniform(Constants.StiffnessMin, Constants.StiffnessMax),
                CutHeight        = 1.0,
                GustVelocity     = 0.0,
                CutAnimStart     = -1.0,
                CutInitialHeight = 1.0,
                EffectiveLean    = 0.0,

                // Regrowth jitter — independent stream. Draw delay first, then
                // duration (order MUST match across impls).
                RegrowDelay      = pRegrow.Uniform(Constants.RegrowDelayMin, Constants.RegrowDelayMax),
                RegrowDuration   = pRegrow.Uniform(Constants.RegrowDurationMin, Constants.RegrowDurationMax),
                RegrowStart      = -1.0,
            };
            list.Add(b);
        }

        Blades = list.ToArray();
        // Re-generating resets runtime state by construction; cursor history
        // is preserved (a DPI change is not a cursor teleport).
    }

    // §10 — one tick per frame.
    public void Tick(double dt, ReadOnlySpan<InputEvent> events)
    {
        GlobalTime += dt;

        // 1. Drain events in order.
        for (int i = 0; i < events.Length; i++)
        {
            ref readonly var e = ref events[i];
            switch (e.Type)
            {
                case InputEventType.Move:  ApplyCursorMove(in e); break;
                case InputEventType.Click: ApplyClick(e.X, e.Y, GlobalTime); break;
            }
        }

        // 2. Per-blade dynamics + cut animation.
        for (int i = 0; i < Blades.Length; i++)
        {
            ref var b = ref Blades[i];
            UpdateBladeDynamics(ref b, GlobalTime, dt);
            AdvanceCut(ref b, GlobalTime);
        }
    }

    // Test helpers — exposed as internal so the test project (via
    // InternalsVisibleTo) can drive specific behaviours without having
    // to round-trip every check through Tick(). The runtime path is still
    // Tick → ApplyCursorMove/ApplyClick; no other production code calls
    // these directly.
    internal void TestApplyCursorMove(in InputEvent e) => ApplyCursorMove(in e);
    internal void TestApplyClick(double x, double y, double t) => ApplyClick(x, y, t);
    internal double TestPrevCursorX => _prevCursorX;
    internal double TestPrevCursorTime => _prevCursorTime;
    internal void TestSetGlobalTime(double t) => GlobalTime = t;
    internal void TestSetBlades(Blade[] blades) => Blades = blades;

    // Test-only convenience: static blade generation, mirroring the API
    // used by the Native and Win2D test projects so the snapshot fixtures
    // are easy to keep in sync.
    internal static Blade[] GenerateBlades(ulong seed, double monitorWidth, double density)
    {
        var sim = new Sim(Constants.StripHeight + Constants.Headroom);
        sim.Generate(seed, monitorWidth, density);
        return sim.Blades;
    }

    // Test-only convenience: static stroke computation that doesn't require
    // a Sim instance. Mirrors §7 directly so the test snapshot fixtures stay
    // sharable with the other impls.
    internal static Stroke ComputeBladeStroke(in Blade b, double groundY)
    {
        var s = new Stroke
        {
            Argb = Constants.Palette[b.Hue],
            Thickness = b.Thickness,
        };

        if (b.CutHeight < Constants.CutStumpThreshold)
        {
            s.BaseX    = b.BaseX; s.BaseY = groundY;
            s.ControlX = b.BaseX; s.ControlY = groundY - 1.0;
            s.TipX     = b.BaseX; s.TipY = groundY - Constants.StumpHeight;
            return s;
        }

        double tipX = b.BaseX + b.EffectiveLean;
        double tipY = groundY - b.Height * b.CutHeight;

        double dx = tipX - b.BaseX;
        double dy = tipY - groundY;
        double len = Math.Sqrt(dx * dx + dy * dy);

        double nx = -dy / len;
        double ny =  dx / len;

        double midX = (b.BaseX + tipX) * 0.5;
        double midY = (groundY + tipY) * 0.5;
        double offset = Constants.CtrlOffsetFactor * b.EffectiveLean;

        s.BaseX    = b.BaseX; s.BaseY = groundY;
        s.ControlX = midX + nx * offset; s.ControlY = midY + ny * offset;
        s.TipX     = tipX; s.TipY = tipY;
        return s;
    }

    // §6 — sway + gust decay.
    internal static void UpdateBladeDynamics(ref Blade b, double globalTime, double dt)
    {
        b.GustVelocity *= Math.Exp(-Constants.DecayRate * dt);

        double swayPhase = b.SwayPhaseOffset + globalTime * Constants.BaseSwaySpeed;
        double baseLean  = Math.Sin(swayPhase) * Constants.BaseAmplitude * b.Stiffness;

        b.EffectiveLean = baseLean + b.GustVelocity * Constants.GustToLeanFactor;
    }

    // §8 — gust band check + impulse distribution.
    private void ApplyCursorMove(in InputEvent e)
    {
        double gustBandTop    = GroundY - Constants.StripHeight - Constants.Headroom;
        double gustBandBottom = GroundY;

        bool inBand = e.Y >= gustBandTop && e.Y <= gustBandBottom;

        // First event / long idle: reinitialise without emitting an impulse.
        if (_prevCursorTime < 0.0 || (e.Time - _prevCursorTime) > Constants.CursorReinitGapSec)
        {
            _prevCursorX = e.X;
            _prevCursorTime = e.Time;
            return;
        }

        double dtEv = Math.Max(e.Time - _prevCursorTime, 1.0 / 1000.0);
        double velX = (e.X - _prevCursorX) / dtEv;
        double capped = Clamp(velX, -Constants.MaxCursorSpeed, Constants.MaxCursorSpeed);

        _prevCursorX = e.X;
        _prevCursorTime = e.Time;

        if (!inBand) return;

        double impulseMagnitude = Math.Abs(capped) * Constants.ImpulseScale;
        double signDir = capped > 0.0 ? 1.0 : (capped < 0.0 ? -1.0 : 0.0);
        if (signDir == 0.0 || impulseMagnitude == 0.0) return;

        for (int i = 0; i < Blades.Length; i++)
        {
            ref var b = ref Blades[i];
            double dxAbs = Math.Abs(b.BaseX - e.X);
            if (dxAbs >= Constants.GustRadius) continue;

            double t = 1.0 - dxAbs / Constants.GustRadius;
            double s = Clamp(t, 0.0, 1.0);
            double smooth = s * s * (3.0 - 2.0 * s);

            b.GustVelocity += impulseMagnitude * smooth * signDir;
        }
    }

    // §9 — cut band check + apply cut to blades within radius.
    private void ApplyClick(double clickX, double clickY, double globalTime)
    {
        double cutBandTop    = GroundY - Constants.StripHeight;
        double cutBandBottom = GroundY;
        if (clickY < cutBandTop || clickY > cutBandBottom) return;

        for (int i = 0; i < Blades.Length; i++)
        {
            ref var b = ref Blades[i];
            if (Math.Abs(b.BaseX - clickX) >= Constants.CutRadius) continue;
            if (b.CutHeight <= 0.0) continue;
            if (b.CutAnimStart >= 0.0) continue;

            b.CutAnimStart = globalTime;
            b.CutInitialHeight = b.CutHeight;
            // Cancel any pending/in-progress regrowth: we're going back down.
            b.RegrowStart = -1.0;
        }
    }

    // §9 — advance the in-flight cut animation OR the regrowth animation.
    internal static void AdvanceCut(ref Blade b, double globalTime)
    {
        // Phase 1: cut animation is running.
        if (b.CutAnimStart >= 0.0)
        {
            double elapsed = globalTime - b.CutAnimStart;
            double t = elapsed / Constants.CutDurationSec;
            if (t >= 1.0)
            {
                b.CutHeight = 0.0;
                b.CutAnimStart = -1.0;
                // Schedule regrowth only if jitter values are well-defined.
                // Production blades from Generate() always satisfy this; test
                // fixtures with default-constructed Blade stay cut (matches
                // the pre-regrowth contract).
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
            b.CutHeight   = 1.0;
            b.RegrowStart = -1.0;
            return;
        }

        double regrowElapsed = globalTime - b.RegrowStart;
        double regrowT       = regrowElapsed / b.RegrowDuration;
        if (regrowT >= 1.0)
        {
            b.CutHeight   = 1.0;
            b.RegrowStart = -1.0;
        }
        else
        {
            // Linear regrowth 0 -> 1.
            b.CutHeight = regrowT;
        }
    }

    // §7 — compute the renderable stroke for a single blade.
    public Stroke GetStroke(int index)
    {
        ref readonly var b = ref Blades[index];
        var s = new Stroke
        {
            Argb = Constants.Palette[b.Hue],
            Thickness = b.Thickness,
        };

        if (b.CutHeight < Constants.CutStumpThreshold)
        {
            s.BaseX    = b.BaseX; s.BaseY = GroundY;
            s.ControlX = b.BaseX; s.ControlY = GroundY - 1.0;
            s.TipX     = b.BaseX; s.TipY = GroundY - Constants.StumpHeight;
            return s;
        }

        double tipX = b.BaseX + b.EffectiveLean;
        double tipY = GroundY - b.Height * b.CutHeight;

        double dx = tipX - b.BaseX;
        double dy = tipY - GroundY;
        double len = Math.Sqrt(dx * dx + dy * dy);

        // Perpendicular: rotate (dx, dy) 90° CCW. Spec §7.
        double nx = -dy / len;
        double ny =  dx / len;

        double midX = (b.BaseX + tipX) * 0.5;
        double midY = (GroundY + tipY) * 0.5;
        double offset = Constants.CtrlOffsetFactor * b.EffectiveLean;

        s.BaseX    = b.BaseX; s.BaseY = GroundY;
        s.ControlX = midX + nx * offset; s.ControlY = midY + ny * offset;
        s.TipX     = tipX; s.TipY = tipY;
        return s;
    }

    // .NET <8 polyfill — Math.Clamp works fine on net8.0 but keeping a local
    // shim makes the snapshot of this file portable into a pinned net6.0
    // build without surprise behaviour changes.
    private static double Clamp(double v, double lo, double hi) =>
        v < lo ? lo : (v > hi ? hi : v);
}
