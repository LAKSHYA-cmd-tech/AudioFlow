namespace AudioFlow.Audio;

/// <summary>
/// Pre-calibrated headphone correction database based on AutoEq measurements and the Harman Target Curve.
/// Provides 1-click compensation curves for the world's most popular headphones across 11 mastering bands.
/// </summary>
public sealed record HeadphoneProfile(
    string Id,
    string Name,
    string Manufacturer,
    string Category,
    string Description,
    float[] Gains
)
{
    public float[] RawAnomalies => Gains.Select(g => -g).ToArray();

    public EqBand[] ToEqBands()
    {
        var bands = new EqBand[EqBand.DefaultFrequencies.Length];
        for (int i = 0; i < bands.Length; i++)
        {
            float gain = i < Gains.Length ? Gains[i] : 0f;
            bands[i] = new EqBand(EqBand.DefaultFrequencies[i], gain, 1.4f, EqFilterType.Peaking);
        }
        return bands;
    }
}

public static class AutoEqDatabase
{
    public static readonly IReadOnlyList<HeadphoneProfile> Profiles =
    [
        new(
            "sony-wh1000xm5",
            "Sony WH-1000XM5",
            "Sony",
            "Over-Ear ANC",
            "Harman calibration: Cleans muddy mid-bass bump, boosts natural female vocal clarity, and expands treble sparkle.",
            [-1.8f, -2.8f, -1.5f, 0.5f, 2.0f, 1.5f, 1.2f, 2.2f, 1.5f, -0.5f, 0.0f]
        ),
        new(
            "sony-wh1000xm4",
            "Sony WH-1000XM4",
            "Sony",
            "Over-Ear ANC",
            "Harman calibration: Tames heavy 150-250 Hz boom, opens up recessed vocal mids and restores airy high frequencies.",
            [-1.5f, -3.2f, -2.0f, 0.5f, 2.5f, 1.8f, 1.2f, 2.2f, 1.2f, -0.5f, 0.0f]
        ),
        new(
            "apple-airpods-max",
            "Apple AirPods Max",
            "Apple",
            "Over-Ear ANC",
            "Harman calibration: Neutralizes slight 3 kHz dip and enhances high-treble acoustic dispersion.",
            [0.5f, -0.5f, -0.5f, 0.0f, 1.5f, 1.2f, 0.8f, 1.2f, 1.0f, 0.5f, 0.0f]
        ),
        new(
            "apple-airpods-pro-2",
            "Apple AirPods Pro 2",
            "Apple",
            "In-Ear (TWS)",
            "Harman In-Ear calibration: Smooths 6 kHz peak, lifts sub-bass impact, and balances mid-range vocals.",
            [1.8f, 1.0f, -0.2f, 0.0f, 1.0f, 1.0f, 0.5f, -1.0f, 0.8f, 0.8f, 0.0f]
        ),
        new(
            "sennheiser-hd600",
            "Sennheiser HD 600",
            "Sennheiser",
            "Open-Back Audiophile",
            "Legendary reference headphone: Restores deep sub-bass roll-off (< 80 Hz) while preserving sublime vocal neutrality.",
            [5.2f, 4.0f, 1.5f, 0.2f, 0.0f, -0.5f, -0.2f, 0.8f, 0.8f, 0.2f, 0.0f]
        ),
        new(
            "sennheiser-hd650",
            "Sennheiser HD 650 / HD 6XX",
            "Sennheiser",
            "Open-Back Audiophile",
            "Harman target: Lifts sub-bass foundation and adds upper-treble sparkle without disturbing warm signature.",
            [4.8f, 3.5f, 1.2f, 0.2f, 0.0f, -0.2f, 0.5f, 1.5f, 1.2f, 0.5f, 0.0f]
        ),
        new(
            "audiotechnica-ath-m50x",
            "Audio-Technica ATH-M50x",
            "Audio-Technica",
            "Closed-Back Studio",
            "Studio balance: Controls aggressive mid-bass punch and smooths harsh 9 kHz sibilance peak.",
            [-0.8f, -2.2f, -1.5f, 0.2f, 0.5f, 0.8f, 0.5f, -2.0f, -1.0f, 0.5f, 0.0f]
        ),
        new(
            "bose-qc45",
            "Bose QuietComfort 45",
            "Bose",
            "Over-Ear ANC",
            "Harman target: Reduces overly aggressive 2-4 kHz treble bite, brings forward punchy sub-bass rhythm.",
            [2.2f, 1.5f, 0.5f, -0.5f, -1.8f, -1.8f, -0.5f, 1.0f, 0.8f, 0.0f, 0.0f]
        ),
        new(
            "beyerdynamic-dt770-80",
            "Beyerdynamic DT 770 Pro (80Ω)",
            "Beyerdynamic",
            "Closed-Back Studio",
            "Tames famous Beyer treble peak (6-8 kHz) and balances warm sub-bass curve for fatigue-free listening.",
            [0.8f, -0.5f, -1.0f, 0.0f, 1.2f, 0.8f, 0.0f, -3.8f, -2.5f, 0.8f, 0.0f]
        ),
        new(
            "beyerdynamic-dt990-250",
            "Beyerdynamic DT 990 Pro (250Ω)",
            "Beyerdynamic",
            "Open-Back Studio",
            "Sub-bass extension + drastic treble reduction (-5 dB at 6 kHz) for pristine open studio monitoring.",
            [3.8f, 2.5f, 0.5f, 0.2f, 1.5f, 0.5f, -1.5f, -5.5f, -3.5f, 0.2f, 0.0f]
        ),
        new(
            "hyperx-cloud-2",
            "HyperX Cloud II",
            "HyperX",
            "Gaming Headset",
            "FPS Footstep & Harman tuning: Cuts boomy 120-250 Hz mud, elevates 2.5-4 kHz footsteps and voice comms.",
            [-1.2f, -2.5f, -1.5f, 0.5f, 2.0f, 2.5f, 1.2f, 0.8f, 1.0f, 0.2f, 0.0f]
        ),
        new(
            "steelseries-arctis-nova-pro",
            "SteelSeries Arctis Nova Pro",
            "SteelSeries",
            "Gaming Headset",
            "Tactical balance: Refines sub-bass impact and heightens spatial cue clarity across virtual surround.",
            [1.2f, 0.5f, -0.5f, 0.2f, 1.5f, 1.8f, 0.8f, 1.5f, 1.2f, 0.2f, 0.0f]
        ),
        new(
            "logitech-g-pro-x",
            "Logitech G Pro X",
            "Logitech",
            "Gaming Headset",
            "Tournament tune: Enhances footsteps and directional sound cues while taming harsh explosions.",
            [-0.8f, -1.8f, -1.0f, 0.2f, 2.2f, 2.5f, 1.2f, 0.5f, 1.0f, 0.2f, 0.0f]
        ),
        new(
            "moondrop-aria",
            "Moondrop Aria / Chu II",
            "Moondrop",
            "In-Ear Monitor (IEM)",
            "VDSF Harman Target refinement: Deep sub-bass warmth, natural lush vocals, and pristine airy extension.",
            [2.2f, 1.5f, 0.2f, 0.2f, 0.8f, 0.2f, -0.5f, 0.8f, 1.0f, 0.5f, 0.0f]
        ),
        new(
            "samsung-galaxy-buds-2-pro",
            "Samsung Galaxy Buds 2 Pro",
            "Samsung",
            "In-Ear (TWS)",
            "Harmon In-Ear tuning: Near-perfect factory tuning with subtle mid-range refinement and smoother highs.",
            [0.8f, 0.2f, -0.5f, 0.2f, 0.8f, 0.8f, -0.5f, 0.2f, 0.8f, 0.2f, 0.0f]
        )
    ];
}
