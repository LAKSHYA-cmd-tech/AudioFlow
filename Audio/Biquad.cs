namespace AudioFlow.Audio;

/// <summary>RBJ biquad design shared by audio processing and response drawing.</summary>
public readonly record struct BiquadCoefficients(float B0, float B1, float B2, float A1, float A2)
{
    public static BiquadCoefficients Design(EqBand band, int sampleRate)
    {
        var frequency = Math.Clamp((double)band.Frequency, 10, sampleRate * 0.45);
        var omega = 2 * Math.PI * frequency / sampleRate;
        var cosine = Math.Cos(omega);
        var sine = Math.Sin(omega);
        var alpha = sine / (2 * band.Q);
        var amplitude = Math.Pow(10, band.GainDb / 40.0);
        var shelfSlope = Math.Clamp(band.Q / (1.0 + band.Q), 0.1, 0.95);
        var shelfAlpha = sine / 2 * Math.Sqrt((amplitude + 1 / amplitude) * (1 / shelfSlope - 1) + 2);
        var rootTerm = 2 * Math.Sqrt(amplitude) * shelfAlpha;
        double b0, b1, b2, a0, a1, a2;

        switch (band.Type)
        {
            case EqFilterType.Peaking:
                b0 = 1 + alpha * amplitude; b1 = -2 * cosine; b2 = 1 - alpha * amplitude;
                a0 = 1 + alpha / amplitude; a1 = -2 * cosine; a2 = 1 - alpha / amplitude;
                break;
            case EqFilterType.LowShelf:
                b0 = amplitude * ((amplitude + 1) - (amplitude - 1) * cosine + rootTerm);
                b1 = 2 * amplitude * ((amplitude - 1) - (amplitude + 1) * cosine);
                b2 = amplitude * ((amplitude + 1) - (amplitude - 1) * cosine - rootTerm);
                a0 = (amplitude + 1) + (amplitude - 1) * cosine + rootTerm;
                a1 = -2 * ((amplitude - 1) + (amplitude + 1) * cosine);
                a2 = (amplitude + 1) + (amplitude - 1) * cosine - rootTerm;
                break;
            case EqFilterType.HighShelf:
                b0 = amplitude * ((amplitude + 1) + (amplitude - 1) * cosine + rootTerm);
                b1 = -2 * amplitude * ((amplitude - 1) + (amplitude + 1) * cosine);
                b2 = amplitude * ((amplitude + 1) + (amplitude - 1) * cosine - rootTerm);
                a0 = (amplitude + 1) - (amplitude - 1) * cosine + rootTerm;
                a1 = 2 * ((amplitude - 1) - (amplitude + 1) * cosine);
                a2 = (amplitude + 1) - (amplitude - 1) * cosine - rootTerm;
                break;
            case EqFilterType.LowPass:
                b0 = (1 - cosine) / 2; b1 = 1 - cosine; b2 = b0;
                a0 = 1 + alpha; a1 = -2 * cosine; a2 = 1 - alpha;
                break;
            case EqFilterType.HighPass:
                b0 = (1 + cosine) / 2; b1 = -(1 + cosine); b2 = b0;
                a0 = 1 + alpha; a1 = -2 * cosine; a2 = 1 - alpha;
                break;
            case EqFilterType.BandPass:
                b0 = alpha; b1 = 0; b2 = -alpha;
                a0 = 1 + alpha; a1 = -2 * cosine; a2 = 1 - alpha;
                break;
            case EqFilterType.Notch:
                b0 = 1; b1 = -2 * cosine; b2 = 1;
                a0 = 1 + alpha; a1 = -2 * cosine; a2 = 1 - alpha;
                break;
            case EqFilterType.AllPass:
                b0 = 1 - alpha; b1 = -2 * cosine; b2 = 1 + alpha;
                a0 = 1 + alpha; a1 = -2 * cosine; a2 = 1 - alpha;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(band));
        }

        return new((float)(b0 / a0), (float)(b1 / a0), (float)(b2 / a0),
            (float)(a1 / a0), (float)(a2 / a0));
    }

    public double MagnitudeDb(double frequency, int sampleRate)
    {
        var omega = 2 * Math.PI * frequency / sampleRate;
        var realB = B0 + B1 * Math.Cos(omega) + B2 * Math.Cos(2 * omega);
        var imaginaryB = -B1 * Math.Sin(omega) - B2 * Math.Sin(2 * omega);
        var realA = 1 + A1 * Math.Cos(omega) + A2 * Math.Cos(2 * omega);
        var imaginaryA = -A1 * Math.Sin(omega) - A2 * Math.Sin(2 * omega);
        var numerator = realB * realB + imaginaryB * imaginaryB;
        var denominator = realA * realA + imaginaryA * imaginaryA;
        return 10 * Math.Log10(Math.Max(numerator / Math.Max(denominator, 1e-30), 1e-30));
    }
}

/// <summary>Per-channel filter state; accessed only from the render callback.</summary>
public sealed class Biquad
{
    private readonly BiquadCoefficients c;
    private float z1, z2;

    public Biquad(BiquadCoefficients coefficients) => c = coefficients;

    public float Transform(float sample)
    {
        var output = c.B0 * sample + z1;
        z1 = c.B1 * sample - c.A1 * output + z2;
        z2 = c.B2 * sample - c.A2 * output;
        return output;
    }
}
