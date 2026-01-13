using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ClaudeCodeOrchestrator.App.Views.Dialogs;

public partial class RepositorySettingsDialog : Window
{
    /// <summary>
    /// Gets the executable value entered by the user.
    /// </summary>
    public string? Executable { get; private set; }

    /// <summary>
    /// Gets the task branch prefix entered by the user.
    /// </summary>
    public string? TaskBranchPrefix { get; private set; }

    /// <summary>
    /// Gets the job branch prefix entered by the user.
    /// </summary>
    public string? JobBranchPrefix { get; private set; }

    /// <summary>
    /// Gets whether the user saved the settings.
    /// </summary>
    public bool WasSaved { get; private set; }

    public RepositorySettingsDialog()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Enter or Ctrl+Enter saves
            Save();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Escape cancels
            Cancel();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Sets the initial executable value.
    /// </summary>
    public void SetExecutable(string? executable)
    {
        ExecutableTextBox.Text = executable ?? string.Empty;
    }

    /// <summary>
    /// Sets the initial branch prefix values.
    /// </summary>
    public void SetBranchPrefixes(string? taskPrefix, string? jobPrefix)
    {
        TaskBranchPrefixTextBox.Text = taskPrefix ?? string.Empty;
        JobBranchPrefixTextBox.Text = jobPrefix ?? string.Empty;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Cancel();
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        Save();
    }

    private void Cancel()
    {
        WasSaved = false;
        Close();
    }

    private void Save()
    {
        Executable = ExecutableTextBox.Text;
        TaskBranchPrefix = TaskBranchPrefixTextBox.Text;
        JobBranchPrefix = JobBranchPrefixTextBox.Text;
        WasSaved = true;
        Close();
    }
}
