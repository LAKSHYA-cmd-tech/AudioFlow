using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace AudioFlow.Models;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new(name)); return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class AudioSession : Observable
{
    private float _volume; private bool _muted;
    public required string Id { get; init; }
    public required string ProcessName { get; init; }
    public required string DisplayName { get; init; }
    public int ProcessId { get; init; }
    public string OutputDeviceId { get; init; } = "";
    public string OutputDeviceName { get; init; } = "";
    public bool IsActive { get; private set; }
    public string PlaybackStatus => IsActive ? "Active" : "Idle";
    public string IconText => string.IsNullOrWhiteSpace(DisplayName) ? "♪" : DisplayName[..1].ToUpperInvariant();
    public float Volume { get => _volume; set { if (_volume == value) return; try { VolumeChanged?.Invoke(this, value); Set(ref _volume, value); } catch (Exception ex) when (ControlFailed is not null) { ControlFailed($"Could not change volume: {ex.Message}"); Raise(); } } }
    public bool IsMuted { get => _muted; set { if (_muted == value) return; try { MuteChanged?.Invoke(this, value); Set(ref _muted, value); } catch (Exception ex) when (ControlFailed is not null) { ControlFailed($"Could not change mute: {ex.Message}"); Raise(); } } }
    [JsonIgnore] public Action<string>? ControlFailed { get; set; }
    [JsonIgnore] public Action<AudioSession, float>? VolumeChanged { get; set; }
    [JsonIgnore] public Action<AudioSession, bool>? MuteChanged { get; set; }
    public void Update(float volume, bool muted, bool? active = null)
    {
        _volume = volume; _muted = muted;
        if (active is bool value) { IsActive = value; Raise(nameof(IsActive)); Raise(nameof(PlaybackStatus)); }
        Raise(nameof(Volume)); Raise(nameof(IsMuted));
    }
}

public sealed record AudioDevice(string Id, string Name, bool IsDefault);
public sealed record MasterAudioState(float Volume, bool IsMuted);
public sealed class VolumeSetting
{
    public string ProcessName { get; set; } = "";
    public string? OutputDeviceId { get; set; }
    public float Volume { get; set; } = 1;
    public bool Muted { get; set; }
}
public sealed class AudioProfile : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public string Glyph { get; set; } = "✦";
    public string? OutputDeviceId { get; set; }
    public float? MasterVolume { get; set; }
    public bool? MasterMuted { get; set; }
    public ObservableCollection<VolumeSetting> Applications { get; set; } = [];
    /// <summary>Signal chain for this profile. Referenced by profile id, applied by the processing engine.</summary>
    public DspSettings Dsp { get; set; } = new();
    [JsonIgnore] public bool HasSavedMix => OutputDeviceId is not null || MasterVolume is not null || Applications.Count > 0;
    [JsonIgnore] public string Summary => HasSavedMix
        ? $"Saved mix: master {(MasterVolume is float volume ? volume.ToString("P0") : "unchanged")}, {Applications.Count} app(s)"
        : "No mix saved yet. Adjust the controls, then select Save current mix.";
    [JsonIgnore] public string DspSummary => Dsp?.Summary ?? "Processing off";
    public void NotifySaved() { Raise(nameof(Summary)); Raise(nameof(HasSavedMix)); }
    public void NotifyProcessing() => Raise(nameof(DspSummary));

    /// <summary>Repairs missing or out-of-range values loaded from disk.</summary>
    public AudioProfile Normalize()
    {
        Dsp ??= new DspSettings();
        Dsp.Normalize();
        Applications ??= [];
        return this;
    }

    public override string ToString() => $"{Glyph}  {Name}";
}
public enum RuleTriggerType { ApplicationRunning, DeviceConnected }
public sealed class AutomationRule : Observable
{
    private bool _enabled = true;
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public RuleTriggerType TriggerType { get; set; }
    public string Match { get; set; } = "";
    public Guid ProfileId { get; set; }
    [JsonIgnore] public string Description { get; set; } = "";
}
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public List<AudioProfile> Profiles { get; set; } = [];
    public List<AutomationRule> Rules { get; set; } = [];
    public Guid? ActiveProfileId { get; set; }
}
