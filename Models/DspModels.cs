using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AudioFlow.Models;

public enum PeqFilterType { Peaking, LowShelf, HighShelf, LowPass, HighPass, Notch, BandPass }

/// <summary>One biquad section. Frequencies are Hz, gains dB, Q is dimensionless.</summary>
public sealed class PeqBand : Observable
{
    public const float MinFrequency = 20f;
    public const float MaxFrequency = 20000f;
    public const float MinGainDb = -24f;
    public const float MaxGainDb = 24f;
    public const float MinQ = 0.1f;
    public const float MaxQ = 10f;

    private bool _enabled = true;
    private PeqFilterType _type = PeqFilterType.Peaking;
    private float _frequency = 1000f;
    private float _gainDb;
    private float _q = 1f;

    // Keeps user-added bands separate from the eleven-band starting layout.
    public bool IsAdditional { get; set; }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public PeqFilterType Type { get => _type; set { if (Set(ref _type, value)) { Raise(nameof(Label)); Raise(nameof(UsesGain)); } } }
    public float Frequency { get => _frequency; set { if (Set(ref _frequency, Clamp(value, MinFrequency, MaxFrequency))) { Raise(nameof(Label)); Raise(nameof(ShortFrequency)); } } }
    public float GainDb { get => _gainDb; set { if (Set(ref _gainDb, Clamp(value, MinGainDb, MaxGainDb))) Raise(nameof(Label)); } }
    public float Q { get => _q; set => Set(ref _q, Clamp(value, MinQ, MaxQ)); }

    [JsonIgnore] public bool UsesGain =>
        Type is PeqFilterType.Peaking or PeqFilterType.LowShelf or PeqFilterType.HighShelf;
    [JsonIgnore] public string ShortFrequency => Frequency >= 1000f
        ? $"{Frequency / 1000f:0.#} kHz" : $"{Frequency:0.#} Hz";

    [JsonIgnore] public string Label
    {
        get
        {
            var frequency = Frequency >= 1000f
                ? (Frequency / 1000f).ToString("0.0") + " kHz"
                : Frequency.ToString("0") + " Hz";
            return UsesGain ? $"{frequency}   {GainDb:+0.0;-0.0;0.0} dB" : $"{frequency}   {Type}";
        }
    }

    internal static float Clamp(float value, float min, float max)
    {
        if (!float.IsFinite(value)) return min;
        return value < min ? min : value > max ? max : value;
    }
}

public sealed class CompressorSettings : Observable
{
    private bool _enabled;
    private float _thresholdDb = -20f;
    private float _ratio = 4f;
    private float _attackMs = 10f;
    private float _releaseMs = 100f;
    private float _makeupDb;
    private float _kneeDb = 6f;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public float ThresholdDb { get => _thresholdDb; set => Set(ref _thresholdDb, PeqBand.Clamp(value, -60f, 0f)); }
    public float Ratio { get => _ratio; set => Set(ref _ratio, PeqBand.Clamp(value, 1f, 20f)); }
    public float AttackMs { get => _attackMs; set => Set(ref _attackMs, PeqBand.Clamp(value, 0.1f, 200f)); }
    public float ReleaseMs { get => _releaseMs; set => Set(ref _releaseMs, PeqBand.Clamp(value, 10f, 2000f)); }
    public float MakeupDb { get => _makeupDb; set => Set(ref _makeupDb, PeqBand.Clamp(value, 0f, 24f)); }
    public float KneeDb { get => _kneeDb; set => Set(ref _kneeDb, PeqBand.Clamp(value, 0f, 24f)); }
}

public sealed class LimiterSettings : Observable
{
    private bool _enabled;
    private float _ceilingDb = -1f;
    private float _releaseMs = 50f;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public float CeilingDb { get => _ceilingDb; set => Set(ref _ceilingDb, PeqBand.Clamp(value, -6f, 0f)); }
    public float ReleaseMs { get => _releaseMs; set => Set(ref _releaseMs, PeqBand.Clamp(value, 1f, 1000f)); }
}

public sealed class SpatialSettings : Observable
{
    private bool _crossfeedEnabled;
    private float _crossfeedAmount = .12f;
    public bool CrossfeedEnabled { get => _crossfeedEnabled; set => Set(ref _crossfeedEnabled, value); }
    public float CrossfeedAmount
    {
        get => _crossfeedAmount;
        set => Set(ref _crossfeedAmount, PeqBand.Clamp(value, 0f, .35f));
    }
}

/// <summary>
/// Signal chain attached to a profile. Persisted with the profile and referenced by profile id so
/// profiles, automation rules and the processing engine stay independent of one another.
/// </summary>
public sealed class DspSettings : Observable
{
    public const int MaxBands = 16;
    public const int MaxGraphicBands = 31;
    public static readonly float[] GraphicFrequencies =
        [20, 25, 31.5f, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500,
         630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000,
         10000, 12500, 16000, 20000];
    public static readonly float[] StartingFrequencies =
        [31.5f, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000, 20000];

    private ObservableCollection<PeqBand> _bands = [];
    private ObservableCollection<PeqBand> _graphicBands = [];
    private SpatialSettings _spatial = new();
    private bool _enabled;
    private bool _graphicEnabled;
    // One-time compact-layout migration; old adjusted extras remain recoverable in the profile.
    public bool CompactEqMigrated { get; set; }
    public List<PeqBand> ArchivedGraphicBands { get; set; } = [];

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool GraphicEnabled
    {
        get => _graphicEnabled;
        set { if (Set(ref _graphicEnabled, value)) { Raise(nameof(HasContent)); Raise(nameof(Summary)); } }
    }
    public SpatialSettings Spatial
    {
        get => _spatial;
        set
        {
            var next = value ?? new SpatialSettings();
            if (ReferenceEquals(_spatial, next)) return;
            _spatial.PropertyChanged -= ChildChanged;
            _spatial = next;
            _spatial.PropertyChanged += ChildChanged;
            Raise(nameof(Spatial));
            Raise(nameof(HasContent));
            Raise(nameof(Summary));
        }
    }

    public ObservableCollection<PeqBand> GraphicBands
    {
        get => _graphicBands;
        set
        {
            var next = value ?? [];
            if (ReferenceEquals(_graphicBands, next)) return;
            foreach (var band in _graphicBands) band.PropertyChanged -= BandChanged;
            _graphicBands.CollectionChanged -= GraphicBandsChanged;
            _graphicBands = next;
            next.CollectionChanged += GraphicBandsChanged;
            foreach (var band in next) band.PropertyChanged += BandChanged;
            Raise(nameof(GraphicBands));
            Raise(nameof(HasContent));
            Raise(nameof(Summary));
        }
    }

    public void CreateGraphicBands()
    {
        if (_graphicBands.Count != 0) return;
        GraphicBands = new ObservableCollection<PeqBand>(StartingFrequencies.Select(f => new PeqBand
        {
            Type = PeqFilterType.Peaking, Frequency = f, Q = 1.4f, GainDb = 0
        }));
        CompactEqMigrated = true;
        GraphicEnabled = true;
    }

    public PeqBand? AddGraphicBand()
    {
        if (_graphicBands.Count >= MaxGraphicBands) return null;
        if (_graphicBands.Count == 0) { CreateGraphicBands(); return _graphicBands[0]; }
        var candidate = GraphicFrequencies
            .Where(f => _graphicBands.All(b => Math.Abs(b.Frequency - f) > .01f))
            .OrderByDescending(f => _graphicBands.Min(b => Math.Abs(Math.Log(f / b.Frequency))))
            .FirstOrDefault();
        if (candidate == 0) return null;
        var band = new PeqBand { Type = PeqFilterType.Peaking, Frequency = candidate, Q = 1.4f, IsAdditional = true };
        var position = _graphicBands.TakeWhile(b => b.Frequency < candidate).Count();
        _graphicBands.Insert(position, band);
        return band;
    }

    public ObservableCollection<PeqBand> Bands
    {
        get => _bands;
        set
        {
            var next = value ?? [];
            var previous = _bands;
            if (ReferenceEquals(previous, next)) return;
            foreach (var band in previous) band.PropertyChanged -= BandChanged;
            previous.CollectionChanged -= BandsChanged;
            _bands = next;
            next.CollectionChanged += BandsChanged;
            foreach (var band in next) band.PropertyChanged += BandChanged;
            Raise(nameof(Bands));
            Raise(nameof(HasContent));
            Raise(nameof(Summary));
        }
    }

    public CompressorSettings Compressor { get; set; } = new();
    public LimiterSettings Limiter { get; set; } = new();

    [JsonIgnore] public bool HasContent =>
        Bands.Any(b => b.Enabled) || (GraphicEnabled && GraphicBands.Any(b => b.Enabled && (!b.UsesGain || Math.Abs(b.GainDb) > .001f)))
        || Compressor.Enabled || Limiter.Enabled || (Spatial.CrossfeedEnabled && Spatial.CrossfeedAmount > 0);

    [JsonIgnore]
    public string Summary => !Enabled ? "Processing off"
        : !HasContent ? "Processing on, nothing configured yet"
        : $"{Bands.Count(b => b.Enabled)} detailed band(s) · {GraphicBands.Count}-band EQ {(GraphicEnabled ? "on" : "off")} · crossfeed {(Spatial.CrossfeedEnabled ? "on" : "off")} · compressor {(Compressor.Enabled ? "on" : "off")} · limiter {(Limiter.Enabled ? "on" : "off")}";

    public DspSettings()
    {
        _bands.CollectionChanged += BandsChanged;
        _graphicBands.CollectionChanged += GraphicBandsChanged;
        Compressor.PropertyChanged += ChildChanged;
        Limiter.PropertyChanged += ChildChanged;
        _spatial.PropertyChanged += ChildChanged;
    }

    private void BandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PeqBand band in e.OldItems) band.PropertyChanged -= BandChanged;
        if (e.NewItems is not null)
            foreach (PeqBand band in e.NewItems) band.PropertyChanged += BandChanged;
        Raise(nameof(HasContent));
        Raise(nameof(Summary));
    }

    private void BandChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(Summary));
    private void GraphicBandsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (PeqBand band in e.OldItems) band.PropertyChanged -= BandChanged;
        if (e.NewItems is not null)
            foreach (PeqBand band in e.NewItems) band.PropertyChanged += BandChanged;
        Raise(nameof(HasContent));
        Raise(nameof(Summary));
    }
    private void ChildChanged(object? sender, PropertyChangedEventArgs e) => Raise(nameof(Summary));

    /// <summary>
    /// Repairs missing, null and out-of-range values after loading settings from disk, then returns
    /// this instance. Non-finite numbers collapse to each setting's minimum rather than propagating.
    /// </summary>
    public DspSettings Normalize()
    {
        ArchivedGraphicBands ??= [];
        Compressor ??= new CompressorSettings();
        Limiter ??= new LimiterSettings();
        Spatial ??= new SpatialSettings();

        var kept = new List<PeqBand>();
        foreach (var band in _bands)
        {
            if (band is null || kept.Count >= MaxBands) continue;
            kept.Add(new PeqBand
            {
                Enabled = band.Enabled,
                Type = Enum.IsDefined(typeof(PeqFilterType), band.Type) ? band.Type : PeqFilterType.Peaking,
                Frequency = band.Frequency,
                GainDb = band.GainDb,
                Q = band.Q
            });
        }
        Bands = new ObservableCollection<PeqBand>(kept);

        if (_graphicBands.Count > 0)
        {
            var legacy31 = _graphicBands.Count == GraphicFrequencies.Length &&
                _graphicBands.Zip(GraphicFrequencies).All(pair =>
                    pair.First is not null && Math.Abs(pair.First.Frequency - pair.Second) < .01f &&
                    pair.First.Type == PeqFilterType.Peaking && Math.Abs(pair.First.Q - 4.3f) < .01f);
            var source = _graphicBands.Where(b => b is not null).Take(MaxGraphicBands);
            var migratedFromFirstFlexibleLayout = !legacy31 && _graphicBands.Count > StartingFrequencies.Length &&
                _graphicBands.All(b => b is not null && !b.IsAdditional);
            if (legacy31)
                source = source.Where(b => StartingFrequencies.Any(f => Math.Abs(f - b.Frequency) < .01f) ||
                    Math.Abs(b.GainDb) > .001f || !b.Enabled);
            GraphicBands = new ObservableCollection<PeqBand>(source.Select(b => new PeqBand
            {
                Enabled = b.Enabled,
                Type = Enum.IsDefined(typeof(PeqFilterType), b.Type) ? b.Type : PeqFilterType.Peaking,
                Frequency = b.Frequency,
                Q = legacy31 ? 1.4f : b.Q,
                GainDb = b.GainDb,
                IsAdditional = b.IsAdditional ||
                    (legacy31 || migratedFromFirstFlexibleLayout) &&
                    !StartingFrequencies.Any(f => Math.Abs(f - b.Frequency) < .01f)
            }).OrderBy(b => b.Frequency));
            if (!CompactEqMigrated)
            {
                ArchivedGraphicBands ??= [];
                ArchivedGraphicBands.AddRange(GraphicBands.Where(b => b.IsAdditional));
                GraphicBands = new ObservableCollection<PeqBand>(GraphicBands.Where(b => !b.IsAdditional));
                CompactEqMigrated = true;
            }
        }
        else GraphicEnabled = false;

        // Assigning through the setters clamps whatever came off disk into the supported range.
        Compressor.ThresholdDb = Compressor.ThresholdDb;
        Compressor.Ratio = Compressor.Ratio;
        Compressor.AttackMs = Compressor.AttackMs;
        Compressor.ReleaseMs = Compressor.ReleaseMs;
        Compressor.MakeupDb = Compressor.MakeupDb;
        Compressor.KneeDb = Compressor.KneeDb;
        Limiter.CeilingDb = Limiter.CeilingDb;
        Limiter.ReleaseMs = Limiter.ReleaseMs;
        Spatial.CrossfeedAmount = Spatial.CrossfeedAmount;
        return this;
    }
}
