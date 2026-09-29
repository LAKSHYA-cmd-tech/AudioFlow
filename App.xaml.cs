using System.Windows;
using AudioFlow.Services;
using AudioFlow.ViewModels;
using Microsoft.Win32;

namespace AudioFlow;

public partial class App : System.Windows.Application
{
    private MainWindow? _window;
    private TrayService? _tray;
    private SingleInstanceService? _instance;
    private readonly DiagnosticLog _log = new();
    private bool _activationRequested;
    private bool _shuttingDown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            _log.Write("app.unhandled", args.Exception);
            args.Handled = true;
            FailAndExit(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += DomainUnhandled;
        try
        {
            _instance = new SingleInstanceService(() =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.BeginInvoke(new Action(() => { _activationRequested = true; ShowMainWindow(); }));
            }, !e.Args.Contains("--minimized"));
            if (!_instance.IsPrimary) { Shutdown(); return; }
            _log.Write("app.start " + typeof(App).Assembly.GetName().Version);
            var store = new SettingsStore();
            var audio = new WindowsAudioService(_log);
            var startup = new StartupService();
            MainViewModel vm;
            try { vm = new MainViewModel(audio, store, startup, _log); }
            catch { audio.Dispose(); throw; }
            if (store.LoadWarning is not null) _log.Write("settings.recovery " + store.LoadWarning);
            _window = new MainWindow { DataContext = vm };
            _tray = new TrayService(_window, () => { vm.IsExiting = true; _shuttingDown = true; Shutdown(); });
            _window.Closing += (_, args) =>
            {
                if (!vm.IsExiting && !_shuttingDown)
                {
                    args.Cancel = true;
                    _window.Hide();
                    _tray.ShowBackgroundHint();
                }
            };
            vm.ExitRequested += (_, _) => { vm.IsExiting = true; _shuttingDown = true; Shutdown(); };
            // Start before showing so the first paint already lists devices, sessions and the
            // restored mix instead of flashing an empty window.
            vm.Start();
            if (_activationRequested || (!e.Args.Contains("--minimized") && !vm.StartMinimized)) ShowMainWindow();
            SystemEvents.PowerModeChanged += PowerModeChanged;
        }
        catch (Exception ex) { _log.Write("app.start.failed", ex); FailAndExit(ex); }
    }

    private void ShowMainWindow()
    {
        if (_window is null || _shuttingDown) return;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void DomainUnhandled(object sender, UnhandledExceptionEventArgs e) =>
        _log.Write("app.domain.unhandled", e.ExceptionObject as Exception);

    private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (_shuttingDown || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_shuttingDown || _window?.DataContext is not MainViewModel vm) return;
            if (e.Mode == PowerModes.Suspend) vm.Suspend();
            else if (e.Mode == PowerModes.Resume) vm.Resume();
        }));
    }

    private void FailAndExit(Exception error)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        System.Windows.MessageBox.Show("AudioFlow could not continue. Please reopen it.\n\n" +
            error.Message + "\n\nDetails are saved in the AudioFlow Logs folder.",
            "AudioFlow", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _shuttingDown = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;
        AppDomain.CurrentDomain.UnhandledException -= DomainUnhandled;
        SystemEvents.PowerModeChanged -= PowerModeChanged;
        try { (_window?.DataContext as IDisposable)?.Dispose(); }
        catch (Exception ex) { _log.Write("app.audio.cleanup", ex); }
        try { _tray?.Dispose(); }
        catch (Exception ex) { _log.Write("app.tray.cleanup", ex); }
        if (_instance?.IsPrimary == true) _log.Write("app.stop");
        _instance?.Dispose();
        base.OnExit(e);
    }
}
