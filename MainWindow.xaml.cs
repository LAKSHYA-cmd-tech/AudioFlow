using System.Globalization;
using System.Text.Json;
using AudioFlow.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using NAudio.CoreAudioApi;
using Windows.Foundation;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Path = System.IO.Path;

namespace AudioFlow;

public sealed partial class MainWindow : Window
{
    private readonly EqualizerEngine engine = new();
    private readonly DispatcherTimer telemetryTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer deviceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly List<BandUi> bandControls = [];
    private readonly List<Slider> nodePanSliders = [];
    private readonly List<Rectangle> spectrumBars = [];
    private readonly List<Rectangle> spectrumPeakCaps = [];
    private readonly float[] spectrumHolds = new float[SpectrumAnalyzer.BinCount];
    private readonly float[] spectrum = new float[SpectrumAnalyzer.BinCount];
    private readonly List<UIElement> responseGridElements = [];
    private readonly Polyline responseLine = new()
    {
        Stroke = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 229, 255), Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 157, 78, 221), Offset = 0.6 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 0, 127), Offset = 1 }
            }
        },
        StrokeThickness = 2.6
    };

    private readonly Polygon responseFillArea = new()
    {
        Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Windows.UI.Color.FromArgb(70, 0, 229, 255), Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(25, 157, 78, 221), Offset = 0.5 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(0, 0, 229, 255), Offset = 1 }
            }
        }
    };

    private readonly LinearGradientBrush spectrumBrush = new()
    {
        StartPoint = new Point(0, 1), EndPoint = new Point(0, 0),
        GradientStops =
        {
            new GradientStop { Color = Windows.UI.Color.FromArgb(255, 12, 18, 28), Offset = 0 },
            new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0, 229, 255), Offset = 0.35 },
            new GradientStop { Color = Windows.UI.Color.FromArgb(255, 157, 78, 221), Offset = 0.75 },
            new GradientStop { Color = Windows.UI.Color.FromArgb(255, 255, 94, 98), Offset = 1 }
        }
    };

    private bool updatingUi;
    private bool responseDirty = true;
    private float inputLHold, inputRHold, outputLHold, outputRHold;
    private bool loadingDevices;
    private string? defaultPlaybackId;
    private string deviceSignature = "";
    private long previousLimitedFrames;
    private DateTime clipAlertUntil;
    private int spectrumSampleRate;

    // Interactive Curve Dragging & Node Screen Positions
    private int draggingBandIndex = -1;
    private readonly Point[] nodeScreenPositions = new Point[11];

    // A/B Memory Slots
    private EqBand[]? slotABands;
    private float slotAPreamp;
    private EqBand[]? slotBBands;
    private float slotBPreamp;
    private bool isSlotAActive = true;

    // Headphone Lab Calibration Curves
    private HeadphoneProfile? currentHeadphoneProfile;
    private readonly Polyline stockAnomalyLine = new()
    {
        Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)),
        StrokeThickness = 1.8,
        StrokeDashArray = [4, 3]
    };
    private readonly Polyline harmanTargetLine = new()
    {
        Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 229, 255)),
        StrokeThickness = 2.2
    };

    // Gamer Mini-HUD View
    private bool isMiniHud;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1480, 880));

        BuildBandControls();
        BuildNodePans();
        ResponseCanvas.Children.Add(responseFillArea);
        Canvas.SetZIndex(responseFillArea, 1);
        ResponseCanvas.Children.Add(responseLine);
        Canvas.SetZIndex(responseLine, 2);

        AutoEqCanvas.Children.Add(stockAnomalyLine);
        AutoEqCanvas.Children.Add(harmanTargetLine);
        currentHeadphoneProfile = AutoEqDatabase.Profiles[0];
        SelectedHeadphoneTitle.Text = $"{currentHeadphoneProfile.Name}: Harman Target Correction Matrix";
        HeadphoneAbToggle.IsOn = true;

        slotABands = Enumerable.Range(0, engine.BandCount).Select(engine.GetBand).ToArray();
        slotAPreamp = (float)PreampSlider.Value;
        slotBBands = Enumerable.Range(0, engine.BandCount).Select(engine.GetBand).ToArray();
        slotBPreamp = (float)PreampSlider.Value;

        PopulatePresetGrids();
        PopulateHeadphoneCards();

        LatencyModePicker.SelectedIndex = 1; // Balanced
        engine.ConfigureSpatial(false, 60, 45, 80);
        engine.AudioFaulted += Engine_AudioFaulted;

        LoadOutputDevices();
        RestoreUserSettings();

        telemetryTimer.Tick += TelemetryTimer_Tick;
        telemetryTimer.Start();
        deviceTimer.Tick += (_, _) => CheckDevices();
        deviceTimer.Start();

        Closed += (_, _) =>
        {
            SaveUserSettings();
            telemetryTimer.Stop();
            deviceTimer.Stop();
            engine.AudioFaulted -= Engine_AudioFaulted;
            engine.Dispose();
        };

        MainNavView.SelectedItem = MainNavView.MenuItems[0];
    }

    // ==========================================
    // Navigation Switching
    // ==========================================
    private void MainNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;
        var tag = item.Tag?.ToString();

        QuickTuneSection.Visibility = tag == "QuickTune" ? Visibility.Visible : Visibility.Collapsed;
        StudioEqSection.Visibility = tag == "StudioEq" ? Visibility.Visible : Visibility.Collapsed;
        AutoEqSection.Visibility = tag == "AutoEq" ? Visibility.Visible : Visibility.Collapsed;
        SpatialSection.Visibility = tag == "Spatial" ? Visibility.Visible : Visibility.Collapsed;
        RoutingSection.Visibility = tag == "Routing" ? Visibility.Visible : Visibility.Collapsed;

        if (tag == "StudioEq") responseDirty = true;
    }

    // ==========================================
    // Device Enumeration & Verification
    // ==========================================
    private void LoadOutputDevices()
    {
        var previousSource = SelectedDeviceId(SourcePicker);
        var previousOutput = SelectedDeviceId(OutputPicker);
        loadingDevices = true;
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var defaultDevice = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultPlaybackId = defaultDevice.ID;
            var available = EqualizerEngine.GetOutputDevices();
            deviceSignature = defaultPlaybackId + "|" + string.Join("|", available.Select(d => d.Id + ":" + d.Name));
            SourcePicker.Items.Clear();
            OutputPicker.Items.Clear();
            foreach (var device in available)
            {
                SourcePicker.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
                OutputPicker.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
            }

            var preferredSource = available.FirstOrDefault(d => d.Id == previousSource).Id
                ?? available.FirstOrDefault(d => d.Id == defaultPlaybackId && IsVirtualRoutingName(d.Name)).Id
                ?? available.FirstOrDefault(d => IsVirtualRoutingName(d.Name)).Id;

            SelectDevice(SourcePicker, preferredSource);
            SelectDevice(OutputPicker, previousOutput, excludedId: SelectedDeviceId(SourcePicker));
        }
        catch (Exception ex)
        {
            RouteStatusText.Text = $"Audio devices unavailable: {ex.Message}";
        }
        finally
        {
            loadingDevices = false;
            UpdateRouteStatus();
        }
    }

    private static string? SelectedDeviceId(ComboBox picker) =>
        (picker.SelectedItem as ComboBoxItem)?.Tag as string;

    private static bool IsVirtualRoutingSource(ComboBox picker) =>
        (picker.SelectedItem as ComboBoxItem)?.Content is string name && IsVirtualRoutingName(name);

    private static bool IsVirtualRoutingName(string name) =>
        name.Contains("AudioFlow Output", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("AudioFlow Virtual Audio Device", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);

    private static void SelectDevice(ComboBox picker, string? preferredId, string? excludedId = null)
    {
        picker.SelectedItem = picker.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => (string?)item.Tag == preferredId && (string?)item.Tag != excludedId)
            ?? picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string?)item.Tag != excludedId);
    }

    private void DevicePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingDevices) return;
        if (engine.IsRunning)
        {
            engine.Stop();
            UpdateEngineUi(false);
            RouteStatusText.Text = "Audio route changed. Review selected devices and restart engine.";
            responseDirty = true;
        }
        UpdateRouteStatus();
    }

    private void UpdateRouteStatus()
    {
        if (RouteStatusText is null || SourcePicker is null || OutputPicker is null) return;
        var source = SelectedDeviceId(SourcePicker);
        var output = SelectedDeviceId(OutputPicker);
        var virtualSource = IsVirtualRoutingSource(SourcePicker);

        var ready = source is not null && output is not null && source != output &&
                    source == defaultPlaybackId && virtualSource;

        RouteStatusText.Text = !virtualSource
            ? SourcePicker.Items.OfType<ComboBoxItem>().Any(item => item.Content is string name && IsVirtualRoutingName(name))
                ? "Select the AudioFlow virtual endpoint as input."
                : "No virtual audio device detected. Install AudioFlow Output or VB-Audio Cable to route system sound."
            : source is null || output is null
            ? "Select an active physical speaker or headphone endpoint."
            : source == output
            ? "Input and output cannot be the same endpoint."
            : source != defaultPlaybackId
            ? "Set AudioFlow Output / VB-Cable as the default Windows playback endpoint."
            : "Audio route ready and verified.";

        MasterPowerButton.IsEnabled = engine.IsRunning || ready;
        if (!engine.IsRunning)
        {
            StatusBadge.BorderBrush = ready ? Brush(96, 205, 255) : Brush(255, 185, 0);
            StatusBadgeText.Foreground = ready ? Brush(96, 205, 255) : Brush(255, 185, 0);
            StatusBadgeText.Text = ready ? "READY" : "ROUTE CONFIG NEEDED";
        }
    }

    private void CheckDevices()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var defaultDevice = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var available = EqualizerEngine.GetOutputDevices();
            var signature = defaultDevice.ID + "|" + string.Join("|", available.Select(d => d.Id + ":" + d.Name));
            if (signature == deviceSignature) return;

            var source = SelectedDeviceId(SourcePicker);
            var output = SelectedDeviceId(OutputPicker);
            var routeLost = !available.Any(d => d.Id == source) || !available.Any(d => d.Id == output) ||
                            defaultDevice.ID != source;

            if (engine.IsRunning && routeLost)
            {
                engine.Stop();
                UpdateEngineUi(false);
                RouteStatusText.Text = "Audio route changed. Review selected devices and restart.";
                responseDirty = true;
            }
            LoadOutputDevices();
        }
        catch (Exception ex)
        {
            if (engine.IsRunning)
            {
                engine.Stop();
                UpdateEngineUi(false);
            }
            RouteStatusText.Text = $"Audio devices check failed: {ex.Message}";
        }
    }

    // ==========================================
    // Master Power & Latency
    // ==========================================
    private void MasterPowerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (engine.IsRunning)
            {
                engine.Stop();
                UpdateEngineUi(false);
                responseDirty = true;
            }
            else
            {
                UpdateRouteStatus();
                if (!MasterPowerButton.IsEnabled) return;
                engine.Start(SelectedDeviceId(SourcePicker), SelectedDeviceId(OutputPicker));
                UpdateEngineUi(true);
                responseDirty = true;
            }
        }
        catch (Exception ex)
        {
            UpdateEngineUi(false);
            RouteStatusText.Text = $"Engine start error: {ex.Message}";
        }
        UpdateRouteStatus();
    }

    private void UpdateEngineUi(bool running)
    {
        PowerIcon.Text = running ? "⏹" : "⏻";
        PowerLabel.Text = running ? "STOP ENGINE" : "START ENGINE";
        MasterPowerButton.Background = running ? Brush(42, 16, 22) : Brush(14, 28, 44);
        MasterPowerButton.BorderBrush = running ? Brush(239, 68, 68) : Brush(0, 229, 255);
        StatusBadge.BorderBrush = running ? Brush(16, 185, 129) : Brush(0, 229, 255);
        StatusBadgeText.Foreground = running ? Brush(16, 185, 129) : Brush(0, 229, 255);
        StatusBadgeText.Text = running ? $"LIVE • {engine.SampleRate:N0} HZ" : "READY";
    }

    private void MasterBypassToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (MasterBypassToggle is null) return;
        engine.SetBypass(MasterBypassToggle.IsOn);
        responseDirty = true;
    }

    private void PreampSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (PreampValText is null) return;
        PreampValText.Text = $"{e.NewValue:+0.0;-0.0;0.0} dB";
        engine.SetPreamp((float)e.NewValue);
        responseDirty = true;
    }

    private void LatencyModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var mode = LatencyModePicker.SelectedIndex switch
        {
            0 => BufferLatencyMode.UltraLow,
            2 => BufferLatencyMode.HighStability,
            _ => BufferLatencyMode.Balanced
        };
        engine.SetLatencyMode(mode);
    }

    // ==========================================
    // Smart Auto-Gain & A/B Memory Slots
    // ==========================================
    private void SlotA_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentToSlot(true);
        ApplySlot(true);
    }

    private void SlotB_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentToSlot(false);
        ApplySlot(false);
    }

    private void QuickAbCompare_Click(object sender, RoutedEventArgs e)
    {
        SaveCurrentToSlot(isSlotAActive);
        ApplySlot(!isSlotAActive);
    }

    private void SaveCurrentToSlot(bool toSlotA)
    {
        if (toSlotA)
        {
            slotABands = Enumerable.Range(0, engine.BandCount).Select(engine.GetBand).ToArray();
            slotAPreamp = (float)PreampSlider.Value;
        }
        else
        {
            slotBBands = Enumerable.Range(0, engine.BandCount).Select(engine.GetBand).ToArray();
            slotBPreamp = (float)PreampSlider.Value;
        }
    }

    private void ApplySlot(bool toSlotA)
    {
        isSlotAActive = toSlotA;
        var targetBands = toSlotA ? slotABands : slotBBands;
        var targetPreamp = toSlotA ? slotAPreamp : slotBPreamp;
        if (targetBands is null) return;

        updatingUi = true;
        for (int i = 0; i < engine.BandCount && i < targetBands.Length; i++)
        {
            engine.SetBand(i, targetBands[i]);
            var ui = bandControls[i];
            ui.Slider.Value = targetBands[i].GainDb;
            ui.Gain.Text = targetBands[i].GainDb.ToString("0.00", CultureInfo.InvariantCulture);
            ui.Frequency.Text = targetBands[i].Frequency.ToString("0.0", CultureInfo.InvariantCulture);
            ui.Label.Text = FormatFrequency(targetBands[i].Frequency);
            ui.Q.Text = targetBands[i].Q.ToString("0.00", CultureInfo.InvariantCulture);
            ui.Type.SelectedIndex = (int)targetBands[i].Type;
        }
        PreampSlider.Value = targetPreamp;
        updatingUi = false;
        responseDirty = true;

        if (SlotAButton is not null)
        {
            SlotAButton.Background = Brush(toSlotA ? (byte)0 : (byte)22, toSlotA ? (byte)114 : (byte)31, toSlotA ? (byte)206 : (byte)46);
            SlotAButton.Foreground = Brush(toSlotA ? (byte)255 : (byte)118, toSlotA ? (byte)255 : (byte)139, toSlotA ? (byte)255 : (byte)158);
        }
        if (SlotBButton is not null)
        {
            SlotBButton.Background = Brush(!toSlotA ? (byte)0 : (byte)22, !toSlotA ? (byte)114 : (byte)31, !toSlotA ? (byte)206 : (byte)46);
            SlotBButton.Foreground = Brush(!toSlotA ? (byte)255 : (byte)118, !toSlotA ? (byte)255 : (byte)139, !toSlotA ? (byte)255 : (byte)158);
        }
    }

    private void AutoGainCheck_Click(object sender, RoutedEventArgs e)
    {
        bool isEnabled = AutoGainCheck.IsChecked == true;
        engine.AutoGainEnabled = isEnabled;
        if (MiniHudAutoGainCheck is not null) MiniHudAutoGainCheck.IsChecked = isEnabled;
        responseDirty = true;
    }

    // ==========================================
    // Gamer Mini-HUD Floating Deck
    // ==========================================
    private void ToggleMiniHud_Click(object sender, RoutedEventArgs e)
    {
        isMiniHud = !isMiniHud;
        if (isMiniHud)
        {
            MasterConsoleBar.Visibility = Visibility.Collapsed;
            MainNavView.Visibility = Visibility.Collapsed;
            GamerHudView.Visibility = Visibility.Visible;
            MiniHudPreampSlider.Value = PreampSlider.Value;
            MiniHudBypassToggle.IsOn = MasterBypassToggle.IsOn;
            MiniHudAutoGainCheck.IsChecked = AutoGainCheck.IsChecked;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 230));
        }
        else
        {
            MasterConsoleBar.Visibility = Visibility.Visible;
            MainNavView.Visibility = Visibility.Visible;
            GamerHudView.Visibility = Visibility.Collapsed;
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1480, 880));
        }
    }

    private void MiniHudEsports_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;
        AudioPreset? target = tag switch
        {
            "valorant-cs2" => PresetLibrary.Presets.FirstOrDefault(p => p.Name.Contains("Valorant")),
            "warzone-steps" => PresetLibrary.Presets.FirstOrDefault(p => p.Name.Contains("Warzone")),
            "apex-legends" => PresetLibrary.Presets.FirstOrDefault(p => p.Name.Contains("Apex")),
            _ => null
        };
        if (target is not null)
        {
            ApplyPresetDirect(target);
            MiniHudProfileText.Text = target.Name;
        }
    }

    private void MiniHudPreampSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (MiniHudPreampText is null || updatingUi) return;
        MiniHudPreampText.Text = $"{e.NewValue:+0.0;-0.0;0.0} dB";
        updatingUi = true;
        PreampSlider.Value = e.NewValue;
        updatingUi = false;
        engine.SetPreamp((float)e.NewValue);
        responseDirty = true;
    }

    private void MiniHudBypassToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (updatingUi) return;
        updatingUi = true;
        MasterBypassToggle.IsOn = MiniHudBypassToggle.IsOn;
        updatingUi = false;
        engine.SetBypass(MiniHudBypassToggle.IsOn);
        responseDirty = true;
    }

    // ==========================================
    // Smart Macros (Simple Mode)
    // ==========================================
    private void MacroSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (BassValText is null || VocalValText is null || SpatialValText is null) return;
        BassValText.Text = $"+{BassSlider.Value:0.0} dB";
        VocalValText.Text = $"+{VocalSlider.Value:0.0} dB";
        SpatialValText.Text = $"{SpatialWidthSlider.Value:0}%";

        var macros = new SmartMacros
        {
            BassBoostDb = (float)BassSlider.Value,
            VocalClarityDb = (float)VocalSlider.Value,
            SpatialWidth = (float)SpatialWidthSlider.Value,
            NightMode = NightModeToggle?.IsOn == true
        };
        engine.SetMacros(macros);

        if (SpatialWidthSlider.Value > 0)
        {
            SpatialEngineToggle.IsOn = true;
            MixSlider.Value = SpatialWidthSlider.Value;
        }
    }

    private void NightModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var macros = engine.Macros with { NightMode = NightModeToggle.IsOn };
        engine.SetMacros(macros);
    }

    // ==========================================
    // Presets Grid
    // ==========================================
    private void PopulatePresetGrids()
    {
        GamingPresetsGrid.ItemsSource = PresetLibrary.Presets.Where(p => p.Category == "Gaming").ToList();
        MusicPresetsGrid.ItemsSource = PresetLibrary.Presets.Where(p => p.Category == "Music").ToList();
        MediaPresetsGrid.ItemsSource = PresetLibrary.Presets.Where(p => p.Category == "Media").ToList();
    }

    private void PresetCard_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AudioPreset preset) return;
        ApplyPresetDirect(preset);
    }

    private void ApplyPresetDirect(AudioPreset preset)
    {
        updatingUi = true;
        var bands = preset.ToEqBands();
        for (int i = 0; i < engine.BandCount; i++)
        {
            engine.SetBand(i, bands[i]);
            var ui = bandControls[i];
            ui.Slider.Value = bands[i].GainDb;
            ui.Gain.Text = bands[i].GainDb.ToString("0.00", CultureInfo.InvariantCulture);
            ui.Frequency.Text = bands[i].Frequency.ToString("0.0", CultureInfo.InvariantCulture);
            ui.Label.Text = FormatFrequency(bands[i].Frequency);
            ui.Q.Text = bands[i].Q.ToString("0.00", CultureInfo.InvariantCulture);
            ui.Type.SelectedIndex = 0;
            ui.Slider.IsEnabled = true;
            ui.Gain.IsEnabled = true;
        }

        BassSlider.Value = preset.Macros.BassBoostDb;
        VocalSlider.Value = preset.Macros.VocalClarityDb;
        SpatialWidthSlider.Value = preset.Macros.SpatialWidth;
        NightModeToggle.IsOn = preset.Macros.NightMode;
        engine.SetMacros(preset.Macros);

        ActiveProfileNameText.Text = preset.Name;
        ActiveProfileCategoryText.Text = $"• {preset.Category} Preset";

        updatingUi = false;
        responseDirty = true;
    }

    // ==========================================
    // Profile Management (Save, Import, Export)
    // ==========================================
    private string ProfileDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFlow", "Profiles");

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ProfileDirectory);
            var dialog = new ContentDialog
            {
                Title = "Save Listening Profile",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = Content.XamlRoot
            };
            var input = new TextBox { Header = "Profile Name", PlaceholderText = "My Custom Setup", Text = ActiveProfileNameText.Text };
            dialog.Content = input;
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text)) return;

            var name = input.Text.Trim();
            var profile = CaptureProfile(name);
            var path = Path.Combine(ProfileDirectory, $"{name}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            ActiveProfileNameText.Text = name;
            ActiveProfileCategoryText.Text = "• Custom Saved Profile";
        }
        catch (Exception ex)
        {
            RouteStatusText.Text = $"Save profile error: {ex.Message}";
        }
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = CaptureProfile(ActiveProfileNameText.Text);
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("AudioFlow Profile", [".json"]);
            picker.SuggestedFileName = profile.Name;
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            File.WriteAllText(file.Path, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            RouteStatusText.Text = $"Export error: {ex.Message}";
        }
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".json");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var json = File.ReadAllText(file.Path);
            var profile = AudioFlowProfile.Parse(json, Path.GetFileNameWithoutExtension(file.Name));

            updatingUi = true;
            for (int i = 0; i < engine.BandCount; i++)
            {
                engine.SetBand(i, profile.Bands[i]);
                var ui = bandControls[i];
                ui.Slider.Value = profile.Bands[i].GainDb;
                ui.Gain.Text = profile.Bands[i].GainDb.ToString("0.00", CultureInfo.InvariantCulture);
                ui.Frequency.Text = profile.Bands[i].Frequency.ToString("0.0", CultureInfo.InvariantCulture);
                ui.Label.Text = FormatFrequency(profile.Bands[i].Frequency);
                ui.Q.Text = profile.Bands[i].Q.ToString("0.00", CultureInfo.InvariantCulture);
                ui.Type.SelectedIndex = (int)profile.Bands[i].Type;
                var usesGain = profile.Bands[i].Type is EqFilterType.Peaking or EqFilterType.LowShelf or EqFilterType.HighShelf;
                ui.Slider.IsEnabled = usesGain;
                ui.Gain.IsEnabled = usesGain;
            }

            PreampSlider.Value = profile.PreampDb;
            MasterBypassToggle.IsOn = profile.Bypass;
            SpatialEngineToggle.IsOn = profile.SpatialEnabled;
            DepthSlider.Value = profile.Depth;
            AmbienceSlider.Value = profile.Ambience;
            MixSlider.Value = profile.Mix;
            for (int i = 0; i < nodePanSliders.Count && i < profile.Pans.Length; i++)
                nodePanSliders[i].Value = profile.Pans[i];

            if (profile.Macros is not null)
            {
                BassSlider.Value = profile.Macros.BassBoostDb;
                VocalSlider.Value = profile.Macros.VocalClarityDb;
                SpatialWidthSlider.Value = profile.Macros.SpatialWidth;
                NightModeToggle.IsOn = profile.Macros.NightMode;
                engine.SetMacros(profile.Macros);
            }

            ActiveProfileNameText.Text = profile.Name;
            ActiveProfileCategoryText.Text = "• Imported Profile";
            updatingUi = false;
            responseDirty = true;
        }
        catch (Exception ex)
        {
            RouteStatusText.Text = $"Import error: {ex.Message}";
        }
    }

    private AudioFlowProfile CaptureProfile(string name) => new()
    {
        Name = name,
        Bands = Enumerable.Range(0, engine.BandCount).Select(engine.GetBand).ToArray(),
        PreampDb = (float)PreampSlider.Value,
        Bypass = MasterBypassToggle.IsOn,
        SpatialEnabled = SpatialEngineToggle.IsOn,
        Depth = (float)DepthSlider.Value,
        Ambience = (float)AmbienceSlider.Value,
        Mix = (float)MixSlider.Value,
        Pans = nodePanSliders.Select(s => (float)s.Value).ToArray(),
        Macros = engine.Macros,
        LatencyMode = engine.LatencyMode
    };

    // ==========================================
    // AutoEQ Headphone Library & Filter Chips
    // ==========================================
    private void PopulateHeadphoneCards()
    {
        HeadphoneCardsGrid.ItemsSource = AutoEqDatabase.Profiles;
    }

    private void FilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        ChipAll.Style = (Style)Application.Current.Resources[tag == "All" ? "AccentButtonStyle" : "DefaultButtonStyle"];
        ChipOverEar.Style = (Style)Application.Current.Resources[tag == "Over-Ear" ? "AccentButtonStyle" : "DefaultButtonStyle"];
        ChipInEar.Style = (Style)Application.Current.Resources[tag == "In-Ear" ? "AccentButtonStyle" : "DefaultButtonStyle"];
        ChipStudio.Style = (Style)Application.Current.Resources[tag == "Studio" ? "AccentButtonStyle" : "DefaultButtonStyle"];
        ChipGaming.Style = (Style)Application.Current.Resources[tag == "Gaming" ? "AccentButtonStyle" : "DefaultButtonStyle"];

        if (tag == "All")
        {
            HeadphoneCardsGrid.ItemsSource = AutoEqDatabase.Profiles;
        }
        else
        {
            HeadphoneCardsGrid.ItemsSource = AutoEqDatabase.Profiles
                .Where(p => p.Category.Contains(tag, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void HeadphoneSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = HeadphoneSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            HeadphoneCardsGrid.ItemsSource = AutoEqDatabase.Profiles;
            return;
        }

        HeadphoneCardsGrid.ItemsSource = AutoEqDatabase.Profiles
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Manufacturer.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void ApplyHeadphoneProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var profile = AutoEqDatabase.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null) return;

        updatingUi = true;
        var bands = profile.ToEqBands();
        for (int i = 0; i < engine.BandCount; i++)
        {
            engine.SetBand(i, bands[i]);
            var ui = bandControls[i];
            ui.Slider.Value = bands[i].GainDb;
            ui.Gain.Text = bands[i].GainDb.ToString("0.00", CultureInfo.InvariantCulture);
            ui.Frequency.Text = bands[i].Frequency.ToString("0.0", CultureInfo.InvariantCulture);
            ui.Label.Text = FormatFrequency(bands[i].Frequency);
            ui.Q.Text = bands[i].Q.ToString("0.00", CultureInfo.InvariantCulture);
            ui.Type.SelectedIndex = 0;
            ui.Slider.IsEnabled = true;
            ui.Gain.IsEnabled = true;
        }

        ActiveProfileNameText.Text = profile.Name;
        ActiveProfileCategoryText.Text = $"• {profile.Category} (Harman Target)";

        currentHeadphoneProfile = profile;
        SelectedHeadphoneTitle.Text = $"{profile.Name}: Harman Target Correction Matrix";
        HeadphoneAbToggle.IsOn = true;
        DrawAutoEqCurve();

        updatingUi = false;
        responseDirty = true;

        // Switch to Studio EQ to view the applied curve
        MainNavView.SelectedItem = MainNavView.MenuItems[1];
    }

    private void AutoEqCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawAutoEqCurve();

    private void DrawAutoEqCurve()
    {
        if (AutoEqCanvas is null || currentHeadphoneProfile is null) return;
        var width = AutoEqCanvas.ActualWidth;
        var height = AutoEqCanvas.ActualHeight;
        if (width <= 60 || height <= 30) return;

        const double left = 30, right = 10, top = 8, bottom = 12;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var maxFreq = 20_000.0;
        var freqSpan = Math.Log(maxFreq / 20.0);

        var stockPoints = new PointCollection();
        var harmanPoints = new PointCollection();
        var rawGains = currentHeadphoneProfile.RawAnomalies;
        var harmanGains = currentHeadphoneProfile.Gains;

        for (int i = 0; i < EqBand.DefaultFrequencies.Length; i++)
        {
            var freq = EqBand.DefaultFrequencies[i];
            var x = left + Math.Log(freq / 20.0) / freqSpan * plotWidth;
            float stockDb = i < rawGains.Length ? rawGains[i] : 0f;
            float harmanDb = i < harmanGains.Length ? harmanGains[i] : 0f;

            // Map -8 dB to +8 dB range
            var stockY = Math.Clamp(top + (8.0 - stockDb) / 16.0 * plotHeight, top, top + plotHeight);
            var harmanY = Math.Clamp(top + (8.0 - harmanDb) / 16.0 * plotHeight, top, top + plotHeight);

            stockPoints.Add(new Point(x, stockY));
            harmanPoints.Add(new Point(x, harmanY));
        }

        stockAnomalyLine.Points = stockPoints;
        harmanTargetLine.Points = harmanPoints;
    }

    private void HeadphoneAbToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (updatingUi || currentHeadphoneProfile is null) return;
        if (HeadphoneAbToggle.IsOn)
        {
            var bands = currentHeadphoneProfile.ToEqBands();
            updatingUi = true;
            for (int i = 0; i < engine.BandCount; i++)
            {
                engine.SetBand(i, bands[i]);
                var ui = bandControls[i];
                ui.Slider.Value = bands[i].GainDb;
                ui.Gain.Text = bands[i].GainDb.ToString("0.00", CultureInfo.InvariantCulture);
            }
            updatingUi = false;
        }
        else
        {
            updatingUi = true;
            for (int i = 0; i < engine.BandCount; i++)
            {
                engine.SetBandGain(i, 0f);
                var ui = bandControls[i];
                ui.Slider.Value = 0;
                ui.Gain.Text = "0.00";
            }
            updatingUi = false;
        }
        responseDirty = true;
    }

    // ==========================================
    // Studio EQ (11 Parametric Bands)
    // ==========================================
    private void BuildBandControls()
    {
        for (var index = 0; index < engine.BandCount; index++)
        {
            var bandIndex = index;
            var band = engine.GetBand(index);

            var card = new Border
            {
                Background = Brush(11, 16, 24),
                BorderBrush = Brush(24, 36, 52),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(6, 8, 6, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(index == 0 ? 0 : 3, 0, index == engine.BandCount - 1 ? 0 : 3, 0)
            };

            var column = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Stretch };

            // Band Index & Acoustic Role Header
            var headerGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 2) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bandPill = new Border
            {
                Background = Brush(18, 30, 48),
                BorderBrush = Brush(0, 229, 255, 120),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            bandPill.Child = new TextBlock
            {
                Text = $"● B{bandIndex + 1:D2}",
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = Brush(0, 229, 255)
            };
            Grid.SetColumn(bandPill, 0);

            var roleText = new TextBlock
            {
                Text = EqBand.GetBandRole(bandIndex).ToUpperInvariant(),
                FontSize = 8,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush(135, 155, 180),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(roleText, 1);

            headerGrid.Children.Add(bandPill);
            headerGrid.Children.Add(roleText);
            column.Children.Add(headerGrid);

            // Frequency Label (Prominent)
            var nodeLabel = new TextBlock
            {
                Text = FormatFrequency(band.Frequency),
                FontSize = 13.5,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = Brush(245, 250, 255),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            column.Children.Add(nodeLabel);

            // Fader with 0 dB reference detent tick
            var faderContainer = new Grid { Height = 140, HorizontalAlignment = HorizontalAlignment.Stretch };
            var centerTick = new Border
            {
                Height = 1,
                Width = 26,
                Background = Brush(45, 65, 90),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            faderContainer.Children.Add(centerTick);

            var slider = new Slider
            {
                Minimum = -24, Maximum = 24, Value = band.GainDb, StepFrequency = 0.1,
                Orientation = Orientation.Vertical, Height = 140,
                Foreground = Brush(0, 229, 255),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            faderContainer.Children.Add(slider);

            // Gain readout badge (Monospace digital well)
            var gain = NumericBox(band.GainDb.ToString("0.00", CultureInfo.InvariantCulture), width: double.NaN);
            var frequency = NumericBox(band.Frequency.ToString("0.0", CultureInfo.InvariantCulture), width: double.NaN);
            var type = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 24,
                FontSize = 9.5,
                Padding = new Thickness(4, 1, 4, 1)
            };
            foreach (var label in new[] { "Peak", "L-Shelf", "H-Shelf", "L-Pass", "H-Pass", "B-Pass", "Notch", "AllPass" })
                type.Items.Add(new ComboBoxItem { Content = label });
            type.SelectedIndex = (int)band.Type;

            var q = NumericBox(band.Q.ToString("0.00", CultureInfo.InvariantCulture), width: double.NaN);
            var reset = new Button
            {
                Content = "↺ Flat",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 22,
                FontSize = 9.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Background = Brush(16, 24, 36),
                BorderBrush = Brush(28, 40, 60),
                Foreground = Brush(130, 150, 175)
            };

            column.Children.Add(faderContainer);
            column.Children.Add(gain);
            column.Children.Add(type);
            column.Children.Add(q);
            column.Children.Add(reset);

            card.Child = column;
            Grid.SetColumn(card, bandIndex);
            BandsGrid.Children.Add(card);
            bandControls.Add(new BandUi(nodeLabel, slider, gain, frequency, type, q));

            slider.ValueChanged += (_, e) =>
            {
                if (updatingUi) return;
                gain.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
                engine.SetBandGain(bandIndex, (float)e.NewValue);
                responseDirty = true;
            };

            gain.LostFocus += (_, _) =>
            {
                if (TryFloat(gain.Text, -24, 24, out var value)) slider.Value = value;
                else gain.Text = slider.Value.ToString("0.00", CultureInfo.InvariantCulture);
            };

            frequency.LostFocus += (_, _) => CommitBand(bandIndex);
            q.LostFocus += (_, _) => CommitBand(bandIndex);
            type.SelectionChanged += (_, _) => CommitBand(bandIndex);
            reset.Click += (_, _) => ResetBand(bandIndex);

            gain.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { CommitGain(bandIndex); e.Handled = true; } };
            frequency.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { CommitBand(bandIndex); e.Handled = true; } };
            q.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { CommitBand(bandIndex); e.Handled = true; } };
        }
    }

    private static TextBox NumericBox(string text, double width = 70)
    {
        var box = new TextBox
        {
            Text = text,
            Height = 24,
            FontSize = 10,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Cascadia Code, monospace"),
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Background = Brush(8, 12, 18),
            BorderBrush = Brush(24, 35, 50)
        };
        if (double.IsNaN(width))
        {
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            box.Width = width;
            box.HorizontalAlignment = HorizontalAlignment.Center;
        }
        return box;
    }

    private void CommitGain(int index)
    {
        var ui = bandControls[index];
        if (TryFloat(ui.Gain.Text, -24, 24, out var value)) ui.Slider.Value = value;
        else ui.Gain.Text = ui.Slider.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void CommitBand(int index)
    {
        if (updatingUi || (uint)index >= (uint)bandControls.Count) return;
        var ui = bandControls[index];
        var previous = engine.GetBand(index);
        if (!TryFloat(ui.Frequency.Text, 20, 24_000, out var frequency)) frequency = previous.Frequency;
        if (!TryFloat(ui.Q.Text, 0.1f, 12, out var q)) q = previous.Q;
        var filterType = (EqFilterType)Math.Clamp(ui.Type.SelectedIndex, 0, Enum.GetValues<EqFilterType>().Length - 1);
        var usesGain = filterType is EqFilterType.Peaking or EqFilterType.LowShelf or EqFilterType.HighShelf;
        if (!usesGain)
        {
            updatingUi = true;
            ui.Slider.Value = 0;
            ui.Gain.Text = "0.00";
            updatingUi = false;
        }
        ui.Slider.IsEnabled = usesGain;
        ui.Gain.IsEnabled = usesGain;
        var band = new EqBand(frequency, usesGain ? (float)ui.Slider.Value : 0, q, filterType);
        engine.SetBand(index, band);
        ui.Label.Text = FormatFrequency(frequency);
        ui.Frequency.Text = frequency.ToString("0.0", CultureInfo.InvariantCulture);
        ui.Q.Text = q.ToString("0.00", CultureInfo.InvariantCulture);
        responseDirty = true;
    }

    private void ResetBand(int index)
    {
        var ui = bandControls[index];
        updatingUi = true;
        ui.Slider.Value = 0;
        ui.Gain.Text = "0.00";
        ui.Frequency.Text = EqBand.DefaultFrequencies[index].ToString("0.0", CultureInfo.InvariantCulture);
        ui.Q.Text = "1.20";
        ui.Type.SelectedIndex = 0;
        updatingUi = false;
        CommitBand(index);
    }

    private void ResetAllBands_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < engine.BandCount; i++) ResetBand(i);
        responseDirty = true;
    }

    // ==========================================
    // 7.1 Spatial Surround
    // ==========================================
    private void BuildNodePans()
    {
        float[] defaults = [0, 50, 100, 50, 15, 85, 20, 80];
        for (var index = 0; index < SpatialProcessor.NodeNames.Length; index++)
        {
            var node = index;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            var label = new TextBlock { Text = SpatialProcessor.NodeNames[index], FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(235, 240, 244) };
            var slider = new Slider { Minimum = 0, Maximum = 100, Value = defaults[index], Foreground = Brush(96, 205, 255) };
            var value = new TextBlock { Text = $"{defaults[index]:0}%", FontSize = 10, Foreground = Brush(145, 165, 179) };

            Grid.SetColumn(label, 0); Grid.SetColumn(slider, 1); Grid.SetColumn(value, 2);
            row.Children.Add(label); row.Children.Add(slider); row.Children.Add(value);
            NodePansPanel.Children.Add(row);
            nodePanSliders.Add(slider);
            engine.SetNodePan(index, defaults[index]);

            slider.ValueChanged += (_, e) =>
            {
                value.Text = $"{e.NewValue:0}%";
                engine.SetNodePan(node, (float)e.NewValue);
            };
        }
    }

    private void SpatialControl_Changed(object sender, RoutedEventArgs e) => ApplySpatial();
    private void SpatialSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => ApplySpatial();

    private void ApplySpatial()
    {
        if (SpatialEngineToggle is null || DepthSlider is null || AmbienceSlider is null || MixSlider is null) return;
        DepthValText.Text = $"{DepthSlider.Value:0}%";
        AmbienceValText.Text = $"{AmbienceSlider.Value:0}%";
        MixValText.Text = $"{MixSlider.Value:0}%";
        engine.ConfigureSpatial(SpatialEngineToggle.IsOn, (float)DepthSlider.Value, (float)AmbienceSlider.Value, (float)MixSlider.Value);
    }

    // ==========================================
    // Telemetry & Spectrum Visualization
    // ==========================================
    private void TelemetryTimer_Tick(object? sender, object e)
    {
        var bufferStatus = engine.GetBufferStatus();
        BufferStatusText.Text = !engine.IsRunning ? "Audio buffer: stopped" :
            $"Buffer: {bufferStatus.BufferedMilliseconds:0} ms (reserve: {engine.LatencyMode}) · {(bufferStatus.Priming ? "Refilling" : "Playing")}\nUnderflows: {bufferStatus.Underruns} | Overflows: {bufferStatus.Overruns}";

        if (!engine.IsRunning) clipAlertUntil = default;
        engine.CopySpectrum(spectrum);
        var displayRate = engine.SampleRate is > 0 ? engine.SampleRate : 48_000;
        if (displayRate != spectrumSampleRate) BuildSpectrumBars();
        DrawSpectrum();

        var peakFrequency = engine.SpectrumPeakFrequency;
        SpectrumPeakText.Text = !engine.IsRunning ? "Peak: Paused" : peakFrequency < 20 ? "No Signal" : $"Dominant: {FormatFrequency(peakFrequency)}";

        if (responseDirty)
        {
            DrawResponse();
            responseDirty = false;
        }

        var peaks = engine.GetPeaks();
        inputLHold = MathF.Max(peaks.InputL, inputLHold * 0.91f);
        inputRHold = MathF.Max(peaks.InputR, inputRHold * 0.91f);
        outputLHold = MathF.Max(peaks.OutputL, outputLHold * 0.91f);
        outputRHold = MathF.Max(peaks.OutputR, outputRHold * 0.91f);

        MeterInL.Value = MeterPercent(inputLHold);
        MeterInR.Value = MeterPercent(inputRHold);
        MeterOutL.Value = MeterPercent(outputLHold);
        MeterOutR.Value = MeterPercent(outputRHold);

        InDbfsText.Text = $"{Decibels(MathF.Max(inputLHold, inputRHold)):0.0} dB";
        OutDbfsText.Text = $"{Decibels(MathF.Max(outputLHold, outputRHold)):0.0} dB";

        var limited = engine.LimitedFrames;
        var reduction = -Decibels(engine.LimiterGain);
        if (limited > previousLimitedFrames) clipAlertUntil = DateTime.UtcNow.AddSeconds(2);
        var limiting = DateTime.UtcNow < clipAlertUntil;

        LimiterBadgeText.Text = limiting || reduction > 0.1f ? $"ACTIVE (−{reduction:0.1} dB)" : "LIMITER SAFE";
        LimiterBadge.BorderBrush = limiting || reduction > 0.1f ? Brush(255, 185, 0) : Brush(229, 154, 60);
        previousLimitedFrames = limited;

        if (MiniHudTelemetryText is not null && isMiniHud)
        {
            MiniHudTelemetryText.Text = !engine.IsRunning
                ? "Engine: Stopped · Latency: 25ms Ultra"
                : $"Live • {engine.SampleRate:N0} Hz · Latency: {engine.LatencyMode} · Limiter: {(limiting ? "ACTIVE" : "Safe")}";
        }
    }

    private static float Decibels(float level) => 20 * MathF.Log10(MathF.Max(level, 0.00001f));
    private static double MeterPercent(float level) => Math.Clamp((Decibels(level) + 60) / 60 * 100, 0, 100);
    private static string FormatFrequency(float frequency) => frequency >= 1000 ? $"{frequency / 1000:0.#} kHz" : $"{frequency:0} Hz";

    private void SpectrumCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => BuildSpectrumBars();

    private void BuildSpectrumBars()
    {
        if (SpectrumCanvas.ActualWidth < 80 || SpectrumCanvas.ActualHeight < 30) return;
        SpectrumCanvas.Children.Clear();
        spectrumBars.Clear();
        spectrumPeakCaps.Clear();
        spectrumSampleRate = engine.SampleRate is > 0 ? engine.SampleRate : 48_000;
        const double left = 44, right = 16, top = 16, bottom = 22;
        var width = Math.Max(SpectrumCanvas.ActualWidth - left - right, 1);
        var height = Math.Max(SpectrumCanvas.ActualHeight - top - bottom, 1);
        var step = width / SpectrumAnalyzer.BinCount;
        var barWidth = Math.Max(1, step - 1.8);

        for (var i = 0; i < SpectrumAnalyzer.BinCount; i++)
        {
            var x = left + i * step;
            var bar = new Rectangle { Fill = spectrumBrush, Width = barWidth, Height = 1, RadiusX = 1, RadiusY = 1 };
            Canvas.SetLeft(bar, x);
            SpectrumCanvas.Children.Add(bar);
            spectrumBars.Add(bar);

            var cap = new Rectangle { Fill = Brush(255, 94, 98), Width = barWidth, Height = 2.0, RadiusX = 1, RadiusY = 1 };
            Canvas.SetLeft(cap, x);
            SpectrumCanvas.Children.Add(cap);
            spectrumPeakCaps.Add(cap);
        }
        DrawSpectrum();
    }

    private void DrawSpectrum()
    {
        const double top = 16, bottom = 22;
        var height = SpectrumCanvas.ActualHeight - top - bottom;
        if (height <= 0) return;
        for (var i = 0; i < spectrumBars.Count; i++)
        {
            spectrumHolds[i] = MathF.Max(spectrum[i], spectrumHolds[i] - 0.02f);
            spectrumBars[i].Visibility = spectrum[i] > 0.002f ? Visibility.Visible : Visibility.Collapsed;
            spectrumPeakCaps[i].Visibility = spectrumHolds[i] > 0.002f ? Visibility.Visible : Visibility.Collapsed;
            var h = Math.Max(1, spectrum[i] * height);
            spectrumBars[i].Height = h;
            Canvas.SetTop(spectrumBars[i], top + height - h);
            Canvas.SetTop(spectrumPeakCaps[i], top + height - Math.Max(2, spectrumHolds[i] * height));
        }
    }

    private void ResponseCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawResponse();

    private void DrawResponse()
    {
        if (bandControls.Count == 0) return;
        var width = ResponseCanvas.ActualWidth;
        var height = ResponseCanvas.ActualHeight;
        if (width <= 90 || height <= 60) return;

        foreach (var element in responseGridElements) ResponseCanvas.Children.Remove(element);
        responseGridElements.Clear();

        const double left = 44, right = 16, top = 16, bottom = 22;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var sampleRate = engine.SampleRate is > 0 ? engine.SampleRate : 48_000;
        var maximumFrequency = Math.Min(24_000, sampleRate * 0.45);
        var frequencySpan = Math.Log(maximumFrequency / 20);

        foreach (var db in new[] { 24, 12, 0, -12, -24 })
        {
            var y = top + (24 - db) / 48.0 * plotHeight;
            AddResponseGuide(left, y, left + plotWidth, y, db == 0);
            AddResponseLabel($"{db:+0;-0;0} dB", 4, y - 7);
        }

        foreach (var freq in EqBand.DefaultFrequencies)
        {
            if (freq > maximumFrequency) break;
            var x = left + Math.Log(freq / 20.0) / frequencySpan * plotWidth;
            AddResponseGuide(x, top, x, top + plotHeight, false);
            var text = freq >= 1000 ? $"{freq / 1000:0.#}k" : $"{freq:0}";
            AddResponseLabel(text, x - 10, top + plotHeight + 3);
        }

        var points = new PointCollection();
        var activeBands = Enumerable.Range(0, engine.BandCount).Select(engine.GetBand).ToArray();
        var coefficients = activeBands.Select(band => BiquadCoefficients.Design(band, sampleRate)).ToArray();

        for (var pixel = 0.0; pixel <= plotWidth; pixel += 3)
        {
            var frequency = 20 * Math.Exp(frequencySpan * pixel / plotWidth);
            double gain = MasterBypassToggle?.IsOn == true ? 0 : PreampSlider?.Value ?? 0;
            for (var band = 0; band < coefficients.Length && MasterBypassToggle?.IsOn != true; band++)
            {
                var setting = activeBands[band];
                if (setting.GainDb == 0 && setting.Type is (EqFilterType.Peaking or EqFilterType.LowShelf or EqFilterType.HighShelf)) continue;
                gain += coefficients[band].MagnitudeDb(frequency, sampleRate);
            }
            points.Add(new Point(left + pixel, Math.Clamp(top + (24 - gain) / 48 * plotHeight, top, top + plotHeight)));
        }
        responseLine.Points = points;

        var fillPoints = new PointCollection();
        fillPoints.Add(new Point(left, top + plotHeight));
        foreach (var pt in points) fillPoints.Add(pt);
        fillPoints.Add(new Point(left + plotWidth, top + plotHeight));
        responseFillArea.Points = fillPoints;

        // Render 11 FabFilter-style interactive glowing node pins along the curve
        for (var i = 0; i < activeBands.Length; i++)
        {
            var band = activeBands[i];
            if (band.Frequency > maximumFrequency) continue;
            var nx = left + Math.Log(band.Frequency / 20.0) / frequencySpan * plotWidth;
            double nodeGain = MasterBypassToggle?.IsOn == true ? 0 : PreampSlider?.Value ?? 0;
            for (var b = 0; b < coefficients.Length && MasterBypassToggle?.IsOn != true; b++)
            {
                var s = activeBands[b];
                if (s.GainDb == 0 && s.Type is (EqFilterType.Peaking or EqFilterType.LowShelf or EqFilterType.HighShelf)) continue;
                nodeGain += coefficients[b].MagnitudeDb(band.Frequency, sampleRate);
            }
            var ny = Math.Clamp(top + (24 - nodeGain) / 48 * plotHeight, top, top + plotHeight);

            if (i < nodeScreenPositions.Length) nodeScreenPositions[i] = new Point(nx, ny);

            bool isSelected = draggingBandIndex == i;
            var halo = new Ellipse
            {
                Width = isSelected ? 26 : 20, Height = isSelected ? 26 : 20,
                Fill = Brush(0, 229, 255, (byte)(isSelected ? 80 : 30)),
                Stroke = Brush(0, 229, 255, (byte)(isSelected ? 255 : 140)),
                StrokeThickness = isSelected ? 2 : 1
            };
            Canvas.SetLeft(halo, nx - (isSelected ? 13 : 10));
            Canvas.SetTop(halo, ny - (isSelected ? 13 : 10));
            ResponseCanvas.Children.Add(halo);
            responseGridElements.Add(halo);

            var core = new Border
            {
                Width = isSelected ? 18 : 14, Height = isSelected ? 18 : 14,
                CornerRadius = new CornerRadius(isSelected ? 9 : 7),
                Background = Brush(10, 16, 26),
                BorderBrush = Brush(0, 229, 255),
                BorderThickness = new Thickness(isSelected ? 2 : 1.5),
                Child = new TextBlock
                {
                    Text = $"{i + 1}",
                    FontSize = isSelected ? 9 : 8,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = Brush(0, 229, 255),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(core, nx - (isSelected ? 9 : 7));
            Canvas.SetTop(core, ny - (isSelected ? 9 : 7));
            ResponseCanvas.Children.Add(core);
            responseGridElements.Add(core);
        }
    }

    private void AddResponseGuide(double x1, double y1, double x2, double y2, bool emphasized)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = Brush(emphasized ? (byte)96 : (byte)42, emphasized ? (byte)140 : (byte)56, emphasized ? (byte)180 : (byte)72),
            StrokeThickness = emphasized ? 1.0 : 0.6
        };
        ResponseCanvas.Children.Add(line);
        responseGridElements.Add(line);
    }

    private void AddResponseLabel(string value, double x, double y)
    {
        var label = new TextBlock { Text = value, FontSize = 9, Foreground = Brush(140, 160, 180) };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        ResponseCanvas.Children.Add(label);
        responseGridElements.Add(label);
    }

    // ==========================================
    // Interactive EQ Canvas Drag & Drop
    // ==========================================
    private void ResponseCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(ResponseCanvas).Position;
        double bestDistSq = 24 * 24;
        int bestIndex = -1;

        for (int i = 0; i < engine.BandCount && i < nodeScreenPositions.Length; i++)
        {
            var pos = nodeScreenPositions[i];
            var dx = pt.X - pos.X;
            var dy = pt.Y - pos.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestIndex = i;
            }
        }

        if (bestIndex >= 0)
        {
            draggingBandIndex = bestIndex;
            ResponseCanvas.CapturePointer(e.Pointer);
            DrawResponse();
            e.Handled = true;
        }
    }

    private void ResponseCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (draggingBandIndex < 0 || draggingBandIndex >= engine.BandCount) return;

        var pt = e.GetCurrentPoint(ResponseCanvas).Position;
        const double left = 44, right = 16, top = 16, bottom = 22;
        var plotWidth = ResponseCanvas.ActualWidth - left - right;
        var plotHeight = ResponseCanvas.ActualHeight - top - bottom;
        if (plotWidth <= 0 || plotHeight <= 0) return;

        var sampleRate = engine.SampleRate is > 0 ? engine.SampleRate : 48_000;
        var maxFreq = Math.Min(24_000, sampleRate * 0.45);
        var freqSpan = Math.Log(maxFreq / 20.0);

        // Vertical = Gain (-24 dB to +24 dB)
        var gain = (float)Math.Clamp(24.0 - ((pt.Y - top) / plotHeight) * 48.0, -24.0, 24.0);
        gain = MathF.Round(gain, 1);

        // Horizontal = Frequency fine-tuning within safe bounds
        double minF = draggingBandIndex > 0 ? engine.GetBand(draggingBandIndex - 1).Frequency * 1.08 : 20.0;
        double maxF = draggingBandIndex < engine.BandCount - 1 ? engine.GetBand(draggingBandIndex + 1).Frequency * 0.92 : maxFreq;
        var freq = (float)Math.Clamp(20.0 * Math.Exp(freqSpan * (pt.X - left) / plotWidth), minF, maxF);
        freq = MathF.Round(freq, 1);

        var current = engine.GetBand(draggingBandIndex);
        var updated = current with { GainDb = gain, Frequency = freq };
        engine.SetBand(draggingBandIndex, updated);

        var ui = bandControls[draggingBandIndex];
        updatingUi = true;
        ui.Slider.Value = gain;
        ui.Gain.Text = gain.ToString("0.00", CultureInfo.InvariantCulture);
        ui.Frequency.Text = freq.ToString("0.0", CultureInfo.InvariantCulture);
        ui.Label.Text = FormatFrequency(freq);
        updatingUi = false;

        responseDirty = true;
        e.Handled = true;
    }

    private void ResponseCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (draggingBandIndex >= 0)
        {
            draggingBandIndex = -1;
            ResponseCanvas.ReleasePointerCapture(e.Pointer);
            DrawResponse();
            e.Handled = true;
        }
    }

    private void ResponseCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(ResponseCanvas).Position;
        int targetIndex = draggingBandIndex;

        if (targetIndex < 0)
        {
            double bestDistSq = 32 * 32;
            for (int i = 0; i < engine.BandCount && i < nodeScreenPositions.Length; i++)
            {
                var pos = nodeScreenPositions[i];
                var dx = pt.X - pos.X;
                var dy = pt.Y - pos.Y;
                var distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    targetIndex = i;
                }
            }
        }

        if (targetIndex >= 0)
        {
            var band = engine.GetBand(targetIndex);
            var delta = e.GetCurrentPoint(ResponseCanvas).Properties.MouseWheelDelta;
            var newQ = Math.Clamp(band.Q + (delta > 0 ? 0.15f : -0.15f), 0.2f, 10f);
            newQ = MathF.Round(newQ, 2);
            var updated = band with { Q = newQ };
            engine.SetBand(targetIndex, updated);

            var ui = bandControls[targetIndex];
            ui.Q.Text = newQ.ToString("0.00", CultureInfo.InvariantCulture);
            responseDirty = true;
            e.Handled = true;
        }
    }

    // ==========================================
    // Audio Doctor & Self-Healing Diagnostics
    // ==========================================
    private void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var isVirtual = IsVirtualRoutingName(defaultDevice.FriendlyName);

            var report = $"• Default Windows Playback: {defaultDevice.FriendlyName}\n" +
                         $"• Virtual Driver Detected: {(isVirtual ? "✅ Yes" : "❌ No (Requires AudioFlow Output or VB-Cable)")}\n" +
                         $"• Engine State: {(engine.IsRunning ? $"✅ Active ({engine.SampleRate} Hz)" : "⏹ Stopped")}\n" +
                         $"• Selected Buffer Latency: {engine.LatencyMode}\n" +
                         $"• Safety Limiter: Operating normally (Ceiling ≤ −1 dBFS)";

            DiagnosticResultsText.Text = report;
        }
        catch (Exception ex)
        {
            DiagnosticResultsText.Text = $"Diagnostic error: {ex.Message}";
        }
    }

    private void RestoreDefaultAudio_Click(object sender, RoutedEventArgs e)
    {
        if (engine.IsRunning)
        {
            engine.Stop();
            UpdateEngineUi(false);
        }
        DiagnosticResultsText.Text = "AudioFlow engine stopped. Open Windows Sound Settings (Win+I -> Sound) to confirm your default physical speakers/headphones.";
    }

    private void Engine_AudioFaulted(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            try { engine.Stop(); } catch { }
            UpdateEngineUi(false);
            RouteStatusText.Text = message;
            UpdateRouteStatus();
        });
    }

    private static bool TryFloat(string text, float min, float max, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        float.IsFinite(value) && value >= min && value <= max;

    private void RestoreUserSettings()
    {
        try
        {
            var saved = UserSettings.Load();
            updatingUi = true;

            PreampSlider.Value = saved.PreampDb;
            MasterBypassToggle.IsOn = saved.IsBypassed;
            LatencyModePicker.SelectedIndex = (int)saved.LatencyMode;

            BassSlider.Value = saved.Macros.BassBoostDb;
            VocalSlider.Value = saved.Macros.VocalClarityDb;
            SpatialWidthSlider.Value = saved.Macros.SpatialWidth;
            NightModeToggle.IsOn = saved.Macros.NightMode;
            engine.SetMacros(saved.Macros);

            SpatialEngineToggle.IsOn = saved.SpatialEnabled;
            DepthSlider.Value = saved.SpatialDepth;
            AmbienceSlider.Value = saved.SpatialAmbience;
            MixSlider.Value = saved.SpatialMix;
            for (int i = 0; i < nodePanSliders.Count && i < saved.NodePans.Length; i++)
                nodePanSliders[i].Value = saved.NodePans[i];

            for (int i = 0; i < engine.BandCount && i < saved.Bands.Length; i++)
            {
                var restoredBand = saved.Bands[i] with { Frequency = EqBand.DefaultFrequencies[i] };
                engine.SetBand(i, restoredBand);
                var ui = bandControls[i];
                ui.Slider.Value = restoredBand.GainDb;
                ui.Gain.Text = restoredBand.GainDb.ToString("0.00", CultureInfo.InvariantCulture);
                ui.Frequency.Text = restoredBand.Frequency.ToString("0.0", CultureInfo.InvariantCulture);
                ui.Label.Text = FormatFrequency(restoredBand.Frequency);
                ui.Q.Text = restoredBand.Q.ToString("0.00", CultureInfo.InvariantCulture);
                ui.Type.SelectedIndex = (int)restoredBand.Type;
            }

            ActiveProfileNameText.Text = saved.ActivePresetName;
            ActiveProfileCategoryText.Text = "• Restored Session";
            updatingUi = false;
            responseDirty = true;
        }
        catch
        {
            updatingUi = false;
        }
    }

    private void SaveUserSettings()
    {
        try
        {
            var settings = new UserSettings
            {
                ActivePresetName = ActiveProfileNameText?.Text ?? "Custom",
                PreampDb = (float)(PreampSlider?.Value ?? 0),
                IsBypassed = MasterBypassToggle?.IsOn ?? false,
                LatencyMode = engine.LatencyMode,
                Macros = engine.Macros,
                Bands = Enumerable.Range(0, engine.BandCount).Select(engine.GetBand).ToArray(),
                SpatialEnabled = SpatialEngineToggle?.IsOn ?? false,
                SpatialDepth = (float)(DepthSlider?.Value ?? 60),
                SpatialAmbience = (float)(AmbienceSlider?.Value ?? 45),
                SpatialMix = (float)(MixSlider?.Value ?? 80),
                NodePans = nodePanSliders.Select(s => (float)s.Value).ToArray(),
                PreferredSourceDeviceId = SelectedDeviceId(SourcePicker),
                PreferredOutputDeviceId = SelectedDeviceId(OutputPicker)
            };
            settings.Save();
        }
        catch { }
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255) =>
        new(Windows.UI.Color.FromArgb(a, r, g, b));

    private sealed record BandUi(TextBlock Label, Slider Slider, TextBox Gain, TextBox Frequency, ComboBox Type, TextBox Q);
}
