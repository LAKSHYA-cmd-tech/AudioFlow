using System.Text.Json;

namespace AudioFlow.Audio;

/// <summary>
/// Persistent application settings and listening state.
/// Automatically saves and restores audio settings between sessions.
/// </summary>
public sealed record UserSettings
{
    public string ActivePresetName { get; init; } = "Harman Target Reference";
    public float PreampDb { get; init; } = 0f;
    public bool IsBypassed { get; init; } = false;
    public BufferLatencyMode LatencyMode { get; init; } = BufferLatencyMode.Balanced;
    public SmartMacros Macros { get; init; } = SmartMacros.Default;
    public EqBand[] Bands { get; init; } = EqBand.CreateDefaults();
    public bool SpatialEnabled { get; init; } = false;
    public float SpatialDepth { get; init; } = 60f;
    public float SpatialAmbience { get; init; } = 45f;
    public float SpatialMix { get; init; } = 80f;
    public float[] NodePans { get; init; } = [0, 50, 100, 50, 15, 85, 20, 80];
    public string? PreferredSourceDeviceId { get; init; }
    public string? PreferredOutputDeviceId { get; init; }

    private static string SettingsFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFlow", "config.json");

    public static UserSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                if (settings is not null) return settings;
            }
        }
        catch
        {
            // Fall back to default on corrupt or unreadable settings
        }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Non-critical persistence failure
        }
    }
}
