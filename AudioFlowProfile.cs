using System.Text.Json;
using AudioFlow.Audio;

namespace AudioFlow;

public sealed record AudioFlowProfile
{
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "Listening setup";
    public EqBand[] Bands { get; init; } = [];
    public float PreampDb { get; init; }
    public bool Bypass { get; init; }
    public bool SpatialEnabled { get; init; }
    public float Depth { get; init; } = 60;
    public float Ambience { get; init; } = 45;
    public float Mix { get; init; } = 80;
    public float[] Pans { get; init; } = [0, 50, 100, 50, 15, 85, 20, 80];
    public SmartMacros? Macros { get; init; } = SmartMacros.Default;
    public BufferLatencyMode LatencyMode { get; init; } = BufferLatencyMode.Balanced;
    public string? SourceDeviceId { get; init; }
    public string? OutputDeviceId { get; init; }

    public static AudioFlowProfile Parse(string json, string fallbackName)
    {
        // Profiles saved by earlier AudioFlow builds contained just the EQ band array.
        if (json.TrimStart().StartsWith('['))
        {
            var legacyBands = JsonSerializer.Deserialize<EqBand[]>(json) ?? [];
            var legacy = new AudioFlowProfile { Name = fallbackName, Bands = legacyBands };
            legacy.Validate();
            return legacy;
        }
        var profile = JsonSerializer.Deserialize<AudioFlowProfile>(json)
            ?? throw new InvalidDataException("The profile file is empty.");
        profile.Validate();
        return profile;
    }

    public void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Name) || Bands is null || Bands.Length != EqBand.DefaultFrequencies.Length ||
            Bands.Any(band => !EqualizerEngine.IsValidBand(band)) ||
            !float.IsFinite(PreampDb) || PreampDb is < -15 or > 15 ||
            !ValidPercent(Depth) || !ValidPercent(Ambience) || !ValidPercent(Mix) ||
            Pans is null || Pans.Length != SpatialProcessor.NodeNames.Length || Pans.Any(pan => !ValidPercent(pan)))
            throw new InvalidDataException("The profile has unsupported or invalid audio settings.");
    }

    private static bool ValidPercent(float value) => float.IsFinite(value) && value is >= 0 and <= 100;
}
