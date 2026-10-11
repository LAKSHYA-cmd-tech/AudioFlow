namespace AudioFlow.Audio;

/// <summary>
/// Configurable latency modes for the audio buffer engine.
/// Allows low-latency gaming/interactive use or high-stability VM/relaxed use.
/// </summary>
public enum BufferLatencyMode
{
    /// <summary>Ultra-low latency for competitive gaming and live interactive audio (~25 ms reserve).</summary>
    UltraLow = 0,

    /// <summary>Balanced latency recommended for music, movies, and daily desktop use (~60 ms reserve).</summary>
    Balanced = 1,

    /// <summary>High-stability mode for virtual machines or high-jitter audio devices (~160 ms reserve).</summary>
    HighStability = 2
}
