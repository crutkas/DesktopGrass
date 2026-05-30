// HedgehogTests.cs - §17.9 Hedgehog critter tests.
// Mirrors tests/DesktopGrass.Native.Tests/src/hedgehog_tests.cpp.

using System;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class HedgehogTests
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

    private static Entity HedgehogEntity(double x = 500.0, double vx = Constants.HEDGEHOG_WALK_SPEED_MIN)
    {
        return new Entity
        {
            Kind = EntityKind.Hedgehog,
            Size = Constants.HEDGEHOG_BODY_RADIUS,
            X = x,
            Y = Constants.STRIP_HEIGHT + Constants.HEADROOM - Constants.HEDGEHOG_BODY_HEIGHT - Constants.HEDGEHOG_LEG_LENGTH,
            Vx = vx,
            RotationSpeed = Math.Abs(vx),
            Lifetime = -1.0,
            State = Constants.HEDGEHOG_STATE_WALKING,
            StateTimer = Constants.HEDGEHOG_WALK_DURATION_MIN,
            PreviousState = Constants.HEDGEHOG_STATE_WALKING,
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

    private static void AdvanceBunnies(ref Prng side, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ = side.Uniform(0.0, 1.0);
            _ = side.NextU64();
            _ = side.Uniform(Constants.BUNNY_HOP_SPEED_MIN, Constants.BUNNY_HOP_SPEED_MAX);
            _ = side.Index((uint)Constants.BUNNY_NAME_POOL.Length);
        }
    }

    [Fact]
    public void HedgehogConstantsArePinnedToSpecValues()
    {
        Assert.Equal(0, Constants.HEDGEHOG_COUNT_MIN);
        Assert.Equal(1, Constants.HEDGEHOG_COUNT_MAX);
        Assert.Equal(0.55, Constants.HEDGEHOG_COUNT_PROBABILITY);
        Assert.Equal(4.0, Constants.HEDGEHOG_WALK_SPEED_MIN);
        Assert.Equal(8.0, Constants.HEDGEHOG_WALK_SPEED_MAX);
        Assert.Equal(9.0, Constants.HEDGEHOG_BODY_RADIUS);
        Assert.Equal(5.5, Constants.HEDGEHOG_BODY_HEIGHT);
        Assert.Equal(3.6, Constants.HEDGEHOG_HEAD_RADIUS);
        Assert.Equal(0.8, Constants.HEDGEHOG_NOSE_RADIUS);
        Assert.Equal(2.5, Constants.HEDGEHOG_LEG_LENGTH);
        Assert.Equal(14, Constants.HEDGEHOG_SPIKE_COUNT);
        Assert.Equal(3.0, Constants.HEDGEHOG_SPIKE_LENGTH);
        Assert.Equal(1.4, Constants.HEDGEHOG_SPIKE_WIDTH);
        Assert.Equal(-20.0, Constants.HEDGEHOG_SPIKE_ARC_START_DEG);
        Assert.Equal(200.0, Constants.HEDGEHOG_SPIKE_ARC_END_DEG);
        Assert.Equal(0xFF5C4633u, Constants.HEDGEHOG_BODY_COLOR);
        Assert.Equal(0xFF3A2A1Fu, Constants.HEDGEHOG_SPIKE_COLOR);
        Assert.Equal(0xFF1E150Eu, Constants.HEDGEHOG_SPIKE_TIP_COLOR);
        Assert.Equal(0xFF1A1208u, Constants.HEDGEHOG_NOSE_COLOR);
        Assert.Equal(0xFF1A1208u, Constants.HEDGEHOG_EYE_COLOR);
        Assert.Equal((byte)0, Constants.HEDGEHOG_STATE_WALKING);
        Assert.Equal((byte)1, Constants.HEDGEHOG_STATE_SNUFFLING);
        Assert.Equal((byte)2, Constants.HEDGEHOG_STATE_IDLE);
        Assert.Equal((byte)3, Constants.HEDGEHOG_STATE_SLEEPING);
        Assert.Equal((byte)4, Constants.HEDGEHOG_STATE_CURLED);
        Assert.Equal(6.0, Constants.HEDGEHOG_WALK_DURATION_MIN);
        Assert.Equal(12.0, Constants.HEDGEHOG_WALK_DURATION_MAX);
        Assert.Equal(3.0, Constants.HEDGEHOG_SNUFFLE_DURATION_MIN);
        Assert.Equal(6.0, Constants.HEDGEHOG_SNUFFLE_DURATION_MAX);
        Assert.Equal(1.5, Constants.HEDGEHOG_IDLE_DURATION_MIN);
        Assert.Equal(3.0, Constants.HEDGEHOG_IDLE_DURATION_MAX);
        Assert.Equal(10.0, Constants.HEDGEHOG_SLEEP_DURATION_MIN);
        Assert.Equal(25.0, Constants.HEDGEHOG_SLEEP_DURATION_MAX);
        Assert.Equal(3.0, Constants.HEDGEHOG_CURL_DURATION_MIN);
        Assert.Equal(5.5, Constants.HEDGEHOG_CURL_DURATION_MAX);
        Assert.Equal(0.55, Constants.HEDGEHOG_SNUFFLE_PROBABILITY);
        Assert.Equal(0.30, Constants.HEDGEHOG_IDLE_PROBABILITY);
        Assert.Equal(0.50, Constants.HEDGEHOG_SLEEP_PROB_DAY);
        Assert.Equal(0.05, Constants.HEDGEHOG_SLEEP_PROB_NIGHT);
        Assert.Equal(70.0, Constants.HEDGEHOG_STARTLE_RADIUS);
        Assert.Equal(5.0, Constants.HEDGEHOG_SNUFFLE_HEAD_FREQ);
        Assert.Equal(0.7, Constants.HEDGEHOG_SNUFFLE_HEAD_AMP);
        Assert.Equal(4.0, Constants.HEDGEHOG_WADDLE_FREQ);
        Assert.Equal(0.8, Constants.HEDGEHOG_WADDLE_AMP);
        Assert.Equal(Constants.SHEEP_ZZZ_CYCLE_SEC, Constants.HEDGEHOG_ZZZ_CYCLE_SEC);
        Assert.Equal(Constants.SHEEP_ZZZ_RISE * 0.5, Constants.HEDGEHOG_ZZZ_RISE);
        Assert.Equal(Constants.SHEEP_ZZZ_SIZE_START * 0.6, Constants.HEDGEHOG_ZZZ_SIZE_START);
        Assert.Equal(Constants.SHEEP_ZZZ_SIZE_END * 0.6, Constants.HEDGEHOG_ZZZ_SIZE_END);
        Assert.Equal(12, Constants.HEDGEHOG_NAME_POOL.Length);
        Assert.Equal("Bristle", Constants.HEDGEHOG_NAME_POOL[0]);
        Assert.Equal("Burdock", Constants.HEDGEHOG_NAME_POOL[11]);
    }

    [Fact]
    public void HedgehogCountDistributionIsProbabilisticRareSighting()
    {
        const int n = 1000;
        int present = 0;
        for (ulong i = 0; i < n; i++)
        {
            ulong seed = unchecked(Constants.CANONICAL_TEST_SEED + i * 0x9E3779B97F4A7C15UL);
            var sim = BuildGrassSim(seed);
            int count = CountKind(sim, EntityKind.Hedgehog);
            Assert.InRange(count, Constants.HEDGEHOG_COUNT_MIN, Constants.HEDGEHOG_COUNT_MAX);
            present += count;
        }
        Assert.InRange(present / (double)n,
                       Constants.HEDGEHOG_COUNT_PROBABILITY - 0.05,
                       Constants.HEDGEHOG_COUNT_PROBABILITY + 0.05);
    }

    [Fact]
    public void HedgehogsAreGrassSceneOnly()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Desert);
        Assert.Equal(0, CountKind(sim, EntityKind.Hedgehog));
        sim.SetScene(Scene.Winter);
        Assert.Equal(0, CountKind(sim, EntityKind.Hedgehog));
    }

    [Fact]
    public void GeneratedHedgehogsHaveSpeedRange()
    {
        bool sawHedgehog = false;
        for (ulong i = 0; i < 128; i++)
        {
            var sim = BuildGrassSim(unchecked(Constants.CANONICAL_TEST_SEED + i * 0xD1B54A32D192ED03UL));
            foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Hedgehog))
            {
                sawHedgehog = true;
                Assert.InRange(Math.Abs(e.Vx), Constants.HEDGEHOG_WALK_SPEED_MIN, Constants.HEDGEHOG_WALK_SPEED_MAX);
                Assert.Equal(Math.Abs(e.Vx), e.RotationSpeed, 9);
            }
        }
        Assert.True(sawHedgehog);
    }

    [Fact]
    public void GeneratedHedgehogsHaveNamesInPool()
    {
        bool sawHedgehog = false;
        for (ulong i = 0; i < 128; i++)
        {
            var sim = BuildGrassSim(unchecked(Constants.CANONICAL_TEST_SEED + i * 0x94D049BB133111EBUL));
            foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Hedgehog))
            {
                sawHedgehog = true;
                Assert.Contains(Constants.HEDGEHOG_NAME_POOL[e.NameIndex], Constants.HEDGEHOG_NAME_POOL);
            }
        }
        Assert.True(sawHedgehog);
    }

    [Fact]
    public void HedgehogPrngDrawOrderFollowsSheepCatsAndBunnies()
    {
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);
        var sim = BuildGrassSim();

        int sheepCount = PrngCount(ref side, Constants.SHEEP_COUNT_MIN, Constants.SHEEP_COUNT_MAX);
        AdvanceSheep(ref side, sheepCount);
        int catCount = PrngCount(ref side, Constants.CAT_COUNT_MIN, Constants.CAT_COUNT_MAX);
        AdvanceCats(ref side, catCount);
        int bunnyCount = PrngCount(ref side, Constants.BUNNY_COUNT_MIN, Constants.BUNNY_COUNT_MAX);
        AdvanceBunnies(ref side, bunnyCount);

        double hasDraw = side.Uniform(0.0, 1.0);
        int hedgehogCount = hasDraw < Constants.HEDGEHOG_COUNT_PROBABILITY ? 1 : 0;
        Assert.Equal(hedgehogCount, CountKind(sim, EntityKind.Hedgehog));

        int seen = 0;
        foreach (var e in sim.Entities.Where(e => e.Kind == EntityKind.Hedgehog))
        {
            double margin = Constants.HEDGEHOG_BODY_RADIUS + 8.0;
            double xFrac = side.Uniform(0.0, 1.0);
            double expectedX = margin + xFrac * (Monitor1920 - 2.0 * margin);
            ulong vxSign = side.NextU64() & 1UL;
            double expectedDir = vxSign != 0UL ? 1.0 : -1.0;
            double expectedSpeed = side.Uniform(Constants.HEDGEHOG_WALK_SPEED_MIN, Constants.HEDGEHOG_WALK_SPEED_MAX);
            byte expectedName = (byte)side.Index((uint)Constants.HEDGEHOG_NAME_POOL.Length);

            Assert.Equal(expectedX, e.X, 9);
            Assert.Equal(expectedDir * expectedSpeed, e.Vx, 9);
            Assert.Equal(expectedName, e.NameIndex);
            seen++;
        }
        Assert.Equal(hedgehogCount, seen);
    }

    [Fact]
    public void HedgehogEdgeBounceFlipsDirection()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Clear();
        var e = HedgehogEntity(Monitor1920 - (Constants.HEDGEHOG_BODY_RADIUS + 2.0) + 0.1,
                               Constants.HEDGEHOG_WALK_SPEED_MIN);
        e.StateTimer = 10.0;
        sim.Entities.Add(e);

        sim.TickEntities(0.016);

        Assert.True(sim.Entities[0].Vx < 0.0);
    }

    [Fact]
    public void HedgehogStartleRadiusCurlsWithoutFlippingVx()
    {
        var sim = BuildSim();
        sim.Entities.Clear();
        var e = HedgehogEntity(500.0, -Constants.HEDGEHOG_WALK_SPEED_MIN);
        e.State = Constants.HEDGEHOG_STATE_WALKING;
        e.StateTimer = 10.0;
        sim.Entities.Add(e);

        sim.ApplyClick(e.X + 10.0, e.Y, 0.0);
        Assert.Equal(Constants.HEDGEHOG_STATE_CURLED, sim.Entities[0].State);
        Assert.Equal(-Constants.HEDGEHOG_WALK_SPEED_MIN, sim.Entities[0].Vx, 9);
        Assert.InRange(sim.Entities[0].StateTimer,
                       Constants.HEDGEHOG_CURL_DURATION_MIN,
                       Constants.HEDGEHOG_CURL_DURATION_MAX);

        var outside = BuildSim();
        outside.Entities.Clear();
        var far = HedgehogEntity(500.0, Constants.HEDGEHOG_WALK_SPEED_MIN);
        outside.Entities.Add(far);
        outside.ApplyClick(far.X + Constants.HEDGEHOG_STARTLE_RADIUS + 10.0, far.Y, 0.0);
        Assert.Equal(Constants.HEDGEHOG_STATE_WALKING, outside.Entities[0].State);
        Assert.Equal(Constants.HEDGEHOG_WALK_SPEED_MIN, outside.Entities[0].Vx, 9);
    }

    [Fact]
    public void HedgehogCurlAutoUncurlsToPreviousState()
    {
        var sim = BuildSim();
        sim.Entities.Clear();
        var e = HedgehogEntity(500.0, Constants.HEDGEHOG_WALK_SPEED_MIN);
        e.State = Constants.HEDGEHOG_STATE_IDLE;
        e.StateTimer = 2.5;
        sim.Entities.Add(e);

        sim.ApplyClick(e.X, e.Y, 0.0);
        Assert.Equal(Constants.HEDGEHOG_STATE_CURLED, sim.Entities[0].State);
        sim.TickEntities(Constants.HEDGEHOG_CURL_DURATION_MAX + 0.1);

        Assert.Equal(Constants.HEDGEHOG_STATE_IDLE, sim.Entities[0].State);
        Assert.Equal(Constants.HEDGEHOG_WALK_SPEED_MIN, sim.Entities[0].Vx, 9);
    }

    [Fact]
    public void HedgehogWakesFromSleepOnStartleAndDoesNotResumeSleep()
    {
        var sim = BuildSim();
        sim.Entities.Clear();
        var e = HedgehogEntity(500.0, Constants.HEDGEHOG_WALK_SPEED_MIN);
        e.State = Constants.HEDGEHOG_STATE_SLEEPING;
        e.StateTimer = 10.0;
        sim.Entities.Add(e);

        sim.ApplyClick(e.X + 10.0, e.Y, 0.0);
        Assert.Equal(Constants.HEDGEHOG_STATE_CURLED, sim.Entities[0].State);
        Assert.NotEqual(Constants.HEDGEHOG_STATE_SLEEPING, sim.Entities[0].State);
        sim.TickEntities(Constants.HEDGEHOG_CURL_DURATION_MAX + 0.1);

        Assert.Equal(Constants.HEDGEHOG_STATE_WALKING, sim.Entities[0].State);
        Assert.NotEqual(Constants.HEDGEHOG_STATE_SLEEPING, sim.Entities[0].State);
    }

    [Fact]
    public void HedgehogStateTransitionProbabilitiesAreStable()
    {
        var p = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.CRITTER_PRNG_SALT);
        const int n = 10000;
        int snuffle = 0;
        int idle = 0;
        int sleep = 0;
        for (int i = 0; i < n; i++)
        {
            byte state = Sim.HedgehogChooseRestState(ref p, 12);
            if (state == Constants.HEDGEHOG_STATE_SNUFFLING) snuffle++;
            else if (state == Constants.HEDGEHOG_STATE_IDLE) idle++;
            else if (state == Constants.HEDGEHOG_STATE_SLEEPING) sleep++;
        }

        double sleepProb = Constants.HEDGEHOG_SLEEP_PROB_DAY;
        double activeWeight = Constants.HEDGEHOG_SNUFFLE_PROBABILITY + Constants.HEDGEHOG_IDLE_PROBABILITY;
        double expectedSnuffle = (1.0 - sleepProb) * Constants.HEDGEHOG_SNUFFLE_PROBABILITY / activeWeight;
        double expectedIdle = (1.0 - sleepProb) * Constants.HEDGEHOG_IDLE_PROBABILITY / activeWeight;
        Assert.InRange(sleep / (double)n, sleepProb - 0.02, sleepProb + 0.02);
        Assert.InRange(snuffle / (double)n, expectedSnuffle - 0.02, expectedSnuffle + 0.02);
        Assert.InRange(idle / (double)n, expectedIdle - 0.02, expectedIdle + 0.02);
    }

    [Fact]
    public void HedgehogTimeOfDaySleepBiasIsNocturnal()
    {
        Assert.Equal(Constants.HEDGEHOG_SLEEP_PROB_DAY, Sim.HedgehogSleepProbForLocalHour(12));
        Assert.Equal(Constants.HEDGEHOG_SLEEP_PROB_NIGHT, Sim.HedgehogSleepProbForLocalHour(0));

        const int n = 10000;
        var noon = Prng.Init(Constants.CANONICAL_TEST_SEED ^ 0x1234UL);
        var midnight = Prng.Init(Constants.CANONICAL_TEST_SEED ^ 0x5678UL);
        int noonSleep = 0;
        int midnightSleep = 0;
        for (int i = 0; i < n; i++)
        {
            if (Sim.HedgehogChooseRestState(ref noon, 12) == Constants.HEDGEHOG_STATE_SLEEPING) noonSleep++;
            if (Sim.HedgehogChooseRestState(ref midnight, 0) == Constants.HEDGEHOG_STATE_SLEEPING) midnightSleep++;
        }
        Assert.InRange(noonSleep / (double)n,
                       Constants.HEDGEHOG_SLEEP_PROB_DAY - 0.02,
                       Constants.HEDGEHOG_SLEEP_PROB_DAY + 0.02);
        Assert.InRange(midnightSleep / (double)n,
                       Constants.HEDGEHOG_SLEEP_PROB_NIGHT - 0.02,
                       Constants.HEDGEHOG_SLEEP_PROB_NIGHT + 0.02);
    }

    [Fact]
    public void HedgehogHasNoActiveInteractionStates()
    {
        var p = Prng.Init(Constants.CANONICAL_TEST_SEED ^ 0xCAFEUL);
        for (int i = 0; i < 1000; i++)
        {
            byte state = Sim.HedgehogChooseRestState(ref p, 0);
            Assert.True(state == Constants.HEDGEHOG_STATE_SNUFFLING
                     || state == Constants.HEDGEHOG_STATE_IDLE
                     || state == Constants.HEDGEHOG_STATE_SLEEPING);
        }

        var sim = BuildSim();
        sim.Entities.Clear();
        var e = HedgehogEntity(500.0, Constants.HEDGEHOG_WALK_SPEED_MIN);
        e.StateTimer = 10.0;
        sim.Entities.Add(e);
        sim.TickEntities(0.016);
        Assert.Equal(Constants.HEDGEHOG_STATE_WALKING, sim.Entities[0].State);
        Assert.Equal(Constants.HEDGEHOG_WALK_SPEED_MIN, Math.Abs(sim.Entities[0].Vx), 9);
    }
}
