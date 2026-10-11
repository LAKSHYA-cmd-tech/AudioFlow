namespace AudioFlow.Audio;

public sealed record AudioPreset(
    string Name,
    string Category,
    string Description,
    float[] Gains,
    SmartMacros Macros
)
{
    public EqBand[] ToEqBands()
    {
        var bands = new EqBand[EqBand.DefaultFrequencies.Length];
        for (int i = 0; i < bands.Length; i++)
        {
            float gain = i < Gains.Length ? Gains[i] : 0f;
            bands[i] = new EqBand(EqBand.DefaultFrequencies[i], gain, 1.2f, EqFilterType.Peaking);
        }
        return bands;
    }
}

public static class PresetLibrary
{
    public static readonly IReadOnlyList<AudioPreset> Presets =
    [
        // Esports Competitive Game Presets
        new(
            "Valorant & CS2: Tactical Steps",
            "Gaming",
            "Laser-focused 125 Hz boot thuds, 2.5 kHz surface clicks, and 6 kHz defusal cues with suppressed grenade boom.",
            [-4.0f, -2.5f, 3.2f, 2.0f, 0.5f, 2.5f, 4.2f, 4.5f, 2.8f, 0.5f, 0.0f],
            new SmartMacros { BassBoostDb = 0f, VocalClarityDb = 5.0f, SpatialWidth = 60f, NightMode = false }
        ),
        new(
            "Warzone: Airstrike Dampener",
            "Gaming",
            "Steep sub-bass cut to suppress deafening airstrikes while amplifying indoor footsteps & armor breaks.",
            [-6.0f, -4.0f, 2.8f, 2.2f, 0.0f, 2.0f, 3.5f, 3.8f, 2.2f, 1.0f, 0.0f],
            new SmartMacros { BassBoostDb = 0f, VocalClarityDb = 4.5f, SpatialWidth = 75f, NightMode = true }
        ),
        new(
            "Apex Legends: Shield & Zipline",
            "Gaming",
            "Directional spatial curve that isolates 1.5-3 kHz shield cracks, zipline motors, and enemy slide audio.",
            [-2.5f, -1.0f, 2.0f, 1.5f, 0.5f, 1.8f, 3.0f, 3.2f, 2.5f, 1.2f, 0.0f],
            new SmartMacros { BassBoostDb = 1.0f, VocalClarityDb = 3.5f, SpatialWidth = 85f, NightMode = false }
        ),
        new(
            "Rainbow Six Siege: Audio Intel",
            "Gaming",
            "Precision acoustic tuning for wood splintering, barricade peeling, drone humming, and floorboard squeaks.",
            [-3.5f, -2.0f, 2.5f, 2.0f, 1.0f, 2.2f, 3.8f, 3.5f, 1.8f, 0.5f, 0.0f],
            new SmartMacros { BassBoostDb = 0.5f, VocalClarityDb = 4.0f, SpatialWidth = 70f, NightMode = false }
        ),
        new(
            "Immersive RPG / Cinematic",
            "Gaming",
            "Expansive orchestral soundscape, subterranean monster roars, wide environmental acoustics, and world reverberation.",
            [4.0f, 3.0f, 1.5f, 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 2.0f, 1.2f, 0.8f],
            new SmartMacros { BassBoostDb = 4.5f, VocalClarityDb = 2.0f, SpatialWidth = 90f, NightMode = false }
        ),
        new(
            "Clear Team Comms & Discord",
            "Gaming",
            "Reduces bass boom and game audio clutter to make Discord and squad voice chat crystal clear.",
            [-5.0f, -3.5f, 0.0f, 1.5f, 3.8f, 4.2f, 3.0f, 1.2f, -0.5f, -2.0f, -3.5f],
            new SmartMacros { BassBoostDb = 0f, VocalClarityDb = 6.0f, SpatialWidth = 20f, NightMode = false }
        ),

        // Music Presets
        new(
            "Harman Target Reference",
            "Music",
            "Neutral, highly acclaimed reference target curve with clean sub-bass shelf and smooth natural treble.",
            [2.5f, 1.8f, 0.5f, 0.0f, 0.5f, 1.2f, 0.8f, 0.5f, 1.2f, 0.8f, 0.2f],
            new SmartMacros { BassBoostDb = 1.0f, VocalClarityDb = 1.0f, SpatialWidth = 35f, NightMode = false }
        ),
        new(
            "Bass Punch / EDM & Hip-Hop",
            "Music",
            "Deep subterranean sub-bass with punchy transient kick drums without muddying vocalists.",
            [6.0f, 4.8f, 2.0f, -0.5f, 0.5f, 0.8f, 0.5f, 1.5f, 2.2f, 1.5f, 0.5f],
            new SmartMacros { BassBoostDb = 6.0f, VocalClarityDb = 1.5f, SpatialWidth = 50f, NightMode = false }
        ),
        new(
            "Acoustic & Vocal Warmth",
            "Music",
            "Rich analog warmth highlighting guitar strings, piano harmonics, and intimate vocal performances.",
            [1.5f, 1.2f, 0.8f, 0.5f, 1.5f, 2.0f, 1.5f, 1.0f, 1.0f, 0.8f, 0.2f],
            new SmartMacros { BassBoostDb = 2.0f, VocalClarityDb = 3.0f, SpatialWidth = 40f, NightMode = false }
        ),
        new(
            "Bright & Airy",
            "Music",
            "Crisp high-frequency air, shimmering cymbals, and sparkling soundstage detail.",
            [0.0f, 0.0f, 0.0f, 0.0f, 0.8f, 1.8f, 2.8f, 3.8f, 3.5f, 2.5f, 1.5f],
            new SmartMacros { BassBoostDb = 0f, VocalClarityDb = 2.5f, SpatialWidth = 60f, NightMode = false }
        ),

        // Media Presets
        new(
            "Movie Dialogue & Night Mode",
            "Media",
            "Volume leveling to tame ear-splitting action scenes while lifting quiet whispered dialogue.",
            [-1.8f, -0.8f, 1.0f, 2.0f, 3.2f, 3.5f, 2.2f, 1.0f, 0.0f, -0.5f, -1.0f],
            new SmartMacros { BassBoostDb = 1.0f, VocalClarityDb = 5.0f, SpatialWidth = 70f, NightMode = true }
        ),
        new(
            "Podcast & Audiobook",
            "Media",
            "Optimized exclusively for the human vocal tract (85 Hz to 4 kHz) with high & low cutoffs.",
            [-4.5f, -2.5f, 1.5f, 3.0f, 4.2f, 3.8f, 2.0f, 0.5f, -1.5f, -3.0f, -4.0f],
            new SmartMacros { BassBoostDb = 0f, VocalClarityDb = 6.0f, SpatialWidth = 0f, NightMode = true }
        ),
        new(
            "Flat Studio Reference",
            "Media",
            "Zero equalization coloration for pure uncolored audio monitoring.",
            [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f],
            new SmartMacros { BassBoostDb = 0f, VocalClarityDb = 0f, SpatialWidth = 0f, NightMode = false }
        )
    ];
}
