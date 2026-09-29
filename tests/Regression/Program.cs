using AudioFlow.Models;
using AudioFlow.Services;
using AudioFlow.ViewModels;
using System.IO;

internal static class Program
{
    private static int checks;
    static void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
    static T? FindDescendant<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
    static void DrainUi()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    [STAThread]
    static void Main()
    {
        var app = new AudioFlow.App();
        app.InitializeComponent();
        var mainWindow = new AudioFlow.MainWindow();
        var pages = (System.Windows.Controls.TabControl)mainWindow.FindName("Pages")!;
        Check(pages.SelectedIndex == 1 &&
              ((System.Windows.Controls.TabItem)pages.SelectedItem).Header?.ToString() == "Tuning",
            "equalizer is the first visible screen");
        mainWindow.Measure(new System.Windows.Size(1050, 720));
        mainWindow.Arrange(new System.Windows.Rect(0, 0, 1050, 720));
        mainWindow.UpdateLayout();
        pages.ApplyTemplate();
        var pageHost = (System.Windows.Controls.ContentPresenter)pages.Template.FindName("PART_SelectedContentHost", pages)!;
        Check(pageHost.Content == ((System.Windows.Controls.TabItem)pages.SelectedItem).Content &&
              pageHost.Content is System.Windows.Controls.ScrollViewer,
            "headerless navigation renders the selected page");
        var menuButton = (System.Windows.Controls.Button)mainWindow.FindName("MenuButton")!;
        Check(menuButton.ContextMenu?.Items.Count == 3 &&
              System.Windows.Automation.AutomationProperties.GetName(menuButton) == "Open navigation menu",
            "three-line menu contains equalizer, mixer and settings");
        var mixerNavigation = typeof(AudioFlow.MainWindow).GetMethod("ShowMixer_Click",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var eqNavigation = typeof(AudioFlow.MainWindow).GetMethod("ShowEqualizer_Click",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var settingsNavigation = typeof(AudioFlow.MainWindow).GetMethod("ShowSettings_Click",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        mixerNavigation.Invoke(mainWindow, [null, new System.Windows.RoutedEventArgs()]);
        Check(pages.SelectedIndex == 0, "menu opens the preserved mixer");
        settingsNavigation.Invoke(mainWindow, [null, new System.Windows.RoutedEventArgs()]);
        Check(pages.SelectedIndex == 2, "menu opens settings");
        eqNavigation.Invoke(mainWindow, [null, new System.Windows.RoutedEventArgs()]);
        Check(pages.SelectedIndex == 1, "menu returns to the equalizer");
        var slider = new System.Windows.Controls.Slider { Minimum = 0, Maximum = 1, Value = .5 };
        slider.Style = (System.Windows.Style)app.Resources[typeof(System.Windows.Controls.Slider)];
        Console.WriteLine($"Slider settings: click-to-position={slider.IsMoveToPointEnabled}, small step={slider.SmallChange}, large step={slider.LargeChange}");
        Check(slider.IsMoveToPointEnabled, "track clicks select their position");
        Check(Math.Abs(slider.SmallChange - .01) < .00001 && Math.Abs(slider.LargeChange - .1) < .00001,
            "slider steps are scaled to the 0-to-1 audio range");
        var volumeWrites = 0; var muteWrites = 0;
        var sliderSession = new AudioSession { Id = "slider-test", ProcessName = "test", DisplayName = "Test",
            VolumeChanged = (_, _) => volumeWrites++, MuteChanged = (_, _) => muteWrites++ };
        sliderSession.Update(.5f, true);
        slider.SetBinding(System.Windows.Controls.Slider.ValueProperty, new System.Windows.Data.Binding("Volume") {
            Source = sliderSession, Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        System.Windows.Controls.Slider.DecreaseLarge.Execute(null, slider);
        Check(Math.Abs(sliderSession.Volume - .4) < .00001, "large decrease changes volume by ten percent");
        System.Windows.Controls.Slider.IncreaseLarge.Execute(null, slider);
        Check(Math.Abs(sliderSession.Volume - .5) < .00001, "large increase restores midpoint");
        slider.SetCurrentValue(System.Windows.Controls.Slider.ValueProperty, .73);
        Check(volumeWrites > 0 && muteWrites == 0 && sliderSession.IsMuted, "volume slider preserves mute state");
        Check(AudioFlow.Controls.SliderTrackDrag.GetEnabled(slider), "track drag enabled by shared slider style");
        slider.Width = 300; slider.Height = 30;
        // A deterministic track template lets these geometry tests run without opening a window.
        slider.Template = (System.Windows.Controls.ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Slider">
              <Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}"
                Value="{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
                <Track.DecreaseRepeatButton><RepeatButton /></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb Width="10" Height="20" /></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton /></Track.IncreaseRepeatButton>
              </Track>
            </ControlTemplate>
            """);
        slider.ApplyTemplate();
        slider.Measure(new System.Windows.Size(300, 30));
        slider.Arrange(new System.Windows.Rect(0, 0, 300, 30));
        slider.UpdateLayout();
        var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
        var setPosition = typeof(AudioFlow.Controls.SliderTrackDrag).GetMethod("SetPosition",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        setPosition.Invoke(null, [slider, track, new System.Windows.Point(60, 15)]);
        var firstPoint = sliderSession.Volume;
        slider.UpdateLayout();
        setPosition.Invoke(null, [slider, track, new System.Windows.Point(220, 15)]);
        Check(sliderSession.Volume > firstPoint + .3f, "successive drag positions update the bound volume");
        setPosition.Invoke(null, [slider, track, new System.Windows.Point(-100, 15)]);
        Check(sliderSession.Volume == 0, "drag outside left edge clamps to zero");
        slider.UpdateLayout();
        setPosition.Invoke(null, [slider, track, new System.Windows.Point(500, 15)]);
        Check(sliderSession.Volume == 1 && muteWrites == 0, "drag outside right edge clamps without toggling mute");
        Check(System.Windows.Data.BindingOperations.IsDataBound(slider, System.Windows.Controls.Slider.ValueProperty),
            "track dragging preserves volume binding");
        var music = new AudioProfile { Name = "Music" };
        var gaming = new AudioProfile { Name = "Gaming" };
        var iem = new AudioProfile { Name = "IEM", OutputDeviceId = "dac", MasterVolume = .4f, MasterMuted = true,
            Applications = [new() { ProcessName = "spotify", Volume = .3f, Muted = true }] };
        List<AudioProfile> profiles = [music, gaming, iem];
        var gameRule = new AutomationRule { Match = "game.exe", ProfileId = gaming.Id };
        var musicRule = new AutomationRule { Match = "Spotify.EXE", ProfileId = music.Id };
        var deviceRule = new AutomationRule { Match = "JA11", ProfileId = iem.Id, TriggerType = RuleTriggerType.DeviceConnected };
        List<AutomationRule> rules = [gameRule, musicRule, deviceRule];
        List<AudioDevice> devices = [new("speakers", "Speakers", true), new("dac", "FiiO JA11", false)];
        var automation = new AutomationService();
        Check(automation.Evaluate(rules, profiles, devices, ["game", "spotify"]) == gaming, "highest matching rule wins");
        Check(automation.Evaluate(rules, profiles, devices, ["game", "spotify"]) is null, "unchanged winner preserves manual choice");
        Check(automation.Evaluate(rules, profiles, devices, ["spotify"]) == music, "lower rule activates after game exits");
        Check(automation.Evaluate(rules, profiles, devices, ["spotifyHelper"]) == iem, "process names do not match substrings");
        Check(automation.Evaluate(rules, profiles, [], []) is null, "no match keeps current profile");
        Check(automation.Evaluate(rules, profiles, devices, []) == iem, "reconnecting device reactivates rule");
        deviceRule.Enabled = false;
        Check(automation.Evaluate(rules, profiles, devices, []) is null, "disabled rule ignored");
        musicRule.ProfileId = Guid.NewGuid();
        Check(automation.Evaluate(rules, profiles, devices, ["spotify"]) is null, "missing profile ignored");
        musicRule.ProfileId = music.Id;

        var audio = new FakeAudio();
        var service = new ProfileService(audio);
        Check(service.Apply(iem) is null && audio.Current == "dac", "profile switches to saved output");
        Check(audio.Calls.IndexOf("switch") < audio.Calls.IndexOf("sessions:dac"), "sessions fetched after device switch");
        Check(audio.Master == new MasterAudioState(.4f, true) && audio.Session.Volume == .3f && audio.Session.IsMuted,
            "master and app settings restored");
        audio.Calls.Clear(); audio.Devices = [new("speakers", "Speakers", true)]; audio.Current = "speakers";
        Check(service.Apply(iem) is not null && audio.Calls.Count == 0, "missing saved device leaves fallback volume untouched");

        var folder = Path.Combine(Environment.GetEnvironmentVariable("AUDIOFLOW_TEST_ROOT") ??
            Path.Combine(AppContext.BaseDirectory, "test-data"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "settings.json");
        var store = new SettingsStore(path);
        var settings = new AppSettings { Profiles = profiles, Rules = rules, ActiveProfileId = music.Id };
        store.Save(settings);
        settings.ActiveProfileId = gaming.Id; store.Save(settings);
        Check(store.Load().ActiveProfileId == gaming.Id && File.Exists(path + ".bak"), "settings roundtrip and backup");
        File.WriteAllText(path, "{broken");
        var recovery = new SettingsStore(path);
        Check(recovery.Load().ActiveProfileId == music.Id && recovery.LoadWarning is not null, "corrupt settings restore backup");
        Check(Directory.GetFiles(folder, "*.corrupt-*").Length > 0, "damaged original preserved");
        recovery.Save(settings);
        Check(new SettingsStore(path).Load().ActiveProfileId == gaming.Id, "save succeeds after recovery");
        File.WriteAllText(path, "{\"Profiles\":null}");
        Check(new SettingsStore(path).Load().Profiles.Count == 3, "invalid null profiles recover safely");
        File.WriteAllText(path + ".bak", "null");
        Check(new SettingsStore(path).Load().Profiles.Any(p => p.Name == "IEM / Speakers"), "unusable primary and backup yield defaults");

        var vmPath = Path.Combine(folder, "vm.json");
        var vmStore = new SettingsStore(vmPath);
        vmStore.Save(new AppSettings { Profiles = profiles, ActiveProfileId = iem.Id });
        var fake = new FakeAudio();
        using (var vm = new MainViewModel(fake, vmStore, new StartupService()))
        {
            vm.Start();
            Check(vm.Dsp?.GraphicBands.Count == 11, "eleven EQ bands are ready on the first screen");
            mainWindow.DataContext = vm;
            mainWindow.UpdateLayout();
            var eqList = (System.Windows.Controls.ListBox)mainWindow.FindName("EqBandsList")!;
            Check(eqList.Items.Count == 11, "equalizer displays exactly eleven starting bands");
            mainWindow.Width = 1536;
            mainWindow.Height = 864;
            mainWindow.Measure(new System.Windows.Size(1536, 864));
            mainWindow.Arrange(new System.Windows.Rect(0, 0, 1536, 864));
            mainWindow.UpdateLayout();
            var eqContent = (System.Windows.Controls.StackPanel)mainWindow.FindName("EqualizerContent")!;
            eqContent.Measure(new System.Windows.Size(1536, 864));
            eqContent.Arrange(new System.Windows.Rect(0, 0, 1536, 864));
            Check(eqContent.ActualWidth > 1200, $"equalizer uses the width of a large window ({eqContent.ActualWidth:0} DIP)");
            Check(app.Resources.Contains(typeof(System.Windows.Controls.MenuItem)) &&
                  app.Resources.Contains(typeof(System.Windows.Controls.ContextMenu)) &&
                  app.Resources.Contains("EqGainSliderStyle"),
                "menu and EQ sliders use dedicated dark styles");
            var lastBand = vm.Dsp!.GraphicBands.Single(b => b.Frequency == 20000);
            eqList.ScrollIntoView(lastBand);
            eqList.ApplyTemplate();
            eqList.Measure(new System.Windows.Size(980, 250));
            eqList.Arrange(new System.Windows.Rect(0, 0, 980, 250));
            mainWindow.UpdateLayout();
            var lastItem = (System.Windows.Controls.ListBoxItem)eqList.ItemContainerGenerator.ContainerFromItem(lastBand)!;
            lastItem.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = System.Windows.UIElement.PreviewMouseLeftButtonDownEvent });
            Check(ReferenceEquals(eqList.SelectedItem, lastBand) && ReferenceEquals(vm.SelectedEqBand, lastBand),
                "clicking the 20 kHz band selects 20 kHz, not another band");
            lastBand.GainDb = 3.5f;
            DrainUi();
            var liveBandValue = (System.Windows.Controls.TextBlock)mainWindow.FindName("LiveBandValue")!;
            Check(liveBandValue.Text.Contains("+3.5") && liveBandValue.Text.Contains("20.0 kHz"),
                "selected band's value readout updates immediately with gain changes");
            var bandSlider = FindDescendant<System.Windows.Controls.Slider>(lastItem)!;
            bandSlider.SetCurrentValue(System.Windows.Controls.Slider.ValueProperty, 4.25);
            DrainUi();
            Check(Math.Abs(lastBand.GainDb - 4.25f) < .001f && liveBandValue.Text.Contains("+4.3"),
                "dragged EQ gain immediately reaches the model and live readout");
            vm.GraphicEqActive = true;
            Check(vm.GraphicEqActive && vmStore.Load().Profiles.Single(p => p.Id == iem.Id).Dsp.GraphicEnabled,
                "EQ enable state is saved with the selected profile");
            vm.GraphicEqActive = false;
            Check(!vm.GraphicEqActive, "EQ can be turned off without deleting its curve");
            Check(fake.Current == "dac" && fake.Master.Volume == .4f, "startup restores active profile");
            fake.Devices = [new("speakers", "Speakers", true)]; fake.Current = "speakers";
            vm.RefreshCommand.Execute(null);
            fake.Devices = devices; vm.RefreshCommand.Execute(null);
            Check(fake.Current == "dac", "saved output profile restores after reconnect");
            fake.Master = new MasterAudioState(.27f, false);
            fake.Session.Update(.18f, false);
            vm.SelectedProfile!.Applications.Add(new VolumeSetting { ProcessName = "closed-game", Volume = .62f, Muted = true });
            vm.SaveProfileCommand.Execute(null);
            var captured = vmStore.Load().Profiles.Single(p => p.Id == iem.Id);
            Check(captured.MasterVolume == .27f && captured.Applications.Single(p => p.ProcessName == "spotify").Volume == .18f,
                "save reads live values even before polling refreshes the UI");
            Check(captured.Applications.Any(p => p.ProcessName == "closed-game" && p.Volume == .62f),
                "save preserves settings for closed apps");
            fake.SetMasterVolume(.71f); fake.Session.Update(.81f, true);
            vm.ApplyProfileCommand.Execute(null);
            Check(vm.MasterVolume == .27f && vm.Sessions.Single().Volume == .18f && !vm.Sessions.Single().IsMuted,
                "applying selected profile updates displayed master and app controls immediately");
            vm.SelectedProfile = vm.Profiles.Single(p => p.Id == music.Id);
            Check(vm.Status.Contains("no saved mix"), "empty profile reports why no audio changes");
            fake.Devices = []; fake.Current = "";
            vm.RefreshCommand.Execute(null);
            Check(!vm.HasOutput && vm.Sessions.Count == 0 && !vm.SaveProfileCommand.CanExecute(null),
                "device loss clears stale sessions and disables mix saving");
            fake.Devices = devices; fake.Current = "speakers";
            vm.RefreshCommand.Execute(null);
            Check(vm.HasOutput && vm.Sessions.Count == 1, "devices and controls recover after reconnection");
            fake.FailReads = true; vm.RefreshCommand.Execute(null);
            Check(!vm.HasOutput && vm.Devices.Count == 0 && vm.Status.Contains("Audio service unavailable"),
                "audio-service failure clears misleading controls");
            fake.FailReads = false; vm.Resume();
            Check(vm.HasOutput && vm.Status == "Audio connection restored.", "resume refreshes audio and clears the recovery warning");
        }
        var instanceName = "Local\\AudioFlow.Test." + Guid.NewGuid().ToString("N");
        using (var activated = new ManualResetEventSlim())
        {
            using (var primary = new SingleInstanceService(() => activated.Set(), instanceName: instanceName))
            {
                Check(primary.IsPrimary, "first launch owns the instance lock");
                var duplicateIsPrimary = Task.Run(() =>
                {
                    using var duplicate = new SingleInstanceService(() => { }, instanceName: instanceName);
                    return duplicate.IsPrimary;
                }).GetAwaiter().GetResult();
                Check(!duplicateIsPrimary && activated.Wait(3000), "duplicate launch signals the primary instead of acquiring ownership");
                activated.Reset();
                Task.Run(() =>
                {
                    using var background = new SingleInstanceService(() => { }, false, instanceName);
                    Check(!background.IsPrimary, "background duplicate does not acquire ownership");
                }).GetAwaiter().GetResult();
                Check(!activated.Wait(100), "background startup does not reopen the window");
            }
            using var restarted = new SingleInstanceService(() => { }, instanceName: instanceName);
            Check(restarted.IsPrimary, "exit releases the lock for a new launch");
        }
        var logDirectory = Path.Combine(folder, "logs");
        var logger = new DiagnosticLog(logDirectory, 100);
        logger.Write("repeated", new InvalidOperationException("test error"));
        logger.Write("repeated", new InvalidOperationException("test error"));
        Check(File.ReadAllLines(Path.Combine(logDirectory, "audioflow.log")).Length == 1,
            "repeated errors are throttled");
        logger.Write(new string('a', 120));
        logger.Write("rotation");
        Check(File.Exists(Path.Combine(logDirectory, "audioflow.previous.log")) &&
              File.ReadAllText(Path.Combine(logDirectory, "audioflow.log")).Contains("rotation"),
            "logs rotate to a bounded previous file");
        new DiagnosticLog(path).Write("unwritable directory");
        Check(true, "diagnostic write failures do not crash the app");
        var failingStartup = new TestStartup { FailWrites = true };
        using (var vm = new MainViewModel(new FakeAudio(), vmStore, failingStartup))
        {
            vm.Start();
            vm.StartWithWindows = true;
            Check(!vm.StartWithWindows && vm.Status.Contains("Could not change Windows startup"),
                "failed startup change preserves checkbox state and shows error");
            failingStartup.FailWrites = false;
            vm.StartWithWindows = true;
            Check(vm.StartWithWindows && vmStore.Load().StartWithWindows,
                "successful startup change persists its preference");
            vm.StartMinimized = true;
            Check(vmStore.Load().StartMinimized, "open-in-tray preference persists");
        }
        var multiple = new FakeAudio();
        multiple.Session = new AudioSession { Id = "speakers|same-session", ProcessName = "spotify", DisplayName = "Spotify", OutputDeviceId = "speakers", OutputDeviceName = "Speakers" };
        var dacSession = new AudioSession { Id = "dac|same-session", ProcessName = "Spotify", DisplayName = "Spotify", OutputDeviceId = "dac", OutputDeviceName = "JA11" };
        multiple.AdditionalSessions.Add(dacSession);
        multiple.Session.Update(.21f, false, true); dacSession.Update(.73f, true, false);
        var multiProfile = new AudioProfile { Name = "Multiple outputs" };
        var multiProfiles = new ProfileService(multiple);
        multiProfiles.Capture(multiProfile);
        Check(multiProfile.Applications.Count == 2, "capture keeps distinct settings for the same app on two outputs");
        multiple.Session.Update(.9f, true); dacSession.Update(.1f, false);
        multiProfiles.Apply(multiProfile);
        Check(multiple.Session.Volume == .21f && !multiple.Session.IsMuted && dacSession.Volume == .73f && dacSession.IsMuted,
            "profile restores each app/output combination independently");
        var legacy = new AudioProfile { Applications = [new() { ProcessName = "SPOTIFY", Volume = .33f }] };
        ProfileService.ApplySession(legacy, dacSession);
        Check(dacSession.Volume == .33f, "legacy profiles without output IDs remain compatible");
        legacy.Applications.Add(new() { ProcessName = "spotify", OutputDeviceId = "dac", Volume = .64f });
        ProfileService.ApplySession(legacy, dacSession);
        Check(dacSession.Volume == .64f, "device-specific saved volume takes precedence over legacy fallback");
        var multiStore = new SettingsStore(Path.Combine(folder, "multiple.json"));
        multiStore.Save(new AppSettings { Profiles = [multiProfile], ActiveProfileId = multiProfile.Id });
        using (var multiVm = new MainViewModel(multiple, multiStore, new TestStartup()))
        {
            multiVm.Start();
            Check(multiVm.Sessions.Count == 2 && multiVm.Sessions.Select(s => s.Id).Distinct().Count() == 2,
                "mixer retains separate rows for each output");
            multiple.Current = "dac"; multiVm.RefreshCommand.Execute(null);
            Check(multiVm.Sessions.Count == 2, "changing default output keeps the other output's session visible");
            multiple.Session.Update(.21f, false, false); multiVm.RefreshCommand.Execute(null);
            Check(multiVm.Sessions.Single(s => s.OutputDeviceId == "speakers").PlaybackStatus == "Idle",
                "playback state updates without removing an idle controllable session");
            var lateSpeaker = new AudioSession { Id = "speakers|late", ProcessName = "spotify",
                DisplayName = "Spotify", OutputDeviceId = "speakers", OutputDeviceName = "Speakers" };
            lateSpeaker.Update(.95f, true);
            multiple.Session = lateSpeaker;
            multiVm.RefreshCommand.Execute(null);
            Check(lateSpeaker.Volume == .21f && !lateSpeaker.IsMuted,
                "new session restores its saved mix when another output is default");
            var lateDac = new AudioSession { Id = "dac|late", ProcessName = "spotify",
                DisplayName = "Spotify", OutputDeviceId = "dac", OutputDeviceName = "JA11" };
            lateDac.Update(.12f, false);
            multiple.AdditionalSessions.Add(lateDac);
            multiVm.RefreshCommand.Execute(null);
            Check(lateDac.Volume == .73f && lateDac.IsMuted,
                "new session on a second output restores its device-specific mix");
        }
        var notificationAudio = new FakeAudio();
        var notificationStore = new SettingsStore(Path.Combine(folder, "notifications.json"));
        using (var notificationVm = new MainViewModel(notificationAudio, notificationStore, new TestStartup()))
        {
            notificationAudio.RaiseTopology(); DrainUi();
            Check(notificationAudio.ReadCount == 0, "callbacks before Start do not read audio");
            notificationVm.Start();
            var beforeMaster = notificationAudio.ReadCount;
            notificationAudio.Master = new(.42f, true);
            Task.Run(notificationAudio.RaiseMaster).GetAwaiter().GetResult(); DrainUi();
            Check(notificationVm.MasterVolume == .42f && notificationVm.MasterMuted,
                "background master callback updates the mixer on its dispatcher");
            Check(notificationAudio.ReadCount == beforeMaster + 1,
                "master callback avoids full device and session scans");
            notificationAudio.RaiseTopology(); notificationAudio.RaiseMaster();
            notificationVm.Suspend();
            var suspendedReads = notificationAudio.ReadCount;
            DrainUi();
            Task.Run(() => { notificationAudio.RaiseTopology(); notificationAudio.RaiseMaster(); }).GetAwaiter().GetResult();
            DrainUi(); notificationVm.RefreshCommand.Execute(null);
            Check(notificationAudio.ReadCount == suspendedReads,
                "queued callbacks, new callbacks and manual refresh do not read audio while suspended");
            notificationAudio.Master = new(.31f, false);
            notificationVm.Resume();
            Check(notificationAudio.ReadCount > suspendedReads && notificationVm.MasterVolume == .31f,
                "resume reads fresh state immediately");
            notificationAudio.Devices.Clear(); notificationAudio.RaiseTopology(); DrainUi();
            Check(!notificationVm.HasOutput && notificationVm.Sessions.Count == 0,
                "device-loss callback clears disconnected controls");
            notificationAudio.Devices.Add(new("speakers", "Speakers", true));
            notificationAudio.RaiseTopology(); DrainUi();
            Check(notificationVm.HasOutput && notificationVm.Sessions.Count == 1,
                "reconnect callback restores controls without waiting for polling");
            notificationAudio.Session = new() { Id = "replacement", ProcessName = "browser", DisplayName = "Browser" };
            notificationAudio.RaiseTopology(); DrainUi();
            Check(notificationVm.Sessions.Count == 1 && notificationVm.Sessions[0].Id == "replacement",
                "session refresh removes terminated app and adds its replacement");
            notificationAudio.RaiseTopology(); notificationAudio.RaiseMaster();
            notificationVm.Dispose();
            var disposedReads = notificationAudio.ReadCount;
            DrainUi(); notificationAudio.RaiseTopology(); notificationAudio.RaiseMaster(); DrainUi();
            notificationVm.Resume(); notificationVm.Start();
            Check(notificationAudio.ReadCount == disposedReads && notificationAudio.SubscriberCount == 0,
                "shutdown removes subscriptions and ignores queued notifications and resume");
        }
        Check(!PackageIdentity.IsPackaged(), "regression host is correctly identified as unpackaged");
        Check(PackageIdentity.InterpretResult(122), "package identity buffer result identifies packaged host");
        Check(!PackageIdentity.InterpretResult(15700), "no-package result identifies portable host");
        var unknownRejected = false;
        try { PackageIdentity.InterpretResult(5); } catch (System.ComponentModel.Win32Exception) { unknownRejected = true; }
        Check(unknownRejected, "identity failures are not silently treated as portable");
        using (var packagedVm = new MainViewModel(new FakeAudio(), vmStore, new TestStartup { IsManagedByWindows = true }))
        {
            Check(!packagedVm.CanChangeStartupHere && packagedVm.StartupDescription.Contains("Windows Settings"),
                "packaged startup is presented as Windows-managed");
            var version = typeof(AudioFlow.MainWindow).Assembly.GetName().Version?.ToString(3);
            var informational = typeof(AudioFlow.MainWindow).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .Single().InformationalVersion.Split('+')[0];
            Check(version == informational, "assembly and product versions agree");
        }
        var off = new DspSettings();
        off.Bands.Add(new PeqBand { Type = PeqFilterType.Peaking, Frequency = 1000, GainDb = 12 });
        var bypass = new PeqProcessor(off, 48000, 2);
        float[] unchanged = [.1f, -.2f, .3f, -.4f];
        bypass.Process(unchanged);
        Check(unchanged.SequenceEqual(new float[] { .1f, -.2f, .3f, -.4f }) && bypass.BandCount == 0,
            "disabled EQ is an exact passthrough");
        off.Enabled = true;
        var stereoEq = new PeqProcessor(off, 48000, 2);
        float[] separateChannels = [1, 0, 0, 0, 0, 0];
        stereoEq.Process(separateChannels);
        Check(separateChannels[1] == 0 && separateChannels[3] == 0 && separateChannels[5] == 0,
            "EQ keeps channel histories independent");
        static double GainAt(PeqFilterType type, float cutoff, float gainDb, double toneHz)
        {
            var settings = new DspSettings { Enabled = true };
            settings.Bands.Add(new PeqBand { Type = type, Frequency = cutoff, GainDb = gainDb, Q = .7071f });
            var processor = new PeqProcessor(settings, 48000, 1);
            var samples = new float[48000];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = (float)(.1 * Math.Sin(2 * Math.PI * toneHz * i / 48000));
            processor.Process(samples);
            var power = 0d;
            for (var i = 24000; i < samples.Length; i++) power += samples[i] * samples[i];
            return Math.Sqrt(power / 24000) / (.1 / Math.Sqrt(2));
        }
        Check(GainAt(PeqFilterType.Peaking, 1000, 6, 1000) is > 1.9 and < 2.1,
            "peaking EQ supplies the requested six-decibel boost");
        Check(GainAt(PeqFilterType.LowPass, 1000, 0, 100) > .95 &&
              GainAt(PeqFilterType.LowPass, 1000, 0, 10000) < .02,
            "low-pass preserves bass and rejects treble");
        Check(GainAt(PeqFilterType.HighPass, 1000, 0, 100) < .02 &&
              GainAt(PeqFilterType.HighPass, 1000, 0, 10000) > .95,
            "high-pass rejects bass and preserves treble");
        Check(GainAt(PeqFilterType.Notch, 1000, 0, 1000) < .01,
            "notch rejects its center frequency");
        Check(GainAt(PeqFilterType.BandPass, 1000, 0, 1000) is > .95 and < 1.05,
            "band-pass has unity gain at its center frequency");
        Check(GainAt(PeqFilterType.LowShelf, 1000, 6, 50) is > 1.9 and < 2.1 &&
              GainAt(PeqFilterType.LowShelf, 1000, 6, 10000) is > .95 and < 1.05,
            "low shelf boosts bass while leaving treble unchanged");
        Check(GainAt(PeqFilterType.HighShelf, 1000, 6, 50) is > .95 and < 1.05 &&
              GainAt(PeqFilterType.HighShelf, 1000, 6, 10000) is > 1.9 and < 2.1,
            "high shelf boosts treble while leaving bass unchanged");
        var whole = new PeqProcessor(off, 48000, 1);
        var split = new PeqProcessor(off, 48000, 1);
        float[] oneBlock = [1, 0, 0, 0, 0, 0], twoBlocks = (float[])oneBlock.Clone();
        whole.Process(oneBlock);
        split.Process(twoBlocks.AsSpan(0, 3));
        split.Process(twoBlocks.AsSpan(3));
        Check(oneBlock.SequenceEqual(twoBlocks), "EQ state continues seamlessly across buffer boundaries");
        var partialFrameRejected = false;
        try { stereoEq.Process(new float[3]); } catch (ArgumentException) { partialFrameRejected = true; }
        Check(partialFrameRejected, "EQ rejects incomplete interleaved frames");
        var highRate = new PeqProcessor(new DspSettings { Enabled = true,
            Bands = [new PeqBand { Type = PeqFilterType.Peaking, Frequency = 20000, GainDb = 12 }] }, 8000, 1);
        float[] lowRateSamples = [1, 0, 0, 0, 0, 0];
        highRate.Process(lowRateSamples);
        Check(lowRateSamples.All(float.IsFinite), "EQ remains finite when a band exceeds Nyquist");
        var previewGraphic = new DspSettings { Enabled = true };
        previewGraphic.CreateGraphicBands();
        previewGraphic.GraphicBands[5].GainDb = 6;
        var beforeWav = EqPreviewAudio.Render(previewGraphic, false);
        var afterWav = EqPreviewAudio.Render(previewGraphic, true);
        Check(beforeWav.Length == 44 + 48000 * 3 * 2 * sizeof(short) &&
              System.Text.Encoding.ASCII.GetString(beforeWav, 0, 4) == "RIFF" &&
              System.Text.Encoding.ASCII.GetString(beforeWav, 8, 4) == "WAVE" &&
              System.Text.Encoding.ASCII.GetString(beforeWav, 36, 4) == "data",
            "EQ comparison produces a valid bounded stereo WAV");
        Check(beforeWav.AsSpan(44).IndexOfAnyExcept((byte)0) >= 0 &&
              !beforeWav.AsSpan(44).SequenceEqual(afterWav.AsSpan(44)),
            "with-EQ preview audibly differs from the same unprocessed sample");
        Check(beforeWav.SequenceEqual(EqPreviewAudio.Render(off, false)),
            "EQ before-and-after sample remains deterministic");
        var signChanges = 0;
        short previous = BitConverter.ToInt16(beforeWav, 44);
        for (var i = 48; i < beforeWav.Length; i += 4)
        {
            var current = BitConverter.ToInt16(beforeWav, i);
            if ((current > 0 && previous < 0) || (current < 0 && previous > 0)) signChanges++;
            previous = current;
        }
        Check(signChanges < 15000, "preview sample is tonal rather than broadband hiss");
        var liveSettings = new DspSettings { Enabled = true };
        liveSettings.CreateGraphicBands();
        liveSettings.GraphicBands.Single(b => b.Frequency == 1000).GainDb = 9;
        var liveSnapshot = LiveEqPreviewStream.Snapshot(liveSettings);
        liveSettings.GraphicBands.Single(b => b.Frequency == 1000).GainDb = -9;
        Check(liveSnapshot.GraphicBands.Single(b => b.Frequency == 1000).GainDb == 9,
            "live EQ snapshot cannot be changed by a concurrent UI edit");
        var livePipeline = new RealtimeEqPipeline(48000, 2, true);
        var liveBlock = new float[960 * 2];
        for (var i = 0; i < 960; i++)
            liveBlock[2 * i] = liveBlock[2 * i + 1] = .05f * MathF.Sin(2 * MathF.PI * 1000 * i / 48000);
        var dryBlock = liveBlock.ToArray();
        livePipeline.Process(liveBlock, liveSnapshot);
        Check(liveBlock.All(float.IsFinite) && !liveBlock.SequenceEqual(dryBlock),
            "live audio block is processed in place");
        var changedSnapshot = LiveEqPreviewStream.Snapshot(liveSettings);
        var nextBlock = dryBlock.ToArray();
        livePipeline.Process(nextBlock, changedSnapshot);
        Check(nextBlock.All(float.IsFinite) &&
              Math.Abs(nextBlock[0] - liveBlock[^2]) < .5f &&
              !nextBlock.SequenceEqual(liveBlock),
            "live EQ switches settings without non-finite samples or large boundary jumps");
        var dryPipeline = new RealtimeEqPipeline(48000, 2, false);
        var untouchedBlock = dryBlock.ToArray();
        dryPipeline.Process(untouchedBlock, liveSnapshot);
        Check(untouchedBlock.SequenceEqual(dryBlock), "original live preview bypasses EQ exactly");
        var flatSettings = new DspSettings { Enabled = true };
        flatSettings.CreateGraphicBands();
        var flatBlock = dryBlock.ToArray();
        new RealtimeEqPipeline(48000, 2, true).Process(flatBlock, LiveEqPreviewStream.Snapshot(flatSettings));
        Check(flatBlock.SequenceEqual(dryBlock), "flat live EQ is an exact passthrough");
        var invalidChannelsRejected = false;
        try { _ = new RealtimeEqPipeline(48000, 0, false); }
        catch (ArgumentOutOfRangeException) { invalidChannelsRejected = true; }
        Check(invalidChannelsRejected, "live pipeline validates channel count even in bypass mode");
        var livePartialFrameRejected = false;
        try { livePipeline.Process(new float[3], liveSnapshot); }
        catch (ArgumentException) { livePartialFrameRejected = true; }
        Check(livePartialFrameRejected, "live pipeline rejects incomplete frames");
        if (Environment.GetEnvironmentVariable("AUDIOFLOW_SMOKE_OUTPUT") == "1")
        {
            using var liveOutput = new LiveEqPreviewStream();
            string? playbackError = null;
            liveOutput.PlaybackFailed += error => playbackError = error;
            liveOutput.Play(liveSnapshot, true);
            Thread.Sleep(350);
            liveOutput.UpdateSettings(changedSnapshot);
            Thread.Sleep(150);
            Check(liveOutput.IsPlaying && playbackError is null,
                "Windows output streams processed EQ while the sample loops");
            liveOutput.Stop();
            Check(!liveOutput.IsPlaying, "live Windows output stops and releases its buffers");
            for (var restart = 0; restart < 3; restart++)
            {
                liveOutput.Play(flatSettings, false);
                Thread.Sleep(80);
                liveOutput.Stop();
            }
            Check(!liveOutput.IsPlaying && playbackError is null, "live output survives repeated start and stop");
            liveOutput.Dispose();
            var disposedPlaybackRejected = false;
            try { liveOutput.Play(flatSettings, false); }
            catch (ObjectDisposedException) { disposedPlaybackRejected = true; }
            Check(disposedPlaybackRejected, "disposed live output cannot restart");
        }
        var graphic = new DspSettings { Enabled = true };
        graphic.CreateGraphicBands();
        Check(graphic.GraphicEnabled && graphic.GraphicBands.Count == 11 &&
              graphic.GraphicBands[0].Frequency == 31.5f &&
              graphic.GraphicBands[^1].Frequency == 20000,
            "tuning starts with eleven useful frequencies");
        graphic.GraphicBands[5].GainDb = 6; // 1 kHz
        Check(new PeqProcessor(graphic, 48000, 2).BandCount == 1,
            "flat graphic bands are bypassed and adjusted bands are processed");
        var legacyDsp = new DspSettings();
        legacyDsp.Bands.Add(new PeqBand { Frequency = 250, GainDb = -3 });
        legacyDsp.Compressor.Enabled = true;
        legacyDsp.Spatial.CrossfeedEnabled = true;
        legacyDsp.CreateGraphicBands();
        Check(legacyDsp.Bands.Count == 1 && legacyDsp.Compressor.Enabled && legacyDsp.Spatial.CrossfeedEnabled,
            "new EQ layout preserves earlier detailed and spatial settings");
        var graphicJson = System.Text.Json.JsonSerializer.Serialize(graphic);
        var restoredGraphic = System.Text.Json.JsonSerializer.Deserialize<DspSettings>(graphicJson)!.Normalize();
        Check(restoredGraphic.GraphicEnabled && restoredGraphic.GraphicBands.Count == 11 &&
              restoredGraphic.GraphicBands[5].GainDb == 6,
            "graphic tuning persists with the profile");
        var addedBand = restoredGraphic.AddGraphicBand()!;
        addedBand.Type = PeqFilterType.LowShelf;
        addedBand.Q = .8f;
        addedBand.GainDb = 4;
        Check(restoredGraphic.GraphicBands.Count == 12 && restoredGraphic.GraphicBands.Contains(addedBand),
            "additional band can be added without replacing the original eleven");
        Check(restoredGraphic.GraphicBands.Count == 12 && restoredGraphic.GraphicBands.Contains(addedBand),
            "explicitly added bands appear immediately");
        var shelfRestored = System.Text.Json.JsonSerializer.Deserialize<DspSettings>(
            System.Text.Json.JsonSerializer.Serialize(restoredGraphic))!.Normalize();
        Check(shelfRestored.GraphicBands.Count == 12 && shelfRestored.CompactEqMigrated &&
              shelfRestored.GraphicBands.Any(b => b.Type == PeqFilterType.LowShelf && b.Q == .8f && b.GainDb == 4),
            "shelf type, Q and gain survive a settings roundtrip");
        addedBand.Type = PeqFilterType.HighShelf;
        Check(new PeqProcessor(restoredGraphic, 48000, 2).BandCount == 2 &&
              !EqPreviewAudio.Render(restoredGraphic, false).AsSpan(44)
                .SequenceEqual(EqPreviewAudio.Render(restoredGraphic, true).AsSpan(44)),
            "high-shelf filter participates in the audible preview");
        var lowPassBand = restoredGraphic.AddGraphicBand()!;
        lowPassBand.Type = PeqFilterType.LowPass;
        Check(new PeqProcessor(restoredGraphic, 48000, 2).BandCount == 3,
            "zero-gain cutoff filters are not skipped");
        while (restoredGraphic.AddGraphicBand() is not null) { }
        Check(restoredGraphic.GraphicBands.Count == DspSettings.MaxGraphicBands &&
              restoredGraphic.AddGraphicBand() is null,
            "additional EQ bands stop at the safe 31-band limit");
        var oldLayout = new DspSettings { Enabled = true, GraphicEnabled = true,
            GraphicBands = new System.Collections.ObjectModel.ObservableCollection<PeqBand>(
                DspSettings.GraphicFrequencies.Select(f => new PeqBand { Frequency = f, Q = 4.3f })) };
        oldLayout.GraphicBands[18].GainDb = -4; // non-default 1.25 kHz band
        oldLayout.Normalize();
        Check(oldLayout.GraphicBands.Count == 11 &&
              oldLayout.ArchivedGraphicBands.Any(b => b.Frequency == 1250 && b.GainDb == -4),
            "older 31-band curves become eleven active bands with extra values archived");
        var archivedRoundtrip = System.Text.Json.JsonSerializer.Deserialize<DspSettings>(
            System.Text.Json.JsonSerializer.Serialize(oldLayout))!.Normalize();
        Check(archivedRoundtrip.GraphicBands.Count == 11 && archivedRoundtrip.ArchivedGraphicBands.Count == 1,
            "archived legacy adjustments survive reload without duplication");
        var previousFlexibleLayout = new DspSettings { GraphicBands = new System.Collections.ObjectModel.ObservableCollection<PeqBand>(
            DspSettings.StartingFrequencies.Select(f => new PeqBand { Frequency = f, Q = 1.4f })
                .Append(new PeqBand { Frequency = 1250, GainDb = -2, Q = 1.4f })) };
        previousFlexibleLayout.Normalize();
        Check(previousFlexibleLayout.GraphicBands.Count == 11 &&
              previousFlexibleLayout.ArchivedGraphicBands.Count == 1 &&
              previousFlexibleLayout.ArchivedGraphicBands[0].GainDb == -2,
            "previous flexible profiles open with eleven bands while archiving extras");
        restoredGraphic.GraphicEnabled = false;
        Check(new PeqProcessor(restoredGraphic, 48000, 2).BandCount == 0,
            "graphic EQ can be bypassed independently of detailed filters");
        var spatial = new SpatialSettings { CrossfeedEnabled = true, CrossfeedAmount = .2f };
        var crossfeed = new StereoCrossfeed(spatial, 48000);
        var leftOnly = new float[400];
        for (var i = 0; i < leftOnly.Length; i += 2) leftOnly[i] = .1f;
        crossfeed.Process(leftOnly);
        Check(leftOnly[0] < .1f && leftOnly[^1] > 0 && leftOnly[^1] < .03f,
            "crossfeed gently blends low-frequency sound between ears");
        var disabledCrossfeed = new StereoCrossfeed(new SpatialSettings(), 48000);
        float[] directStereo = [.2f, -.1f, .3f, -.2f];
        disabledCrossfeed.Process(directStereo);
        Check(directStereo.SequenceEqual(new float[] { .2f, -.1f, .3f, -.2f }),
            "disabled crossfeed leaves stereo samples untouched");
        var oddStereoRejected = false;
        try { crossfeed.Process(new float[3]); } catch (ArgumentException) { oddStereoRejected = true; }
        Check(oddStereoRejected, "crossfeed rejects incomplete stereo frames");
        var spatialOnly = new DspSettings { Enabled = true };
        spatialOnly.Spatial.CrossfeedEnabled = true;
        var spatialBefore = EqPreviewAudio.Render(spatialOnly, false);
        var spatialAfter = EqPreviewAudio.Render(spatialOnly, true);
        Check(spatialBefore.AsSpan(44).SequenceEqual(spatialAfter.AsSpan(44)) &&
              beforeWav.AsSpan(44).SequenceEqual(EqPreviewAudio.Render(off, true).AsSpan(44)),
            "hidden detailed and spatial settings do not change the simplified EQ preview");
        var spatialSaved = System.Text.Json.JsonSerializer.Deserialize<DspSettings>(
            System.Text.Json.JsonSerializer.Serialize(spatialOnly))!.Normalize();
        Check(spatialSaved.Spatial.CrossfeedEnabled && spatialSaved.Spatial.CrossfeedAmount == .12f,
            "crossfeed settings persist with a profile");
        Console.WriteLine($"ALL {checks} CHECKS PASSED");
    }
}

internal sealed class FakeAudio : IAudioService, IAudioChangeNotifier
{
    public event Action? Changed;
    public event Action? MasterStateChanged;
    public int SubscriberCount => (Changed?.GetInvocationList().Length ?? 0) + (MasterStateChanged?.GetInvocationList().Length ?? 0);
    public int ReadCount;
    public void RaiseTopology() => Changed?.Invoke();
    public void RaiseMaster() => MasterStateChanged?.Invoke();
    public List<AudioDevice> Devices = [new("speakers", "Speakers", true), new("dac", "FiiO JA11", false)];
    public string Current = "speakers";
    public List<string> Calls = [];
    public MasterAudioState Master = new(.75f, false);
    public AudioSession Session = new() { Id = "session", ProcessName = "spotify", DisplayName = "Spotify" };
    public List<AudioSession> AdditionalSessions = [];
    public bool FailReads;
    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        ReadCount++;
        return FailReads ? throw new InvalidOperationException("simulated audio restart")
            : Devices.Select(d => d with { IsDefault = d.Id == Current }).ToList();
    }
    public IReadOnlyList<AudioSession> GetSessions() { ReadCount++; Calls.Add("sessions:" + Current); return [Session, ..AdditionalSessions]; }
    public MasterAudioState GetMasterState() { ReadCount++; return Master; }
    public void SetMasterVolume(float value) { Calls.Add("volume"); Master = Master with { Volume = value }; }
    public void SetMasterMute(bool value) { Calls.Add("mute"); Master = Master with { IsMuted = value }; }
    public void SetDefaultDevice(string id) { Calls.Add("switch"); Current = id; }
    public void Dispose() { }
}

internal sealed class TestStartup : IStartupService
{
    public bool IsManagedByWindows { get; set; }
    public bool FailWrites;
    private bool enabled;
    public bool IsEnabled
    {
        get => enabled;
        set { if (FailWrites) throw new UnauthorizedAccessException("test denial"); enabled = value; }
    }
}
