using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClaudeCodeOrchestrator.App.Views.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.Major ?? 0}.{version?.Minor ?? 0}.{version?.Build ?? 0}";
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
