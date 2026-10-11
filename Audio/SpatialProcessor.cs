namespace AudioFlow.Audio;

/// <summary>
/// Built-in stereo virtual speaker renderer for eight 7.1 speaker nodes.
/// </summary>
public sealed class SpatialProcessor
{
    public static readonly string[] NodeNames = ["L", "C", "R", "LFE", "SL", "SR", "BL", "BR"];
    private SpatialSettings settings = BuildMatrix(SpatialSettings.Default);

    public void Configure(bool enabled, float depth, float ambience, float mix)
    {
        CheckPercent(depth); CheckPercent(ambience); CheckPercent(mix);
        var old = Volatile.Read(ref settings);
        Volatile.Write(ref settings, BuildMatrix(old with
        {
            Enabled = enabled, Depth = depth / 100, Ambience = ambience / 100, Mix = mix / 100
        }));
    }

    public void SetPan(int node, float percent)
    {
        if ((uint)node >= 8) throw new ArgumentOutOfRangeException(nameof(node));
        CheckPercent(percent);
        var old = Volatile.Read(ref settings);
        var pans = (float[])old.Pans.Clone();
        pans[node] = percent / 100;
        Volatile.Write(ref settings, BuildMatrix(old with { Pans = pans }));
    }

    // Audio thread only. This runs after EQ and before output metering/FFT.
    public void Process(ref float left, ref float right)
    {
        var current = Volatile.Read(ref settings);
        if (!current.Enabled || current.Mix <= 0) return;

        var dryL = left;
        var dryR = right;
        left = dryL * current.LL + dryR * current.LR;
        right = dryL * current.RL + dryR * current.RR;
    }

    // Spatial routing is a linear stereo mix. Calculate its pan gains when
    // settings change instead of doing sixteen square roots for every frame.
    private static SpatialSettings BuildMatrix(SpatialSettings current)
    {
        ReadOnlySpan<float> fromLeft = [1, 0.35f, 0, 0.08f,
            current.Ambience * 0.3f, 0, current.Ambience * 0.2f, 0];
        ReadOnlySpan<float> fromRight = [0, 0.35f, 1, 0.08f,
            0, current.Ambience * 0.3f, 0, current.Ambience * 0.2f];
        float ll = 0, lr = 0, rl = 0, rr = 0;
        for (var node = 0; node < 8; node++)
        {
            var pan = 0.5f + (current.Pans[node] - 0.5f) * current.Depth;
            var leftGain = MathF.Sqrt(1 - pan);
            var rightGain = MathF.Sqrt(pan);
            ll += fromLeft[node] * leftGain;
            lr += fromRight[node] * leftGain;
            rl += fromLeft[node] * rightGain;
            rr += fromRight[node] * rightGain;
        }
        float wet = current.Mix * 0.65f;
        return current with { LL = 1 - current.Mix + ll * wet, LR = lr * wet,
            RL = rl * wet, RR = 1 - current.Mix + rr * wet };
    }

    private static void CheckPercent(float value)
    {
        if (!float.IsFinite(value) || value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "Value must be 0 to 100 percent.");
    }

    private sealed record SpatialSettings(
        bool Enabled, float Depth, float Ambience, float Mix, float[] Pans)
    {
        public float LL { get; init; }
        public float LR { get; init; }
        public float RL { get; init; }
        public float RR { get; init; }
        public static SpatialSettings Default => new(false, 0.6f, 0.45f, 0.8f,
            [0, 0.5f, 1, 0.5f, 0.15f, 0.85f, 0.2f, 0.8f]);
    }
}
