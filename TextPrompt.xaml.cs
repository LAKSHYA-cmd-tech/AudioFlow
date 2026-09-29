using System.Windows;

namespace AudioFlow;
public partial class TextPrompt : Window
{
    public string Value => ValueText.Text;
    public TextPrompt(string title, string prompt, string initial)
    {
        InitializeComponent(); Title = title; PromptText.Text = prompt; ValueText.Text = initial;
        Loaded += (_, _) => { ValueText.Focus(); ValueText.SelectAll(); };
    }
    private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = !string.IsNullOrWhiteSpace(Value);
}
