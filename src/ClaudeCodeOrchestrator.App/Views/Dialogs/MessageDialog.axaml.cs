using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ClaudeCodeOrchestrator.App.Views.Dialogs;

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public MessageDialog(string title, string message, bool isError = false)
    {
        InitializeComponent();

        Title = isError ? "Error" : "Information";
        TitleText.Text = title;
        MessageText.Text = message;

        if (isError)
        {
            TitleText.Foreground = new SolidColorBrush(Color.Parse("#F44747"));
        }
    }

    private void OK_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
