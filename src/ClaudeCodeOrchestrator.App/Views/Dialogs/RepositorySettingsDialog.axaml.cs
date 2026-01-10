using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClaudeCodeOrchestrator.App.Views.Dialogs;

public partial class RepositorySettingsDialog : Window
{
    /// <summary>
    /// Gets the executable value entered by the user.
    /// </summary>
    public string? Executable { get; private set; }

    /// <summary>
    /// Gets whether the user saved the settings.
    /// </summary>
    public bool WasSaved { get; private set; }

    public RepositorySettingsDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the initial executable value.
    /// </summary>
    public void SetExecutable(string? executable)
    {
        ExecutableTextBox.Text = executable ?? string.Empty;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        WasSaved = false;
        Close();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        Executable = ExecutableTextBox.Text;
        WasSaved = true;
        Close();
    }
}
