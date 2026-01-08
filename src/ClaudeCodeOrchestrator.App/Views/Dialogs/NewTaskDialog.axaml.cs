using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClaudeCodeOrchestrator.App.Views.Dialogs;

public partial class NewTaskDialog : Window
{
    public NewTaskDialog()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        var description = TaskDescriptionBox.Text?.Trim();

        if (string.IsNullOrEmpty(description))
        {
            ErrorText.Text = "Please enter a task description.";
            ErrorText.IsVisible = true;
            return;
        }

        Close(description);
    }
}
