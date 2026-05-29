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

    public uint NextU32() => (uint)(NextU64() >> 32);

    // Uniform double in [0,1) using top 53 bits, per §3.
    public double NextUnit() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

    public double Uniform(double lo, double hi) => lo + NextUnit() * (hi - lo);

    public double Exponential(double lambda) => -Math.Log(1.0 - Uniform(0.0, 1.0)) / lambda;

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

    // Original Grass-scene slot variants restored after leaving Desert.
    public bool   OriginalIsFlower;
    public bool   OriginalIsMushroom;

    // Cactus (§14). Desert-only slot-bound blade variant.
    public bool   IsCactus;
    public byte   CactusType;      // 0 = column, 1 = single-arm, 2 = saguaro
    public double CactusHeight;    // DIP
    public double CactusWidth;     // DIP
    public sbyte  CactusArmSide;   // -1 or +1 for type 1

    // Pine (§15.1). Winter-only slot-bound blade variant.
    public bool   IsPine;
    public byte   PineTierCount;   // 2..4
    public double PineHeight;      // DIP
    public double PineWidth;       // DIP (base-tier width)

    public double EffectiveLean;
}

// Roaming entities (architecture.md §13.2). Tumbleweeds (Desert §14) and
// snowflakes (Winter §15) live in Sim.Entities. The struct fields are
// shared across kinds; per-kind tick logic branches on Kind.
public enum EntityKind : byte { None = 0, Tumbleweed = 1, Snowflake = 2 }

public struct Entity
{
    public EntityKind Kind;
    public double X;
    public double Y;
    public double Vx;
    public double Vy;
    public double Size;
    public double Rotation;
    public double RotationSpeed;
    public double Age;
    public double Lifetime;   // <= 0 means infinite (respawn-in-place)
    public uint   Seed;
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

    // Ambient gust scheduler (§8.1). Initialized by ResetAmbientGusts.
    public Prng AmbientPrng;
    public double NextAmbientGustTime;
    public double MonitorWidth;

    // Scene (§13). State-only field — switching scenes does not regenerate
    // blades or perturb any PRNG stream.
    public Scene CurrentScene = Constants.SCENE_DEFAULT;

    // Roaming entities (§13.2). Empty in the Grass scene; populated by the
    // per-scene generators dispatched from SetScene (Desert: tumbleweeds,
    // Winter: snowflakes). Pre-sized to MAX_ENTITIES_PER_MONITOR capacity
    // at construction so the tick path never grows the list.
    public List<Entity> Entities = new(Constants.MAX_ENTITIES_PER_MONITOR);

    // Per-scene entity-stream seed. Initially zero; set by ResetEntities().
    public ulong EntitySeed;

    // Persistent tumbleweed stream (§14), consumed by off-edge respawns.
    public Prng TumbleweedPrng;

    // §15 snowflake emitter (Winter scene only).
    public Prng SnowflakePrng;
    public double NextSnowflakeSpawnTime;

    public void SetScene(Scene s)
    {
        CurrentScene = s;
        Entities.Clear();

        // Every scene transition starts from a clean blade-variant slate so
        // that e.g. Desert→Winter doesn't leave cacti on screen. Desert then
        // promotes selected slots back into cacti below.
        for (int i = 0; i < Blades.Length; i++)
        {
            RestoreOriginalVariants(ref Blades[i]);
        }

        switch (s)
        {
            case Scene.Grass:
                break;
            case Scene.Desert:
                GenerateCactiForDesert(this);
                GenerateTumbleweeds(this);
                break;
            case Scene.Winter:
                GeneratePinesForWinter(this);
                SnowflakePrng = Prng.Init(EntitySeed ^ Constants.SNOWFLAKE_PRNG_SALT);
                double lambda = Constants.SNOWFLAKE_EMIT_RATE_PER_1920DIP * MonitorWidth / 1920.0;
                NextSnowflakeSpawnTime = GlobalTime + SnowflakePrng.Exponential(lambda);
                break;
        }
    }

    public void ResetEntities(ulong seed)
    {
        EntitySeed = seed;
        Entities.Clear();
    }

    private static void RestoreOriginalVariants(ref Blade b)
    {
        b.IsCactus = false;
        b.CactusType = 0;
        b.CactusHeight = 0.0;
        b.CactusWidth = 0.0;
        b.CactusArmSide = +1;
        b.IsPine = false;
        b.PineTierCount = 0;
        b.PineHeight = 0.0;
        b.PineWidth = 0.0;
        b.IsFlower = b.OriginalIsFlower;
        b.IsMushroom = b.OriginalIsMushroom;
    }

    public static void GeneratePinesForWinter(Sim sim)
    {
        var pinePrng = Prng.Init(sim.EntitySeed ^ Constants.PINE_PRNG_SALT);

        for (int i = 0; i < sim.Blades.Length; i++)
        {
            ref Blade b = ref sim.Blades[i];
            RestoreOriginalVariants(ref b);

            double r = pinePrng.Uniform(0.0, 1.0);
            if (r >= Constants.PINE_PROBABILITY) continue;

            b.IsPine = true;
            b.IsFlower = false;
            b.IsMushroom = false;
            b.PineHeight = pinePrng.Uniform(Constants.PINE_HEIGHT_MIN, Constants.PINE_HEIGHT_MAX);
            b.PineWidth = pinePrng.Uniform(Constants.PINE_WIDTH_MIN, Constants.PINE_WIDTH_MAX);

            double tierDraw = pinePrng.Uniform(Constants.PINE_TIER_COUNT_MIN,
                                                Constants.PINE_TIER_COUNT_MAX + 1);
            int tiers = (int)Math.Floor(tierDraw);
            if (tiers < Constants.PINE_TIER_COUNT_MIN) tiers = Constants.PINE_TIER_COUNT_MIN;
            if (tiers > Constants.PINE_TIER_COUNT_MAX) tiers = Constants.PINE_TIER_COUNT_MAX;
            b.PineTierCount = (byte)tiers;
        }
    }

    public static void GenerateCactiForDesert(Sim sim)
    {
        var cactusPrng = Prng.Init(sim.EntitySeed ^ Constants.CACTUS_PRNG_SALT);

        for (int i = 0; i < sim.Blades.Length; i++)
        {
            ref Blade b = ref sim.Blades[i];
            RestoreOriginalVariants(ref b);

            double r = cactusPrng.Uniform(0.0, 1.0);
            if (r >= Constants.CACTUS_PROBABILITY) continue;

            b.IsCactus = true;
            b.IsFlower = false;
            b.IsMushroom = false;
            b.CactusHeight = cactusPrng.Uniform(Constants.CACTUS_HEIGHT_MIN, Constants.CACTUS_HEIGHT_MAX);
            b.CactusWidth = cactusPrng.Uniform(Constants.CACTUS_WIDTH_MIN, Constants.CACTUS_WIDTH_MAX);

            double armDraw = cactusPrng.Uniform(0.0, 1.0);
            double noArmThreshold = 1.0 - Constants.CACTUS_ARM_PROBABILITY;
            double twoArmThreshold = noArmThreshold + Constants.CACTUS_TWO_ARM_PROBABILITY * Constants.CACTUS_ARM_PROBABILITY;
            if (armDraw < noArmThreshold)
            {
                b.CactusType = 0;
                b.CactusArmSide = +1;
            }
            else if (armDraw < twoArmThreshold)
            {
                b.CactusType = 2;
                b.CactusArmSide = +1;
            }
            else
            {
                b.CactusType = 1;
                b.CactusArmSide = cactusPrng.Uniform(0.0, 1.0) < 0.5 ? (sbyte)-1 : (sbyte)+1;
            }
        }
    }

    public static void GenerateTumbleweeds(Sim sim)
    {
        sim.TumbleweedPrng = Prng.Init(sim.EntitySeed ^ Constants.TUMBLEWEED_PRNG_SALT);
        int count = TumbleweedCountForWidth(sim.MonitorWidth);
        for (int i = 0; i < count; i++)
        {
            sim.Entities.Add(MakeTumbleweed(ref sim.TumbleweedPrng, sim.MonitorWidth, sim.GroundY));
        }
    }

    private static int TumbleweedCountForWidth(double monitorWidth)
    {
        if (monitorWidth < 480.0) return 0;
        int count = (int)Math.Floor(monitorWidth / 1920.0 * Constants.TUMBLEWEED_COUNT_PER_1920DIP);
        if (count < 1) count = 1;
        return Math.Min(count, Constants.MAX_ENTITIES_PER_MONITOR);
    }

    private static Entity MakeTumbleweed(ref Prng prng, double monitorWidth, double groundY)
    {
        Entity e = default;
        e.Kind = EntityKind.Tumbleweed;
        e.Size = prng.Uniform(Constants.TUMBLEWEED_SIZE_MIN, Constants.TUMBLEWEED_SIZE_MAX);
        e.X = prng.Uniform(0.0, monitorWidth);
        e.Y = groundY - prng.Uniform(Constants.TUMBLEWEED_Y_OFFSET_MIN, Constants.TUMBLEWEED_Y_OFFSET_MAX);
        double speed = prng.Uniform(Constants.TUMBLEWEED_SPEED_MIN, Constants.TUMBLEWEED_SPEED_MAX);
        double direction = prng.Uniform(0.0, 1.0) < 0.5 ? -1.0 : 1.0;
        e.Vx = direction * speed;
        e.Vy = 0.0;
        e.Rotation = prng.Uniform(0.0, 2.0 * Math.PI);
        e.RotationSpeed = e.Vx / e.Size;
        e.Age = 0.0;
        e.Lifetime = -1.0;
        e.Seed = prng.NextU32();
        return e;
    }

    private static void RespawnTumbleweed(ref Entity e, ref Prng prng, double monitorWidth,
                                          double groundY, bool fromLeft)
    {
        e.Size = prng.Uniform(Constants.TUMBLEWEED_SIZE_MIN, Constants.TUMBLEWEED_SIZE_MAX);
        e.Y = groundY - prng.Uniform(Constants.TUMBLEWEED_Y_OFFSET_MIN, Constants.TUMBLEWEED_Y_OFFSET_MAX);
        double speed = prng.Uniform(Constants.TUMBLEWEED_SPEED_MIN, Constants.TUMBLEWEED_SPEED_MAX);
        e.X = fromLeft ? -e.Size : monitorWidth + e.Size;
        e.Vx = fromLeft ? speed : -speed;
        e.Vy = 0.0;
        e.RotationSpeed = e.Vx / e.Size;
        e.Age = 0.0;
        e.Lifetime = -1.0;
    }

    // §13.2 — generic roaming-entity tick. Integrates position + rotation,
    // ages each entity. Per-kind logic (tumbleweed respawn, snowflake sway
    // and culling) is added by the §14 / §15 content agents.
    public void TickEntities(double dt)
    {
        double groundY = WindowHeight;
        double twoPi = 2.0 * Math.PI;

        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            e.X        += e.Vx * dt;
            e.Y        += e.Vy * dt;
            e.Rotation += e.RotationSpeed * dt;
            e.Age      += dt;

            if (e.Kind == EntityKind.Tumbleweed)
            {
                if (e.X < -e.Size - 10.0)
                {
                    RespawnTumbleweed(ref e, ref TumbleweedPrng, MonitorWidth, GroundY, fromLeft: false);
                }
                else if (e.X > MonitorWidth + e.Size + 10.0)
                {
                    RespawnTumbleweed(ref e, ref TumbleweedPrng, MonitorWidth, GroundY, fromLeft: true);
                }
            }

            Entities[i] = e;
        }

        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Snowflake) continue;
            double phase = e.Seed / 4294967295.0 * twoPi;
            e.Vx = Constants.SNOWFLAKE_SWAY_AMPLITUDE * Constants.SNOWFLAKE_SWAY_FREQUENCY * twoPi
                 * Math.Cos(e.Age * twoPi * Constants.SNOWFLAKE_SWAY_FREQUENCY + phase);
            Entities[i] = e;
        }

        Entities.RemoveAll(e => e.Kind == EntityKind.Snowflake
                             && (e.Age >= e.Lifetime || e.Y > groundY));

        if (CurrentScene == Scene.Winter)
        {
            double lambda = Constants.SNOWFLAKE_EMIT_RATE_PER_1920DIP * MonitorWidth / 1920.0;
            while (GlobalTime >= NextSnowflakeSpawnTime && Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR)
            {
                Entity e = default;
                e.Kind = EntityKind.Snowflake;
                e.Size = SnowflakePrng.Uniform(Constants.SNOWFLAKE_SIZE_MIN, Constants.SNOWFLAKE_SIZE_MAX);
                e.X = SnowflakePrng.Uniform(-20.0, MonitorWidth + 20.0);
                double fallSpeed = SnowflakePrng.Uniform(Constants.SNOWFLAKE_FALL_SPEED_MIN,
                                                         Constants.SNOWFLAKE_FALL_SPEED_MAX);
                e.Y = -e.Size - 4.0;
                e.Vx = 0.0;
                e.Vy = fallSpeed;
                e.Rotation = SnowflakePrng.Uniform(0.0, twoPi);
                e.RotationSpeed = SnowflakePrng.Uniform(-1.5, 1.5);
                e.Age = 0.0;
                e.Lifetime = (groundY + e.Size) / fallSpeed + Constants.SNOWFLAKE_LIFETIME_PADDING_SEC;
                e.Seed = SnowflakePrng.NextU32();
                Entities.Add(e);
                NextSnowflakeSpawnTime += SnowflakePrng.Exponential(lambda);
            }
        }
    }

    // (§8.1) Initialize / reset the ambient gust scheduler. Called by the
    // window factory after constructing the Sim and assigning MonitorWidth.
    // Public for unit tests.
    public void ResetAmbientGusts(ulong seed, double monitorWidth)
    {
        AmbientPrng = Prng.Init(seed ^ Constants.AMBIENT_GUST_PRNG_SALT);
        MonitorWidth = monitorWidth;
        // First interval drawn immediately so the first puff never fires
        // at t=0 and every subsequent fire is exactly 4 PRNG draws.
        NextAmbientGustTime = GlobalTime
                            + AmbientPrng.Uniform(Constants.AMBIENT_GUST_INTERVAL_MIN,
                                                   Constants.AMBIENT_GUST_INTERVAL_MAX);
    }

    // (§8.1) Same impulse kernel as cursor gusts but with GUST_RADIUS *
    // AMBIENT_GUST_RADIUS_FACTOR and an impulse magnitude parameterised by
    // magFactor instead of the cursor-derived capped velocity.
    public void ApplyAmbientGust(double x, double signDir, double magFactor)
    {
        double impulseMagnitude = Constants.MAX_CURSOR_SPEED * magFactor * Constants.IMPULSE_SCALE;
        double radius           = Constants.GUST_RADIUS * Constants.AMBIENT_GUST_RADIUS_FACTOR;
        if (signDir == 0.0 || impulseMagnitude == 0.0 || radius <= 0.0) return;

        for (int i = 0; i < Blades.Length; i++)
        {
            ref Blade b = ref Blades[i];
            double dxAbs = Math.Abs(b.BaseX - x);
            if (dxAbs >= radius) continue;

            double tRaw   = 1.0 - dxAbs / radius;
            double s      = Math.Clamp(tRaw, 0.0, 1.0);
            double smooth = s * s * (3.0 - 2.0 * s);
            b.GustVelocity += impulseMagnitude * smooth * signDir;
        }
    }

    // (§8.1) Run the ambient gust scheduler one tick. Fires zero or more
    // puffs depending on how many scheduled fire times GlobalTime has
    // crossed. Idempotent (zero PRNG draws) on idle ticks.
    public void TickAmbientGusts()
    {
        // Per-fire draw order is fixed (§8.1): x, signDir, magFactor, interval.
        while (GlobalTime >= NextAmbientGustTime)
        {
            double x         = AmbientPrng.Uniform(0.0, MonitorWidth);
            double signDir   = AmbientPrng.Uniform(0.0, 1.0) < 0.5 ? -1.0 : 1.0;
            double magFactor = AmbientPrng.Uniform(Constants.AMBIENT_GUST_MAG_FACTOR_MIN,
                                                    Constants.AMBIENT_GUST_MAG_FACTOR_MAX);
            ApplyAmbientGust(x, signDir, magFactor);

            double interval  = AmbientPrng.Uniform(Constants.AMBIENT_GUST_INTERVAL_MIN,
                                                    Constants.AMBIENT_GUST_INTERVAL_MAX);
            NextAmbientGustTime += interval;
        }
    }

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

            b.OriginalIsFlower = b.IsFlower;
            b.OriginalIsMushroom = b.IsMushroom;

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

        // §8.1 — fire any scheduled ambient gusts that the new GlobalTime
        // has crossed. Runs BEFORE the per-blade update so puffs contribute
        // to the same decay step as cursor impulses.
        TickAmbientGusts();

        for (int i = 0; i < Blades.Length; i++)
        {
            ref Blade b = ref Blades[i];
            UpdateBladeDynamics(ref b, GlobalTime, dt);
            AdvanceCut(ref b, GlobalTime);
        }

        TickEntities(dt);
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

    public static Stroke ComputeBladeStroke(in Blade b, double groundY, Scene scene)
    {
        uint argb = Constants.SCENE_PALETTES[(int)scene, b.Hue];

        if (b.CutHeight < Constants.CUT_STUMP_THRESHOLD)
        {
            return new Stroke(
                b.BaseX, groundY,
                b.BaseX, groundY - 1.0,
                b.BaseX, groundY - Constants.STUMP_HEIGHT,
                b.Thickness, argb);
        }

        double L = b.Height * b.HeightBonus * b.CutHeight;
        if (scene == Scene.Desert && !b.IsCactus && !b.IsMushroom)
        {
            L *= Constants.DESERT_GRASS_HEIGHT_SCALE;
        }

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
