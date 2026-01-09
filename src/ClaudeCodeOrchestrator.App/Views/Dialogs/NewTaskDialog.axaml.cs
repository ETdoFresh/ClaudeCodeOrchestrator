using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ClaudeCodeOrchestrator.App.Models;
using ClaudeCodeOrchestrator.App.Views.Controls;

namespace ClaudeCodeOrchestrator.App.Views.Dialogs;

public partial class NewTaskDialog : Window
{
    private readonly List<ImageAttachment> _attachments = new();

    private static readonly FilePickerFileType ImageFileTypes = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" },
        MimeTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp" }
    };

    public NewTaskDialog()
    {
        InitializeComponent();
        Opened += OnOpened;

        // Handle keyboard shortcuts
        KeyDown += OnKeyDown;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        TaskDescriptionBox.Focus();
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Ctrl+V for paste
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await TryPasteImageFromClipboard();
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
        var storageProvider = StorageProvider;
        if (storageProvider == null || !storageProvider.CanOpen) return;

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

        var result = TaskInput.Create(description, _attachments.ToList());
        Close(result);
    }
}
