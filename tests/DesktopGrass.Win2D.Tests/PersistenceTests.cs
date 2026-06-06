using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests;

[Collection("Persistence state")]
public sealed class PersistenceTests
{
    private static string UseStatePath(string name)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), ".copilot-scratch", "win2d-persistence-tests", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "state.json");
        Persistence.SetStateFilePathForTest(path);
        return path;
    }

    private static AppState StateWithCuts()
    {
        var monitors = new List<MonitorState>();
        for (int i = 0; i < 3; i++)
        {
            var cuts = new List<CutRecord>();
            for (int j = 0; j < 2 + i; j++)
            {
                cuts.Add(new CutRecord(i * 100 + j, -5.0 - i - j * 0.5));
            }

            monitors.Add(new MonitorState(
                Width: 1920 + i * 320,
                Height: 1080 + i * 120,
                Left: i * 1920,
                Top: i == 2 ? -120 : 0,
                cuts));
        }

        return new AppState(2, Scene.Winter, CritterKind.Cat, 4, AutoStart: true, monitors);
    }

    private static void AssertStateEqual(AppState expected, AppState actual)
    {
        Assert.Equal(2, actual.Version);
        Assert.Equal(expected.Scene, actual.Scene);
        Assert.Equal(expected.Critter, actual.Critter);
        Assert.Equal(expected.CritterCountOverride, actual.CritterCountOverride);
        Assert.Equal(expected.AutoStart, actual.AutoStart);
        Assert.Equal(expected.Monitors.Count, actual.Monitors.Count);

        for (int i = 0; i < expected.Monitors.Count; i++)
        {
            MonitorState e = expected.Monitors[i];
            MonitorState a = actual.Monitors[i];
            Assert.Equal(e.Width, a.Width);
            Assert.Equal(e.Height, a.Height);
            Assert.Equal(e.Left, a.Left);
            Assert.Equal(e.Top, a.Top);
            Assert.Equal(e.Cuts.Count, a.Cuts.Count);
            for (int j = 0; j < e.Cuts.Count; j++)
            {
                Assert.Equal(e.Cuts[j].BladeIndex, a.Cuts[j].BladeIndex);
                Assert.Equal(e.Cuts[j].CutTime, a.Cuts[j].CutTime, 9);
            }
        }
    }

    private static Blade MakeBlade(double regrowDelay, double regrowDuration)
    {
        Blade b = default;
        b.BaseX = 100.0;
        b.Height = 20.0;
        b.Thickness = 1.0;
        b.CutHeight = 1.0;
        b.CutAnimStart = -1.0;
        b.CutInitialHeight = 1.0;
        b.RegrowDelay = regrowDelay;
        b.RegrowDuration = regrowDuration;
        b.RegrowStart = -1.0;
        return b;
    }

    [Fact]
    public void RoundTripEmptyState()
    {
        UseStatePath(nameof(RoundTripEmptyState));
        var expected = new AppState(2, Scene.Grass, CritterKind.None, 0, AutoStart: false, []);

        Persistence.Save(expected);
        AppState? actual = Persistence.Load();

        Assert.NotNull(actual);
        AssertStateEqual(expected, actual);
    }

    [Fact]
    public void RoundTripWithCuts()
    {
        UseStatePath(nameof(RoundTripWithCuts));
        AppState expected = StateWithCuts();

        Persistence.Save(expected);
        AppState? actual = Persistence.Load();

        Assert.NotNull(actual);
        AssertStateEqual(expected, actual);
    }

    [Theory]
    [InlineData(Scene.Grass)]
    [InlineData(Scene.Desert)]
    [InlineData(Scene.Winter)]
    [InlineData(Scene.Autumn)]
    [InlineData(Scene.Ocean)]
    public void RoundTripsEveryScene(Scene scene)
    {
        UseStatePath(nameof(RoundTripsEveryScene));
        var expected = new AppState(2, scene, CritterKind.None, 0, AutoStart: false, []);

        Persistence.Save(expected);
        AppState? actual = Persistence.Load();

        Assert.NotNull(actual);
        Assert.Equal(scene, actual.Scene);
    }

    [Fact]
    public void VersionMismatchReturnsNull()
    {
        string path = UseStatePath(nameof(VersionMismatchReturnsNull));
        File.WriteAllText(path, "{ \"version\": 999, \"monitors\": {} }");

        Assert.Null(Persistence.Load());
    }

    [Fact]
    public void MissingFileReturnsNull()
    {
        UseStatePath(nameof(MissingFileReturnsNull));

        Assert.Null(Persistence.Load());
    }

    [Fact]
    public void MalformedJsonReturnsNull()
    {
        string path = UseStatePath(nameof(MalformedJsonReturnsNull));
        File.WriteAllText(path, "not-json");

        Assert.Null(Persistence.Load());
    }

    [Fact]
    public void AtomicWriteLeavesFinalFileAndRemovesTmp()
    {
        string path = UseStatePath(nameof(AtomicWriteLeavesFinalFileAndRemovesTmp));

        Persistence.Save(new AppState(2, Scene.Grass, CritterKind.None, 0, AutoStart: false, []));

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void MonitorKeyFormatRoundTrips()
    {
        string path = UseStatePath(nameof(MonitorKeyFormatRoundTrips));
        var state = new AppState(2, Scene.Grass, CritterKind.None, 0, AutoStart: false,
        [
            new MonitorState(1920, 1080, 0, 0, [])
        ]);

        Persistence.Save(state);
        string json = File.ReadAllText(path);
        AppState? loaded = Persistence.Load();

        Assert.Contains("\"1920x1080@0,0\"", json);
        Assert.NotNull(loaded);
        Assert.Single(loaded.Monitors);
        Assert.Equal("1920x1080@0,0", Persistence.MonitorKey(loaded.Monitors[0]));
    }

    [Fact]
    public void CutTimestampsShiftForFreshSimLoad()
    {
        UseStatePath(nameof(CutTimestampsShiftForFreshSimLoad));
        var running = new Sim
        {
            GlobalTime = 100.0,
            Blades = [MakeBlade(30.0, 10.0)],
            GroundY = 110.0,
            WindowHeight = 110.0,
        };
        running.Blades[0].CutHeight = 0.0;
        running.Blades[0].RegrowStart = 80.0 + Constants.CUT_DURATION_SEC + running.Blades[0].RegrowDelay;

        List<CutRecord> cuts = running.GetCuts();
        Assert.Single(cuts);
        Assert.Equal(-20.0, cuts[0].CutTime, 9);

        var state = new AppState(2, Scene.Grass, CritterKind.None, 0, AutoStart: false,
        [
            new MonitorState(1920, 1080, 0, 0, cuts)
        ]);
        Persistence.Save(state);

        AppState? loaded = Persistence.Load();
        Assert.NotNull(loaded);
        Assert.True(loaded.Monitors[0].Cuts[0].CutTime < 0.0);

        var fresh = new Sim
        {
            GlobalTime = 0.0,
            Blades = [MakeBlade(30.0, 10.0)],
            GroundY = 110.0,
            WindowHeight = 110.0,
        };
        fresh.ApplyCuts(loaded.Monitors[0].Cuts);

        Assert.Equal(0.0, fresh.Blades[0].CutHeight, 9);
        Assert.Equal(10.0 + Constants.CUT_DURATION_SEC, fresh.Blades[0].RegrowStart, 9);
    }

    [Fact]
    public void UnmatchedMonitorCutsAreSkipped()
    {
        UseStatePath(nameof(UnmatchedMonitorCutsAreSkipped));
        var state = new AppState(2, Scene.Grass, CritterKind.None, 0, AutoStart: false,
        [
            new MonitorState(9999, 9999, 99, 99, [new CutRecord(0, -20.0)])
        ]);
        Persistence.Save(state);

        AppState? loaded = Persistence.Load();
        Assert.NotNull(loaded);

        var sim = new Sim
        {
            Blades = [MakeBlade(30.0, 10.0)],
            GroundY = 110.0,
            WindowHeight = 110.0,
        };
        MonitorState? match = loaded.Monitors.Find(m =>
            m.Width == 1920 && m.Height == 1080 && m.Left == 0 && m.Top == 0);
        if (match is not null)
        {
            sim.ApplyCuts(match.Cuts);
        }

        Assert.Empty(sim.GetCuts());
    }

    [Fact]
    public void JsonFileIsHumanReadable()
    {
        string path = UseStatePath(nameof(JsonFileIsHumanReadable));

        Persistence.Save(StateWithCuts());
        string json = File.ReadAllText(path);

        Assert.Contains("\n", json);
        Assert.Contains("  \"version\"", json);
        Assert.Contains("    ", json);
    }
}
