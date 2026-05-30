// EntitySkeletonTests.cs - §13.2 roaming-entity subsystem.

using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class EntitySkeletonTests
{
    private const double Monitor1920 = 1920.0;

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

    [Fact]
    public void EntityKindHasSpecLockedDiscriminants()
    {
        Assert.Equal(0, (int)EntityKind.None);
        Assert.Equal(1, (int)EntityKind.Tumbleweed);
        Assert.Equal(2, (int)EntityKind.Snowflake);
        Assert.Equal(3, (int)EntityKind.Sheep);
        Assert.Equal(4, (int)EntityKind.Cat);
        Assert.Equal(5, (int)EntityKind.Raindrop);
        Assert.Equal(64, Constants.MAX_ENTITIES_PER_MONITOR);
    }

    [Fact]
    public void SimDefaultsEntitiesToEmptyWithReservedCapacity()
    {
        var sim = new Sim();
        Assert.Empty(sim.Entities);
        Assert.True(sim.Entities.Capacity >= Constants.MAX_ENTITIES_PER_MONITOR);
    }

    [Fact]
    public void SetSceneClearsEntities()
    {
        var sim = BuildSim();
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Tumbleweed,
            X = 100.0,
            Size = 10.0,
        });
        Assert.Single(sim.Entities);

        sim.SetScene(Scene.Grass);
        Assert.Empty(sim.Entities);
    }

    [Fact]
    public void TickEntitiesIsNoOpOnEmptyOutsideGrass()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        ulong prngBefore = sim.AmbientPrng.State;
        int bladesBefore = sim.Blades.Length;

        sim.TickEntities(0.016);
        sim.TickEntities(0.5);

        Assert.Empty(sim.Entities);
        Assert.Equal(bladesBefore, sim.Blades.Length);
        Assert.Equal(prngBefore, sim.AmbientPrng.State);
    }

    [Fact]
    public void TickEntitiesAdvancesPopulatedEntity()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Add(new Entity
        {
            Kind          = EntityKind.Tumbleweed,
            X             = 100.0,
            Y             = 50.0,
            Vx            = 50.0,
            Vy            = 0.0,
            Size          = 10.0,
            Rotation      = 0.5,
            RotationSpeed = 1.0,
            Age           = 0.0,
            Lifetime      = -1.0,
            Seed          = 0xDEADBEEF,
        });

        sim.TickEntities(0.5);

        Assert.Single(sim.Entities);
        var after = sim.Entities[0];
        Assert.Equal(100.0 + 50.0 * 0.5, after.X);
        Assert.Equal(50.0, after.Y);
        Assert.Equal(0.5 + 1.0 * 0.5, after.Rotation);
        Assert.Equal(0.5, after.Age);
        Assert.Equal(EntityKind.Tumbleweed, after.Kind);
    }

    [Fact]
    public void TickCallsTickEntitiesWiringCheck()
    {
        var sim = BuildSim();
        sim.CurrentScene = Scene.Desert;
        sim.Entities.Add(new Entity
        {
            Kind     = EntityKind.Snowflake,
            X        = 0.0,
            Y        = 0.0,
            Vx       = 10.0,
            Vy       = 20.0,
            Size     = 2.0,
            Age      = 0.0,
            Lifetime = 100.0,
        });

        sim.Tick(0.1, System.ReadOnlySpan<InputEvent>.Empty);
        Assert.Single(sim.Entities);
        Assert.Equal(1.0, sim.Entities[0].X, 9);
        Assert.Equal(2.0, sim.Entities[0].Y, 9);
    }
}
