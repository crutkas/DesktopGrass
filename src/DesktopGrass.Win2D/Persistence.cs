using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopGrass.Win2D;

public sealed record CutRecord(
    [property: JsonPropertyName("bladeIndex")] int BladeIndex,
    [property: JsonPropertyName("cutTime")] double CutTime);

public sealed record MonitorState(
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("left")] int Left,
    [property: JsonPropertyName("top")] int Top,
    [property: JsonPropertyName("cuts")] List<CutRecord> Cuts);

public sealed record AppState(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("scene")] Scene Scene,
    [property: JsonPropertyName("critter")] CritterKind Critter,
    [property: JsonPropertyName("critterCount")] int CritterCountOverride,
    [property: JsonPropertyName("autoStart")] bool AutoStart,
    [property: JsonPropertyName("monitors")] List<MonitorState> Monitors);

public static class Persistence
{
    private const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string? s_stateFilePathForTest;

    public static string StateFilePath => s_stateFilePathForTest
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopGrass", "state.json");

    public static void SetStateFilePathForTest(string? path) => s_stateFilePathForTest = string.IsNullOrWhiteSpace(path) ? null : path;

    public static AppState? Load()
    {
        try
        {
            string path = StateFilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            AppStateDto? dto = JsonSerializer.Deserialize<AppStateDto>(json, Options);
            if (dto is null)
            {
                return null;
            }

            if (dto.Version is not (1 or CurrentVersion))
            {
                Trace.WriteLine($"DesktopGrass persistence: unsupported state.json version {dto.Version}; starting fresh.");
                return null;
            }

            List<MonitorState> monitors = [];
            if (dto.Monitors is not null)
            {
                foreach ((string key, MonitorDto? monitorDto) in dto.Monitors)
                {
                    if (!TryParseMonitorKey(key, out int width, out int height, out int left, out int top))
                    {
                        continue;
                    }

                    monitors.Add(new MonitorState(width, height, left, top, monitorDto?.Cuts ?? []));
                }
            }

            int critterCount = dto.CritterCount is >= 0 and <= Constants.PET_COUNT_MAX_PER_MONITOR
                ? dto.CritterCount
                : 0;

            return new AppState(CurrentVersion, dto.Scene, dto.Critter, critterCount, dto.AutoStart, monitors);
        }
        catch (JsonException ex)
        {
            Trace.WriteLine($"DesktopGrass persistence: malformed state.json; starting fresh. {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"DesktopGrass persistence: unable to load state.json; starting fresh. {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.WriteLine($"DesktopGrass persistence: unable to access state.json; starting fresh. {ex.Message}");
            return null;
        }
    }

    public static void Save(AppState state)
    {
        string path = StateFilePath;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var dto = new AppStateDto
        {
            Version = CurrentVersion,
            SavedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            Scene = state.Scene,
            Critter = state.Critter,
            CritterCount = state.CritterCountOverride,
            AutoStart = state.AutoStart,
            Monitors = state.Monitors.ToDictionary(
                monitor => MonitorKey(monitor.Width, monitor.Height, monitor.Left, monitor.Top),
                monitor => new MonitorDto { Cuts = monitor.Cuts })
        };

        string json = JsonSerializer.Serialize(dto, Options) + Environment.NewLine;
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    public static string MonitorKey(int width, int height, int left, int top) => $"{width}x{height}@{left},{top}";

    public static string MonitorKey(MonitorState monitor) => MonitorKey(monitor.Width, monitor.Height, monitor.Left, monitor.Top);

    private static bool TryParseMonitorKey(string key, out int width, out int height, out int left, out int top)
    {
        width = height = left = top = 0;

        int xIndex = key.IndexOf('x', StringComparison.Ordinal);
        int atIndex = key.IndexOf('@', StringComparison.Ordinal);
        int commaIndex = key.IndexOf(',', StringComparison.Ordinal);
        if (xIndex <= 0 || atIndex <= xIndex + 1 || commaIndex <= atIndex + 1 || commaIndex >= key.Length - 1)
        {
            return false;
        }

        return int.TryParse(key.AsSpan(0, xIndex), out width)
            && int.TryParse(key.AsSpan(xIndex + 1, atIndex - xIndex - 1), out height)
            && int.TryParse(key.AsSpan(atIndex + 1, commaIndex - atIndex - 1), out left)
            && int.TryParse(key.AsSpan(commaIndex + 1), out top);
    }

    private sealed class AppStateDto
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("savedAt")]
        public string SavedAt { get; set; } = string.Empty;

        [JsonPropertyName("scene")]
        public Scene Scene { get; set; } = Scene.Grass;

        [JsonPropertyName("critter")]
        public CritterKind Critter { get; set; } = CritterKind.None;

        [JsonPropertyName("critterCount")]
        public int CritterCount { get; set; }

        [JsonPropertyName("autoStart")]
        public bool AutoStart { get; set; }

        [JsonPropertyName("monitors")]
        public Dictionary<string, MonitorDto>? Monitors { get; set; } = [];
    }

    private sealed class MonitorDto
    {
        [JsonPropertyName("cuts")]
        public List<CutRecord> Cuts { get; set; } = [];
    }
}
