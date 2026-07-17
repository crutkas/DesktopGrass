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

    private static AppState StateWithMonitorMetadata()
    {
        var monitors = new List<MonitorState>();
        for (int i = 0; i < 3; i++)
        {
            monitors.Add(new MonitorState(
                Width: 1920 + i * 320,
                Height: 1080 + i * 120,
                Left: i * 1920,
                Top: i == 2 ? -120 : 0));
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
        }
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
    public void RoundTripWithMonitorMetadata()
    {
        UseStatePath(nameof(RoundTripWithMonitorMetadata));
        AppState expected = StateWithMonitorMetadata();

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
    public void DropsLegacyInteractionData()
    {
        string path = UseStatePath(nameof(DropsLegacyInteractionData));
        File.WriteAllText(path,
            """
            {
              "version": 2,
              "scene": "Grass",
              "cursor": { "x": 123, "y": 456 },
              "clickHistory": [{ "x": 123, "y": 456 }],
              "monitors": {
                "1920x1080@0,0": {
                  "cuts": [{ "bladeIndex": 7, "cutTime": -4.0 }]
                }
              }
            }
            """);

        AppState? loaded = Persistence.Load();
        Assert.NotNull(loaded);
        Persistence.Save(loaded);

        string saved = File.ReadAllText(path);
        Assert.DoesNotContain("\"cursor\"", saved);
        Assert.DoesNotContain("\"clickHistory\"", saved);
        Assert.DoesNotContain("\"cuts\"", saved);
        Assert.DoesNotContain("\"cutTime\"", saved);
        Assert.DoesNotContain("\"bladeIndex\"", saved);
    }

    [Fact]
    public void MonitorKeyFormatRoundTrips()
    {
        string path = UseStatePath(nameof(MonitorKeyFormatRoundTrips));
        var state = new AppState(2, Scene.Grass, CritterKind.None, 0, AutoStart: false,
        [
            new MonitorState(1920, 1080, 0, 0)
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
    public void JsonFileIsHumanReadable()
    {
        string path = UseStatePath(nameof(JsonFileIsHumanReadable));

        Persistence.Save(StateWithMonitorMetadata());
        string json = File.ReadAllText(path);

        Assert.Contains("\n", json);
        Assert.Contains("  \"version\"", json);
        Assert.Contains("    ", json);
    }
}
