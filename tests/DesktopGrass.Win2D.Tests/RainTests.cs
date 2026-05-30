using System;
using System.Collections.Generic;
using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests;

public class RainTests
{
    private static Sim BuildSim(ulong seed = Constants.CANONICAL_TEST_SEED,
                                double monitorWidth = 1920.0,
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

    private static int CountRaindrops(Sim sim) =>
        sim.Entities.Count(e => e.Kind == EntityKind.Raindrop);

    private static HashSet<uint> ObserveRaindrops(Sim sim, double seconds, double dt = 0.01)
    {
        var seen = new HashSet<uint>();
        int steps = (int)(seconds / dt);
        for (int i = 0; i < steps; i++)
        {
            sim.Tick(dt, ReadOnlySpan<InputEvent>.Empty);
            foreach (Entity e in sim.Entities)
            {
                if (e.Kind == EntityKind.Raindrop) seen.Add(e.Seed);
            }
        }
        return seen;
    }

    [Fact]
    public void EntityKindRaindropIsPinned()
    {
        Assert.Equal(0, (int)EntityKind.None);
        Assert.Equal(1, (int)EntityKind.Tumbleweed);
        Assert.Equal(2, (int)EntityKind.Snowflake);
        Assert.Equal(3, (int)EntityKind.Sheep);
        Assert.Equal(4, (int)EntityKind.Cat);
        Assert.Equal(5, (int)EntityKind.Raindrop);
        Assert.Equal(6, (int)EntityKind.Bunny);
    }

    [Fact]
    public void RaindropPrngSaltIsDistinct()
    {
        ulong[] salts =
        {
            Constants.REGROW_PRNG_SALT,
            Constants.FLOWER_PRNG_SALT,
            Constants.MUSHROOM_PRNG_SALT,
            Constants.AMBIENT_GUST_PRNG_SALT,
            Constants.CACTUS_PRNG_SALT,
            Constants.TUMBLEWEED_PRNG_SALT,
            Constants.CRITTER_PRNG_SALT,
            Constants.SNOWFLAKE_PRNG_SALT,
            Constants.PINE_PRNG_SALT,
            Constants.RAINDROP_PRNG_SALT,
        };

        Assert.Equal(salts.Length, salts.Distinct().Count());
    }

    [Fact]
    public void RainConstantsArePinned()
    {
        Assert.Equal(0xD40F0A1DD40F0A1Dul, Constants.RAINDROP_PRNG_SALT);
        Assert.Equal(6.0, Constants.RAINDROP_EMIT_RATE_PER_1920DIP);
        Assert.Equal(4.0, Constants.RAINDROP_LENGTH_MIN);
        Assert.Equal(7.0, Constants.RAINDROP_LENGTH_MAX);
        Assert.Equal(0.9, Constants.RAINDROP_THICKNESS);
        Assert.Equal(240.0, Constants.RAINDROP_FALL_SPEED_MIN);
        Assert.Equal(360.0, Constants.RAINDROP_FALL_SPEED_MAX);
        Assert.Equal(-8.0, Constants.RAINDROP_DRIFT_MIN);
        Assert.Equal(8.0, Constants.RAINDROP_DRIFT_MAX);
        Assert.Equal(0x88B0C4D0u, Constants.RAINDROP_COLOR);
        Assert.Equal(0.3, Constants.RAINDROP_LIFETIME_PADDING_SEC);
    }

    [Fact]
    public void RainDoesNotEmitInDesertOrWinter()
    {
        var sim = BuildSim();

        sim.SetScene(Scene.Desert);
        sim.Tick(5.0, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0, CountRaindrops(sim));

        sim.SetScene(Scene.Winter);
        sim.Tick(5.0, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0, CountRaindrops(sim));
    }

    [Fact]
    public void RainEmitsInGrass()
    {
        var sim = BuildSim();

        HashSet<uint> seen = ObserveRaindrops(sim, 5.0);

        Assert.True(seen.Count >= 20);
    }

    [Fact]
    public void RainEmissionRateScalesWithMonitorWidth()
    {
        var wide = BuildSim(monitorWidth: 1920.0);
        var narrow = BuildSim(monitorWidth: 960.0);

        int wideCount = ObserveRaindrops(wide, 5.0).Count;
        int narrowCount = ObserveRaindrops(narrow, 5.0).Count;

        Assert.True(wideCount > narrowCount);
        Assert.True(wideCount >= narrowCount * 1.5);
        Assert.True(wideCount <= narrowCount * 2.8);
    }

    [Fact]
    public void RaindropFieldsAreWithinExpectedRanges()
    {
        var sim = BuildSim();

        sim.TickEntities(0.0);

        Assert.True(CountRaindrops(sim) >= 1);
        foreach (Entity e in sim.Entities.Where(e => e.Kind == EntityKind.Raindrop))
        {
            Assert.InRange(e.Size, Constants.RAINDROP_LENGTH_MIN, Constants.RAINDROP_LENGTH_MAX);
            Assert.Equal(-e.Size - 2.0, e.Y, 12);
            Assert.True(e.Y < 0.0);
            Assert.InRange(e.Vy, Constants.RAINDROP_FALL_SPEED_MIN, Constants.RAINDROP_FALL_SPEED_MAX);
            Assert.InRange(e.Vx, Constants.RAINDROP_DRIFT_MIN, Constants.RAINDROP_DRIFT_MAX);
            Assert.True(e.Lifetime > 0.0);
        }
    }

    [Fact]
    public void RaindropExpiresViaLifetime()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Clear();
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Raindrop,
            Lifetime = 0.5,
            Age = 0.4,
            Vy = Constants.RAINDROP_FALL_SPEED_MIN,
        });

        sim.TickEntities(0.2);

        Assert.Equal(0, CountRaindrops(sim));
    }

    [Fact]
    public void RaindropPrngDrawOrderIsBitIdentical()
    {
        var sim = BuildSim();
        var side = Prng.Init(Constants.CANONICAL_TEST_SEED ^ Constants.RAINDROP_PRNG_SALT);
        double lambda = Constants.RAINDROP_EMIT_RATE_PER_1920DIP * sim.MonitorWidth / 1920.0;
        double expectedNext = 0.0;

        for (int i = 0; i < 5; i++)
        {
            sim.GlobalTime = sim.NextRaindropSpawnTime;
            sim.TickEntities(0.0);

            Assert.Equal(i + 1, CountRaindrops(sim));
            Entity e = sim.Entities[^1];
            double expectedSize = side.Uniform(Constants.RAINDROP_LENGTH_MIN, Constants.RAINDROP_LENGTH_MAX);
            double expectedX = side.Uniform(-10.0, sim.MonitorWidth + 10.0);
            double expectedFallSpeed = side.Uniform(Constants.RAINDROP_FALL_SPEED_MIN,
                                                    Constants.RAINDROP_FALL_SPEED_MAX);
            double expectedVx = side.Uniform(Constants.RAINDROP_DRIFT_MIN, Constants.RAINDROP_DRIFT_MAX);
            uint expectedSeed = side.NextU32();
            expectedNext += side.Exponential(lambda);

            Assert.Equal(expectedSize, e.Size, 12);
            Assert.Equal(expectedX, e.X, 12);
            Assert.Equal(-expectedSize - 2.0, e.Y, 12);
            Assert.Equal(expectedVx, e.Vx, 12);
            Assert.Equal(expectedFallSpeed, e.Vy, 12);
            Assert.Equal(0.0, e.Rotation, 12);
            Assert.Equal(0.0, e.RotationSpeed, 12);
            Assert.Equal(expectedSeed, e.Seed);
            Assert.Equal((sim.WindowHeight + expectedSize) / expectedFallSpeed
                         + Constants.RAINDROP_LIFETIME_PADDING_SEC, e.Lifetime, 12);
            Assert.Equal(expectedNext, sim.NextRaindropSpawnTime, 12);
        }
    }

    [Fact]
    public void SceneSwitchFromGrassToDesertSoftFadesRain()
    {
        var sim = BuildSim();
        sim.TickEntities(0.0);

        HashSet<uint> before = sim.Entities
            .Where(e => e.Kind == EntityKind.Raindrop)
            .Select(e => e.Seed)
            .ToHashSet();
        Assert.NotEmpty(before);

        sim.SetScene(Scene.Desert);
        Assert.Equal(before.Count, CountRaindrops(sim));

        sim.Tick(0.2, ReadOnlySpan<InputEvent>.Empty);
        foreach (Entity e in sim.Entities.Where(e => e.Kind == EntityKind.Raindrop))
        {
            Assert.Contains(e.Seed, before);
        }

        sim.Tick(2.0, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0, CountRaindrops(sim));
    }
}
