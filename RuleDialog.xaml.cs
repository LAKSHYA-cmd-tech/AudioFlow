using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AudioFlow.Models;

namespace AudioFlow;
public partial class RuleDialog : Window
{
    public AutomationRule? Rule { get; private set; }
    public RuleDialog(ObservableCollection<AudioProfile> profiles)
    {
        InitializeComponent(); ProfileBox.ItemsSource = profiles; TriggerBox.SelectedIndex = 0; ProfileBox.SelectedIndex = 0;
    }
    private void TriggerBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MatchHint is null) return; MatchHint.Text = TriggerBox.SelectedIndex == 0 ? "Example: spotify or valorant" : "Example: FiiO or Bluetooth";
    }
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MatchText.Text) || ProfileBox.SelectedItem is not AudioProfile profile) return;
        Rule = new() { TriggerType = TriggerBox.SelectedIndex == 0 ? RuleTriggerType.ApplicationRunning : RuleTriggerType.DeviceConnected, Match = MatchText.Text.Trim(), ProfileId = profile.Id };
        DialogResult = true;
    }
}
