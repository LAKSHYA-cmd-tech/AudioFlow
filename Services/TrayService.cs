using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace AudioFlow.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Window _window;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _hintShown;
    public TrayService(Window window, Action exit)
    {
        _window = window;
        var menu = _menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open AudioFlow", null, (_, _) => Show());
        menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon { Text = "AudioFlow", Icon = SystemIcons.Information, ContextMenuStrip = menu, Visible = true };
        _icon.DoubleClick += (_, _) => Show();
    }
    private void Show() { _window.Show(); _window.WindowState = WindowState.Normal; _window.Activate(); }
    public void ShowBackgroundHint()
    {
        if (_hintShown) return; _hintShown = true;
        _icon.ShowBalloonTip(2500, "AudioFlow is still running", "Use the tray icon to reopen or exit.", Forms.ToolTipIcon.Info);
    }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _menu.Dispose(); }
}
