// BunnyTests.cs - §18 Bunny critter tests.
// Mirrors tests/DesktopGrass.Native.Tests/src/bunny_tests.cpp.

using System;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class BunnyTests
{
    private const double Monitor1920 = 1920.0;

    private static Sim BuildSim(ulong seed = Constants.CANONICAL_TEST_SEED)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, Monitor1920, Constants.DEFAULT_DENSITY),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(seed, Monitor1920);
        sim.ResetEntities(seed);
        return sim;
    }

    private static Sim BuildGrassSim(ulong seed = Constants.CANONICAL_TEST_SEED)
    {
        var sim = BuildSim(seed);
        sim.SetScene(Scene.Grass);
        return sim;
    }

    private static int CountKind(Sim sim, EntityKind kind) => sim.Entities.Count(e => e.Kind == kind);

    private static Entity BunnyEntity(double x = 500.0, double vx = Constants.BUNNY_HOP_SPEED_MIN)
    {
        return new Entity
        {
            Kind = EntityKind.Bunny,
            Size = Constants.BUNNY_BODY_RADIUS,
            X = x,
            Y = Constants.STRIP_HEIGHT + Constants.HEADROOM - Constants.BUNNY_BODY_HEIGHT - Constants.BUNNY_LEG_LENGTH,
            Vx = vx,
            RotationSpeed = Math.Abs(vx),
            Lifetime = -1.0,
            State = Constants.BUNNY_STATE_HOPPING,
            StateTimer = Constants.BUNNY_HOP_DURATION,
        };
    }

    private static int PrngCount(ref Prng side, int minCount, int maxCount)
    {
        double draw = side.Uniform(minCount, maxCount + 1);
        int count = (int)Math.Floor(draw);
        if (count < minCount) count = minCount;
        if (count > maxCount) count = maxCount;
        return count;
    }

    private static void AdvanceSheep(ref Prng side, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double margin = Constants.SHEEP_BODY_RADIUS + 8.0;
            _ = side.Uniform(margin, Monitor1920 - margin);
            _ = side.Uniform(Constants.SHEEP_WALK_SPEED_MIN, Constants.SHEEP_WALK_SPEED_MAX);
            _ = side.Uniform(0.0, 1.0);
            _ = side.NextU32();
            _ = side.Uniform(Constants.SHEEP_WALK_DURATION_MIN, Constants.SHEEP_WALK_DURATION_MAX);
            _ = side.Index((uint)Constants.SHEEP_NAME_POOL.Length);
        }
    }

    private static void AdvanceCats(ref Prng side, int count)
    {
        for (int i = 0; i < count; i++)
        {
            double margin = Constants.CAT_BODY_RADIUS + 8.0;
            _ = side.Uniform(margin, Monitor1920 - margin);
            _ = side.Uniform(Constants.CAT_WALK_SPEED_MIN, Constants.CAT_WALK_SPEED_MAX);
            _ = side.Uniform(0.0, 1.0);
            _ = side.NextU32();
            _ = side.Uniform(Constants.CAT_WALK_DURATION_MIN, Constants.CAT_WALK_DURATION_MAX);
            _ = side.Index((uint)Constants.CAT_NAME_POOL.Length);
            _ = side.Index((uint)Constants.CAT_COAT_VARIANT_COUNT);
        }
    }

    [Fact]
    public void BunnyConstantsArePinnedToSpecValues()
    {
        Assert.Equal(1, Constants.BUNNY_COUNT_MIN);
        Assert.Equal(2, Constants.BUNNY_COUNT_MAX);
        Assert.Equal(22.0, Constants.BUNNY_HOP_SPEED_MIN);
        Assert.Equal(38.0, Constants.BUNNY_HOP_SPEED_MAX);
        Assert.Equal(8.0, Constants.BUNNY_BODY_RADIUS);
        Assert.Equal(6.5, Constants.BUNNY_BODY_HEIGHT);
        Assert.Equal(4.2, Constants.BUNNY_HEAD_RADIUS);
        Assert.Equal(9.0, Constants.BUNNY_EAR_HEIGHT);
        Assert.Equal(2.2, Constants.BUNNY_EAR_WIDTH);
        Assert.Equal(3.0, Constants.BUNNY_EAR_SPACING);
        Assert.Equal(4.0, Constants.BUNNY_LEG_LENGTH);
        Assert.Equal(2.4, Constants.BUNNY_TAIL_RADIUS);
        Assert.Equal(0xFF8A6A4Au, Constants.BUNNY_BODY_COLOR);
        Assert.Equal(0xFFC4A98Du, Constants.BUNNY_BELLY_COLOR);
        Assert.Equal(0xFF8A6A4Au, Constants.BUNNY_EAR_COLOR);
        Assert.Equal(0xFFD9A0A0u, Constants.BUNNY_EAR_INNER_COLOR);
        Assert.Equal(0xFFF7F4EBu, Constants.BUNNY_TAIL_COLOR);
        Assert.Equal(0xFF1A1208u, Constants.BUNNY_EYE_COLOR);
        Assert.Equal(0xFF8A4040u, Constants.BUNNY_NOSE_COLOR);
        Assert.Equal((byte)0, Constants.BUNNY_STATE_HOPPING);
        Assert.Equal((byte)1, Constants.BUNNY_STATE_GRAZING);
        Assert.Equal((byte)2, Constants.BUNNY_STATE_IDLE);
        Assert.Equal((byte)3, Constants.BUNNY_STATE_SLEEPING);
        Assert.Equal((byte)4, Constants.BUNNY_STATE_STARTLED);
        Assert.Equal(0.40, Constants.BUNNY_HOP_DURATION);
        Assert.Equal(8.0, Constants.BUNNY_HOP_HEIGHT);
        Assert.Equal(0.05, Constants.BUNNY_HOP_GAP_MIN);
        Assert.Equal(0.20, Constants.BUNNY_HOP_GAP_MAX);
        Assert.Equal(2.5, Constants.BUNNY_GRAZE_DURATION_MIN);
        Assert.Equal(4.5, Constants.BUNNY_GRAZE_DURATION_MAX);
        Assert.Equal(2.0, Constants.BUNNY_IDLE_DURATION_MIN);
        Assert.Equal(4.0, Constants.BUNNY_IDLE_DURATION_MAX);
        Assert.Equal(6.0, Constants.BUNNY_SLEEP_DURATION_MIN);
        Assert.Equal(12.0, Constants.BUNNY_SLEEP_DURATION_MAX);
        Assert.Equal(0.55, Constants.BUNNY_GRAZE_PROBABILITY);
        Assert.Equal(0.30, Constants.BUNNY_IDLE_PROBABILITY);
        Assert.Equal(0.05, Constants.BUNNY_SLEEP_PROB_DAY);
        Assert.Equal(0.40, Constants.BUNNY_SLEEP_PROB_NIGHT);
        Assert.Equal(90.0, Constants.BUNNY_STARTLE_RADIUS);
        Assert.Equal(2.0, Constants.BUNNY_STARTLE_BOOST);
        Assert.Equal(14.0, Constants.BUNNY_STARTLE_HOP_HEIGHT);
        Assert.Equal(3.0, Constants.BUNNY_STARTLE_DURATION);
        Assert.Equal(6.0, Constants.BUNNY_NOSE_TWITCH_FREQ);
        Assert.Equal(0.5, Constants.BUNNY_NOSE_TWITCH_AMP);
        Assert.Equal(1.2, Constants.BUNNY_EAR_WIGGLE_FREQ);
        Assert.Equal(0.20, Constants.BUNNY_EAR_WIGGLE_AMP);
        Assert.Equal(Constants.SHEEP_ZZZ_CYCLE_SEC, Constants.BUNNY_ZZZ_CYCLE_SEC);
        Assert.Equal(Constants.SHEEP_ZZZ_RISE * 0.7, Constants.BUNNY_ZZZ_RISE);
        Assert.Equal(Constants.SHEEP_ZZZ_SIZE_START * 0.7, Constants.BUNNY_ZZZ_SIZE_START);
        Assert.Equal(Constants.SHEEP_ZZZ_SIZE_END * 0.7, Constants.BUNNY_ZZZ_SIZE_END);
        Assert.Equal(12, Constants.BUNNY_NAME_POOL.Length);
        Assert.Equal("Clover", Constants.BUNNY_NAME_POOL[0]);
        Assert.Equal("Snowdrop", Constants.BUNNY_NAME_POOL[11]);
    }

    [Fact]
    public void GrassGenerationProducesBunnyCountInRange()
    {
        for (ulong i = 0; i < 128; i++)
        {
            ulong seed = unchecked(Constants.CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15UL);
            var sim = BuildGrassSim(seed);
            Assert.InRange(CountKind(sim, EntityKind.Bunny), Constants.BUNNY_COUNT_MIN, Constants.BUNNY_COUNT_MAX);
        }
    }

    [Fact]
    public void BunniesAreGrassSceneOnly()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);
        Assert.Equal(0, CountKind(sim, EntityKind.Bunny));
        sim.SetScene(Scene.Winter);
        Assert.Equal(0, CountKind(sim, EntityKind.Bunny));
        sim.SetCritter(CritterKind.Bunny);
        Assert.Equal(0, CountKind(sim, EntityKind.Bunny));
    }

    [Fact]
    public void GeneratedBunniesHaveSpeedRange()
    {
        var sim = BuildGrassSim();
        foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Bunny))
        {
            Assert.InRange(Math.Abs(e.Vx), Constants.BUNNY_HOP_SPEED_MIN, Constants.BUNNY_HOP_SPEED_MAX);
            Assert.Equal(Math.Abs(e.Vx), e.RotationSpeed, 9);
        }
    }

    [Fact]
    public void GeneratedBunniesHaveNamesInPool()
    {
        var sim = BuildGrassSim();
        foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Bunny))
        {
            Assert.Contains(Constants.BUNNY_NAME_POOL[e.NameIndex], Constants.BUNNY_NAME_POOL);
        }
    }

    [Fact]
    public void BunnyPrngDrawOrderFollowsSheepAndCats()
    {
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);
        var sim = BuildGrassSim();

        int sheepCount = PrngCount(ref side, Constants.SHEEP_COUNT_MIN, Constants.SHEEP_COUNT_MAX);
        AdvanceSheep(ref side, sheepCount);
        int catCount = PrngCount(ref side, Constants.CAT_COUNT_MIN, Constants.CAT_COUNT_MAX);
        AdvanceCats(ref side, catCount);
        int bunnyCount = PrngCount(ref side, Constants.BUNNY_COUNT_MIN, Constants.BUNNY_COUNT_MAX);
        Assert.Equal(bunnyCount, CountKind(sim, EntityKind.Bunny));

        int seen = 0;
        foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Bunny))
        {
            double margin = Constants.BUNNY_BODY_RADIUS + 8.0;
            double xFrac = side.Uniform(0.0, 1.0);
            double expectedX = margin + xFrac * (Monitor1920 - 2.0 * margin);
            ulong vxSign = side.NextU64() & 1UL;
            double expectedDir = vxSign != 0UL ? 1.0 : -1.0;
            double expectedSpeed = side.Uniform(Constants.BUNNY_HOP_SPEED_MIN, Constants.BUNNY_HOP_SPEED_MAX);
            byte expectedName = (byte)side.Index((uint)Constants.BUNNY_NAME_POOL.Length);

            Assert.Equal(expectedX, e.X, 9);
            Assert.Equal(expectedDir * expectedSpeed, e.Vx, 9);
            Assert.Equal(expectedName, e.NameIndex);
            seen++;
        }
        Assert.Equal(bunnyCount, seen);
    }

    [Fact]
    public void BunnyEdgeBounceFlipsDirection()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Clear();
        var e = BunnyEntity(Monitor1920 - (Constants.BUNNY_BODY_RADIUS + 2.0) + 0.1,
                            Constants.BUNNY_HOP_SPEED_MIN);
        e.StateTimer = 10.0;
        sim.Entities.Add(e);

        sim.TickEntities(0.016);

        Assert.True(sim.Entities[0].Vx < 0.0);
    }

    [Fact]
    public void BunnyStartleRadiusHopsAwayAndOutsideClickDoesNothing()
    {
        var sim = BuildSim();
        sim.Entities.Clear();
        var e = BunnyEntity(500.0, -Constants.BUNNY_HOP_SPEED_MIN);
        e.State = Constants.BUNNY_STATE_IDLE;
        e.StateTimer = 3.0;
        sim.Entities.Add(e);

        sim.ApplyClick(e.X - 20.0, e.Y, 0.0);
        Assert.Equal(Constants.BUNNY_STATE_STARTLED, sim.Entities[0].State);
        Assert.True(sim.Entities[0].Vx > 0.0);
        Assert.Equal(Constants.BUNNY_STARTLE_DURATION, sim.Entities[0].StateTimer, 9);

        var after = sim.Entities[0];
        double vxBefore = after.Vx;
        byte stateBefore = after.State;
        sim.ApplyClick(after.X + Constants.BUNNY_STARTLE_RADIUS + 10.0, after.Y, 0.0);
        Assert.Equal(stateBefore, sim.Entities[0].State);
        Assert.Equal(vxBefore, sim.Entities[0].Vx, 9);
    }

    [Fact]
    public void BunnyWakesFromSleepOnStartle()
    {
        var sim = BuildSim();
        sim.Entities.Clear();
        var e = BunnyEntity(500.0, Constants.BUNNY_HOP_SPEED_MIN);
        e.State = Constants.BUNNY_STATE_SLEEPING;
        e.StateTimer = 10.0;
        sim.Entities.Add(e);

        sim.ApplyClick(e.X + 10.0, e.Y, 0.0);

        Assert.Equal(Constants.BUNNY_STATE_STARTLED, sim.Entities[0].State);
        Assert.NotEqual(Constants.BUNNY_STATE_SLEEPING, sim.Entities[0].State);
        Assert.True(sim.Entities[0].Vx < 0.0);
    }

    [Fact]
    public void BunnyHopArcIsBounded()
    {
        Assert.Equal(0.0, Sim.BunnyHopYOffset(0.0, startled: false), 12);
        Assert.Equal(0.0, Sim.BunnyHopYOffset(Constants.BUNNY_HOP_DURATION, startled: false), 12);
        double peak = Sim.BunnyHopYOffset(Constants.BUNNY_HOP_DURATION * 0.5, startled: false);
        Assert.True(peak > 0.0);
        Assert.True(peak <= Constants.BUNNY_HOP_HEIGHT);
    }

    [Fact]
    public void BunnyStateTransitionProbabilitiesAreStable()
    {
        var p = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);
        const int n = 10000;
        int graze = 0;
        int idle = 0;
        int sleep = 0;
        for (int i = 0; i < n; i++)
        {
            byte state = Sim.BunnyChooseRestState(ref p, 12);
            if (state == Constants.BUNNY_STATE_GRAZING) graze++;
            else if (state == Constants.BUNNY_STATE_IDLE) idle++;
            else if (state == Constants.BUNNY_STATE_SLEEPING) sleep++;
        }

        double sleepProb = Constants.BUNNY_SLEEP_PROB_DAY;
        double activeWeight = Constants.BUNNY_GRAZE_PROBABILITY + Constants.BUNNY_IDLE_PROBABILITY;
        double expectedGraze = (1.0 - sleepProb) * Constants.BUNNY_GRAZE_PROBABILITY / activeWeight;
        double expectedIdle = (1.0 - sleepProb) * Constants.BUNNY_IDLE_PROBABILITY / activeWeight;
        Assert.InRange(sleep / (double)n, sleepProb - 0.02, sleepProb + 0.02);
        Assert.InRange(graze / (double)n, expectedGraze - 0.02, expectedGraze + 0.02);
        Assert.InRange(idle / (double)n, expectedIdle - 0.02, expectedIdle + 0.02);
    }

    [Fact]
    public void BunnyTimeOfDaySleepBiasIsDayNight()
    {
        Assert.Equal(Constants.BUNNY_SLEEP_PROB_DAY, Sim.BunnySleepProbForLocalHour(12));
        Assert.Equal(Constants.BUNNY_SLEEP_PROB_NIGHT, Sim.BunnySleepProbForLocalHour(0));

        const int n = 10000;
        var noon = Prng.Init(Constants.CANONICAL_TEST_SEED ^ 0x1234UL);
        var midnight = Prng.Init(Constants.CANONICAL_TEST_SEED ^ 0x5678UL);
        int noonSleep = 0;
        int midnightSleep = 0;
        for (int i = 0; i < n; i++)
        {
            if (Sim.BunnyChooseRestState(ref noon, 12) == Constants.BUNNY_STATE_SLEEPING) noonSleep++;
            if (Sim.BunnyChooseRestState(ref midnight, 0) == Constants.BUNNY_STATE_SLEEPING) midnightSleep++;
        }
        Assert.InRange(noonSleep / (double)n,
                       Constants.BUNNY_SLEEP_PROB_DAY - 0.02,
                       Constants.BUNNY_SLEEP_PROB_DAY + 0.02);
        Assert.InRange(midnightSleep / (double)n,
                       Constants.BUNNY_SLEEP_PROB_NIGHT - 0.02,
                       Constants.BUNNY_SLEEP_PROB_NIGHT + 0.02);
    }
}
