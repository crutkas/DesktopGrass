// OceanTests.cs - Ocean scene (coral blade variant + bubble emitter +
// fish swimmers). Smoke tests that match the conventions used by
// PineTests / AutumnTests.

using System.Linq;
using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

public class OceanTests
{
    private const double Monitor1920 = 1920.0;

    private static Sim BuildSim() => BuildSim(Monitor1920);

    private static Sim BuildSim(double width)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, width, Constants.DEFAULT_DENSITY),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(Constants.CANONICAL_TEST_SEED, width);
        sim.ResetEntities(Constants.CANONICAL_TEST_SEED);
        return sim;
    }

    [Fact]
    public void SetSceneOcean_GeneratesAtLeastOneCoral_AndKeepsValuesInRange()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Ocean);

        var corals = sim.Blades.Where(b => b.IsCoral).ToArray();
        Assert.NotEmpty(corals);

        foreach (var b in corals)
        {
            Assert.False(b.IsPine);
            Assert.False(b.IsCactus);
            Assert.False(b.IsMaple);
            Assert.False(b.IsFlower);
            Assert.False(b.IsMushroom);
            Assert.InRange(b.CoralHeight, Constants.CORAL_HEIGHT_MIN, Constants.CORAL_HEIGHT_MAX);
            Assert.InRange(b.CoralWidth,  Constants.CORAL_WIDTH_MIN,  Constants.CORAL_WIDTH_MAX);
            Assert.InRange((int)b.CoralType,     0, Constants.CORAL_TYPE_COUNT  - 1);
            Assert.InRange((int)b.CoralColorIdx, 0, Constants.CORAL_COLOR_COUNT - 1);
        }
    }

    [Fact]
    public void SetSceneOcean_SpawnsInitialFish_AtOrAboveTargetMinimum()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Ocean);

        int fishCount = sim.Entities.Count(e => e.Kind == EntityKind.Fish);
        Assert.True(fishCount >= Constants.FISH_COUNT_MIN, $"Expected at least {Constants.FISH_COUNT_MIN} fish, got {fishCount}");
        Assert.True(fishCount <= Constants.FISH_COUNT_MAX, $"Expected at most {Constants.FISH_COUNT_MAX} fish, got {fishCount}");
    }

    [Theory]
    [InlineData(1920.0, 2)] // scaled 2.5 -> round-half-to-even -> 2
    [InlineData(3456.0, 4)] // scaled 4.5 -> round-half-to-even -> 4
    public void SetSceneOcean_FishCountRoundsHalfToEven(double width, int expected)
    {
        var sim = BuildSim(width);
        sim.SetScene(Scene.Ocean);

        int fishCount = sim.Entities.Count(e => e.Kind == EntityKind.Fish);
        Assert.Equal(expected, fishCount);
    }

    [Fact]
    public void TickEntities_EmitsBubbles_OverTime()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Ocean);

        // Advance ~10 seconds of sim time.
        const double dt = 1.0 / 60.0;
        for (int i = 0; i < 600; i++)
        {
            sim.GlobalTime += dt;
            sim.TickEntities(dt);
        }

        int bubbleCount = sim.Entities.Count(e => e.Kind == EntityKind.Bubble);
        Assert.True(bubbleCount > 0, "Expected at least one bubble after 10s of simulated Ocean ticks.");
    }

    [Fact]
    public void SwitchingFromOceanToGrass_WipesBubblesAndFish()
    {
        var sim = BuildSim();
        sim.SetScene(Scene.Ocean);

        const double dt = 1.0 / 60.0;
        for (int i = 0; i < 120; i++)
        {
            sim.GlobalTime += dt;
            sim.TickEntities(dt);
        }
        Assert.Contains(sim.Entities, e => e.Kind == EntityKind.Fish);

        sim.SetScene(Scene.Grass);

        Assert.DoesNotContain(sim.Entities, e => e.Kind == EntityKind.Bubble);
        Assert.DoesNotContain(sim.Entities, e => e.Kind == EntityKind.Fish);
        Assert.DoesNotContain(sim.Blades, b => b.IsCoral);
    }
}
