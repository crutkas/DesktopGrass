using System.Diagnostics;
using System.Text.Json;

namespace DesktopGrass.Win2D;

/// <summary>
/// User-editable settings loaded from config.json. Distinct from state.json
/// (which the app owns and rewrites): config.json is written once with annotated
/// defaults if missing, then only ever read — never overwritten — so hand edits
/// are preserved. Loaded once at startup.
/// </summary>
public sealed record AppConfig(int Version, int TargetFps, double BladeDensity);

public static class Config
{
    public const int ConfigVersion = 1;
    public const int TargetFpsDefault = 30;
    public const int TargetFpsMin = 5;
    public const int TargetFpsMax = 144;
    public static double BladeDensityDefault => Constants.DEFAULT_DENSITY; // 2.8125
    public const double BladeDensityMin = 0.2;
    public const double BladeDensityMax = 5.0;

    // Annotated default config written verbatim on first run. The loader skips
    // JSONC comments, so users can keep these notes while editing.
    private const string DefaultConfigTemplate =
        "{\n" +
        "  // DesktopGrass settings. Edit and restart the app to apply.\n" +
        "  // This file is created once and never overwritten, so your edits stick.\n" +
        "  \"version\": 1,\n" +
        "\n" +
        "  // Animation frame rate. Lower = less CPU, choppier motion. Range 5-144.\n" +
        "  \"targetFps\": 30,\n" +
        "\n" +
        "  // Grass blade density. Lower = fewer blades (less CPU). Range 0.2-5.0.\n" +
        "  // Default 2.8125.\n" +
        "  \"bladeDensity\": 2.8125\n" +
        "}\n";

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string ConfigFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopGrass", "config.json");

    public static AppConfig Default => new(ConfigVersion, TargetFpsDefault, BladeDensityDefault);

    /// <summary>Loads the config from the default location, creating an annotated
    /// default file if missing. Always returns a valid, clamped config.</summary>
    public static AppConfig Load() => Load(ConfigFilePath);

    /// <summary>Path overload for tests. Reads/creates the config at the given path.</summary>
    public static AppConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                TryWriteDefault(path);
                return Default;
            }

            string json = File.ReadAllText(path);
            ConfigDto? dto = JsonSerializer.Deserialize<ConfigDto>(json, Options);
            if (dto is null)
            {
                Trace.WriteLine("DesktopGrass config: empty config.json; using defaults.");
                return Default;
            }

            return new AppConfig(
                dto.Version ?? ConfigVersion,
                Math.Clamp(dto.TargetFps ?? TargetFpsDefault, TargetFpsMin, TargetFpsMax),
                Math.Clamp(dto.BladeDensity ?? BladeDensityDefault, BladeDensityMin, BladeDensityMax));
        }
        catch (JsonException ex)
        {
            // Preserve the user's (broken) file; just fall back to defaults.
            Trace.WriteLine($"DesktopGrass config: malformed config.json; using defaults. {ex.Message}");
            return Default;
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"DesktopGrass config: unable to load config.json; using defaults. {ex.Message}");
            return Default;
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.WriteLine($"DesktopGrass config: unable to access config.json; using defaults. {ex.Message}");
            return Default;
        }
    }

    // Writes the annotated default, but only if no file exists yet. CreateNew is
    // atomic create-if-absent, so a concurrent writer or user file is never lost.
    private static void TryWriteDefault(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(DefaultConfigTemplate);
        }
        catch (IOException)
        {
            // File already exists (CreateNew) or transient IO: leave it untouched.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class ConfigDto
    {
        public int? Version { get; set; }
        public int? TargetFps { get; set; }
        public double? BladeDensity { get; set; }
    }
}
