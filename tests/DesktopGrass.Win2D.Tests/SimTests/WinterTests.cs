using DesktopGrass.Win2D;
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
}
