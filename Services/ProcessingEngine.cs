using AudioFlow.Models;

namespace AudioFlow.Services;

public enum ProcessingStatus
{
    /// <summary>No processing backend can render audio, so the chain is stored but not applied.</summary>
    NotAvailable,
    /// <summary>A backend exists and the chain was applied, but every stage is bypassed.</summary>
    Inactive,
    /// <summary>A backend applied the chain.</summary>
    Active,
    Failed
}

/// <summary>
/// Optional DSP back end. A profile's chain is addressed by profile id, so adding or replacing a
/// back end does not affect profiles, rules or the existing UI.
/// </summary>
public interface IProcessingEngine
{
    string Name { get; }
    bool IsAvailable { get; }
    string UnavailableReason { get; }
    ProcessingStatus Apply(AudioProfile profile);
    void Bypass(AudioProfile profile);
}

/// <summary>
/// The default engine. AudioFlow controls Windows volume; it does not own the audio stream, so it
/// cannot render biquads, compression or limiting. Reporting that plainly is better than pretending
/// a chain is running while audio passes through untouched.
/// </summary>
public sealed class PassthroughProcessingEngine : IProcessingEngine
{
    public string Name => "Passthrough";
    public bool IsAvailable => false;
    public string UnavailableReason =>
        "AudioFlow can process its own looping EQ sample in real time, but does not yet process "
        + "other apps' audio. Your settings stay saved with the profile.";

    public ProcessingStatus Apply(AudioProfile profile) => ProcessingStatus.NotAvailable;
    public void Bypass(AudioProfile profile) { }
}

public static class ProcessingEngines
{
    /// <summary>
    /// Picks the back end to use. Later builds can select a real renderer here without touching
    /// profiles, automation or the view models.
    /// </summary>
    public static IProcessingEngine CreateDefault() => new PassthroughProcessingEngine();
}

/// <summary>
/// Checks a stored chain for configurations that will sound wrong or inaudible once rendered.
/// Purely advisory; nothing here changes the saved values.
/// </summary>
public static class DspChainDiagnostics
{
    public static IReadOnlyList<string> Describe(AudioProfile? profile)
    {
        var notes = new List<string>();
        var dsp = profile?.Dsp;
        if (dsp is null || !dsp.Enabled || !dsp.HasContent) return notes;

        var bands = dsp.Bands.Where(b => b.Enabled).ToList();
        var lowPass = bands.Where(b => b.Type == PeqFilterType.LowPass).ToList();
        var highPass = bands.Where(b => b.Type == PeqFilterType.HighPass).ToList();

        foreach (var cut in lowPass.Concat(highPass).Where(b => b.Frequency > 20000f))
            notes.Add($"{cut.Label.Trim()} sits above the audible range and will do nothing.");
        foreach (var cut in lowPass.Concat(highPass).Where(b => b.Frequency < 20f))
            notes.Add($"{cut.Label.Trim()} is below the audible range and will do nothing.");

        if (lowPass.Count > 0 && highPass.Count > 0)
        {
            var low = Math.Max(lowPass.Max(b => b.Frequency), highPass.Max(b => b.Frequency));
            var high = Math.Min(lowPass.Min(b => b.Frequency), highPass.Min(b => b.Frequency));
            if (high <= low)
                notes.Add("A low-pass and a high-pass overlap, so almost no frequencies will pass through.");
        }

        var steep = bands.Where(b => b.Q > 8f && b.Type == PeqFilterType.Peaking).ToList();
        foreach (var band in steep)
            notes.Add($"{band.Label.Trim()} uses a narrow, high-Q peak and may ring or distort loud passages.");

        foreach (var band in bands.Where(b => b.UsesGain && Math.Abs(b.GainDb) >= 18f))
            notes.Add($"{band.Label.Trim()} is a large boost. Watch for clipping if a limiter is not enabled.");

        if (dsp.Compressor.Enabled && dsp.Limiter.Enabled &&
            dsp.Compressor.MakeupDb + dsp.Limiter.CeilingDb > 0f)
            notes.Add("Compressor makeup plus limiter ceiling adds headroom instead of preventing clipping.");

        if (!dsp.Limiter.Enabled && bands.Any(b => b.UsesGain && b.GainDb > 6f))
            notes.Add("Boosting several bands with no limiter enabled can push peaks into distortion.");

        return notes;
    }
}
