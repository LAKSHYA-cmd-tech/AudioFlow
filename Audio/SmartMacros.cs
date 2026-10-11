namespace AudioFlow.Audio;

/// <summary>
/// Smart Macro DSP settings for instant intuitive audio enhancement.
/// Powers the "Simple Mode" with one-touch controls.
/// </summary>
public sealed record SmartMacros
{
    /// <summary>Sub-bass and punch boost in decibels (0 to 12 dB).</summary>
    public float BassBoostDb { get; init; } = 0f;

    /// <summary>Speech intelligibility and vocal presence in decibels (0 to 10 dB).</summary>
    public float VocalClarityDb { get; init; } = 0f;

    /// <summary>3D spatial stage immersion (0 to 100%).</summary>
    public float SpatialWidth { get; init; } = 0f;

    /// <summary>Dynamic range leveling / night mode to tame loud sounds and boost dialogue.</summary>
    public bool NightMode { get; init; } = false;

    public static SmartMacros Default => new();
}
