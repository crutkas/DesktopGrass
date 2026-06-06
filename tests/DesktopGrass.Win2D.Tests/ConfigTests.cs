using DesktopGrass.Win2D;
using Xunit;

namespace DesktopGrass.Win2D.Tests;

public sealed class ConfigTests
{
    private static string FreshConfigPath(string name)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), ".copilot-scratch", "win2d-config-tests", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    [Fact]
    public void MissingFile_YieldsDefaults_AndWritesTemplate()
    {
        string path = FreshConfigPath("missing");
        Assert.False(File.Exists(path));

        AppConfig cfg = Config.Load(path);

        Assert.Equal(Config.TargetFpsDefault, cfg.TargetFps);
        Assert.Equal(Config.BladeDensityDefault, cfg.BladeDensity, 6);

        // A default file should now exist and re-read identically (it is JSONC).
        Assert.True(File.Exists(path));
        AppConfig reread = Config.Load(path);
        Assert.Equal(Config.TargetFpsDefault, reread.TargetFps);
        Assert.Equal(Config.BladeDensityDefault, reread.BladeDensity, 6);
    }

    [Fact]
    public void ValidValues_AreParsed()
    {
        string path = FreshConfigPath("valid");
        File.WriteAllText(path, "{ \"version\": 1, \"targetFps\": 60, \"bladeDensity\": 1.5 }");

        AppConfig cfg = Config.Load(path);
        Assert.Equal(60, cfg.TargetFps);
        Assert.Equal(1.5, cfg.BladeDensity, 6);
    }

    [Fact]
    public void OutOfRangeValues_AreClamped()
    {
        string path = FreshConfigPath("clamp");

        File.WriteAllText(path, "{ \"targetFps\": 1000, \"bladeDensity\": 99.0 }");
        AppConfig high = Config.Load(path);
        Assert.Equal(Config.TargetFpsMax, high.TargetFps);
        Assert.Equal(Config.BladeDensityMax, high.BladeDensity, 6);

        File.WriteAllText(path, "{ \"targetFps\": 0, \"bladeDensity\": 0.0 }");
        AppConfig low = Config.Load(path);
        Assert.Equal(Config.TargetFpsMin, low.TargetFps);
        Assert.Equal(Config.BladeDensityMin, low.BladeDensity, 6);
    }

    [Fact]
    public void JsoncCommentsAndTrailingCommas_AreTolerated()
    {
        string path = FreshConfigPath("jsonc");
        File.WriteAllText(path,
            "{\n" +
            "  // a comment\n" +
            "  \"targetFps\": 24, /* inline */\n" +
            "  \"bladeDensity\": 2.0,\n" +
            "}\n");

        AppConfig cfg = Config.Load(path);
        Assert.Equal(24, cfg.TargetFps);
        Assert.Equal(2.0, cfg.BladeDensity, 6);
    }

    [Fact]
    public void MalformedFile_FallsBackToDefaults_AndIsPreserved()
    {
        string path = FreshConfigPath("malformed");
        File.WriteAllText(path, "{ not valid json ");

        AppConfig cfg = Config.Load(path);
        Assert.Equal(Config.TargetFpsDefault, cfg.TargetFps);
        Assert.Equal(Config.BladeDensityDefault, cfg.BladeDensity, 6);

        // The user's broken file must be left untouched for them to fix.
        Assert.Equal("{ not valid json ", File.ReadAllText(path));
    }

    [Fact]
    public void MissingKeys_FallBackToPerKeyDefaults()
    {
        string path = FreshConfigPath("partial");
        File.WriteAllText(path, "{ \"targetFps\": 45 }");

        AppConfig cfg = Config.Load(path);
        Assert.Equal(45, cfg.TargetFps);
        Assert.Equal(Config.BladeDensityDefault, cfg.BladeDensity, 6);
        Assert.Equal(Config.SwaySpeedDefault, cfg.SwaySpeed, 6);
        Assert.Equal(Config.SwayAmplitudeDefault, cfg.SwayAmplitude, 6);
    }

    [Fact]
    public void Keys_AreMatchedCaseInsensitively()
    {
        string path = FreshConfigPath("case-insensitive");
        File.WriteAllText(path,
            "{ \"TargetFps\": 60, \"BLADEDENSITY\": 1.5, " +
            "\"SwaySpeed\": 0.5, \"swayamplitude\": 2.0 }");

        AppConfig cfg = Config.Load(path);
        Assert.Equal(60, cfg.TargetFps);
        Assert.Equal(1.5, cfg.BladeDensity, 6);
        Assert.Equal(0.5, cfg.SwaySpeed, 6);
        Assert.Equal(2.0, cfg.SwayAmplitude, 6);
    }

    [Fact]
    public void SwayKnobs_Parse_Clamp_AndRejectNonFinite()
    {
        string path = FreshConfigPath("sway");

        // Defaults when absent.
        File.WriteAllText(path, "{ }");
        AppConfig cfg = Config.Load(path);
        Assert.Equal(Config.SwaySpeedDefault, cfg.SwaySpeed, 6);
        Assert.Equal(Config.SwayAmplitudeDefault, cfg.SwayAmplitude, 6);

        // Valid values parsed.
        File.WriteAllText(path, "{ \"swaySpeed\": 0.5, \"swayAmplitude\": 2.0 }");
        cfg = Config.Load(path);
        Assert.Equal(0.5, cfg.SwaySpeed, 6);
        Assert.Equal(2.0, cfg.SwayAmplitude, 6);

        // Out-of-range clamped to bounds.
        File.WriteAllText(path, "{ \"swaySpeed\": 99.0, \"swayAmplitude\": -5.0 }");
        cfg = Config.Load(path);
        Assert.Equal(Config.SwaySpeedMax, cfg.SwaySpeed, 6);
        Assert.Equal(Config.SwayAmplitudeMin, cfg.SwayAmplitude, 6);

        // Non-finite (inf from overflow) falls back to default, never poisons the sim.
        File.WriteAllText(path, "{ \"swaySpeed\": 1e999, \"swayAmplitude\": 1e999 }");
        cfg = Config.Load(path);
        Assert.Equal(Config.SwaySpeedDefault, cfg.SwaySpeed, 6);
        Assert.Equal(Config.SwayAmplitudeDefault, cfg.SwayAmplitude, 6);
    }
}
