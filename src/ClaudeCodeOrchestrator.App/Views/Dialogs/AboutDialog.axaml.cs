using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
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

    private void GitHubLink_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var url = "https://github.com/ETdoFresh/ClaudeCodeOrchestrator";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
    }
}
