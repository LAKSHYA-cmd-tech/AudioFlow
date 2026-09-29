using System.IO;
using System.Text.Json;
using AudioFlow.Models;

namespace AudioFlow.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private bool _recovered;
    public string? LoadWarning { get; private set; }
    public SettingsStore(string? path = null) => _path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFlow", "settings.json"));
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path)) return Read(_path);
        }
        catch
        {
            TryPreserveCorruptFile();
            _recovered = true;
            LoadWarning = "Settings could not be read. Default profiles were loaded; the original file was left in place.";
        }
        try
        {
            if (File.Exists(_path + ".bak"))
            {
                var recovered = Read(_path + ".bak");
                _recovered = true;
                LoadWarning = "Settings were recovered from the previous saved backup.";
                return recovered;
            }
        }
        catch (Exception) { }
        return Defaults();
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(_path)) File.Replace(temp, _path, _recovered ? null : _path + ".bak", true);
            else File.Move(temp, _path);
            _recovered = false;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static AppSettings Read(string path)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new JsonException("Empty settings.");
        if (settings.Profiles is null || settings.Rules is null || settings.Profiles.Count == 0 ||
            settings.Profiles.Any(p => p is null || p.Applications is null || string.IsNullOrWhiteSpace(p.Name) ||
                p.Applications.Any(a => a is null || string.IsNullOrWhiteSpace(a.ProcessName) ||
                    !float.IsFinite(a.Volume) || a.Volume < 0 || a.Volume > 1) ||
                (p.MasterVolume is float volume && (!float.IsFinite(volume) || volume < 0 || volume > 1)) ||
                !IsValidDsp(p.Dsp)) ||
            settings.Profiles.Select(p => p.Id).Distinct().Count() != settings.Profiles.Count ||
            settings.Rules.Any(r => r is null || r.Match is null))
            throw new JsonException("Invalid profile or rule data.");
        foreach (var profile in settings.Profiles) profile.Normalize();
        return settings;
    }

    /// <summary>
    /// Rejects values that cannot be repaired later, such as NaN, so a bad file is preserved
    /// instead of silently loading as a chain of silent filters.
    /// </summary>
    private static bool IsValidDsp(DspSettings? dsp)
    {
        if (dsp is null) return true; // Normalize() creates a default chain.
        if (dsp.Bands is null) return false;
        // A band count above the limit is trimmed by Normalize() rather than failing the whole file.
        foreach (var band in dsp.Bands)
        {
            if (band is null || !float.IsFinite(band.Frequency) || !float.IsFinite(band.GainDb) || !float.IsFinite(band.Q))
                return false;
        }
        if (dsp.ArchivedGraphicBands is not null && dsp.ArchivedGraphicBands.Any(b => b is null ||
            !float.IsFinite(b.Frequency) || !float.IsFinite(b.GainDb) || !float.IsFinite(b.Q))) return false;
        var compressor = dsp.Compressor;
        if (compressor is null) return true;
        if (!float.IsFinite(compressor.ThresholdDb) || !float.IsFinite(compressor.Ratio) ||
            !float.IsFinite(compressor.AttackMs) || !float.IsFinite(compressor.ReleaseMs) ||
            !float.IsFinite(compressor.MakeupDb) || !float.IsFinite(compressor.KneeDb)) return false;
        var limiter = dsp.Limiter;
        if (limiter is null) return true;
        return float.IsFinite(limiter.CeilingDb) && float.IsFinite(limiter.ReleaseMs);
    }

    private void TryPreserveCorruptFile()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var recoveryPath = _path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(_path, recoveryPath, false);
        }
        catch { }
    }
    private static AppSettings Defaults()
    {
        var music = new AudioProfile { Name = "Music", Glyph = "♫" };
        var gaming = new AudioProfile { Name = "Gaming", Glyph = "◆" };
        var movie = new AudioProfile { Name = "Movie", Glyph = "▶" };
        var iem = new AudioProfile { Name = "IEM / Speakers", Glyph = "◉" };
        return new() { Profiles = [music, gaming, movie, iem], ActiveProfileId = music.Id };
    }
}
