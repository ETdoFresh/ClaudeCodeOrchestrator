using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClaudeCodeOrchestrator.App.Models;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;
using ClaudeCodeOrchestrator.App.Views.Controls;

namespace ClaudeCodeOrchestrator.App.Views.Panels;

public partial class SessionView : UserControl
{
    private readonly List<ImageAttachment> _attachments = new();

    private static readonly FilePickerFileType ImageFileTypes = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" },
        MimeTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp" }
    };

    public SessionView()
    {
        InitializeComponent();

        // Set up event handlers
        AttachButton.Click += AttachButton_Click;
    }

    private async void MessageInput_KeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Ctrl+V for paste
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await TryPasteImageFromClipboard();
            return;
        }

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

    private async Task TryPasteImageFromClipboard()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            var formats = await clipboard.GetFormatsAsync();

            // Check for image data
            if (formats.Contains("image/png") || formats.Contains("PNG"))
            {
                var data = await clipboard.GetDataAsync("image/png")
                        ?? await clipboard.GetDataAsync("PNG");

                if (data is byte[] imageBytes)
                {
                    AddImageAttachment(imageBytes, "image/png", "pasted-image.png");
                    return;
                }
            }

            // Try to get files from clipboard
            var files = await clipboard.GetDataAsync(DataFormats.Files) as IEnumerable<IStorageItem>;
            if (files != null)
            {
                foreach (var file in files.OfType<IStorageFile>())
                {
                    await AddImageFromFile(file);
                }
            }
        }
        catch
        {
            // Clipboard access failed, ignore
        }
    }

    private async void AttachButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var storageProvider = topLevel.StorageProvider;
        if (!storageProvider.CanOpen) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Images",
            AllowMultiple = true,
            FileTypeFilter = new[] { ImageFileTypes }
        });

        foreach (var file in files)
        {
            await AddImageFromFile(file);
        }
    }

    private async Task AddImageFromFile(IStorageFile file)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var mediaType = GetMediaTypeFromFileName(file.Name);
            AddImageAttachment(bytes, mediaType, file.Name);
        }
        catch
        {
            // Failed to read file, ignore
        }
    }

    private void AddImageAttachment(byte[] bytes, string mediaType, string fileName)
    {
        var attachment = ImageAttachment.FromBytes(bytes, mediaType, fileName);
        _attachments.Add(attachment);
        UpdateAttachmentsDisplay();

        // Notify ViewModel of attachments change
        if (DataContext is SessionDocumentViewModel vm)
        {
            vm.SetAttachments(_attachments.ToList());
        }
    }

    private void UpdateAttachmentsDisplay()
    {
        AttachmentsArea.IsVisible = _attachments.Count > 0;

        // Clear and rebuild the attachments panel
        var items = new List<ImageAttachmentControl>();
        foreach (var attachment in _attachments)
        {
            var control = new ImageAttachmentControl { Attachment = attachment };
            control.RemoveRequested += OnAttachmentRemoveRequested;
            items.Add(control);
        }
        AttachmentsPanel.ItemsSource = items;
    }

    private void OnAttachmentRemoveRequested(object? sender, ImageAttachment attachment)
    {
        _attachments.Remove(attachment);

        if (sender is ImageAttachmentControl control)
        {
            control.RemoveRequested -= OnAttachmentRemoveRequested;
        }

        UpdateAttachmentsDisplay();

        // Notify ViewModel of attachments change
        if (DataContext is SessionDocumentViewModel vm)
        {
            vm.SetAttachments(_attachments.ToList());
        }
    }

    private static string GetMediaTypeFromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }

    /// <summary>
    /// Called when the ViewModel sends a message to clear attachments.
    /// </summary>
    public void ClearAttachments()
    {
        _attachments.Clear();
        UpdateAttachmentsDisplay();
    }
}
