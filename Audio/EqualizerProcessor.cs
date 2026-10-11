using NAudio.Wave;

namespace AudioFlow.Audio;

/// <summary>Render-thread DSP. UI writes publish immutable filter-bank snapshots.</summary>
public sealed class EqualizerProcessor : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly SpectrumAnalyzer analyzer;
    private readonly SpatialProcessor spatial;
    private readonly PeakLimiter limiter;
    private FilterSlot[] slots;
    private FilterSlot? bassMacroSlot;
    private FilterSlot? vocalMacroSlot;
    private SmartMacros currentMacros = SmartMacros.Default;
    private float targetPreamp = 1;
    private float currentPreamp = 1;
    private float targetBypass;
    private float currentBypass;
    private float inputLeft, inputRight, outputLeft, outputRight;
    private long clippedFrames;
    private float nightModeGain = 1.0f;

    public EqualizerProcessor(ISampleProvider source, SpectrumAnalyzer analyzer, SpatialProcessor spatial, EqBand[] bands)
    {
        if (source.WaveFormat.Channels != 2)
            throw new NotSupportedException("The virtual cable must be configured for stereo playback.");
        this.source = source;
        this.analyzer = analyzer;
        this.spatial = spatial;
        WaveFormat = source.WaveFormat;
        limiter = new PeakLimiter(WaveFormat.SampleRate);
        slots = bands.Select(band => CreateSlot(band)).ToArray();
        UpdateMacroFilters(currentMacros);
    }

    public WaveFormat WaveFormat { get; }

    public (float InputL, float InputR, float OutputL, float OutputR) Peaks =>
        (Volatile.Read(ref inputLeft), Volatile.Read(ref inputRight),
         Volatile.Read(ref outputLeft), Volatile.Read(ref outputRight));
    public long ClippedFrames => Interlocked.Read(ref clippedFrames);
    public long LimitedFrames => limiter.LimitedFrames;
    public float LimiterGain => limiter.Gain;
    public SmartMacros Macros => Volatile.Read(ref currentMacros);

    public void SetMacros(SmartMacros macros)
    {
        Volatile.Write(ref currentMacros, macros);
        UpdateMacroFilters(macros);
    }

    private void UpdateMacroFilters(SmartMacros m)
    {
        var sampleRate = WaveFormat.SampleRate;
        var bassBand = new EqBand(80f, m.BassBoostDb, 0.75f, EqFilterType.LowShelf);
        bassMacroSlot = CreateSlot(bassBand, bassMacroSlot);

        var vocalBand = new EqBand(3200f, m.VocalClarityDb, 1.2f, EqFilterType.Peaking);
        vocalMacroSlot = CreateSlot(vocalBand, vocalMacroSlot);
    }

    public void UpdateBand(int index, EqBand band)
    {
        var old = Volatile.Read(ref slots);
        var replacement = (FilterSlot[])old.Clone();
        replacement[index] = CreateSlot(band, old[index]);
        Volatile.Write(ref slots, replacement);
    }

    public void SetPreamp(float decibels) =>
        Volatile.Write(ref targetPreamp, MathF.Pow(10, decibels / 20));

    public void SetBypass(bool enabled) => Volatile.Write(ref targetBypass, enabled ? 1 : 0);

    public int Read(Span<float> samples)
    {
        var count = source.Read(samples);
        var active = Volatile.Read(ref slots);
        var bass = Volatile.Read(ref bassMacroSlot);
        var vocal = Volatile.Read(ref vocalMacroSlot);
        var macros = Volatile.Read(ref currentMacros);
        var target = Volatile.Read(ref targetPreamp);
        var bypass = Volatile.Read(ref targetBypass);
        float inL = 0, inR = 0, outL = 0, outR = 0;
        long clipped = 0;

        for (var i = 0; i + 1 < count; i += 2)
        {
            var left = samples[i];
            var right = samples[i + 1];
            inL = MathF.Max(inL, MathF.Abs(left));
            inR = MathF.Max(inR, MathF.Abs(right));

            foreach (var slot in active) slot.Process(ref left, ref right);
            bass?.Process(ref left, ref right);
            vocal?.Process(ref left, ref right);

            currentPreamp += (target - currentPreamp) * 0.002f;
            left *= currentPreamp;
            right *= currentPreamp;
            spatial.Process(ref left, ref right);

            if (macros.NightMode)
            {
                float peak = MathF.Max(MathF.Abs(left), MathF.Abs(right));
                float desiredGain = peak < 0.15f ? 1.4f : (peak > 0.6f ? 0.75f : 1.0f);
                nightModeGain += (desiredGain - nightModeGain) * 0.001f;
                left *= nightModeGain;
                right *= nightModeGain;
            }

            currentBypass += (bypass - currentBypass) * 0.002f;
            left += (samples[i] - left) * currentBypass;
            right += (samples[i + 1] - right) * currentBypass;

            limiter.Process(ref left, ref right);

            outL = MathF.Max(outL, MathF.Abs(left));
            outR = MathF.Max(outR, MathF.Abs(right));
            if (MathF.Abs(left) > 1 || MathF.Abs(right) > 1)
                clipped++;
            samples[i] = Math.Clamp(left, -1, 1);
            samples[i + 1] = Math.Clamp(right, -1, 1);
        }

        if (clipped != 0) Interlocked.Add(ref clippedFrames, clipped);
        analyzer.PushStereo(samples[..count]);

        Volatile.Write(ref inputLeft, inL);
        Volatile.Write(ref inputRight, inR);
        Volatile.Write(ref outputLeft, outL);
        Volatile.Write(ref outputRight, outR);
        return count;
    }

    private FilterSlot CreateSlot(EqBand band, FilterSlot? previous = null)
    {
        var coefficients = BiquadCoefficients.Design(band, WaveFormat.SampleRate);
        var usesGain = band.Type is EqFilterType.Peaking or EqFilterType.LowShelf or EqFilterType.HighShelf;
        return new FilterSlot(new Biquad(coefficients), new Biquad(coefficients),
            !usesGain || band.GainDb != 0, previous);
    }

    private sealed class FilterSlot(Biquad left, Biquad right, bool enabled, FilterSlot? prior)
    {
        private const int FadeFrames = 256;
        private FilterSlot? previous = prior;
        private int fadedFrames;

        public void Process(ref float leftSample, ref float rightSample)
        {
            var dryLeft = leftSample;
            var dryRight = rightSample;
            var newLeft = enabled ? left.Transform(dryLeft) : dryLeft;
            var newRight = enabled ? right.Transform(dryRight) : dryRight;
            if (previous is null)
            {
                leftSample = newLeft;
                rightSample = newRight;
                return;
            }
            var oldLeft = dryLeft;
            var oldRight = dryRight;
            previous.Process(ref oldLeft, ref oldRight);
            var blend = (float)++fadedFrames / FadeFrames;
            leftSample = oldLeft + (newLeft - oldLeft) * blend;
            rightSample = oldRight + (newRight - oldRight) * blend;
            if (fadedFrames >= FadeFrames) previous = null;
        }
    }
}
