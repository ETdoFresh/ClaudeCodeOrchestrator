using Avalonia.Controls;
using Avalonia.Input;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;

namespace ClaudeCodeOrchestrator.App.Views.Panels;

public partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SessionDocumentViewModel viewModel)
        {
            viewModel.CopyToClipboard = CopyToClipboardAsync;
        }
    }

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void MessageInputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        // Only handle Enter key without modifiers (Shift+Enter should still add newlines)
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;

        if (sender is not TextBox textBox)
            return;

        // Check if cursor is at the end of the text
        var text = textBox.Text ?? string.Empty;
        var caretIndex = textBox.CaretIndex;

        if (caretIndex != text.Length)
            return;

        // Get the view model and execute the appropriate command
        if (DataContext is not SessionDocumentViewModel viewModel)
            return;

        // Determine which command to execute based on state
        if (viewModel.SendMessageCommand.CanExecute(null))
        {
            viewModel.SendMessageCommand.Execute(null);
            e.Handled = true;
        }
        else if (viewModel.QueueMessageCommand.CanExecute(null))
        {
            viewModel.QueueMessageCommand.Execute(null);
            e.Handled = true;
        }
    }
}
