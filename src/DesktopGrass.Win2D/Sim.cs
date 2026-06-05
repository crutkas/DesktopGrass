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

    // Residual normalized height a mowed blade settles at (stubble). Assigned
    // once at generation from an independent salted PRNG stream. Defaults to
    // 0.0 so default-constructed Blade fixtures collapse fully as before.
    public double CutFloor;

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
    public byte   PineTierCount;   // 2..4 (only meaningful for TreeVariant == 0)
    public byte   TreeVariant;     // 0 = pine, 1 = birch
    public double PineHeight;      // DIP
    public double PineWidth;       // DIP (pine base-tier width OR birch trunk width)

    // Maple (§16.5). Autumn-only slot-bound blade variant.
    public bool   IsMaple;
    public double MapleHeight;
    public double MapleTrunkWidth;
    public double MapleCanopyRadius;
    public byte   MapleCanopyColorIdx;
    public bool   MapleIsBare;

    // Leaf puff cooldown (§16.6). Absolute GlobalTime before which this maple
    // will not shed another hover-triggered leaf flurry. Runtime-only; default
    // 0.0 keeps default-constructed Blade fixtures ready to puff immediately.
    public double LeafPuffCooldownEnd;

    public double EffectiveLean;
}

// Roaming entities (architecture.md §13.2). Tumbleweeds (Desert §14),
// snowflakes (Winter §15), sheep (§16), cats (§17), bunnies (§18), and birds (§17.8) live in Sim.Entities.
// The struct fields are shared across kinds; per-kind tick logic branches on Kind.
// 5 (Raindrop) retired — rain effect removed; discriminant left as a gap so the
// remaining cross-impl-locked ordinals stay stable.
public enum EntityKind : byte { None = 0, Tumbleweed = 1, Snowflake = 2, Sheep = 3, Cat = 4, Bunny = 6, Butterfly = 7, Firefly = 8, Bird = 9, Hedgehog = 10, Leaf = 11, SnowPuff = 12 }

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
    // Critter state machine (§16, §17). Sheep and Cat share state bytes;
    // Cat reuses Hopping semantically as Pouncing.
    // Values are ignored by tumbleweeds/snowflakes and inert by default.
    public byte   State;       // critters: see species STATE constants
    public double StateTimer;  // sec remaining in current state
    public byte   PreviousState; // hedgehog: pre-curl state
    public double PreviousStateTimer; // hedgehog: remaining pre-curl time
    public byte   NameIndex;   // critters: index into species name pool
    public byte   CoatVariantIndex; // cat: index into CAT_COAT_PALETTES

    // Ambient flyers (§17.6-§17.8). Ignored by grounded pets.
    public double BaseSpeed;
    public double AltitudeAnchor;
    public double PhaseY; // butterflies/fireflies: Y phase; birds: vertical drift phase
    public double PhaseX; // butterflies/fireflies: X phase; birds: wing phase offset
    public double BlinkPeriod;
    public double BlinkPhase;
    public byte   ColorVariant;

    // Bird flyby (§17.8) transient metadata.
    public double X0;
    public double SpawnTime;
    public double FormationOffsetAlongFlight;
    public double FormationOffsetPerpendicular;
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

    // Roaming entities (§13.2). Desert and Winter add their own scene entities
    // via SetScene. Pre-sized to MAX_ENTITIES_PER_MONITOR capacity at
    // construction so the tick path never grows the list.
    public List<Entity> Entities = new(Constants.MAX_ENTITIES_PER_MONITOR);

    // Per-scene entity-stream seed. Initially zero; set by ResetEntities().
    public ulong EntitySeed;

    // Persistent tumbleweed stream (§14), consumed by off-edge respawns.
    public Prng TumbleweedPrng;

    // §15 snowflake emitter (Winter scene only).
    public Prng SnowflakePrng;
    public double NextSnowflakeSpawnTime;

    // §15.2 passive snow accumulation, persisted per monitor.
    public double SnowDepth;
    public ulong SnowPhaseSeed;

    // §16.5 leaf emitter (Autumn scene only).
    public Prng LeafPrng;
    public double NextLeafSpawnTime;

    // §16.6 leaf-puff emitter (Autumn scene only). Independent salted stream so
    // cursor-triggered puffs never perturb the ambient leaf emitter's draws.
    public Prng LeafPuffPrng;

    // §21 snow-puff emitter (Winter scene only). Independent salted stream so
    // click-triggered powder bursts never perturb the snowflake emitter's draws.
    public Prng SnowPuffPrng;

    // §17.8 daytime bird-flyby emitter. Transient Grass-only flocks share one
    // persistent stream and one next-event time across scene switches.
    public Prng BirdFlybyPrng;
    public double NextBirdFlybyAtTime;

    // Critter subsystem (§13.3 / §16). Independent of Scene. CurrentCritter
    // drives which (if any) generator runs at the END of SetScene so critter
    // entities survive scene changes. CritterPrng is reseeded on every
    // generator call from EntitySeed XOR CRITTER_PRNG_SALT. CritterCountOverride
    // is 0 for random species count, or a fixed count capped at generation.
    public CritterKind CurrentCritter = Constants.CRITTER_DEFAULT;
    public Prng CritterPrng;
    public int CritterCountOverride;

    private static bool HourInHalfOpenRange(int hour, int start, int end) =>
        start <= end ? hour >= start && hour < end : hour >= start || hour < end;

    public static bool BirdFlybyIsDayHour(int hour) =>
        hour >= 0 && hour <= 23 && HourInHalfOpenRange(hour, Constants.BIRD_FLYBY_HOUR_START, Constants.BIRD_FLYBY_HOUR_END);

    public static double BirdFlybySampleInterval(ref Prng p) =>
        p.Exponential(Constants.BIRD_FLYBY_SPAWN_RATE_PER_HOUR / 3600.0);

    public static ulong SnowPhaseSeedForMonitor(int width, int height, int left, int top)
    {
        unchecked
        {
            ulong h = 1469598103934665603UL;
            void Mix(int value)
            {
                ulong v = (ulong)(long)value;
                for (int i = 0; i < 8; i++)
                {
                    h ^= v & 0xFFUL;
                    h *= 1099511628211UL;
                    v >>= 8;
                }
            }

            Mix(width);
            Mix(height);
            Mix(left);
            Mix(top);
            return h == 0UL ? 1UL : h;
        }
    }

    private static ulong SplitMix64(ulong z)
    {
        unchecked
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public void SetSnowDepth(double depth)
    {
        if (!double.IsFinite(depth) || depth <= 0.0)
        {
            SnowDepth = 0.0;
            return;
        }
        SnowDepth = Math.Min(depth, Constants.SNOW_DEPTH_MAX);
    }

    public double SnowTopYAt(double x)
    {
        if (SnowDepth <= 0.0) return WindowHeight;
        ulong identity = SnowPhaseSeed != 0UL
            ? SnowPhaseSeed
            : SnowPhaseSeedForMonitor((int)Math.Round(MonitorWidth), (int)Math.Round(WindowHeight), 0, 0);
        ulong phaseBits = SplitMix64(identity ^ Constants.SNOW_TOP_UNDULATION_PHASE_SALT);
        double phase = (phaseBits >> 11) * (1.0 / 9007199254740992.0) * (2.0 * Math.PI);
        double top = WindowHeight - SnowDepth
                   + Math.Sin((x / Constants.SNOW_TOP_UNDULATION_WAVELENGTH) * (2.0 * Math.PI) + phase)
                   * Constants.SNOW_TOP_UNDULATION_AMP;
        return Math.Min(top, WindowHeight);
    }

    public double SnowTreeBaseYOffset => SnowDepth <= 0.0
        ? 0.0
        : Math.Clamp(SnowDepth - Constants.SNOW_TOP_UNDULATION_AMP,
                     0.0,
                     Constants.SNOW_DEPTH_MAX - Constants.SNOW_TOP_UNDULATION_AMP);

    internal static double SheepSleepProbForLocalHour(int hour)
    {
        if (hour < 0 || hour > 23) return Constants.SHEEP_SLEEP_PROB_DEFAULT;
        if (HourInHalfOpenRange(hour, Constants.SHEEP_MORNING_START_HOUR,
                                Constants.SHEEP_MORNING_END_HOUR))
            return Constants.SHEEP_SLEEP_PROB_MORNING;
        if (HourInHalfOpenRange(hour, Constants.SHEEP_NIGHT_START_HOUR,
                                Constants.SHEEP_NIGHT_END_HOUR))
            return Constants.SHEEP_SLEEP_PROB_NIGHT;
        return Constants.SHEEP_SLEEP_PROB_DEFAULT;
    }

    internal static double CatSleepProbForLocalHour(int hour)
    {
        if (hour < 0 || hour > 23) return Constants.CAT_SLEEP_FROM_IDLE_PROB_DEFAULT;
        if (HourInHalfOpenRange(hour, Constants.SHEEP_MORNING_START_HOUR,
                                Constants.SHEEP_MORNING_END_HOUR))
            return Constants.CAT_SLEEP_FROM_IDLE_PROB_MORNING;
        if (HourInHalfOpenRange(hour, Constants.SHEEP_NIGHT_START_HOUR,
                                Constants.SHEEP_NIGHT_END_HOUR))
            return Constants.CAT_SLEEP_FROM_IDLE_PROB_NIGHT;
        return Constants.CAT_SLEEP_FROM_IDLE_PROB_DEFAULT;
    }

    internal static double BunnySleepProbForLocalHour(int hour)
    {
        if (hour < 0 || hour > 23) return Constants.BUNNY_SLEEP_PROB_DAY;
        return HourInHalfOpenRange(hour, 10, 20)
            ? Constants.BUNNY_SLEEP_PROB_DAY
            : Constants.BUNNY_SLEEP_PROB_NIGHT;
    }

    internal static double HedgehogSleepProbForLocalHour(int hour)
    {
        if (hour < 0 || hour > 23) return Constants.HEDGEHOG_SLEEP_PROB_DAY;
        return HourInHalfOpenRange(hour, 6, 18)
            ? Constants.HEDGEHOG_SLEEP_PROB_DAY
            : Constants.HEDGEHOG_SLEEP_PROB_NIGHT;
    }

    internal static double BunnyHopYOffset(double age, bool startled)
    {
        double height = startled ? Constants.BUNNY_STARTLE_HOP_HEIGHT : Constants.BUNNY_HOP_HEIGHT;
        double t = Math.Clamp(age / Constants.BUNNY_HOP_DURATION, 0.0, 1.0);
        return 4.0 * height * t * (1.0 - t);
    }

    internal static byte BunnyChooseRestState(ref Prng p, int hour)
    {
        double sleepProb = BunnySleepProbForLocalHour(hour);
        double r = p.Uniform(0.0, 1.0);
        if (r < sleepProb) return Constants.BUNNY_STATE_SLEEPING;

        double activeWeight = Constants.BUNNY_GRAZE_PROBABILITY + Constants.BUNNY_IDLE_PROBABILITY;
        double activeT = activeWeight > 0.0 && sleepProb < 1.0
            ? (r - sleepProb) / (1.0 - sleepProb)
            : 0.0;
        return activeT < Constants.BUNNY_GRAZE_PROBABILITY / activeWeight
            ? Constants.BUNNY_STATE_GRAZING
            : Constants.BUNNY_STATE_IDLE;
    }

    internal static byte HedgehogChooseRestState(ref Prng p, int hour)
    {
        double sleepProb = HedgehogSleepProbForLocalHour(hour);
        double r = p.Uniform(0.0, 1.0);
        if (r < sleepProb) return Constants.HEDGEHOG_STATE_SLEEPING;

        double activeWeight = Constants.HEDGEHOG_SNUFFLE_PROBABILITY + Constants.HEDGEHOG_IDLE_PROBABILITY;
        double activeT = activeWeight > 0.0 && sleepProb < 1.0
            ? (r - sleepProb) / (1.0 - sleepProb)
            : 0.0;
        return activeT < Constants.HEDGEHOG_SNUFFLE_PROBABILITY / activeWeight
            ? Constants.HEDGEHOG_STATE_SNUFFLING
            : Constants.HEDGEHOG_STATE_IDLE;
    }

    public void SetScene(Scene s)
    {
        if (s != Scene.Winter)
        {
            SnowDepth = 0.0;
        }
        CurrentScene = s;
        // Scene transitions clear all roaming entities; each scene repopulates
        // its own below.
        RemoveSceneTransitionEntities();

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
                SnowPuffPrng = Prng.Init(EntitySeed ^ Constants.SNOW_PUFF_PRNG_SALT);
                double lambda = Constants.SNOWFLAKE_EMIT_RATE_PER_1920DIP * MonitorWidth / 1920.0;
                NextSnowflakeSpawnTime = GlobalTime + SnowflakePrng.Exponential(lambda);
                break;
            case Scene.Autumn:
                GenerateMaplesForAutumn(this);
                LeafPrng = Prng.Init(EntitySeed ^ Constants.LEAF_PRNG_SALT);
                LeafPuffPrng = Prng.Init(EntitySeed ^ Constants.LEAF_PUFF_PRNG_SALT);
                NextLeafSpawnTime = GlobalTime;
                break;
        }

        // Grass critters/flyers are scene-gated inside GenerateCrittersForKind.
        // Run LAST so scene entity snapshots stay pinned.
        GenerateCrittersForKind(this);
    }

    public void SetCritter(CritterKind c)
    {
        CurrentCritter = c;
        // Erase only critter entities — scene entities (tumbleweeds,
        // snowflakes) are preserved across critter toggles.
        RemoveCritters();
        GenerateCrittersForKind(this);
    }

    public void SetCritterCount(int n)
    {
        CritterCountOverride = n > 0 ? n : 0;
        RemoveCritters();
        GenerateCrittersForKind(this);
    }

    private void RemoveCritters() =>
        Entities.RemoveAll(e => e.Kind == EntityKind.Sheep
                             || e.Kind == EntityKind.Cat
                             || e.Kind == EntityKind.Bunny
                             || e.Kind == EntityKind.Hedgehog
                             || e.Kind == EntityKind.Butterfly
                             || e.Kind == EntityKind.Firefly);

    private void RemoveSceneTransitionEntities() =>
        Entities.Clear();

    private static void GenerateCrittersForKind(Sim sim)
    {
        sim.CritterPrng = Prng.Init(sim.EntitySeed ^ Constants.CRITTER_PRNG_SALT);
        if (sim.CurrentScene != Scene.Grass) return;

        switch (sim.CurrentCritter)
        {
            case CritterKind.None:
                break;
            case CritterKind.Sheep:
                GenerateCrittersSheep(sim, allowOverride: true);
                break;
            case CritterKind.Cat:
                GenerateCrittersCat(sim, allowOverride: true);
                break;
            case CritterKind.Bunny:
                GenerateGrassCrittersAll(sim);
                break;
        }

        GenerateButterflies(sim);
        GenerateFireflies(sim);
    }

    private static int ResolveCountFromPrng(ref Prng prng, int minCount, int maxCount)
    {
        double countDraw = prng.Uniform(minCount, maxCount + 1);
        int count = (int)Math.Floor(countDraw);
        if (count < minCount) count = minCount;
        if (count > maxCount) count = maxCount;
        return count;
    }

    private static int ResolveCritterCount(Sim sim, int minCount, int maxCount, bool allowOverride)
    {
        if (allowOverride && sim.CritterCountOverride > 0)
        {
            return Math.Min(sim.CritterCountOverride, Constants.PET_COUNT_MAX_PER_MONITOR);
        }

        return ResolveCountFromPrng(ref sim.CritterPrng, minCount, maxCount);
    }

    private static double FlyerGrassTopY(Sim sim) => sim.WindowHeight - Constants.BLADE_HEIGHT_MAX;

    private static double ButterflyVelocity(in Entity e)
    {
        double dir = e.Vx >= 0.0 ? 1.0 : -1.0;
        return e.BaseSpeed * dir * (1.0 + Constants.BUTTERFLY_MEANDER_AMP_X
            * Math.Sin(e.Age * Constants.BUTTERFLY_MEANDER_FREQ_X + e.PhaseX));
    }

    private static double FireflyVelocity(in Entity e)
    {
        double dir = e.Vx >= 0.0 ? 1.0 : -1.0;
        return e.BaseSpeed * dir * (1.0 + Constants.FIREFLY_DRIFT_AMP_X
            * Math.Sin(e.Age * Constants.FIREFLY_DRIFT_FREQ_X + e.PhaseX));
    }

    private static void UpdateButterflyPosition(ref Entity e, Sim sim)
    {
        e.Vx = ButterflyVelocity(in e);
        e.Y = FlyerGrassTopY(sim) - e.AltitudeAnchor
            + Constants.BUTTERFLY_MEANDER_AMP_Y * Math.Sin(e.Age * Constants.BUTTERFLY_MEANDER_FREQ_Y + e.PhaseY);
    }

    private static void UpdateFireflyPosition(ref Entity e, Sim sim)
    {
        e.Vx = FireflyVelocity(in e);
        e.Y = FlyerGrassTopY(sim) - e.AltitudeAnchor
            + Constants.FIREFLY_DRIFT_AMP_Y * Math.Sin(e.Age * Constants.FIREFLY_DRIFT_FREQ_Y + e.PhaseY);
    }

    private static void UpdateBirdPosition(ref Entity e, Sim sim)
    {
        e.Y = FlyerGrassTopY(sim) - e.AltitudeAnchor
            + Constants.BIRD_DRIFT_AMP_Y * Math.Sin(e.Age * Constants.BIRD_DRIFT_FREQ_Y + e.PhaseY);
    }

    public void SpawnBirdFlyby()
    {
        if (MonitorWidth <= 0.0) return;

        int flockSize = ResolveCountFromPrng(ref BirdFlybyPrng, Constants.BIRD_FLOCK_SIZE_MIN, Constants.BIRD_FLOCK_SIZE_MAX);
        ulong directionBit = BirdFlybyPrng.NextU64() & 1UL;
        double direction = directionBit != 0UL ? 1.0 : -1.0;
        double leaderAltitude = BirdFlybyPrng.Uniform(Constants.BIRD_ALTITUDE_MIN, Constants.BIRD_ALTITUDE_MAX);
        double leaderSpeed = BirdFlybyPrng.Uniform(Constants.BIRD_SPEED_MIN, Constants.BIRD_SPEED_MAX);
        ulong formationStyle = BirdFlybyPrng.NextU64() & 1UL;

        Span<double> wingPhaseOffsets = stackalloc double[Constants.BIRD_FLOCK_SIZE_MAX];
        Span<double> verticalDriftPhases = stackalloc double[Constants.BIRD_FLOCK_SIZE_MAX];
        for (int i = 0; i < flockSize; i++)
        {
            wingPhaseOffsets[i] = BirdFlybyPrng.Uniform(
                -Constants.BIRD_WING_FLAP_PHASE_JITTER, Constants.BIRD_WING_FLAP_PHASE_JITTER);
            verticalDriftPhases[i] = BirdFlybyPrng.Uniform(0.0, 2.0 * Math.PI);
        }

        if (Entities.Count + flockSize > Constants.MAX_ENTITIES_PER_MONITOR) return;

        double spawnX = direction > 0.0 ? -50.0 : MonitorWidth + 50.0;
        double sinAngle = Math.Sin(Constants.BIRD_FLOCK_V_ANGLE_DEG * Math.PI / 180.0);
        for (int i = 0; i < flockSize; i++)
        {
            double along = -i * Constants.BIRD_FLOCK_FORMATION_SPACING;
            double perpendicular;
            if (formationStyle == 0UL)
            {
                int armIndex = (i + 1) / 2;
                double side = (i % 2) == 0 ? 1.0 : -1.0;
                perpendicular = side * armIndex * Constants.BIRD_FLOCK_FORMATION_SPACING * sinAngle;
            }
            else
            {
                perpendicular = i * Constants.BIRD_FLOCK_FORMATION_SPACING * sinAngle;
            }

            Entity e = default;
            e.Kind = EntityKind.Bird;
            e.Size = Constants.BIRD_WING_SPAN * 0.5;
            e.X = spawnX + direction * along;
            e.X0 = e.X;
            e.Vx = direction * leaderSpeed;
            e.Vy = 0.0;
            e.BaseSpeed = leaderSpeed;
            e.AltitudeAnchor = leaderAltitude - perpendicular;
            e.PhaseX = wingPhaseOffsets[i];
            e.PhaseY = verticalDriftPhases[i];
            e.Age = 0.0;
            e.Lifetime = -1.0;
            e.SpawnTime = GlobalTime;
            e.FormationOffsetAlongFlight = along;
            e.FormationOffsetPerpendicular = perpendicular;
            e.ColorVariant = (byte)formationStyle;
            e.Seed = (uint)(i + 1);
            UpdateBirdPosition(ref e, this);
            Entities.Add(e);
        }
    }

    public void TickBirdFlybys(int hour)
    {
        if (CurrentScene != Scene.Grass || MonitorWidth <= 0.0) return;
        if (!BirdFlybyIsDayHour(hour)) return;
        if (GlobalTime < NextBirdFlybyAtTime) return;

        SpawnBirdFlyby();
        NextBirdFlybyAtTime = GlobalTime + BirdFlybySampleInterval(ref BirdFlybyPrng);
    }

    private static void GenerateCrittersSheep(Sim sim, bool allowOverride)
    {
        int count = ResolveCritterCount(sim, Constants.SHEEP_COUNT_MIN, Constants.SHEEP_COUNT_MAX, allowOverride);

        double groundY = sim.WindowHeight;
        for (int i = 0; i < count
             && sim.Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR; i++)
        {
            Entity e = default;
            e.Kind = EntityKind.Sheep;
            e.Size = Constants.SHEEP_BODY_RADIUS;
            double margin = e.Size + 8.0;
            e.X = sim.CritterPrng.Uniform(margin, sim.MonitorWidth - margin);
            e.Y = groundY - Constants.SHEEP_BODY_HEIGHT - Constants.SHEEP_LEG_LENGTH;
            double speed = sim.CritterPrng.Uniform(
                Constants.SHEEP_WALK_SPEED_MIN, Constants.SHEEP_WALK_SPEED_MAX);
            double dir = sim.CritterPrng.Uniform(0.0, 1.0) < 0.5 ? -1.0 : 1.0;
            e.Vx = dir * speed;
            e.Vy = 0.0;
            e.Rotation = 0.0;
            e.RotationSpeed = 0.0;
            e.Age = 0.0;
            e.Lifetime = -1.0;
            e.Seed = sim.CritterPrng.NextU32();
            e.State = Constants.SHEEP_STATE_WALKING;
            e.StateTimer = sim.CritterPrng.Uniform(
                Constants.SHEEP_WALK_DURATION_MIN, Constants.SHEEP_WALK_DURATION_MAX);
            e.NameIndex = (byte)sim.CritterPrng.Index((uint)Constants.SHEEP_NAME_POOL.Length);
            sim.Entities.Add(e);
        }
    }

    private static void GenerateCrittersCat(Sim sim, bool allowOverride)
    {
        int count = ResolveCritterCount(sim, Constants.CAT_COUNT_MIN, Constants.CAT_COUNT_MAX, allowOverride);

        double groundY = sim.WindowHeight;
        for (int i = 0; i < count
             && sim.Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR; i++)
        {
            Entity e = default;
            e.Kind = EntityKind.Cat;
            e.Size = Constants.CAT_BODY_RADIUS;
            double margin = e.Size + 8.0;
            e.X = sim.CritterPrng.Uniform(margin, sim.MonitorWidth - margin);
            e.Y = groundY - Constants.CAT_BODY_HEIGHT - Constants.CAT_LEG_LENGTH;
            double speed = sim.CritterPrng.Uniform(
                Constants.CAT_WALK_SPEED_MIN, Constants.CAT_WALK_SPEED_MAX);
            double dir = sim.CritterPrng.Uniform(0.0, 1.0) < 0.5 ? -1.0 : 1.0;
            e.Vx = dir * speed;
            e.Vy = 0.0;
            e.Rotation = 0.0;
            e.RotationSpeed = 0.0;
            e.Age = 0.0;
            e.Lifetime = -1.0;
            e.Seed = sim.CritterPrng.NextU32();
            e.State = Constants.CAT_STATE_WALKING;
            e.StateTimer = sim.CritterPrng.Uniform(
                Constants.CAT_WALK_DURATION_MIN, Constants.CAT_WALK_DURATION_MAX);
            e.NameIndex = (byte)sim.CritterPrng.Index((uint)Constants.CAT_NAME_POOL.Length);
            e.CoatVariantIndex = (byte)sim.CritterPrng.Index((uint)Constants.CAT_COAT_VARIANT_COUNT);
            sim.Entities.Add(e);
        }
    }

    private static void GenerateCrittersBunny(Sim sim, bool allowOverride)
    {
        int count = ResolveCritterCount(sim, Constants.BUNNY_COUNT_MIN, Constants.BUNNY_COUNT_MAX, allowOverride);

        double groundY = sim.WindowHeight;
        for (int i = 0; i < count
             && sim.Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR; i++)
        {
            Entity e = default;
            e.Kind = EntityKind.Bunny;
            e.Size = Constants.BUNNY_BODY_RADIUS;
            double margin = e.Size + 8.0;
            double usableWidth = Math.Max(0.0, sim.MonitorWidth - 2.0 * margin);
            double xFrac = sim.CritterPrng.Uniform(0.0, 1.0);
            e.X = margin + xFrac * usableWidth;
            ulong vxSign = sim.CritterPrng.NextU64() & 1UL;
            double dir = vxSign != 0UL ? 1.0 : -1.0;
            double speed = sim.CritterPrng.Uniform(
                Constants.BUNNY_HOP_SPEED_MIN, Constants.BUNNY_HOP_SPEED_MAX);
            e.Vx = dir * speed;
            e.Vy = 0.0;
            e.Rotation = 0.0;
            e.RotationSpeed = speed;
            e.Age = 0.0;
            e.Lifetime = -1.0;
            e.Seed = (uint)(i + 1);
            e.State = Constants.BUNNY_STATE_HOPPING;
            e.StateTimer = Constants.BUNNY_HOP_DURATION;
            e.NameIndex = (byte)sim.CritterPrng.Index((uint)Constants.BUNNY_NAME_POOL.Length);
            e.Y = groundY - Constants.BUNNY_BODY_HEIGHT - Constants.BUNNY_LEG_LENGTH;
            sim.Entities.Add(e);
        }
    }

    private static void GenerateCrittersHedgehog(Sim sim)
    {
        int count = sim.CritterPrng.Uniform(0.0, 1.0) < Constants.HEDGEHOG_COUNT_PROBABILITY ? 1 : 0;

        double groundY = sim.WindowHeight;
        for (int i = 0; i < count
             && sim.Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR; i++)
        {
            Entity e = default;
            e.Kind = EntityKind.Hedgehog;
            e.Size = Constants.HEDGEHOG_BODY_RADIUS;
            double margin = e.Size + 8.0;
            double usableWidth = Math.Max(0.0, sim.MonitorWidth - 2.0 * margin);
            double xFrac = sim.CritterPrng.Uniform(0.0, 1.0);
            e.X = margin + xFrac * usableWidth;
            ulong vxSign = sim.CritterPrng.NextU64() & 1UL;
            double dir = vxSign != 0UL ? 1.0 : -1.0;
            double speed = sim.CritterPrng.Uniform(
                Constants.HEDGEHOG_WALK_SPEED_MIN, Constants.HEDGEHOG_WALK_SPEED_MAX);
            e.Vx = dir * speed;
            e.Vy = 0.0;
            e.Rotation = 0.0;
            e.RotationSpeed = speed;
            e.Age = 0.0;
            e.Lifetime = -1.0;
            e.Seed = (uint)(i + 1);
            e.State = Constants.HEDGEHOG_STATE_WALKING;
            e.StateTimer = Constants.HEDGEHOG_WALK_DURATION_MIN;
            e.PreviousState = Constants.HEDGEHOG_STATE_WALKING;
            e.PreviousStateTimer = 0.0;
            e.NameIndex = (byte)sim.CritterPrng.Index((uint)Constants.HEDGEHOG_NAME_POOL.Length);
            e.Y = groundY - Constants.HEDGEHOG_BODY_HEIGHT - Constants.HEDGEHOG_LEG_LENGTH;
            sim.Entities.Add(e);
        }
    }

    private static void GenerateButterflies(Sim sim)
    {
        if (sim.MonitorWidth <= 0.0) return;

        var butterflyPrng = Prng.Init(sim.EntitySeed ^ Constants.BUTTERFLY_PRNG_SALT);
        int count = ResolveCountFromPrng(ref butterflyPrng, Constants.BUTTERFLY_COUNT_MIN, Constants.BUTTERFLY_COUNT_MAX);

        for (int i = 0; i < count && sim.Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR; i++)
        {
            Entity e = default;
            e.Kind = EntityKind.Butterfly;
            e.Size = Constants.BUTTERFLY_WING_RADIUS;
            double xFrac = butterflyPrng.Uniform(0.0, 1.0);
            double yFrac = butterflyPrng.Uniform(0.0, 1.0);
            ulong vxSign = butterflyPrng.NextU64() & 1UL;
            double dir = vxSign != 0UL ? 1.0 : -1.0;
            e.BaseSpeed = butterflyPrng.Uniform(Constants.BUTTERFLY_SPEED_MIN, Constants.BUTTERFLY_SPEED_MAX);
            e.RotationSpeed = e.BaseSpeed;
            e.ColorVariant = (byte)butterflyPrng.Index((uint)Constants.BUTTERFLY_COLOR_COUNT);
            e.PhaseY = butterflyPrng.Uniform(0.0, 2.0 * Math.PI);
            e.PhaseX = butterflyPrng.Uniform(0.0, 2.0 * Math.PI);
            e.AltitudeAnchor = Constants.BUTTERFLY_ALTITUDE_MIN
                + yFrac * (Constants.BUTTERFLY_ALTITUDE_MAX - Constants.BUTTERFLY_ALTITUDE_MIN);
            e.X = xFrac * sim.MonitorWidth;
            e.Vx = dir * e.BaseSpeed;
            e.Vy = 0.0;
            e.Age = 0.0;
            e.Lifetime = -1.0;
            e.Seed = (uint)(i + 1);
            UpdateButterflyPosition(ref e, sim);
            sim.Entities.Add(e);
        }
    }

    private static void GenerateFireflies(Sim sim)
    {
        if (sim.MonitorWidth <= 0.0) return;

        var fireflyPrng = Prng.Init(sim.EntitySeed ^ Constants.FIREFLY_PRNG_SALT);
        int count = ResolveCountFromPrng(ref fireflyPrng, Constants.FIREFLY_COUNT_MIN, Constants.FIREFLY_COUNT_MAX);

        for (int i = 0; i < count && sim.Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR; i++)
        {
            Entity e = default;
            e.Kind = EntityKind.Firefly;
            e.Size = Constants.FIREFLY_BODY_RADIUS;
            double xFrac = fireflyPrng.Uniform(0.0, 1.0);
            double yFrac = fireflyPrng.Uniform(0.0, 1.0);
            ulong vxSign = fireflyPrng.NextU64() & 1UL;
            double dir = vxSign != 0UL ? 1.0 : -1.0;
            e.BaseSpeed = fireflyPrng.Uniform(Constants.FIREFLY_DRIFT_SPEED_MIN, Constants.FIREFLY_DRIFT_SPEED_MAX);
            e.RotationSpeed = e.BaseSpeed;
            e.BlinkPeriod = fireflyPrng.Uniform(Constants.FIREFLY_BLINK_PERIOD_MIN, Constants.FIREFLY_BLINK_PERIOD_MAX);
            e.BlinkPhase = fireflyPrng.Uniform(0.0, 1.0);
            e.PhaseY = fireflyPrng.Uniform(0.0, 2.0 * Math.PI);
            e.PhaseX = fireflyPrng.Uniform(0.0, 2.0 * Math.PI);
            e.AltitudeAnchor = Constants.FIREFLY_ALTITUDE_MIN
                + yFrac * (Constants.FIREFLY_ALTITUDE_MAX - Constants.FIREFLY_ALTITUDE_MIN);
            e.X = xFrac * sim.MonitorWidth;
            e.Vx = dir * e.BaseSpeed;
            e.Vy = 0.0;
            e.Age = 0.0;
            e.Lifetime = -1.0;
            e.Seed = (uint)(i + 1);
            UpdateFireflyPosition(ref e, sim);
            sim.Entities.Add(e);
        }
    }

    private static void GenerateGrassCrittersAll(Sim sim)
    {
        GenerateCrittersSheep(sim, allowOverride: false);
        GenerateCrittersCat(sim, allowOverride: false);
        GenerateCrittersBunny(sim, allowOverride: false);
        GenerateCrittersHedgehog(sim);
    }

    public void ResetEntities(ulong seed)
    {
        EntitySeed = seed;
        Entities.Clear();
        LeafPrng = Prng.Init(EntitySeed ^ Constants.LEAF_PRNG_SALT);
        NextLeafSpawnTime = GlobalTime;
        LeafPuffPrng = Prng.Init(EntitySeed ^ Constants.LEAF_PUFF_PRNG_SALT);
        SnowPuffPrng = Prng.Init(EntitySeed ^ Constants.SNOW_PUFF_PRNG_SALT);
        BirdFlybyPrng = Prng.Init(EntitySeed ^ Constants.BIRD_FLYBY_PRNG_SALT);
        NextBirdFlybyAtTime = GlobalTime + BirdFlybySampleInterval(ref BirdFlybyPrng);
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
        b.TreeVariant = 0;
        b.PineHeight = 0.0;
        b.PineWidth = 0.0;
        b.IsMaple = false;
        b.MapleHeight = 0.0;
        b.MapleTrunkWidth = 0.0;
        b.MapleCanopyRadius = 0.0;
        b.MapleCanopyColorIdx = 0;
        b.MapleIsBare = false;
        b.LeafPuffCooldownEnd = 0.0;
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
            // Winter biome suppresses mushrooms — they don't fit a snowy, cold scene.
            b.IsMushroom = false;

            double r = pinePrng.Uniform(0.0, 1.0);
            if (r >= Constants.PINE_PROBABILITY) continue;

            // Draw order: r (consumed above), variant, height, width, tierCount.
            // Locked sequence so both impls are bit-identical regardless of variant.
            double variantDraw = pinePrng.Uniform(0.0, 1.0);
            byte variant = (variantDraw < Constants.BIRCH_VARIANT_PROBABILITY) ? (byte)1 : (byte)0;

            b.IsPine = true;
            b.IsFlower = false;
            b.IsMushroom = false;
            b.TreeVariant = variant;
            b.PineHeight = pinePrng.Uniform(Constants.PINE_HEIGHT_MIN, Constants.PINE_HEIGHT_MAX);

            if (variant == 1)
            {
                b.PineWidth = pinePrng.Uniform(Constants.BIRCH_TRUNK_WIDTH_MIN, Constants.BIRCH_TRUNK_WIDTH_MAX);
            }
            else
            {
                b.PineWidth = pinePrng.Uniform(Constants.PINE_WIDTH_MIN, Constants.PINE_WIDTH_MAX);
            }

            // Tier count drawn for every tree slot to keep the PRNG stream
            // bit-identical across variant outcomes. Only used by pines.
            double tierDraw = pinePrng.Uniform(Constants.PINE_TIER_COUNT_MIN,
                                                Constants.PINE_TIER_COUNT_MAX + 1);
            int tiers = (int)Math.Floor(tierDraw);
            if (tiers < Constants.PINE_TIER_COUNT_MIN) tiers = Constants.PINE_TIER_COUNT_MIN;
            if (tiers > Constants.PINE_TIER_COUNT_MAX) tiers = Constants.PINE_TIER_COUNT_MAX;
            b.PineTierCount = (byte)tiers;
        }
    }

    public static void GenerateMaplesForAutumn(Sim sim)
    {
        var maplePrng = Prng.Init(sim.EntitySeed ^ Constants.MAPLE_PRNG_SALT);

        for (int i = 0; i < sim.Blades.Length; i++)
        {
            ref Blade b = ref sim.Blades[i];
            RestoreOriginalVariants(ref b);

            double r = maplePrng.Uniform(0.0, 1.0);
            if (r >= Constants.MAPLE_PROBABILITY) continue;

            b.IsMaple = true;
            b.IsFlower = false;
            b.IsMushroom = false;
            b.MapleHeight = maplePrng.Uniform(Constants.MAPLE_HEIGHT_MIN, Constants.MAPLE_HEIGHT_MAX);
            b.MapleTrunkWidth = maplePrng.Uniform(Constants.MAPLE_TRUNK_WIDTH_MIN, Constants.MAPLE_TRUNK_WIDTH_MAX);
            b.MapleCanopyRadius = maplePrng.Uniform(Constants.MAPLE_CANOPY_RADIUS_MIN, Constants.MAPLE_CANOPY_RADIUS_MAX);
            b.MapleCanopyColorIdx = (byte)maplePrng.Index((uint)Constants.MAPLE_CANOPY_COLOR_COUNT);
            b.MapleIsBare = maplePrng.Uniform(0.0, 1.0) < Constants.MAPLE_BARE_FRACTION;
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

            if (b.CactusHeight < Constants.CACTUS_ARM_MIN_HEIGHT)
            {
                b.CactusType = 0;
                b.CactusArmSide = +1;
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

    // Deterministic [0,1) hash used to give each tumbleweed its own bounce
    // cadence/height without drawing from the shared PRNG stream (which would
    // shift the spec-pinned spawn snapshots).
    private static double TumbleweedHash01(uint seed, uint salt)
    {
        unchecked
        {
            uint x = seed + salt * 0x9E3779B9u;
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return (x >> 8) * (1.0 / 16777216.0);
        }
    }

    private static double TumbleweedBouncePeriod(uint seed)
        => Constants.TUMBLEWEED_BOUNCE_PERIOD_MIN
         + (Constants.TUMBLEWEED_BOUNCE_PERIOD_MAX - Constants.TUMBLEWEED_BOUNCE_PERIOD_MIN)
           * TumbleweedHash01(seed, 11u);

    private static double TumbleweedHopHeight(uint seed, double size)
    {
        double frac = Constants.TUMBLEWEED_BOUNCE_HEIGHT_MIN_FRAC
            + (Constants.TUMBLEWEED_BOUNCE_HEIGHT_MAX_FRAC - Constants.TUMBLEWEED_BOUNCE_HEIGHT_MIN_FRAC)
              * TumbleweedHash01(seed, 7u);
        return size * frac;
    }

    private static double TumbleweedNextGap(uint seed, double age)
    {
        uint salt = seed ^ (uint)Math.Floor(age);
        return TumbleweedBouncePeriod(seed) * (0.6 + 0.8 * TumbleweedHash01(salt, 17u));
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
        e.AltitudeAnchor = e.Y; // grounded baseline the hop returns to
        e.StateTimer = TumbleweedHash01(e.Seed, 3u) * TumbleweedBouncePeriod(e.Seed);
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
        e.AltitudeAnchor = e.Y;
        e.StateTimer = TumbleweedHash01(e.Seed, 3u) * TumbleweedBouncePeriod(e.Seed);
    }

    private static void RestoreBunnyBaseSpeed(ref Entity e)
    {
        double baseSpeed = e.RotationSpeed;
        if (baseSpeed <= 0.0)
        {
            baseSpeed = Math.Min(Math.Max(Math.Abs(e.Vx), Constants.BUNNY_HOP_SPEED_MIN),
                                 Constants.BUNNY_HOP_SPEED_MAX);
            e.RotationSpeed = baseSpeed;
        }
        double dir = e.Vx >= 0.0 ? 1.0 : -1.0;
        e.Vx = dir * baseSpeed;
    }

    private void StartBunnyHopping(ref Entity e, bool includeGap)
    {
        RestoreBunnyBaseSpeed(ref e);
        e.State = Constants.BUNNY_STATE_HOPPING;
        e.StateTimer = Constants.BUNNY_HOP_DURATION;
        if (includeGap)
        {
            e.StateTimer += CritterPrng.Uniform(Constants.BUNNY_HOP_GAP_MIN,
                                                Constants.BUNNY_HOP_GAP_MAX);
        }
        e.Age = 0.0;
    }

    private void EnterBunnyRestState(ref Entity e)
    {
        byte next = BunnyChooseRestState(ref CritterPrng, DateTime.Now.Hour);
        e.State = next;
        if (next == Constants.BUNNY_STATE_GRAZING)
        {
            e.StateTimer = CritterPrng.Uniform(Constants.BUNNY_GRAZE_DURATION_MIN,
                                               Constants.BUNNY_GRAZE_DURATION_MAX);
        }
        else if (next == Constants.BUNNY_STATE_IDLE)
        {
            e.StateTimer = CritterPrng.Uniform(Constants.BUNNY_IDLE_DURATION_MIN,
                                               Constants.BUNNY_IDLE_DURATION_MAX);
        }
        else
        {
            e.StateTimer = CritterPrng.Uniform(Constants.BUNNY_SLEEP_DURATION_MIN,
                                               Constants.BUNNY_SLEEP_DURATION_MAX);
        }
        e.Age = 0.0;
    }

    private double HedgehogDurationForState(byte state) => state switch
    {
        Constants.HEDGEHOG_STATE_SNUFFLING => CritterPrng.Uniform(Constants.HEDGEHOG_SNUFFLE_DURATION_MIN,
                                                                   Constants.HEDGEHOG_SNUFFLE_DURATION_MAX),
        Constants.HEDGEHOG_STATE_IDLE => CritterPrng.Uniform(Constants.HEDGEHOG_IDLE_DURATION_MIN,
                                                             Constants.HEDGEHOG_IDLE_DURATION_MAX),
        Constants.HEDGEHOG_STATE_SLEEPING => CritterPrng.Uniform(Constants.HEDGEHOG_SLEEP_DURATION_MIN,
                                                                 Constants.HEDGEHOG_SLEEP_DURATION_MAX),
        Constants.HEDGEHOG_STATE_CURLED => CritterPrng.Uniform(Constants.HEDGEHOG_CURL_DURATION_MIN,
                                                               Constants.HEDGEHOG_CURL_DURATION_MAX),
        _ => CritterPrng.Uniform(Constants.HEDGEHOG_WALK_DURATION_MIN,
                                 Constants.HEDGEHOG_WALK_DURATION_MAX),
    };

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

                // Gentle staggered hop: the generic pass above already advanced
                // Y by Vy*dt. While airborne, gravity pulls it back to the
                // baseline; once grounded, count down to the next launch.
                double yBase = e.AltitudeAnchor;
                bool airborne = e.Vy != 0.0 || e.Y < yBase - 1e-9;
                if (airborne)
                {
                    e.Vy += Constants.TUMBLEWEED_BOUNCE_GRAVITY * dt;
                    if (e.Vy >= 0.0 && e.Y >= yBase)
                    {
                        e.Y = yBase;
                        e.Vy = 0.0;
                        e.StateTimer = TumbleweedNextGap(e.Seed, e.Age);
                    }
                }
                else
                {
                    e.StateTimer -= dt;
                    if (e.StateTimer <= 0.0)
                    {
                        double hopH = TumbleweedHopHeight(e.Seed, e.Size);
                        e.Vy = -Math.Sqrt(2.0 * Constants.TUMBLEWEED_BOUNCE_GRAVITY * hopH);
                    }
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

        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind == EntityKind.Leaf)
            {
                // Puff leaves carry an outward burst (Vx) that drifts the anchor
                // out, then decays so they settle into the ordinary flutter.
                // Ambient leaves have Vx == 0 and so are completely unaffected.
                if (e.Vx != 0.0)
                {
                    e.X0 += e.Vx * dt;
                    e.Vx *= Math.Exp(-Constants.LEAF_PUFF_DRAG * dt);
                    if (Math.Abs(e.Vx) < 0.5) e.Vx = 0.0;
                }
                e.X = e.X0 + Constants.LEAF_HORIZONTAL_DRIFT_AMP
                    * Math.Sin(e.Age * Constants.LEAF_HORIZONTAL_DRIFT_FREQ + e.PhaseX);
                Entities[i] = e;
            }
        }

        // Snow-puff powder (§21): gravity pulls the upward burst back toward the
        // ground (y is screen-down) while horizontal velocity decays via drag.
        // The generic pass above already integrated position and age; culling is
        // by lifetime (below) plus the y > groundY rule.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind == EntityKind.SnowPuff)
            {
                e.Vy += Constants.SNOW_PUFF_GRAVITY * dt;
                e.Vx *= Math.Exp(-Constants.SNOW_PUFF_DRAG * dt);
                Entities[i] = e;
            }
        }

        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind == EntityKind.Butterfly)
            {
                double margin = Constants.BUTTERFLY_WING_OFFSET + Constants.BUTTERFLY_WING_RADIUS;
                if (e.X > MonitorWidth + margin)
                {
                    e.X = -margin;
                }
                else if (e.X < -margin)
                {
                    e.X = MonitorWidth + margin;
                }
                UpdateButterflyPosition(ref e, this);
                Entities[i] = e;
            }
            else if (e.Kind == EntityKind.Firefly)
            {
                double margin = Constants.FIREFLY_GLOW_RADIUS;
                if (e.X > MonitorWidth + margin)
                {
                    e.X = -margin;
                }
                else if (e.X < -margin)
                {
                    e.X = MonitorWidth + margin;
                }
                UpdateFireflyPosition(ref e, this);
                Entities[i] = e;
            }
            else if (e.Kind == EntityKind.Bird)
            {
                UpdateBirdPosition(ref e, this);
                Entities[i] = e;
            }
        }

        Entities.RemoveAll(e => (e.Lifetime > 0.0 && e.Age >= e.Lifetime)
                             || (e.Kind == EntityKind.Snowflake && e.Y > groundY)
                             || (e.Kind == EntityKind.Leaf && e.Y > groundY)
                             || (e.Kind == EntityKind.SnowPuff && e.Y > groundY)
                             || (e.Kind == EntityKind.Snowflake
                                 && CurrentScene == Scene.Winter
                                 && SnowDepth > 0.0
                                 && e.Y >= SnowTopYAt(e.X))
                             || (e.Kind == EntityKind.Bird
                                 && ((e.Vx >= 0.0 && e.X > MonitorWidth + 50.0)
                                  || (e.Vx < 0.0 && e.X < -50.0))));

        // §16 Sheep state machine. Walking moves horizontally and bounces off
        // the edges; Grazing/Idle/Sleeping/Greeting freeze position (the generic
        // pass above already added vx*dt, so we undo it). Hopping continues
        // horizontal motion (the parabolic Y offset is renderer-side).
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Sheep) continue;

            bool frozen = e.State == Constants.SHEEP_STATE_GRAZING
                       || e.State == Constants.SHEEP_STATE_IDLE
                       || e.State == Constants.SHEEP_STATE_SLEEPING
                       || e.State == Constants.SHEEP_STATE_GREETING;
            if (frozen)
            {
                e.X -= e.Vx * dt;
            }

            // Edge bounce — runs in every state. Even a stationary sheep
            // that spawned near the edge gets reflected on its first walk.
            double margin = e.Size + 2.0;
            if (e.X < margin)
            {
                e.X  = margin;
                e.Vx = Math.Abs(e.Vx);
            }
            else if (e.X > MonitorWidth - margin)
            {
                e.X  = MonitorWidth - margin;
                e.Vx = -Math.Abs(e.Vx);
            }

            e.StateTimer -= dt;
            if (e.StateTimer <= 0.0)
            {
                byte oldState = e.State;
                if (oldState == Constants.SHEEP_STATE_WALKING)
                {
                    double r = CritterPrng.Uniform(0.0, 1.0);
                    if (r < Constants.SHEEP_GRAZE_PROBABILITY)
                    {
                        e.State = Constants.SHEEP_STATE_GRAZING;
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.SHEEP_GRAZE_DURATION_MIN,
                            Constants.SHEEP_GRAZE_DURATION_MAX);
                    }
                    else if (r < Constants.SHEEP_GRAZE_PROBABILITY
                               + Constants.SHEEP_IDLE_PROBABILITY)
                    {
                        e.State = Constants.SHEEP_STATE_IDLE;
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.SHEEP_IDLE_DURATION_MIN,
                            Constants.SHEEP_IDLE_DURATION_MAX);
                    }
                    else
                    {
                        e.State = Constants.SHEEP_STATE_HOPPING;
                        e.StateTimer = Constants.SHEEP_HOP_DURATION;
                    }
                }
                else if (oldState == Constants.SHEEP_STATE_IDLE)
                {
                    double r = CritterPrng.Uniform(0.0, 1.0);
                    double sleepProb = SheepSleepProbForLocalHour(DateTime.Now.Hour);
                    if (r < sleepProb)
                    {
                        e.State = Constants.SHEEP_STATE_SLEEPING;
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.SHEEP_SLEEP_DURATION_MIN,
                            Constants.SHEEP_SLEEP_DURATION_MAX);
                    }
                    else
                    {
                        e.State = Constants.SHEEP_STATE_WALKING;
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.SHEEP_WALK_DURATION_MIN,
                            Constants.SHEEP_WALK_DURATION_MAX);
                    }
                }
                else
                {
                    // Grazing / Sleeping / Hopping / Greeting → Walking.
                    e.State = Constants.SHEEP_STATE_WALKING;
                    e.StateTimer = CritterPrng.Uniform(
                        Constants.SHEEP_WALK_DURATION_MIN,
                        Constants.SHEEP_WALK_DURATION_MAX);
                    if (oldState == Constants.SHEEP_STATE_GREETING)
                    {
                        e.Vx = -e.Vx;
                    }
                }
                // Reset animation phase on every transition so hop arcs,
                // sleep Z's, and walk cycles all start at phase 0.
                e.Age = 0.0;
            }

            Entities[i] = e;
        }

        // Pair-wise sheep greeting trigger. Runs after all per-sheep transitions
        // and before the snowflake spawner so critter PRNG draw order stays locked.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity a = Entities[i];
            if (a.Kind != EntityKind.Sheep
                || (a.State != Constants.SHEEP_STATE_WALKING
                    && a.State != Constants.SHEEP_STATE_GRAZING
                    && a.State != Constants.SHEEP_STATE_IDLE)
                || a.Age < Constants.SHEEP_GREET_MIN_AGE)
            {
                continue;
            }

            for (int j = i + 1; j < Entities.Count; j++)
            {
                Entity b = Entities[j];
                if (b.Kind != EntityKind.Sheep
                    || (b.State != Constants.SHEEP_STATE_WALKING
                        && b.State != Constants.SHEEP_STATE_GRAZING
                        && b.State != Constants.SHEEP_STATE_IDLE)
                    || b.Age < Constants.SHEEP_GREET_MIN_AGE)
                {
                    continue;
                }

                double dx = b.X - a.X;
                if (Math.Abs(dx) >= Constants.SHEEP_GREET_RADIUS) continue;

                double duration = CritterPrng.Uniform(
                    Constants.SHEEP_GREET_DURATION_MIN,
                    Constants.SHEEP_GREET_DURATION_MAX);
                double dir = dx >= 0.0 ? 1.0 : -1.0;
                a.Vx =  dir * Math.Abs(a.Vx);
                b.Vx = -dir * Math.Abs(b.Vx);
                a.State = Constants.SHEEP_STATE_GREETING;
                b.State = Constants.SHEEP_STATE_GREETING;
                a.StateTimer = duration;
                b.StateTimer = duration;
                a.Age = 0.0;
                b.Age = 0.0;
                Entities[i] = a;
                Entities[j] = b;
                break;
            }
        }

        // §17 Cat state machine. Cats never join the sheep greeting loop.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Cat) continue;

            bool frozen = e.State == Constants.CAT_STATE_IDLE
                       || e.State == Constants.CAT_STATE_SLEEPING;
            if (frozen)
            {
                e.X -= e.Vx * dt;
            }

            double margin = e.Size + 2.0;
            if (e.X < margin)
            {
                e.X  = margin;
                e.Vx = Math.Abs(e.Vx);
            }
            else if (e.X > MonitorWidth - margin)
            {
                e.X  = MonitorWidth - margin;
                e.Vx = -Math.Abs(e.Vx);
            }

            e.StateTimer -= dt;
            if (e.StateTimer <= 0.0)
            {
                if (e.State == Constants.CAT_STATE_WALKING)
                {
                    double r = CritterPrng.Uniform(0.0, 1.0);
                    if (r < Constants.CAT_IDLE_PROBABILITY)
                    {
                        e.State = Constants.CAT_STATE_IDLE;
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.CAT_IDLE_DURATION_MIN,
                            Constants.CAT_IDLE_DURATION_MAX);
                    }
                    else if (r < Constants.CAT_IDLE_PROBABILITY + Constants.CAT_SLEEP_PROBABILITY)
                    {
                        e.State = Constants.CAT_STATE_SLEEPING;
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.CAT_SLEEP_DURATION_MIN,
                            Constants.CAT_SLEEP_DURATION_MAX);
                    }
                    else
                    {
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.CAT_WALK_DURATION_MIN,
                            Constants.CAT_WALK_DURATION_MAX);
                    }
                }
                else if (e.State == Constants.CAT_STATE_IDLE)
                {
                    double sleepProb = CatSleepProbForLocalHour(DateTime.Now.Hour);
                    double r = CritterPrng.Uniform(0.0, 1.0);
                    if (r < sleepProb)
                    {
                        e.State = Constants.CAT_STATE_SLEEPING;
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.CAT_SLEEP_DURATION_MIN,
                            Constants.CAT_SLEEP_DURATION_MAX);
                    }
                    else
                    {
                        e.State = Constants.CAT_STATE_WALKING;
                        e.StateTimer = CritterPrng.Uniform(
                            Constants.CAT_WALK_DURATION_MIN,
                            Constants.CAT_WALK_DURATION_MAX);
                    }
                }
                else
                {
                    e.State = Constants.CAT_STATE_WALKING;
                    e.StateTimer = CritterPrng.Uniform(
                        Constants.CAT_WALK_DURATION_MIN,
                        Constants.CAT_WALK_DURATION_MAX);
                }
                e.Age = 0.0;
            }

            Entities[i] = e;
        }

        // §18 Bunny state machine. Bunnies never walk smoothly: movement is a hop arc,
        // optional landing gap, then a calm stationary pose unless startled.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Bunny) continue;

            bool stationary = e.State == Constants.BUNNY_STATE_GRAZING
                           || e.State == Constants.BUNNY_STATE_IDLE
                           || e.State == Constants.BUNNY_STATE_SLEEPING
                           || (e.State == Constants.BUNNY_STATE_HOPPING && e.Age > Constants.BUNNY_HOP_DURATION);
            if (stationary)
            {
                e.X -= e.Vx * dt;
            }

            double margin = e.Size + 2.0;
            if (e.X < margin)
            {
                e.X = margin;
                e.Vx = Math.Abs(e.Vx);
            }
            else if (e.X > MonitorWidth - margin)
            {
                e.X = MonitorWidth - margin;
                e.Vx = -Math.Abs(e.Vx);
            }

            e.StateTimer -= dt;
            if (e.StateTimer <= 0.0)
            {
                byte oldState = e.State;
                if (oldState == Constants.BUNNY_STATE_HOPPING)
                {
                    EnterBunnyRestState(ref e);
                }
                else if (oldState == Constants.BUNNY_STATE_STARTLED)
                {
                    StartBunnyHopping(ref e, includeGap: false);
                }
                else
                {
                    StartBunnyHopping(ref e, includeGap: true);
                }
            }

            Entities[i] = e;
        }

        // §17.9 Hedgehog state machine. Walking waddles slowly; every other state is stationary.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Hedgehog) continue;

            if (e.State != Constants.HEDGEHOG_STATE_WALKING)
            {
                e.X -= e.Vx * dt;
            }

            double margin = e.Size + 2.0;
            if (e.X < margin)
            {
                e.X = margin;
                e.Vx = Math.Abs(e.Vx);
            }
            else if (e.X > MonitorWidth - margin)
            {
                e.X = MonitorWidth - margin;
                e.Vx = -Math.Abs(e.Vx);
            }

            e.StateTimer -= dt;
            if (e.StateTimer <= 0.0)
            {
                byte oldState = e.State;
                if (oldState == Constants.HEDGEHOG_STATE_WALKING)
                {
                    byte next = HedgehogChooseRestState(ref CritterPrng, DateTime.Now.Hour);
                    e.State = next;
                    e.StateTimer = HedgehogDurationForState(next);
                }
                else if (oldState == Constants.HEDGEHOG_STATE_CURLED)
                {
                    byte resume = e.PreviousState;
                    if (resume == Constants.HEDGEHOG_STATE_SLEEPING
                        || resume > Constants.HEDGEHOG_STATE_CURLED
                        || resume == Constants.HEDGEHOG_STATE_CURLED)
                    {
                        resume = Constants.HEDGEHOG_STATE_WALKING;
                    }
                    e.State = resume;
                    e.StateTimer = e.PreviousStateTimer > 0.0
                        ? e.PreviousStateTimer
                        : HedgehogDurationForState(resume);
                    e.PreviousState = Constants.HEDGEHOG_STATE_WALKING;
                    e.PreviousStateTimer = 0.0;
                }
                else
                {
                    e.State = Constants.HEDGEHOG_STATE_WALKING;
                    e.StateTimer = HedgehogDurationForState(Constants.HEDGEHOG_STATE_WALKING);
                }
                e.Age = 0.0;
            }

            Entities[i] = e;
        }

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

        TickBirdFlybys(DateTime.Now.Hour);

        if (CurrentScene == Scene.Autumn && MonitorWidth > 0.0)
        {
            double lambda = Constants.LEAF_SPAWN_RATE_PER_SEC_1920DIP * MonitorWidth / 1920.0;
            while (lambda > 0.0 && GlobalTime >= NextLeafSpawnTime && Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR)
            {
                Entity e = default;
                e.Kind = EntityKind.Leaf;
                double xFrac = LeafPrng.Uniform(0.0, 1.0);
                double spawnX = xFrac * MonitorWidth;
                double fallSpeed = LeafPrng.Uniform(Constants.LEAF_FALL_SPEED_MIN, Constants.LEAF_FALL_SPEED_MAX);
                e.PhaseX = LeafPrng.Uniform(0.0, twoPi);
                double rotationSpeedMag = LeafPrng.Uniform(Constants.LEAF_ROTATION_SPEED_MIN, Constants.LEAF_ROTATION_SPEED_MAX);
                double rotationSign = (LeafPrng.NextU64() & 1UL) != 0UL ? 1.0 : -1.0;
                e.RotationSpeed = rotationSpeedMag * rotationSign;
                e.Rotation = LeafPrng.Uniform(0.0, twoPi);
                e.Size = LeafPrng.Uniform(Constants.LEAF_SIZE_MIN, Constants.LEAF_SIZE_MAX);
                e.ColorVariant = (byte)LeafPrng.Index((uint)Constants.LEAF_COLOR_COUNT);
                e.X0 = spawnX;
                e.X = spawnX + Constants.LEAF_HORIZONTAL_DRIFT_AMP * Math.Sin(e.PhaseX);
                e.Y = Constants.LEAF_SPAWN_Y_OFFSET;
                e.Vx = 0.0;
                e.Vy = fallSpeed;
                e.BaseSpeed = fallSpeed;
                e.Age = 0.0;
                e.Lifetime = -1.0;
                Entities.Add(e);
                NextLeafSpawnTime += LeafPrng.Exponential(lambda);
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
        if (SnowPhaseSeed == 0UL)
        {
            SnowPhaseSeed = SnowPhaseSeedForMonitor((int)Math.Round(monitorWidth), (int)Math.Round(WindowHeight), 0, 0);
        }
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
        // Cut-floor (stubble) stream — independent salted stream so the
        // per-blade mowed residual height does NOT perturb existing sequences.
        var rngCutFloor = Prng.Init(seed ^ Constants.CUT_FLOOR_PRNG_SALT);
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
            b.CutFloor = rngCutFloor.Uniform(Constants.CUT_FLOOR_MIN, Constants.CUT_FLOOR_MAX);

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

    // §16.6 leaf shaken loose by a cursor hover. Same visual leaf, but spawned
    // at the canopy with an outward burst (Vx) that decays in the tick before it
    // settles into the usual flutter. Draw order locked to the native mirror.
    private Entity MakePuffLeaf(double cx, double cy, double canopyR)
    {
        double twoPi = 2.0 * Math.PI;
        Entity e = default;
        e.Kind = EntityKind.Leaf;
        e.PhaseX = LeafPuffPrng.Uniform(0.0, twoPi);
        double rotationSpeedMag = LeafPuffPrng.Uniform(Constants.LEAF_ROTATION_SPEED_MIN, Constants.LEAF_ROTATION_SPEED_MAX);
        double rotationSign = (LeafPuffPrng.NextU64() & 1UL) != 0UL ? 1.0 : -1.0;
        e.RotationSpeed = rotationSpeedMag * rotationSign;
        e.Rotation = LeafPuffPrng.Uniform(0.0, twoPi);
        e.Size = LeafPuffPrng.Uniform(Constants.LEAF_SIZE_MIN, Constants.LEAF_SIZE_MAX);
        e.ColorVariant = (byte)LeafPuffPrng.Index((uint)Constants.LEAF_COLOR_COUNT);
        double ang = LeafPuffPrng.Uniform(0.0, twoPi);
        double speed = LeafPuffPrng.Uniform(Constants.LEAF_PUFF_BURST_SPEED_MIN, Constants.LEAF_PUFF_BURST_SPEED_MAX);
        double offFrac = LeafPuffPrng.Uniform(0.0, Constants.LEAF_PUFF_START_OFFSET_FRAC);
        double fallSpeed = LeafPuffPrng.Uniform(Constants.LEAF_FALL_SPEED_MIN, Constants.LEAF_FALL_SPEED_MAX);
        e.X0 = cx + Math.Cos(ang) * canopyR * offFrac;
        e.Y = cy + Math.Sin(ang) * canopyR * offFrac;
        e.Vx = Math.Cos(ang) * speed;
        e.Vy = fallSpeed;
        e.BaseSpeed = fallSpeed;
        e.X = e.X0 + Constants.LEAF_HORIZONTAL_DRIFT_AMP * Math.Sin(e.PhaseX);
        e.Age = 0.0;
        e.Lifetime = -1.0;
        return e;
    }

    // §21 powder kicked up by a click on the Winter snowbank. Spawns at the
    // click with an upward, outward burst that gravity (positive Vy, y is
    // screen-down) pulls back to ground; Vx decays via drag in the tick and the
    // particle fades over its lifetime. Draw order locked to the native mirror:
    // size, theta, speed, offA, offR, lifetime.
    private Entity MakeSnowPuff(double cx, double cy, double groundY)
    {
        double twoPi = 2.0 * Math.PI;
        Entity e = default;
        e.Kind = EntityKind.SnowPuff;
        e.Size = SnowPuffPrng.Uniform(Constants.SNOW_PUFF_SIZE_MIN, Constants.SNOW_PUFF_SIZE_MAX);
        double theta = SnowPuffPrng.Uniform(-Constants.SNOW_PUFF_SPREAD_RAD, Constants.SNOW_PUFF_SPREAD_RAD);
        double speed = SnowPuffPrng.Uniform(Constants.SNOW_PUFF_BURST_SPEED_MIN, Constants.SNOW_PUFF_BURST_SPEED_MAX);
        double offA = SnowPuffPrng.Uniform(0.0, twoPi);
        double offR = SnowPuffPrng.Uniform(0.0, Constants.SNOW_PUFF_START_RADIUS);
        e.Lifetime = SnowPuffPrng.Uniform(Constants.SNOW_PUFF_LIFETIME_MIN, Constants.SNOW_PUFF_LIFETIME_MAX);
        e.X = cx + Math.Cos(offA) * offR;
        // Bias the start to at/above ground so a puff never spawns under the bank.
        e.Y = Math.Min(cy - Math.Abs(Math.Sin(offA)) * offR, groundY);
        e.Vx = Math.Sin(theta) * speed;
        e.Vy = -Math.Cos(theta) * speed;
        e.Age = 0.0;
        return e;
    }

    // §8 cursor-move impulse.
    public void ApplyCursorMove(in InputEvent e)
    {
        // §16.6 leaf puff: hovering a leafy maple canopy shakes leaves loose.
        // Independent of the gust band so it fires even directly over the crown.
        if (CurrentScene == Scene.Autumn && double.IsFinite(e.X) && double.IsFinite(e.Y))
        {
            double groundY2 = GroundY;
            for (int i = 0; i < Blades.Length; i++)
            {
                ref Blade b = ref Blades[i];
                if (!b.IsMaple || b.MapleIsBare) continue;
                if (b.CutHeight < Constants.LEAF_PUFF_MIN_CUT_HEIGHT) continue;
                if (GlobalTime < b.LeafPuffCooldownEnd) continue;
                double cx = b.BaseX;
                double cy = groundY2 - b.MapleHeight * b.CutHeight;
                double canopyR = b.MapleCanopyRadius * b.CutHeight;
                double hoverR = canopyR * Constants.LEAF_PUFF_HOVER_RADIUS_MUL;
                double dxh = e.X - cx;
                double dyh = e.Y - cy;
                if (dxh * dxh + dyh * dyh > hoverR * hoverR) continue;
                int count = Constants.LEAF_PUFF_COUNT_MIN
                    + (int)LeafPuffPrng.Index((uint)(Constants.LEAF_PUFF_COUNT_MAX - Constants.LEAF_PUFF_COUNT_MIN + 1));
                bool emittedAny = false;
                for (int k = 0; k < count; k++)
                {
                    if (Entities.Count >= Constants.MAX_ENTITIES_PER_MONITOR) break;
                    Entities.Add(MakePuffLeaf(cx, cy, canopyR));
                    emittedAny = true;
                }
                // Only arm the cooldown when leaves actually shed; if the entity
                // cap was full the hover was a visual no-op and may retry.
                if (emittedAny) b.LeafPuffCooldownEnd = GlobalTime + Constants.LEAF_PUFF_COOLDOWN_SEC;
            }
        }

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
        // Reject non-finite coordinates before anything else: NaN compares false,
        // so an unguarded NaN click would slip past the radius checks and cut
        // every blade (and emit a degenerate puff).
        if (!double.IsFinite(clickX) || !double.IsFinite(clickY)) return;

        double cutBandTop = GroundY - Constants.STRIP_HEIGHT;
        double cutBandBottom = GroundY;
        if (clickY < cutBandTop || clickY > cutBandBottom) return;

        // §21 snow puff: a click on the Winter snowbank kicks up a burst of
        // powder. We always draw the full locked PRNG sequence per intended puff
        // and only append when capacity allows, so the stream stays independent
        // of the transient entity count.
        if (CurrentScene == Scene.Winter)
        {
            int count = Constants.SNOW_PUFF_COUNT_MIN
                + (int)SnowPuffPrng.Index((uint)(Constants.SNOW_PUFF_COUNT_MAX - Constants.SNOW_PUFF_COUNT_MIN + 1));
            for (int i = 0; i < count; i++)
            {
                Entity puff = MakeSnowPuff(clickX, clickY, GroundY);
                if (Entities.Count < Constants.MAX_ENTITIES_PER_MONITOR)
                {
                    Entities.Add(puff);
                }
            }
        }

        for (int i = 0; i < Blades.Length; i++)
        {
            ref Blade b = ref Blades[i];
            if (Math.Abs(b.BaseX - clickX) >= Constants.CUT_RADIUS) continue;
            // Already at (or below) its stubble floor — can't be cut shorter.
            if (b.CutHeight <= b.CutFloor) continue;
            if (b.CutAnimStart >= 0.0) continue;

            b.CutAnimStart = GlobalTime;
            b.CutInitialHeight = b.CutHeight;
            // Cancel any pending or in-progress regrowth: we're going back down.
            b.RegrowStart = -1.0;
        }

        // §16 Sheep click-startle: clicks within SHEEP_STARTLE_RADIUS (in x)
        // push any sheep into a Hopping state and flip vx away from the
        // cursor at boosted speed (capped to prevent repeated-click compounding).
        // 1D distance only — matches Native; the y-band gate above already
        // restricts clicks to the strip, and the sheep stands above it.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Sheep) continue;
            double dxClick = e.X - clickX;
            if (Math.Abs(dxClick) >= Constants.SHEEP_STARTLE_RADIUS) continue;

            double awayDir = dxClick >= 0.0 ? 1.0 : -1.0;
            double speed = Math.Min(Math.Abs(e.Vx) * Constants.SHEEP_STARTLE_BOOST,
                                    Constants.SHEEP_WALK_SPEED_MAX * Constants.SHEEP_STARTLE_BOOST);
            e.Vx           = speed * awayDir;
            e.State        = Constants.SHEEP_STATE_HOPPING;
            e.StateTimer   = Constants.SHEEP_HOP_DURATION;
            e.Age          = 0.0;
            Entities[i] = e;
        }

        // §17 Cat pounce: click-only and TOWARD the click. The sheep Hopping
        // byte is reused semantically as Pouncing.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Cat) continue;
            double dxClick = clickX - e.X;
            if (Math.Abs(dxClick) >= Constants.CAT_POUNCE_RADIUS) continue;

            double towardDir = dxClick >= 0.0 ? 1.0 : -1.0;
            e.Vx = towardDir * Constants.CAT_POUNCE_SPEED;
            e.State = Constants.CAT_STATE_POUNCING;
            e.StateTimer = Constants.CAT_POUNCE_DURATION;
            e.Age = 0.0;
            Entities[i] = e;
        }

        // §18 Bunny startle: nearby clicks wake/break the current pose and
        // send the bunny hopping AWAY from the click point.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Bunny) continue;
            double dxClick = e.X - clickX;
            double dyClick = e.Y - clickY;
            if (dxClick * dxClick + dyClick * dyClick > Constants.BUNNY_STARTLE_RADIUS * Constants.BUNNY_STARTLE_RADIUS) continue;

            double awayDir = dxClick >= 0.0 ? 1.0 : -1.0;
            double baseSpeed = e.RotationSpeed;
            if (baseSpeed <= 0.0)
            {
                baseSpeed = Math.Min(Math.Max(Math.Abs(e.Vx), Constants.BUNNY_HOP_SPEED_MIN),
                                     Constants.BUNNY_HOP_SPEED_MAX);
            }
            e.RotationSpeed = baseSpeed;
            e.Vx = awayDir * baseSpeed * Constants.BUNNY_STARTLE_BOOST;
            e.State = Constants.BUNNY_STATE_STARTLED;
            e.StateTimer = Constants.BUNNY_STARTLE_DURATION;
            e.Age = 0.0;
            Entities[i] = e;
        }

        // §17.9 Hedgehog startle: passive defense curls without flipping vx.
        for (int i = 0; i < Entities.Count; i++)
        {
            Entity e = Entities[i];
            if (e.Kind != EntityKind.Hedgehog || e.State == Constants.HEDGEHOG_STATE_CURLED) continue;
            double dxClick = e.X - clickX;
            double dyClick = e.Y - clickY;
            if (dxClick * dxClick + dyClick * dyClick > Constants.HEDGEHOG_STARTLE_RADIUS * Constants.HEDGEHOG_STARTLE_RADIUS) continue;

            e.PreviousState = e.State == Constants.HEDGEHOG_STATE_SLEEPING
                ? Constants.HEDGEHOG_STATE_WALKING
                : e.State;
            e.PreviousStateTimer = e.State == Constants.HEDGEHOG_STATE_SLEEPING ? 0.0 : e.StateTimer;
            e.State = Constants.HEDGEHOG_STATE_CURLED;
            e.StateTimer = CritterPrng.Uniform(Constants.HEDGEHOG_CURL_DURATION_MIN,
                                               Constants.HEDGEHOG_CURL_DURATION_MAX);
            e.Age = 0.0;
            Entities[i] = e;
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
                b.CutHeight = b.CutFloor;
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
                // Lerp from the height at cut time down to the per-blade
                // stubble floor. With CutFloor == 0 this reduces to the
                // original CutInitialHeight * (1 - t).
                b.CutHeight = b.CutFloor + (b.CutInitialHeight - b.CutFloor) * (1.0 - t);
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
            // Linear regrowth from the stubble floor back to full height,
            // same easing as the cut animation in reverse. With CutFloor == 0
            // this reduces to the original CutHeight = regrowT.
            b.CutHeight = b.CutFloor + (1.0 - b.CutFloor) * regrowT;
        }
    }

    public List<CutRecord> GetCuts()
    {
        var cuts = new List<CutRecord>();
        for (int i = 0; i < Blades.Length; i++)
        {
            ref readonly Blade b = ref Blades[i];
            bool hasCutState = b.CutAnimStart >= 0.0 || b.RegrowStart >= 0.0 || b.CutHeight < 1.0;
            if (!hasCutState) continue;

            double originalCutTime = GlobalTime;
            if (b.CutAnimStart >= 0.0)
            {
                originalCutTime = b.CutAnimStart;
            }
            else if (b.RegrowStart >= 0.0)
            {
                originalCutTime = b.RegrowStart - b.RegrowDelay - Constants.CUT_DURATION_SEC;
            }

            double totalRegrowTime = Constants.CUT_DURATION_SEC
                                   + Math.Max(0.0, b.RegrowDelay)
                                   + Math.Max(0.0, b.RegrowDuration);
            if (GlobalTime - originalCutTime >= totalRegrowTime && b.RegrowDuration > 0.0)
            {
                continue;
            }

            cuts.Add(new CutRecord(i, originalCutTime - GlobalTime));
        }

        return cuts;
    }

    public void ApplyCuts(IReadOnlyList<CutRecord> cuts)
    {
        foreach (CutRecord cut in cuts)
        {
            if (cut.BladeIndex < 0 || cut.BladeIndex >= Blades.Length) continue;

            ref Blade b = ref Blades[cut.BladeIndex];
            double cutTime = cut.CutTime <= 0.0 ? GlobalTime + cut.CutTime : cut.CutTime;
            double age = Math.Max(0.0, GlobalTime - cutTime);

            b.CutInitialHeight = 1.0;
            if (age < Constants.CUT_DURATION_SEC)
            {
                double t = age / Constants.CUT_DURATION_SEC;
                b.CutAnimStart = cutTime;
                b.CutHeight = b.CutFloor + (1.0 - b.CutFloor) * (1.0 - t);
                b.RegrowStart = -1.0;
                continue;
            }

            b.CutAnimStart = -1.0;
            b.CutHeight = b.CutFloor;
            if (b.RegrowDelay <= 0.0 || b.RegrowDuration <= 0.0)
            {
                b.RegrowStart = -1.0;
                continue;
            }

            double regrowStart = cutTime + Constants.CUT_DURATION_SEC + b.RegrowDelay;
            double regrowElapsed = GlobalTime - regrowStart;
            if (regrowElapsed < 0.0)
            {
                b.RegrowStart = regrowStart;
                continue;
            }

            if (regrowElapsed >= b.RegrowDuration)
            {
                b.CutHeight = 1.0;
                b.RegrowStart = -1.0;
                continue;
            }

            b.CutHeight = b.CutFloor + (1.0 - b.CutFloor) * (regrowElapsed / b.RegrowDuration);
            b.RegrowStart = regrowStart;
        }
    }

    // §10 single per-frame entry point.
    public void Tick(double dt, ReadOnlySpan<InputEvent> events)
    {
        GlobalTime += dt;

        if (CurrentScene == Scene.Winter)
        {
            SetSnowDepth(SnowDepth + Constants.SNOW_ACCUMULATION_RATE * Math.Max(0.0, dt));
        }
        else
        {
            SnowDepth = 0.0;
        }

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
        else if (scene == Scene.Winter && !b.IsPine && !b.IsMushroom)
        {
            L *= Constants.WINTER_GRASS_HEIGHT_SCALE;
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
