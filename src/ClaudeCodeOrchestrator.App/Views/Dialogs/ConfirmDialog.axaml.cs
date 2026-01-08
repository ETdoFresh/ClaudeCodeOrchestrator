using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ClaudeCodeOrchestrator.App.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void Yes_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
