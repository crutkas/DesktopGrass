using DesktopGrass.Win2D;
using System;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class WinterTests
{
    private const double Monitor1920 = 1920.0;
    private const double FirstBladeBaseX = 4.941073726820111;
    private const double TwoPi = 6.28318530717958647692;

    private static Sim BuildSim(ulong seed = Constants.CANONICAL_TEST_SEED,
                                double monitorWidth = Monitor1920,
                                double density = 1.0)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(seed, monitorWidth, density),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(seed, monitorWidth);
        sim.ResetEntities(seed);
        return sim;
    }

    private static void TickUntilFirstSnowflake(Sim sim)
    {
        for (int i = 0; i < 10000 && sim.Entities.Count == 0; i++)
        {
            sim.Tick(0.01, System.ReadOnlySpan<InputEvent>.Empty);
        }
        Assert.NotEmpty(sim.Entities);
    }

    [Fact]
    public void WinterConstantsArePinned()
    {
        Assert.Equal(8.0, Constants.SNOWFLAKE_EMIT_RATE_PER_1920DIP);
        Assert.Equal(20.0, Constants.SNOWFLAKE_FALL_SPEED_MIN);
        Assert.Equal(40.0, Constants.SNOWFLAKE_FALL_SPEED_MAX);
        Assert.Equal(1.5, Constants.SNOWFLAKE_SIZE_MIN);
        Assert.Equal(10.0, Constants.SNOWFLAKE_SWAY_AMPLITUDE);
        Assert.Equal(0xC0FFEE1CECAFEBABul, Constants.SNOWFLAKE_PRNG_SALT);
        Assert.Equal(1.25, Constants.SNOW_TIP_RADIUS_FACTOR);
        Assert.Equal(0xFFFFFFFFu, Constants.SNOW_TIP_COLOR);
    }

    [Fact]
    public void SetSceneWinterInitializesSnowflakeScheduler()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        Assert.True(sim.NextSnowflakeSpawnTime > sim.GlobalTime);
        Assert.True(sim.NextSnowflakeSpawnTime < 100.0);
    }

    [Fact]
    public void FirstWinterSnowflakeEmitsOnScheduledTick()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        TickUntilFirstSnowflake(sim);

        Assert.Single(sim.Entities);
        Assert.Equal(EntityKind.Snowflake, sim.Entities[0].Kind);
    }

    [Fact]
    public void FirstWinterSnowflakeMatchesSpecDerivedPrngSnapshot()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        TickUntilFirstSnowflake(sim);
        Assert.Single(sim.Entities);

        var expected = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.SNOWFLAKE_PRNG_SALT);
        double lambda = Constants.SNOWFLAKE_EMIT_RATE_PER_1920DIP * sim.MonitorWidth / 1920.0;
        double firstInterval = expected.Exponential(lambda);
        double expectedSize = expected.Uniform(Constants.SNOWFLAKE_SIZE_MIN, Constants.SNOWFLAKE_SIZE_MAX);
        double expectedX = expected.Uniform(-20.0, sim.MonitorWidth + 20.0);
        double expectedFallSpeed = expected.Uniform(Constants.SNOWFLAKE_FALL_SPEED_MIN,
                                                   Constants.SNOWFLAKE_FALL_SPEED_MAX);
        double expectedRotation = expected.Uniform(0.0, TwoPi);
        double expectedRotationSpeed = expected.Uniform(-1.5, 1.5);
        uint expectedSeed = expected.NextU32();
        double nextInterval = expected.Exponential(lambda);

        Entity e = sim.Entities[0];
        Assert.Equal(expectedSize, e.Size, 12);
        Assert.Equal(expectedX, e.X, 12);
        Assert.Equal(expectedFallSpeed, e.Vy, 12);
        Assert.Equal(expectedRotation, e.Rotation, 12);
        Assert.Equal(expectedRotationSpeed, e.RotationSpeed, 12);
        Assert.Equal(expectedSeed, e.Seed);
        Assert.Equal(firstInterval + nextInterval, sim.NextSnowflakeSpawnTime, 12);
    }

    [Fact]
    public void SnowflakeSwayVelocityWobblesFromSeedPhase()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Snowflake,
            Seed = 0,
            Age = 0.0,
            Lifetime = 100.0,
        });

        sim.TickEntities(0.0);

        double expectedVx = Constants.SNOWFLAKE_SWAY_AMPLITUDE * Constants.SNOWFLAKE_SWAY_FREQUENCY
                          * TwoPi * Math.Cos(0.0);
        Assert.Single(sim.Entities);
        Assert.Equal(expectedVx, sim.Entities[0].Vx, 12);
    }

    [Fact]
    public void SnowflakesAreCulledAfterLifetime()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Snowflake,
            Lifetime = 1.0,
            Age = 0.9,
        });

        sim.TickEntities(0.2);

        Assert.Empty(sim.Entities);
    }

    [Fact]
    public void SnowflakesAreCulledBelowGroundLine()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Snowflake,
            Y = sim.WindowHeight + 5.0,
            Lifetime = 100.0,
        });

        sim.TickEntities(0.0);

        Assert.Empty(sim.Entities);
    }

    [Fact]
    public void WinterSnowflakeEmitterHonorsMaxEntityCap()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);
        sim.NextSnowflakeSpawnTime = sim.GlobalTime;
        for (int i = 0; i < Constants.MAX_ENTITIES_PER_MONITOR; i++)
        {
            sim.Entities.Add(new Entity
            {
                Kind = EntityKind.Snowflake,
                Lifetime = 100.0,
            });
        }

        sim.TickEntities(0.0);

        Assert.True(sim.Entities.Count <= Constants.MAX_ENTITIES_PER_MONITOR);
        Assert.Equal(Constants.MAX_ENTITIES_PER_MONITOR, sim.Entities.Count);
    }

    [Fact]
    public void WinterSceneDoesNotPerturbFirstBladeSnapshot()
    {
        var sim = BuildSim();
        Assert.Equal(FirstBladeBaseX, sim.Blades[0].BaseX, 12);

        sim.SetScene(Scene.Winter);
        Assert.Equal(FirstBladeBaseX, sim.Blades[0].BaseX, 12);

        sim.SetScene(Scene.Grass);
        Assert.Equal(FirstBladeBaseX, sim.Blades[0].BaseX, 12);
    }

    [Fact]
    public void SnowflakesDoNotEmitInNonWinterScenes()
    {
        var sim = BuildSim();

        sim.SetScene(Scene.Grass);
        sim.NextSnowflakeSpawnTime = 0.0;
        sim.Tick(2.0, System.ReadOnlySpan<InputEvent>.Empty);
        Assert.DoesNotContain(sim.Entities, e => e.Kind == EntityKind.Snowflake);

        sim.SetScene(Scene.Desert);
        sim.Entities.Clear();
        sim.NextSnowflakeSpawnTime = 0.0;
        sim.Tick(2.0, System.ReadOnlySpan<InputEvent>.Empty);
        Assert.Empty(sim.Entities);
    }

    private static int CountSnowPuffs(Sim sim)
    {
        int n = 0;
        foreach (Entity e in sim.Entities)
            if (e.Kind == EntityKind.SnowPuff) n++;
        return n;
    }

    [Fact]
    public void SnowPuffConstantsArePinned()
    {
        Assert.Equal(9, Constants.SNOW_PUFF_COUNT_MIN);
        Assert.Equal(16, Constants.SNOW_PUFF_COUNT_MAX);
        Assert.Equal(2.0, Constants.SNOW_PUFF_SIZE_MIN);
        Assert.Equal(4.5, Constants.SNOW_PUFF_SIZE_MAX);
        Assert.Equal(150.0, Constants.SNOW_PUFF_GRAVITY);
        Assert.Equal(1.6, Constants.SNOW_PUFF_DRAG);
        Assert.Equal(1.25, Constants.SNOW_PUFF_SPREAD_RAD);
        Assert.Equal(0x5503FF1E5503FF1Eul, Constants.SNOW_PUFF_PRNG_SALT);
        Assert.Equal(0.42, Constants.WINTER_DRIFT_HEIGHT_SCALE);
        Assert.Equal(0xFFE8EEF6u, Constants.WINTER_DRIFT_BASE_COLOR);
    }

    [Fact]
    public void ClickingWinterSnowbankShedsSnowPuffBurst()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        sim.ApplyClick(400.0, sim.WindowHeight - 5.0, sim.GlobalTime);

        int puffs = CountSnowPuffs(sim);
        Assert.True(puffs >= Constants.SNOW_PUFF_COUNT_MIN);
        Assert.True(puffs <= Constants.SNOW_PUFF_COUNT_MAX);

        foreach (Entity e in sim.Entities)
        {
            if (e.Kind != EntityKind.SnowPuff) continue;
            Assert.True(e.Vy < 0.0);
            Assert.True(e.Y <= sim.WindowHeight + 1e-9);
        }
    }

    [Fact]
    public void SnowPuffOnlyFiresInWinter()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Grass);

        sim.ApplyClick(400.0, sim.WindowHeight - 5.0, sim.GlobalTime);

        Assert.Equal(0, CountSnowPuffs(sim));
    }

    [Fact]
    public void NonFiniteClickShedsNoSnowPuff()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        sim.ApplyClick(double.NaN, sim.WindowHeight - 5.0, sim.GlobalTime);

        Assert.Equal(0, CountSnowPuffs(sim));
    }

    [Fact]
    public void SnowPuffBurstRisesThenSettlesAndIsCulled()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        sim.ApplyClick(400.0, sim.WindowHeight - 5.0, sim.GlobalTime);
        Assert.True(CountSnowPuffs(sim) > 0);

        for (int i = 0; i < 200; i++) sim.TickEntities(0.02);
        Assert.Equal(0, CountSnowPuffs(sim));
    }

    [Fact]
    public void SnowPuffDrawOrderMatchesSidePrngStream()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        sim.ApplyClick(300.0, sim.WindowHeight - 5.0, sim.GlobalTime);

        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.SNOW_PUFF_PRNG_SALT);
        int expectedCount = Constants.SNOW_PUFF_COUNT_MIN
            + (int)side.Index((uint)(Constants.SNOW_PUFF_COUNT_MAX - Constants.SNOW_PUFF_COUNT_MIN + 1));
        Assert.Equal(expectedCount, CountSnowPuffs(sim));

        double expectedSize = side.Uniform(Constants.SNOW_PUFF_SIZE_MIN, Constants.SNOW_PUFF_SIZE_MAX);
        foreach (Entity e in sim.Entities)
        {
            if (e.Kind != EntityKind.SnowPuff) continue;
            Assert.Equal(expectedSize, e.Size, 12);
            break;
        }
    }

    [Fact]
    public void SnowPuffSaltIsUniqueAmongWinterSalts()
    {
        ulong[] otherSalts =
        {
            Constants.REGROW_PRNG_SALT, Constants.FLOWER_PRNG_SALT, Constants.MUSHROOM_PRNG_SALT,
            Constants.AMBIENT_GUST_PRNG_SALT, Constants.CACTUS_PRNG_SALT, Constants.TUMBLEWEED_PRNG_SALT,
            Constants.CRITTER_PRNG_SALT, Constants.BUTTERFLY_PRNG_SALT, Constants.FIREFLY_PRNG_SALT,
            Constants.BIRD_FLYBY_PRNG_SALT, Constants.SNOWFLAKE_PRNG_SALT,
            Constants.PINE_PRNG_SALT, Constants.LEAF_PUFF_PRNG_SALT,
        };
        foreach (ulong s in otherSalts)
            Assert.NotEqual(Constants.SNOW_PUFF_PRNG_SALT, s);
        Assert.NotEqual(Constants.SNOW_PUFF_PRNG_SALT, Constants.SNOW_DRIFT_PRNG_SALT);
    }

    // -----------------------------------------------------------------------
    // §21.1 snow drift (cursor-move spindrift)
    // -----------------------------------------------------------------------

    private static void WinterDrift(Sim sim, double x0, double x1, double dt)
    {
        double y = sim.WindowHeight - 5.0;
        sim.ApplyCursorMove(new InputEvent(EventType.Move, x0, y, sim.GlobalTime));
        sim.ApplyCursorMove(new InputEvent(EventType.Move, x1, y, sim.GlobalTime + dt));
    }

    [Fact]
    public void SnowDriftConstantsArePinned()
    {
        Assert.Equal(3, Constants.SNOW_DRIFT_COUNT_MIN);
        Assert.Equal(6, Constants.SNOW_DRIFT_COUNT_MAX);
        Assert.Equal(70.0, Constants.SNOW_DRIFT_REACH_DIP);
        Assert.Equal(90.0, Constants.SNOW_DRIFT_MIN_SPEED);
        Assert.Equal(0.12, Constants.SNOW_DRIFT_COOLDOWN_SEC);
        Assert.Equal(0.75, Constants.SNOW_DRIFT_SIZE_SCALE);
        Assert.Equal(0.6, Constants.SNOW_DRIFT_SPEED_SCALE);
        Assert.Equal(0x5D81F77D5D81F77Dul, Constants.SNOW_DRIFT_PRNG_SALT);
    }

    [Fact]
    public void BrushingCursorAcrossSnowbankKicksUpDriftWisp()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        WinterDrift(sim, 300.0, 360.0, 0.05); // 1200 DIP/s

        int puffs = CountSnowPuffs(sim);
        Assert.True(puffs >= Constants.SNOW_DRIFT_COUNT_MIN);
        Assert.True(puffs <= Constants.SNOW_DRIFT_COUNT_MAX);

        foreach (Entity e in sim.Entities)
        {
            if (e.Kind != EntityKind.SnowPuff) continue;
            Assert.True(e.Vy < 0.0);
            Assert.True(e.Size <= Constants.SNOW_PUFF_SIZE_MAX * Constants.SNOW_DRIFT_SIZE_SCALE + 1e-9);
        }
    }

    [Fact]
    public void SnowDriftOnlyFiresInWinter()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Grass);

        WinterDrift(sim, 300.0, 360.0, 0.05);

        Assert.Equal(0, CountSnowPuffs(sim));
    }

    [Fact]
    public void SlowCursorBrushKicksUpNoDrift()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        WinterDrift(sim, 300.0, 302.0, 0.05); // 40 DIP/s < 90

        Assert.Equal(0, CountSnowPuffs(sim));
    }

    [Fact]
    public void HighCursorBrushAboveSnowBandKicksUpNoDrift()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        double y = sim.WindowHeight - Constants.SNOW_DRIFT_REACH_DIP - 20.0;
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 300.0, y, sim.GlobalTime));
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 360.0, y, sim.GlobalTime + 0.05));

        Assert.Equal(0, CountSnowPuffs(sim));
    }

    [Fact]
    public void SnowDriftRespectsGlobalCooldown()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        WinterDrift(sim, 300.0, 360.0, 0.05);
        int first = CountSnowPuffs(sim);
        Assert.True(first >= Constants.SNOW_DRIFT_COUNT_MIN);

        // Same frame (GlobalTime unchanged): a second qualifying brush is gated.
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 420.0, sim.WindowHeight - 5.0, sim.GlobalTime + 0.10));
        Assert.Equal(first, CountSnowPuffs(sim));

        // Advance past the cooldown: a fresh brush kicks up another wisp.
        sim.GlobalTime += Constants.SNOW_DRIFT_COOLDOWN_SEC + 0.01;
        sim.ApplyCursorMove(new InputEvent(EventType.Move, 480.0, sim.WindowHeight - 5.0, sim.GlobalTime + 0.05));
        Assert.True(CountSnowPuffs(sim) > first);
    }

    [Fact]
    public void SnowDriftMovesLeaveClickPuffStreamUntouched()
    {
        var a = BuildSim();
        a.SetScene(Scene.Winter);
        var b = BuildSim();
        b.SetScene(Scene.Winter);

        WinterDrift(a, 300.0, 360.0, 0.05);
        int aPreClick = a.Entities.Count;

        a.ApplyClick(800.0, a.WindowHeight - 5.0, a.GlobalTime);
        b.ApplyClick(800.0, b.WindowHeight - 5.0, b.GlobalTime);

        var aClick = a.Entities.GetRange(aPreClick, a.Entities.Count - aPreClick);
        var bClick = b.Entities.FindAll(e => e.Kind == EntityKind.SnowPuff);

        Assert.Equal(bClick.Count, aClick.Count);
        for (int i = 0; i < aClick.Count; i++)
        {
            Assert.Equal(bClick[i].Size, aClick[i].Size, 12);
            Assert.Equal(bClick[i].Vx, aClick[i].Vx, 12);
            Assert.Equal(bClick[i].Vy, aClick[i].Vy, 12);
            Assert.Equal(bClick[i].Lifetime, aClick[i].Lifetime, 12);
        }
    }

    // -----------------------------------------------------------------------
    // §21.2 snow carve (footprints)
    // -----------------------------------------------------------------------

    [Fact]
    public void SnowCarveConstantsArePinned()
    {
        Assert.Equal(192, Constants.SNOW_CARVE_BUCKETS);
        Assert.Equal(24.0, Constants.SNOW_CARVE_RADIUS_DIP);
        Assert.Equal(7.0, Constants.SNOW_CARVE_DEPTH_PER_CLICK);
        Assert.Equal(11.0, Constants.SNOW_CARVE_MAX_DEPTH);
        Assert.Equal(1.8, Constants.SNOW_CARVE_REFILL_RATE);
    }

    [Fact]
    public void WinterSimInitializesCarveHeightfield()
    {
        var sim = BuildSim();
        Assert.Equal(Constants.SNOW_CARVE_BUCKETS, sim.SnowCarve.Length);
        sim.SetScene(Scene.Winter);
        Assert.Equal(Constants.SNOW_CARVE_BUCKETS, sim.SnowCarve.Length);
        foreach (double c in sim.SnowCarve) Assert.Equal(0.0, c);
    }

    [Fact]
    public void ClickingWinterSnowbankPressesDent()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        // 405 sits exactly on a bucket center (bucketWidth = 1920/192 = 10).
        sim.ApplyClick(405.0, sim.WindowHeight - 5.0, sim.GlobalTime);

        Assert.Equal(Constants.SNOW_CARVE_DEPTH_PER_CLICK, sim.SnowCarveDepthAt(405.0), 9);
    }

    [Fact]
    public void SnowCarveFallsOffWithDistanceAndStopsPastRadius()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);
        sim.ApplyClick(405.0, sim.WindowHeight - 5.0, sim.GlobalTime);

        double center = sim.SnowCarveDepthAt(405.0);
        double edge = sim.SnowCarveDepthAt(425.0);
        double beyond = sim.SnowCarveDepthAt(435.0);

        Assert.True(center > edge);
        Assert.True(edge > 0.0);
        Assert.Equal(0.0, beyond, 9);
    }

    [Fact]
    public void RepeatedClicksClampCarveAtMaxDepth()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        for (int i = 0; i < 10; i++) sim.ApplyClick(405.0, sim.WindowHeight - 5.0, sim.GlobalTime);

        Assert.Equal(Constants.SNOW_CARVE_MAX_DEPTH, sim.SnowCarveDepthAt(405.0), 9);
    }

    [Fact]
    public void ClicksOutsideWinterNeverCarve()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Grass);

        sim.ApplyClick(405.0, sim.WindowHeight - 5.0, sim.GlobalTime);
        sim.ApplySnowCarve(405.0);

        Assert.Equal(0.0, sim.SnowCarveDepthAt(405.0), 9);
    }

    [Fact]
    public void SceneChangesClearSnowFootprints()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);
        sim.ApplyClick(405.0, sim.WindowHeight - 5.0, sim.GlobalTime);
        Assert.True(sim.SnowCarveDepthAt(405.0) > 0.0);

        sim.SetScene(Scene.Grass);
        Assert.Equal(0.0, sim.SnowCarveDepthAt(405.0), 9);

        sim.SetScene(Scene.Winter);
        Assert.Equal(0.0, sim.SnowCarveDepthAt(405.0), 9);
    }

    [Fact]
    public void SnowCarveSettlesBackAndNeverGoesNegative()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);
        sim.ApplyClick(405.0, sim.WindowHeight - 5.0, sim.GlobalTime);

        sim.DecaySnowCarve(1.0);
        Assert.Equal(Constants.SNOW_CARVE_DEPTH_PER_CLICK - Constants.SNOW_CARVE_REFILL_RATE,
                     sim.SnowCarveDepthAt(405.0), 9);

        for (int i = 0; i < 100; i++) sim.DecaySnowCarve(1.0);
        foreach (double c in sim.SnowCarve) Assert.True(c >= 0.0);
        Assert.Equal(0.0, sim.SnowCarveDepthAt(405.0), 9);
    }

    [Fact]
    public void SameFrameClickLandsFullDentBeforeDecay()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        var ev = new InputEvent(EventType.Click, 405.0, sim.WindowHeight - 5.0, sim.GlobalTime);
        InputEvent[] evs = { ev };
        sim.Tick(0.016, evs);

        Assert.Equal(Constants.SNOW_CARVE_DEPTH_PER_CLICK, sim.SnowCarveDepthAt(405.0), 9);
    }

    [Fact]
    public void SnowCarveQueryInterpolatesBetweenBuckets()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);
        sim.ApplyClick(405.0, sim.WindowHeight - 5.0, sim.GlobalTime);

        double at395 = sim.SnowCarveDepthAt(395.0);
        double at405 = sim.SnowCarveDepthAt(405.0);
        double at400 = sim.SnowCarveDepthAt(400.0);
        Assert.True(at400 > Math.Min(at395, at405) - 1e-9);
        Assert.True(at400 < Math.Max(at395, at405) + 1e-9);
    }

    [Fact]
    public void SnowCarveHandlesEdgeAndNonFiniteCoordinates()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Winter);

        sim.ApplySnowCarve(0.0);
        sim.ApplySnowCarve(sim.MonitorWidth);
        sim.ApplySnowCarve(-50.0);
        sim.ApplySnowCarve(sim.MonitorWidth + 500.0);
        sim.ApplySnowCarve(double.NaN);

        Assert.True(sim.SnowCarveDepthAt(0.0) > 0.0);
        Assert.True(double.IsFinite(sim.SnowCarveDepthAt(-100.0)));
        Assert.True(sim.SnowCarveDepthAt(-100.0) >= 0.0);
        Assert.True(double.IsFinite(sim.SnowCarveDepthAt(sim.MonitorWidth + 1000.0)));
        Assert.Equal(0.0, sim.SnowCarveDepthAt(double.NaN), 9);
    }
}
