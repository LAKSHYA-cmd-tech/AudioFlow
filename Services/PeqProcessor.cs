using AudioFlow.Models;

namespace AudioFlow.Services;

/// <summary>
/// AudioFlow's independent parametric-EQ signal processor. It operates on interleaved
/// floating-point samples supplied by a renderer; constructing it does not
/// attach to Windows audio or change the live output.
/// </summary>
public sealed class PeqProcessor
{
    private readonly Section[] _sections;
    private readonly FilterState[,] _states;

    public int SampleRate { get; }
    public int Channels { get; }
    public int BandCount => _sections.Length;

    public PeqProcessor(DspSettings settings, int sampleRate, int channels)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (sampleRate is < 8000 or > 384000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(channels));

        SampleRate = sampleRate;
        Channels = channels;
        if (settings.Enabled)
        {
            var graphic = settings.GraphicEnabled
                ? settings.GraphicBands.Where(b => b is { Enabled: true } &&
                    (!b.UsesGain || Math.Abs(b.GainDb) > .001f))
                : [];
            _sections = graphic.Concat(settings.Bands.Where(b => b is { Enabled: true })
                    .Take(DspSettings.MaxBands))
                .Select(b => Section.FromBand(b, sampleRate)).ToArray();
        }
        else _sections = [];
        _states = new FilterState[_sections.Length, channels];
    }

    /// <summary>Processes whole interleaved frames in place, without allocating.</summary>
    public void Process(Span<float> samples)
    {
        if (samples.Length % Channels != 0)
            throw new ArgumentException("The buffer must contain whole frames.", nameof(samples));
        for (var sample = 0; sample < samples.Length; sample++)
        {
            var channel = sample % Channels;
            var value = (double)samples[sample];
            for (var band = 0; band < _sections.Length; band++)
            {
                var state = _states[band, channel];
                var section = _sections[band];
                var output = section.B0 * value + state.Z1;
                state.Z1 = section.B1 * value - section.A1 * output + state.Z2;
                state.Z2 = section.B2 * value - section.A2 * output;
                _states[band, channel] = state;
                value = output;
            }
            samples[sample] = (float)value;
        }
    }

    public void Reset() => Array.Clear(_states);

    private struct FilterState
    {
        public double Z1;
        public double Z2;
    }

    private readonly record struct Section(double B0, double B1, double B2, double A1, double A2)
    {
        // W3C Audio EQ Cookbook, adapted from Robert Bristow-Johnson:
        // https://www.w3.org/TR/audio-eq-cookbook/
        public static Section FromBand(PeqBand band, int sampleRate)
        {
            var frequency = Math.Clamp((double)band.Frequency, 20, Math.Min(20000, sampleRate * 0.49));
            var q = Math.Clamp((double)band.Q, PeqBand.MinQ, PeqBand.MaxQ);
            var gain = Math.Clamp((double)band.GainDb, PeqBand.MinGainDb, PeqBand.MaxGainDb);
            var omega = 2 * Math.PI * frequency / sampleRate;
            var cosine = Math.Cos(omega);
            var alpha = Math.Sin(omega) / (2 * q);
            var a = Math.Pow(10, gain / 40);
            var shelf = 2 * Math.Sqrt(a) * alpha;
            double b0, b1, b2, a0, a1, a2;
            switch (band.Type)
            {
                case PeqFilterType.Peaking:
                    (b0, b1, b2) = (1 + alpha * a, -2 * cosine, 1 - alpha * a);
                    (a0, a1, a2) = (1 + alpha / a, -2 * cosine, 1 - alpha / a);
                    break;
                case PeqFilterType.LowShelf:
                    (b0, b1, b2) = (a * ((a + 1) - (a - 1) * cosine + shelf),
                        2 * a * ((a - 1) - (a + 1) * cosine),
                        a * ((a + 1) - (a - 1) * cosine - shelf));
                    (a0, a1, a2) = ((a + 1) + (a - 1) * cosine + shelf,
                        -2 * ((a - 1) + (a + 1) * cosine),
                        (a + 1) + (a - 1) * cosine - shelf);
                    break;
                case PeqFilterType.HighShelf:
                    (b0, b1, b2) = (a * ((a + 1) + (a - 1) * cosine + shelf),
                        -2 * a * ((a - 1) + (a + 1) * cosine),
                        a * ((a + 1) + (a - 1) * cosine - shelf));
                    (a0, a1, a2) = ((a + 1) - (a - 1) * cosine + shelf,
                        2 * ((a - 1) - (a + 1) * cosine),
                        (a + 1) - (a - 1) * cosine - shelf);
                    break;
                case PeqFilterType.LowPass:
                    (b0, b1, b2) = ((1 - cosine) / 2, 1 - cosine, (1 - cosine) / 2);
                    (a0, a1, a2) = (1 + alpha, -2 * cosine, 1 - alpha);
                    break;
                case PeqFilterType.HighPass:
                    (b0, b1, b2) = ((1 + cosine) / 2, -(1 + cosine), (1 + cosine) / 2);
                    (a0, a1, a2) = (1 + alpha, -2 * cosine, 1 - alpha);
                    break;
                case PeqFilterType.Notch:
                    (b0, b1, b2) = (1, -2 * cosine, 1);
                    (a0, a1, a2) = (1 + alpha, -2 * cosine, 1 - alpha);
                    break;
                case PeqFilterType.BandPass:
                    (b0, b1, b2) = (alpha, 0, -alpha); // 0 dB peak gain
                    (a0, a1, a2) = (1 + alpha, -2 * cosine, 1 - alpha);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(band), "Unsupported EQ filter type.");
            }
            return new Section(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
        }
    }
}
