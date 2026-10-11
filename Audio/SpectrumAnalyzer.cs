using NAudio.Dsp;

namespace AudioFlow.Audio;

/// <summary>Stereo audio ring with windowed FFT analysis on a background worker.</summary>
public sealed class SpectrumAnalyzer : IDisposable
{
    public const int BinCount = 64;
    private const int FftSize = 4096;
    private const int RingSize = 32_768;
    private const float MinimumFrequency = 20;
    private readonly float[] leftRing = new float[RingSize];
    private readonly float[] rightRing = new float[RingSize];
    private readonly int sampleRate;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task worker;
    private long writePosition;
    private long readPosition;
    private float dominantFrequency;
    private float[] published = new float[BinCount];
    private readonly float[][] snapshotBuffers = [new float[BinCount], new float[BinCount]];
    private int activeSnapshotIndex;

    public SpectrumAnalyzer(int sampleRate)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        this.sampleRate = sampleRate;
        worker = Task.Run(AnalyzeLoop);
    }

    public static float MaximumFrequency(int sampleRate) => MathF.Min(20_000, sampleRate * 0.45f);
    public float DominantFrequency => Volatile.Read(ref dominantFrequency);

    // Called only by the render thread. No locks, waits, or heap allocations.
    public void PushStereo(float left, float right)
    {
        var next = writePosition + 1;
        if (next - Volatile.Read(ref readPosition) >= RingSize)
            return; // Drop analysis data, never audio.
        var index = (int)(writePosition & (RingSize - 1));
        leftRing[index] = left;
        rightRing[index] = right;
        Volatile.Write(ref writePosition, next);
    }

    // Publish a whole output callback once, rather than synchronizing with the
    // analysis worker for every frame. Analysis overflow never delays playback.
    public void PushStereo(ReadOnlySpan<float> samples)
    {
        var start = writePosition;
        int free = (int)Math.Max(0, RingSize - 1 - (start - Volatile.Read(ref readPosition)));
        int frames = Math.Min(samples.Length / 2, free);
        for (int i = 0; i < frames; i++)
        {
            int index = (int)((start + i) & (RingSize - 1));
            leftRing[index] = samples[i * 2];
            rightRing[index] = samples[i * 2 + 1];
        }
        Volatile.Write(ref writePosition, start + frames);
    }

    public void CopyBins(Span<float> destination)
    {
        if (destination.Length < BinCount)
            throw new ArgumentException("Destination is too short.", nameof(destination));
        Volatile.Read(ref published).AsSpan().CopyTo(destination);
    }

    private async Task AnalyzeLoop()
    {
        var leftFft = new Complex[FftSize];
        var rightFft = new Complex[FftSize];
        var hann = new float[FftSize];
        var magnitudes = new float[FftSize / 2];
        var smoothed = new float[BinCount];
        var maximumFrequency = MaximumFrequency(sampleRate);
        var logSpan = MathF.Log(maximumFrequency / MinimumFrequency);
        for (var i = 0; i < FftSize; i++)
            hann[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (FftSize - 1));
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var available = Volatile.Read(ref writePosition) - readPosition;
                if (available < FftSize)
                {
                    await Task.Delay(8, cancellation.Token).ConfigureAwait(false);
                    continue;
                }
                if (available > RingSize / 2)
                    Volatile.Write(ref readPosition, Volatile.Read(ref writePosition) - FftSize);

                for (var i = 0; i < FftSize; i++)
                {
                    var index = (int)((readPosition + i) & (RingSize - 1));
                    leftFft[i].X = leftRing[index] * hann[i];
                    leftFft[i].Y = 0;
                    rightFft[i].X = rightRing[index] * hann[i];
                    rightFft[i].Y = 0;
                }
                Volatile.Write(ref readPosition, readPosition + FftSize / 2);
                FastFourierTransform.FFT(true, 12, leftFft);
                FastFourierTransform.FFT(true, 12, rightFft);

                var dominantBin = 0;
                var dominantMagnitude = 0f;
                for (var bin = 1; bin < magnitudes.Length; bin++)
                {
                    var l = leftFft[bin].X * leftFft[bin].X + leftFft[bin].Y * leftFft[bin].Y;
                    var r = rightFft[bin].X * rightFft[bin].X + rightFft[bin].Y * rightFft[bin].Y;
                    // NAudio's forward FFT already applies 1/N. Hann needs 4x
                    // amplitude correction; stereo RMS retains out-of-phase content.
                    magnitudes[bin] = 4 * MathF.Sqrt((l + r) * 0.5f);
                    if (magnitudes[bin] > dominantMagnitude)
                    {
                        dominantMagnitude = magnitudes[bin];
                dominantBin = bin;
                    }
                }
                Volatile.Write(ref dominantFrequency,
                    dominantMagnitude >= 0.001f ? (float)dominantBin * sampleRate / FftSize : 0);

                activeSnapshotIndex = 1 - activeSnapshotIndex;
                var snapshot = snapshotBuffers[activeSnapshotIndex];
                for (var bar = 0; bar < BinCount; bar++)
                {
                    var low = MinimumFrequency * MathF.Exp(logSpan * bar / BinCount);
                    var high = MinimumFrequency * MathF.Exp(logSpan * (bar + 1) / BinCount);
                    var first = Math.Clamp((int)MathF.Floor(low * FftSize / sampleRate), 1, magnitudes.Length - 1);
                    var last = Math.Clamp((int)MathF.Ceiling(high * FftSize / sampleRate), first, magnitudes.Length - 1);
                    var peak = 0f;
                    for (var bin = first; bin <= last; bin++) peak = MathF.Max(peak, magnitudes[bin]);
                    var db = 20 * MathF.Log10(MathF.Max(peak, 0.00001f));
                    var target = Math.Clamp((db + 72) / 72, 0, 1);
                    smoothed[bar] = target > smoothed[bar]
                        ? smoothed[bar] * 0.2f + target * 0.8f
                        : smoothed[bar] * 0.75f + target * 0.25f;
                    snapshot[bar] = smoothed[bar];
                }
                Volatile.Write(ref published, snapshot);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        try { worker.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        cancellation.Dispose();
    }
}
