using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests.SimTests;

[Collection("Persistence state")]
public sealed class SnowAccumulationTests
{
    private static Sim BuildSim(Scene scene = Scene.Winter)
    {
        var sim = new Sim
        {
            Blades = Sim.GenerateBlades(Constants.CANONICAL_TEST_SEED, 1920.0, 1.0),
            WindowHeight = Constants.STRIP_HEIGHT + Constants.HEADROOM,
            GroundY = Constants.STRIP_HEIGHT + Constants.HEADROOM,
        };
        sim.ResetAmbientGusts(Constants.CANONICAL_TEST_SEED, 1920.0);
        sim.ResetEntities(Constants.CANONICAL_TEST_SEED);
        sim.SetScene(scene);
        return sim;
    }

    private static string UseStatePath(string name)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), ".copilot-scratch", "win2d-snow-accumulation-tests", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        Persistence.SetStateFilePathForTest(path);
        return path;
    }

    [Fact]
    public void SnowAccumulationConstantsArePinned()
    {
        Assert.Equal(0.012, Constants.SNOW_ACCUMULATION_RATE, 12);
        Assert.Equal(30.0, Constants.SNOW_DEPTH_MAX, 12);
        Assert.Equal(0.3, Constants.SNOW_DEPTH_MIN_RENDER, 12);
        Assert.Equal(0xFFFFFFFFu, Constants.SNOW_LAYER_COLOR_TOP);
        Assert.Equal(0xFFE8E8F0u, Constants.SNOW_LAYER_COLOR_BOTTOM);
        Assert.Equal(0xFFFFFFFFu, Constants.SNOW_LAYER_HIGHLIGHT);
        Assert.Equal(2.5, Constants.SNOW_TOP_UNDULATION_AMP, 12);
        Assert.Equal(90.0, Constants.SNOW_TOP_UNDULATION_WAVELENGTH, 12);
        Assert.Equal(0x5E0A1ul, Constants.SNOW_TOP_UNDULATION_PHASE_SALT);
    }

    [Fact]
    public void FreshWinterSimStartsWithNoSnowAccumulation()
    {
        Sim sim = BuildSim(Scene.Winter);
        Assert.Equal(0.0, sim.SnowDepth, 12);
    }

    [Fact]
    public void WinterSnowAccumulationFollowsPinnedRate()
    {
        Sim sim = BuildSim(Scene.Winter);
        sim.Tick(100.0, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(1.2, sim.SnowDepth, 9);
    }

    [Fact]
    public void WinterSnowAccumulationClampsAtMaxDepth()
    {
        Sim sim = BuildSim(Scene.Winter);
        double enough = Constants.SNOW_DEPTH_MAX / Constants.SNOW_ACCUMULATION_RATE + 1000.0;
        sim.Tick(enough, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(Constants.SNOW_DEPTH_MAX, sim.SnowDepth, 9);
    }

    [Fact]
    public void SwitchingAwayFromWinterResetsSnowAccumulation()
    {
        Sim sim = BuildSim(Scene.Winter);
        sim.SetSnowDepth(15.0);

        sim.SetScene(Scene.Grass);
        Assert.Equal(0.0, sim.SnowDepth, 12);

        sim.SetScene(Scene.Winter);
        Assert.Equal(0.0, sim.SnowDepth, 12);
    }

    [Fact]
    public void DesertSceneNeverAccumulatesSnow()
    {
        Sim sim = BuildSim(Scene.Desert);
        sim.Tick(10000.0, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.0, sim.SnowDepth, 12);
    }

    [Fact]
    public void GrassSceneNeverAccumulatesSnow()
    {
        Sim sim = BuildSim(Scene.Grass);
        sim.Tick(10000.0, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(0.0, sim.SnowDepth, 12);
    }

    [Fact]
    public void SnowAccumulationPersistsThroughV2StateRoundTrip()
    {
        UseStatePath(nameof(SnowAccumulationPersistsThroughV2StateRoundTrip));
        Sim running = BuildSim(Scene.Winter);
        running.SetSnowDepth(18.0);

        var state = new AppState(2, Scene.Winter, CritterKind.None, 0, AutoStart: true,
        [
            new MonitorState(1920, 1080, 0, 0, [], running.SnowDepth)
        ]);
        Persistence.Save(state);

        AppState? loaded = Persistence.Load();
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Version);
        MonitorState monitor = Assert.Single(loaded.Monitors);

        Sim fresh = BuildSim(Scene.Winter);
        fresh.SetSnowDepth(monitor.SnowDepth);
        Assert.Equal(18.0, fresh.SnowDepth, 9);
    }

    [Fact]
    public void V1StateFilesLoadWithZeroSnowAccumulation()
    {
        string path = UseStatePath(nameof(V1StateFilesLoadWithZeroSnowAccumulation));
        File.WriteAllText(path,
            """
            {
              "version": 1,
              "scene": "Winter",
              "autoStart": true,
              "monitors": {
                "1920x1080@0,0": { "cuts": [] }
              }
            }
            """);

        AppState? loaded = Persistence.Load();
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Version);
        MonitorState monitor = Assert.Single(loaded.Monitors);
        Assert.Equal(0.0, monitor.SnowDepth, 12);
    }

    [Fact]
    public void SnowTopEdgeUndulationRemainsBoundedAboveGround()
    {
        Sim sim = BuildSim(Scene.Winter);
        sim.SetSnowDepth(12.0);

        for (double x = 0.0; x <= 1920.0; x += 17.0)
        {
            double top = sim.SnowTopYAt(x);
            Assert.True(top >= sim.WindowHeight - sim.SnowDepth - Constants.SNOW_TOP_UNDULATION_AMP - 1e-9);
            Assert.True(top <= sim.WindowHeight - sim.SnowDepth + Constants.SNOW_TOP_UNDULATION_AMP + 1e-9);
            Assert.True(top <= sim.WindowHeight + 1e-9);
        }
    }

    [Fact]
    public void SnowflakesDespawnWhenTheyTouchAccumulatedSnow()
    {
        Sim sim = BuildSim(Scene.Winter);
        sim.SetSnowDepth(10.0);
        const double x = 100.0;
        sim.Entities.Clear();
        sim.Entities.Add(new Entity
        {
            Kind = EntityKind.Snowflake,
            X = x,
            Y = sim.SnowTopYAt(x),
            Lifetime = 100.0,
        });

        sim.TickEntities(0.0);
        Assert.Empty(sim.Entities);
    }

    [Fact]
    public void SnowDepthExposesTreeBaseBurialOffset()
    {
        Sim sim = BuildSim(Scene.Winter);
        sim.SetSnowDepth(15.0);
        Assert.Equal(12.5, sim.SnowTreeBaseYOffset, 12);

        sim.SetSnowDepth(1.0);
        Assert.Equal(0.0, sim.SnowTreeBaseYOffset, 12);
    }

    [Fact]
    public void SnowDepthIdentitySnapshotIsDeterministicAcrossImplementations()
    {
        Sim sim = BuildSim(Scene.Winter);
        sim.Tick(1234.5, ReadOnlySpan<InputEvent>.Empty);
        Assert.Equal(14.814, sim.SnowDepth, 9);
    }
}
