using System.Windows;
using AudioFlow.ViewModels;

namespace AudioFlow;
public partial class RulesWindow : Window
{
    public RulesWindow(MainViewModel vm) { InitializeComponent(); DataContext = vm; }
}
