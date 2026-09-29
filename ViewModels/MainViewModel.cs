using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AudioFlow.Models;
using AudioFlow.Services;

namespace AudioFlow.ViewModels;

public sealed class MainViewModel : Observable, IDisposable
{
    private readonly IAudioService _audio;
    private readonly IAudioChangeNotifier? _notifier;
    private readonly SettingsStore _store;
    private readonly IStartupService _startup;
    private readonly IDiagnosticLog _log;
    private string? _startupWarning;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _eqSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private AppSettings _settings;
    private AudioDevice? _selectedDevice;
    private AudioProfile? _selectedProfile;
    private string _status = "Starting audio engine…";
    private bool _startWithWindows;
    private bool _refreshing;
    private readonly AutomationService _automation = new();
    private readonly ProfileService _profiles;
    private readonly IProcessingEngine _engine = ProcessingEngines.CreateDefault();
    private readonly EqPreviewAudio _eqPreview = new();
    private ProcessingStatus _lastProcessingStatus = ProcessingStatus.NotAvailable;
    private HashSet<string> _previousDevices = [];
    private bool _restoreOnRefresh = true;
    private bool _started;
    private float _masterVolume;
    private bool _masterMuted;
    private bool _hasOutput;
    private bool _audioUnavailable;
    private volatile bool _disposed;
    private bool _suspended;
    public bool HasOutput { get => _hasOutput; private set { if (Set(ref _hasOutput, value)) CommandManager.InvalidateRequerySuggested(); } }
    public string LogDirectory => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFlow", "Logs");
    private string _activitySummary = "";
    public string ActivitySummary { get => _activitySummary; private set => Set(ref _activitySummary, value); }

    public ObservableCollection<AudioSession> Sessions { get; } = [];
    public ObservableCollection<AudioDevice> Devices { get; } = [];
    public ObservableCollection<AudioProfile> Profiles { get; } = [];
    public ObservableCollection<AutomationRule> Rules { get; } = [];
    public AudioDevice? SelectedDevice { get => _selectedDevice; set { if (Set(ref _selectedDevice, value) && value is not null && !value.IsDefault && !_refreshing) { try { _audio.SetDefaultDevice(value.Id); Refresh(); } catch (Exception ex) { Status = $"Could not switch output: {ex.Message}"; } } } }
    public AudioProfile? SelectedProfile { get => _selectedProfile; set { if (Set(ref _selectedProfile, value)) { _eqPreview.Stop(); EqPreviewStatus = ""; EnsureGraphicEq(value); UpdateProcessingState(); if (value is not null && _started) ApplyProfile(value); } } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public float MasterVolume { get => _masterVolume; set { if (Set(ref _masterVolume, value) && !_refreshing) TryAudioChange(() => _audio.SetMasterVolume(value), "master volume"); } }
    public bool MasterMuted { get => _masterMuted; set { if (Set(ref _masterMuted, value) && !_refreshing) TryAudioChange(() => _audio.SetMasterMute(value), "master mute"); } }
    private string _emptyMessage = "";
    public string EmptyMessage { get => _emptyMessage; private set => Set(ref _emptyMessage, value); }
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (value == _startWithWindows) return;
            try
            {
                _startup.IsEnabled = value;
                Set(ref _startWithWindows, value);
                _settings.StartWithWindows = value;
                if (Save()) Status = value ? "Start with Windows enabled." : "Start with Windows disabled.";
            }
            catch (Exception ex)
            {
                _log.Write("startup.change", ex);
                Status = $"Could not change Windows startup: {ex.Message}";
                Raise(nameof(StartWithWindows));
            }
        }
    }
    public bool CanChangeStartupHere => !_startup.IsManagedByWindows;
    public string StartupDescription => _startup.IsManagedByWindows
        ? "Packaged app: manage AudioFlow in Windows Settings > Apps > Startup. Its enabled state is shown there."
        : "Launch AudioFlow in the tray when you sign in to Windows.";
    public bool StartMinimized
    {
        get => _settings.StartMinimized;
        set { if (value == _settings.StartMinimized) return; _settings.StartMinimized = value; Raise(); if (Save()) Status = value ? "AudioFlow will open in the system tray." : "AudioFlow will show its window when opened."; }
    }
    public bool IsExiting { get; set; }
    public event EventHandler? ExitRequested;
    public ICommand RefreshCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand ApplyProfileCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand MoveRuleUpCommand { get; }
    public ICommand MoveRuleDownCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand OpenLogsCommand { get; }
    public ICommand OpenStartupSettingsCommand { get; }
    public ICommand AddBandCommand { get; }
    public ICommand RemoveBandCommand { get; }
    public ICommand ResetProcessingCommand { get; }
    public ICommand CreateGraphicEqCommand { get; }
    public ICommand AddGraphicEqBandCommand { get; }
    public ICommand RemoveGraphicEqBandCommand { get; }
    public ICommand ResetGraphicEqCommand { get; }
    public ICommand PreviewBeforeCommand { get; }
    public ICommand PreviewAfterCommand { get; }
    public ICommand StopPreviewCommand { get; }
    private string _eqPreviewStatus = "";
    public string EqPreviewStatus { get => _eqPreviewStatus; private set => Set(ref _eqPreviewStatus, value); }

    public DspSettings? Dsp => SelectedProfile?.Dsp;
    private PeqBand? _selectedEqBand;
    public PeqBand? SelectedEqBand { get => _selectedEqBand; set { if (Set(ref _selectedEqBand, value)) CommandManager.InvalidateRequerySuggested(); } }
    public IReadOnlyList<PeqFilterType> GraphicEqTypes { get; } = Enum.GetValues<PeqFilterType>();
    public bool GraphicEqActive
    {
        get => Dsp is { Enabled: true, GraphicEnabled: true };
        set
        {
            if (Dsp is not { } dsp || value == GraphicEqActive) return;
            if (value && dsp.GraphicBands.Count == 0) dsp.CreateGraphicBands();
            dsp.GraphicEnabled = value;
            if (value) dsp.Enabled = true;
            Raise();
            Save();
        }
    }
    public string ProcessingNote => _engine.IsAvailable
        ? $"Processing back end: {_engine.Name}."
        : _engine.UnavailableReason;
    public IReadOnlyList<string> PeqTypes { get; } = Enum.GetNames<PeqFilterType>();
    private string _processingStatusText = "";
    // Named ...Text because a member called ProcessingStatus would shadow the ProcessingStatus enum.
    public string ProcessingStatusText { get => _processingStatusText; private set => Set(ref _processingStatusText, value); }
    private IReadOnlyList<string> _dspNotes = [];
    public IReadOnlyList<string> DspNotes { get => _dspNotes; private set => Set(ref _dspNotes, value); }

    public MainViewModel(IAudioService audio, SettingsStore store, IStartupService startup, IDiagnosticLog? log = null)
    {
        _audio = audio; _notifier = audio as IAudioChangeNotifier;
        if (_notifier is not null)
        {
            _notifier.Changed += OnAudioChanged;
            _notifier.MasterStateChanged += OnMasterStateChanged;
        }
        _profiles = new ProfileService(audio); _store = store; _startup = startup; _settings = store.Load();
        _log = log ?? new NullDiagnosticLog();
        _eqPreview.PlaybackFailed += message => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed) EqPreviewStatus = $"Audio preview stopped: {message}";
        });
        foreach (var profile in _settings.Profiles) Profiles.Add(profile);
        WireProfiles();
        foreach (var rule in _settings.Rules) TrackRule(rule);
        try { _startWithWindows = startup.IsEnabled; }
        catch (Exception ex) { _log.Write("startup.read", ex); _startupWarning = $"Windows startup settings unavailable: {ex.Message}"; }
        RefreshCommand = new RelayCommand(Refresh);
        SaveProfileCommand = new RelayCommand(CaptureProfile, () => SelectedProfile is not null && HasOutput);
        ApplyProfileCommand = new RelayCommand(() => { if (SelectedProfile is not null) ApplyProfile(SelectedProfile); }, () => SelectedProfile is not null);
        AddProfileCommand = new RelayCommand(AddProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile, () => Profiles.Count > 1 && SelectedProfile is not null);
        AddRuleCommand = new RelayCommand(AddRule);
        DeleteRuleCommand = new RelayCommand<AutomationRule>(DeleteRule);
        MoveRuleUpCommand = new RelayCommand<AutomationRule>(r => MoveRule(r, -1));
        MoveRuleDownCommand = new RelayCommand<AutomationRule>(r => MoveRule(r, 1));
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        OpenStartupSettingsCommand = new RelayCommand(() =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
            catch (Exception ex) { _log.Write("startup.settings.open", ex); Status = $"Could not open Windows startup settings: {ex.Message}"; }
        });
        OpenLogsCommand = new RelayCommand(() =>
        {
            try { System.IO.Directory.CreateDirectory(LogDirectory); Process.Start(new ProcessStartInfo(LogDirectory) { UseShellExecute = true }); }
            catch (Exception ex) { _log.Write("logs.open", ex); Status = $"Could not open logs: {ex.Message}"; }
        });
        _timer.Tick += (_, _) => Refresh();
        _eqSaveTimer.Tick += (_, _) => { _eqSaveTimer.Stop(); Save(); };

        AddBandCommand = new RelayCommand(AddBand, () => CanEditDsp && (Dsp?.Bands.Count ?? 0) < DspSettings.MaxBands);
        RemoveBandCommand = new RelayCommand<PeqBand>(RemoveBand, _ => CanEditDsp);
        ResetProcessingCommand = new RelayCommand(ResetProcessing, () => CanEditDsp);
        CreateGraphicEqCommand = new RelayCommand(() => { Dsp?.CreateGraphicBands(); if (Dsp is not null) Dsp.Enabled = true; Save(); },
            () => Dsp is { GraphicBands.Count: 0 });
        AddGraphicEqBandCommand = new RelayCommand(() =>
        {
            if (Dsp is not { } dsp) return;
            SelectedEqBand = dsp.AddGraphicBand();
            Save();
        }, () => Dsp is { GraphicBands.Count: < DspSettings.MaxGraphicBands });
        RemoveGraphicEqBandCommand = new RelayCommand(() =>
        {
            if (Dsp is not { } dsp || SelectedEqBand is not { } band) return;
            dsp.GraphicBands.Remove(band);
            SelectedEqBand = dsp.GraphicBands.FirstOrDefault();
            Save();
        }, () => Dsp is { GraphicBands.Count: > 1 } && SelectedEqBand is not null);
        ResetGraphicEqCommand = new RelayCommand(() =>
        {
            if (Dsp is not { } dsp) return;
            dsp.GraphicBands = new ObservableCollection<PeqBand>(dsp.GraphicBands.Select(b =>
                new PeqBand { Type = PeqFilterType.Peaking, Frequency = b.Frequency, Q = 1.4f }));
            SelectedEqBand = dsp.GraphicBands.FirstOrDefault();
            Save();
        }, () => Dsp is { GraphicBands.Count: > 0 });
        PreviewBeforeCommand = new RelayCommand(() => PlayEqPreview(false), () => SelectedProfile is not null);
        PreviewAfterCommand = new RelayCommand(() => PlayEqPreview(true),
            () => GraphicEqActive);
        StopPreviewCommand = new RelayCommand(() => { _eqPreview.Stop(); EqPreviewStatus = "Preview stopped."; });
        UpdateProcessingState();
    }

    private bool CanEditDsp => SelectedProfile is not null;

    private void EnsureGraphicEq(AudioProfile? profile)
    {
        if (profile is null) { SelectedEqBand = null; return; }
        if (profile.Dsp.GraphicBands.Count == 0)
        {
            profile.Dsp.CreateGraphicBands();
            Save();
        }
        SelectedEqBand = profile.Dsp.GraphicBands.FirstOrDefault();
    }

    private void AddBand()
    {
        if (Dsp is not { } dsp) return;
        if (dsp.Bands.Count >= DspSettings.MaxBands) return;
        dsp.Bands.Add(new PeqBand { Enabled = true, Type = PeqFilterType.Peaking, Frequency = 1000f, GainDb = 0f, Q = 1f });
        Save();
    }

    private void RemoveBand(PeqBand? band)
    {
        if (band is null || Dsp is not { } dsp) return;
        dsp.Bands.Remove(band);
        Save();
    }

    private void ResetProcessing()
    {
        if (SelectedProfile is not { } profile) return;
        _eqPreview.Stop();
        if (profile.Dsp is not null) profile.Dsp.PropertyChanged -= DspChanged;
        var replacement = new DspSettings();
        replacement.Bands = new ObservableCollection<PeqBand>();
        profile.Dsp = replacement;
        replacement.PropertyChanged += DspChanged;
        UpdateProcessingState();
        Save();
    }

    private void DspChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (ReferenceEquals(sender, Dsp) && _eqPreview.IsPlaying && Dsp is { } playingDsp)
            _eqPreview.UpdateSettings(playingDsp);
        SelectedProfile?.NotifyProcessing();
        Raise(nameof(GraphicEqActive));
        UpdateProcessingState();
        if (e.PropertyName == nameof(DspSettings.Summary))
        {
            _eqSaveTimer.Stop();
            _eqSaveTimer.Start();
        }
        else Save();
    }

    private void PlayEqPreview(bool processed)
    {
        if (Dsp is not { } dsp) return;
        try
        {
            _eqPreview.Play(dsp, processed);
            EqPreviewStatus = processed
                ? "Live EQ sample looping. Move a slider to hear the change; press Stop when finished."
                : "Original sample looping without AudioFlow EQ; press Stop when finished.";
        }
        catch (Exception ex)
        {
            _log.Write("eq.preview", ex);
            EqPreviewStatus = $"Could not play the EQ preview: {ex.Message}";
        }
    }

    private void UpdateProcessingState()
    {
        var profile = SelectedProfile;
        // Reads the outcome of the last real Apply call. Calling the engine here would re-apply the
        // chain on every keystroke once a back end exists.
        ProcessingStatusText = profile is null ? "No profile selected."
            : !profile.Dsp.Enabled ? "Processing is off for this profile."
            : !profile.Dsp.HasContent ? "Processing is on, but no filters are configured."
            : _lastProcessingStatus switch
            {
                ProcessingStatus.Active => $"Applied through {_engine.Name}.",
                ProcessingStatus.Inactive => "Every stage is bypassed.",
                ProcessingStatus.Failed => "The processing back end reported a failure.",
                _ => "Saved with the profile. Not rendered: no DSP back end is installed."
            };
        DspNotes = DspChainDiagnostics.Describe(profile);
        Raise(nameof(Dsp));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Re-attaches change tracking after a profile is loaded, created or deleted.</summary>
    private void WireProfiles()
    {
        foreach (var profile in Profiles)
        {
            profile.Dsp ??= new DspSettings();
            profile.Dsp.PropertyChanged -= DspChanged;
            profile.Dsp.PropertyChanged += DspChanged;
        }
    }

    public void Start()
    {
        if (_started || _disposed) return;
        _selectedProfile = Profiles.FirstOrDefault(x => x.Id == _settings.ActiveProfileId) ?? Profiles.FirstOrDefault();
        EnsureGraphicEq(_selectedProfile);
        Raise(nameof(SelectedProfile));
        Raise(nameof(GraphicEqActive));
        UpdateProcessingState();
        _started = true;
        Refresh();
        if (Status == "Starting audio engine…")
            Status = _store.LoadWarning ?? _startupWarning ?? SelectedProfile?.Summary ?? "Ready";
        _timer.Start();
    }

    private void Refresh() => Refresh(true);

    public void Suspend() { _suspended = true; _timer.Stop(); }
    public void Resume()
    {
        if (_disposed || !_started) return;
        _suspended = false;
        _processNamesScanned = DateTime.MinValue;
        _log.Write("audio.resume");
        Refresh();
        _timer.Start();
    }

    private void Refresh(bool evaluateRules)
    {
        if (_refreshing || _disposed || _suspended) return; _refreshing = true;
        try
        {
            var devices = _audio.GetOutputDevices();
            var reconnected = SelectedProfile?.OutputDeviceId is string target &&
                !_previousDevices.Contains(target) && devices.Any(d => d.Id == target);
            if (evaluateRules && (_restoreOnRefresh || reconnected || (_previousDevices.Count == 0 && devices.Count > 0)) && SelectedProfile is not null)
            {
                var notice = _profiles.Apply(SelectedProfile);
                if (notice is not null) Status = notice;
                _restoreOnRefresh = false;
                devices = _audio.GetOutputDevices();
            }
            _previousDevices = devices.Select(d => d.Id).ToHashSet();
            Sync(Devices, devices, x => x.Id);
            _selectedDevice = Devices.FirstOrDefault(x => x.IsDefault); Raise(nameof(SelectedDevice));
            HasOutput = _selectedDevice is not null;
            if (_selectedDevice is not null)
            {
                var master = _audio.GetMasterState();
                _masterVolume = master.Volume; _masterMuted = master.IsMuted;
                Raise(nameof(MasterVolume)); Raise(nameof(MasterMuted));
            }
            else
            {
                _masterVolume = 0; _masterMuted = false;
                Raise(nameof(MasterVolume)); Raise(nameof(MasterMuted));
            }
            IReadOnlyList<AudioSession> sessions = Devices.Count > 0 ? _audio.GetSessions() : [];
            var byId = Sessions.ToDictionary(x => x.Id);
            foreach (var session in sessions)
            {
                if (byId.Remove(session.Id, out var old)) old.Update(session.Volume, session.IsMuted, session.IsActive);
                else
                {
                    session.ControlFailed = message => Status = message;
                    Sessions.Add(session);
                    if (SelectedProfile is not null &&
                        (string.IsNullOrWhiteSpace(SelectedProfile.OutputDeviceId) ||
                         Devices.Any(d => d.Id == SelectedProfile.OutputDeviceId)))
                        ProfileService.ApplySession(SelectedProfile, session);
                }
            }
            foreach (var stale in byId.Values) Sessions.Remove(stale);
            ActivitySummary = $"{Sessions.Count} audio session{(Sessions.Count == 1 ? "" : "s")} across {Devices.Count} output{(Devices.Count == 1 ? "" : "s")}";
            EmptyMessage = !HasOutput ? "No default output is available. Choose an output to enable master controls." : Sessions.Count == 0 ? "No audio apps detected. Play something in Spotify, a browser, or a game to see its controls here." : "";
            if (_audioUnavailable && HasOutput) Status = "Audio connection restored.";
            _audioUnavailable = !HasOutput;
        }
        catch (Exception ex)
        {
            _audioUnavailable = true; HasOutput = false;
            Sessions.Clear(); Devices.Clear(); _selectedDevice = null; Raise(nameof(SelectedDevice));
            ActivitySummary = "Audio temporarily unavailable";
            _log.Write("audio.refresh", ex); Status = $"Audio service unavailable: {ex.Message}";
            EmptyMessage = "AudioFlow will retry automatically. Check that your output device is connected.";
            return;
        }
        finally { _refreshing = false; }
        if (evaluateRules)
        {
            try { EvaluateRules(); }
            catch (Exception ex) { _log.Write("automation.evaluate", ex); Status = $"Automation unavailable: {ex.Message}"; }
        }
    }

    private void EvaluateRules()
    {
        var profile = _automation.Evaluate(Rules, Profiles.ToList(), Devices.ToList(), GetProcessNames());
        if (profile is not null) { _selectedProfile = profile; Raise(nameof(SelectedProfile)); ApplyProfile(profile); }
    }

    private void ApplyProfile(AudioProfile profile)
    {
        try
        {
        _settings.ActiveProfileId = profile.Id;
        var warning = _profiles.Apply(profile);
        // Volume and mix first, then the chain, so a failing engine cannot block the mix.
        _lastProcessingStatus = _engine.Apply(profile);
        UpdateProcessingState();
        Refresh(false);
        if (Save()) Status = warning ?? $"{profile.Name} profile active";
        }
        catch (Exception ex) { _log.Write("profile.apply", ex); Status = $"Could not apply profile: {ex.Message}"; }
    }

    // Notifications arrive on a COM or thread-pool thread, so every hop lands on the UI thread.
    // Use the timer's owning dispatcher, which is also the owner of these collections.
    private void OnAudioChanged() => PostToUi(() => { if (!_disposed && !_refreshing) Refresh(); });

    /// <summary>Master volume or mute moved in another app; re-read just those two values.</summary>
    private void OnMasterStateChanged() => PostToUi(() =>
    {
        if (_disposed || _refreshing || !HasOutput) return;
        try
        {
            var master = _audio.GetMasterState();
            _masterVolume = master.Volume; _masterMuted = master.IsMuted;
            Raise(nameof(MasterVolume)); Raise(nameof(MasterMuted));
        }
        catch (Exception ex) { _log.Write("audio.master.refresh", ex); }
    });

    private void PostToUi(Action action)
    {
        if (_disposed) return;
        var dispatcher = _timer.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        try
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                // Recheck at execution time: a callback may have queued just before sleep/exit.
                if (_disposed || !_started || _suspended) return;
                action();
            }));
        }
        catch (Exception ex) { _log.Write("audio.notify.dispatch", ex); }
    }

    private string[] _processNames = [];
    private DateTime _processNamesScanned;
    private IReadOnlyCollection<string> GetProcessNames()
    {
        // Reused across the timer and notification refreshes so bursts of changes do not rescan constantly.
        if (DateTime.UtcNow - _processNamesScanned < TimeSpan.FromSeconds(2)) return _processNames;
        _processNamesScanned = DateTime.UtcNow;
        var processes = Process.GetProcesses();
        try { _processNames = processes.Select(p => { try { return p.ProcessName; } catch { return ""; } }).Where(p => p.Length > 0).ToArray(); }
        finally { foreach (var process in processes) process.Dispose(); }
        return _processNames;
    }

    private void CaptureProfile()
    {
        if (SelectedProfile is null) return;
        try
        {
            _profiles.Capture(SelectedProfile);
            if (Save()) Status = $"Saved {SelectedProfile.Name}: {SelectedProfile.Summary}";
        }
        catch (Exception ex) { _log.Write("profile.capture", ex); Status = $"Could not save profile: {ex.Message}"; }
    }

    private void AddProfile()
    {
        var dialog = new TextPrompt("New profile", "Profile name", "My profile") { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        var profile = new AudioProfile { Name = dialog.Value.Trim(), Glyph = "✦" }; Profiles.Add(profile); WireProfiles(); _settings.Profiles.Add(profile); SelectedProfile = profile; CaptureProfile();
    }
    private void DeleteProfile()
    {
        if (SelectedProfile is null || Profiles.Count <= 1) return;
        var old = SelectedProfile; var next = Profiles.First(x => x != old);
        if (old.Dsp is not null) old.Dsp.PropertyChanged -= DspChanged;
        Profiles.Remove(old); _settings.Profiles.RemoveAll(x => x.Id == old.Id); _settings.Rules.RemoveAll(x => x.ProfileId == old.Id);
        foreach (var rule in Rules.Where(x => x.ProfileId == old.Id).ToList()) Rules.Remove(rule); SelectedProfile = next; Save();
    }
    private void AddRule()
    {
        var dialog = new RuleDialog(Profiles) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true || dialog.Rule is null) return;
        TrackRule(dialog.Rule); _settings.Rules.Add(dialog.Rule); if (Save()) Status = "Automation rule added";
    }
    private void DeleteRule(AutomationRule? rule) { if (rule is null) return; rule.PropertyChanged -= RuleChanged; Rules.Remove(rule); _settings.Rules.RemoveAll(x => x.Id == rule.Id); Save(); }
    private void TrackRule(AutomationRule rule) { rule.Description = Profiles.FirstOrDefault(p => p.Id == rule.ProfileId)?.Name ?? "Missing profile"; rule.PropertyChanged += RuleChanged; Rules.Add(rule); }
    private void RuleChanged(object? sender, PropertyChangedEventArgs e) { _automation.Invalidate(); Save(); }
    private void MoveRule(AutomationRule? rule, int offset)
    {
        if (rule is null) return;
        var index = Rules.IndexOf(rule); var next = index + offset;
        if (index < 0 || next < 0 || next >= Rules.Count) return;
        Rules.Move(index, next); _settings.Rules = Rules.ToList(); _automation.Invalidate(); Save();
    }
    private bool Save()
    {
        try { _settings.StartWithWindows = StartWithWindows; _store.Save(_settings); return true; }
        catch (Exception ex) { _log.Write("settings.save", ex); Status = $"Settings could not be saved: {ex.Message}"; return false; }
    }
    private void TryAudioChange(Action change, string control)
    {
        try { change(); }
        catch (Exception ex) { _log.Write("audio." + control, ex); Status = $"Could not change {control}: {ex.Message}"; }
    }
    private static void Sync<T, TKey>(ObservableCollection<T> target, IReadOnlyList<T> source, Func<T, TKey> key) where TKey : notnull
    {
        var wanted = source.ToDictionary(key); foreach (var old in target.Where(x => !wanted.ContainsKey(key(x))).ToList()) target.Remove(old);
        foreach (var item in source) { var existing = target.FirstOrDefault(x => EqualityComparer<TKey>.Default.Equals(key(x), key(item))); if (existing is null) target.Add(item); else if (!EqualityComparer<T>.Default.Equals(existing, item)) { var index = target.IndexOf(existing); target[index] = item; } }
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (_eqSaveTimer.IsEnabled) { _eqSaveTimer.Stop(); Save(); }
        _disposed = true; _timer.Stop();
        if (_notifier is not null) { _notifier.Changed -= OnAudioChanged; _notifier.MasterStateChanged -= OnMasterStateChanged; }
        foreach (var rule in Rules) rule.PropertyChanged -= RuleChanged;
        foreach (var profile in Profiles) if (profile.Dsp is not null) profile.Dsp.PropertyChanged -= DspChanged;
        _eqPreview.Dispose();
        _audio.Dispose();
    }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
}
public sealed class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute?.Invoke(Coerce(parameter)) ?? true;
    public void Execute(object? parameter) => execute(Coerce(parameter));
    private static T? Coerce(object? parameter) => parameter is T typed ? typed : default;
}
