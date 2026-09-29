using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioFlow.Models;
using AudioFlow.ViewModels;

namespace AudioFlow;
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Read the version from the assembly so the title can never drift from AudioFlow.csproj.
        // InformationalVersion carries <Version> verbatim; AssemblyVersion is truncated to
        // major.minor.0.0 by the SDK and would hide the patch number.
        Title = "AudioFlow" + (BuildVersion() is { Length: > 0 } version ? " " + version : "");
    }

    private static string? BuildVersion()
    {
        var informational = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }
        return typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
    }

    private void ManageRules_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) new RulesWindow(vm) { Owner = this }.ShowDialog();
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = MenuButton;
        menu.IsOpen = true;
    }

    private void ShowEqualizer_Click(object sender, RoutedEventArgs e) => Pages.SelectedIndex = 1;
    private void ShowMixer_Click(object sender, RoutedEventArgs e) => Pages.SelectedIndex = 0;
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => Pages.SelectedIndex = 2;

    private void EqBandItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: PeqBand band }) EqBandsList.SelectedItem = band;
    }

}
