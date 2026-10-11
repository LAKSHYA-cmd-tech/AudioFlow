namespace AudioFlow.Audio;

public enum EqFilterType
{
    Peaking,
    LowShelf,
    HighShelf,
    LowPass,
    HighPass,
    BandPass,
    Notch,
    AllPass
}

public readonly record struct EqBand(float Frequency, float GainDb, float Q, EqFilterType Type)
{
    /// <summary>
    /// Standard 11-band studio mastering frequencies covering the full human audible spectrum (31 Hz sub-bass to 16 kHz air).
    /// </summary>
    public static readonly float[] DefaultFrequencies =
        [31, 63, 125, 250, 500, 1_000, 2_000, 4_000, 8_000, 12_000, 16_000];

    public static readonly string[] BandRoles =
        ["Sub-Bass", "Bass Punch", "Low-End", "Warmth", "Body", "Vocal Mids", "Attack", "Presence", "Brilliance", "Shimmer", "Air"];

    public static string GetBandRole(int index) =>
        index >= 0 && index < BandRoles.Length ? BandRoles[index] : "Band";

    public static EqBand[] CreateDefaults() =>
        DefaultFrequencies.Select(f => new EqBand(f, 0, 1.2f, EqFilterType.Peaking)).ToArray();
}
