namespace AudioFlow.Audio;

/// <summary>
/// Stereo-linked output safety limiter. Attack is immediate so no sample crosses the
/// ceiling; release is gradual to avoid rapid gain changes after a peak.
/// Render-thread-owned state, with scalar telemetry read by the UI thread.
/// </summary>
public sealed class PeakLimiter
{
    private const float Ceiling = 0.8912509f; // -1 dBFS
    private readonly float releaseStep;
    private float gain = 1;
    private long limitedFrames;

    public PeakLimiter(int sampleRate)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        releaseStep = 1 - MathF.Exp(-1f / (sampleRate * 0.1f)); // 100 ms
    }

    public float Gain => Volatile.Read(ref gain);
    public long LimitedFrames => Interlocked.Read(ref limitedFrames);

    public void Process(ref float left, ref float right)
    {
        if (!float.IsFinite(left) || !float.IsFinite(right))
        {
            left = right = 0;
            return;
        }
        var peak = MathF.Max(MathF.Abs(left), MathF.Abs(right));
        var required = peak > Ceiling ? Ceiling / peak : 1f;
        var nextGain = required < gain ? required :
            MathF.Min(required, gain + (1 - gain) * releaseStep);
        gain = nextGain;
        left *= nextGain;
        right *= nextGain;
        if (required < 1) Interlocked.Increment(ref limitedFrames);
    }
}
