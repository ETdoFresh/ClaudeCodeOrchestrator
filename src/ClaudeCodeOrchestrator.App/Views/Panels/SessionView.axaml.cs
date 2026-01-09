using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClaudeCodeOrchestrator.App.Models;
using ClaudeCodeOrchestrator.App.ViewModels.Docking;
using ClaudeCodeOrchestrator.App.Views.Controls;

namespace ClaudeCodeOrchestrator.App.Views.Panels;

public partial class SessionView : UserControl
{
    private readonly List<ImageAttachment> _attachments = new();
    private DispatcherTimer? _statusTimer;

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
        // Handle Ctrl+V (Windows/Linux) or Cmd+V (macOS) for paste
        if (e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
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
            if (clipboard == null)
            {
                ShowStatus("Clipboard not available", isError: true);
                return;
            }

            var formats = await clipboard.GetFormatsAsync();
            Debug.WriteLine($"[ImagePaste] Available clipboard formats: {string.Join(", ", formats)}");

            // Check for various image formats (different platforms use different names)
            string[] imageFormats = { "image/png", "PNG", "image/jpeg", "JPEG", "image/bmp", "BMP", "image/gif", "GIF" };

            foreach (var format in imageFormats)
            {
                if (!formats.Contains(format)) continue;

                var data = await clipboard.GetDataAsync(format);
                Debug.WriteLine($"[ImagePaste] Got data for format {format}: {data?.GetType().Name ?? "null"}");

                if (data is byte[] imageBytes && imageBytes.Length > 0)
                {
                    var mediaType = format.StartsWith("image/") ? format : $"image/{format.ToLowerInvariant()}";
                    AddImageAttachment(imageBytes, mediaType, $"pasted-image.{GetExtensionForFormat(format)}");
                    ShowStatus($"✓ Image attached ({imageBytes.Length / 1024}KB)", isError: false);
                    return;
                }

                // Some platforms return a stream instead of bytes
                if (data is Stream stream)
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    if (bytes.Length > 0)
                    {
                        var mediaType = format.StartsWith("image/") ? format : $"image/{format.ToLowerInvariant()}";
                        AddImageAttachment(bytes, mediaType, $"pasted-image.{GetExtensionForFormat(format)}");
                        ShowStatus($"✓ Image attached ({bytes.Length / 1024}KB)", isError: false);
                        return;
                    }
                }
            }

            // Try to get files from clipboard (for copied image files)
            var files = await clipboard.GetDataAsync(DataFormats.Files) as IEnumerable<IStorageItem>;
            if (files != null)
            {
                var imageFiles = files.OfType<IStorageFile>().ToList();
                var count = 0;
                foreach (var file in imageFiles)
                {
                    var ext = Path.GetExtension(file.Name).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp")
                    {
                        await AddImageFromFile(file);
                        count++;
                    }
                }
                if (count > 0)
                {
                    ShowStatus($"✓ {count} image(s) attached", isError: false);
                    return;
                }
            }

            // No image found - show available formats for debugging
            ShowStatus($"No image in clipboard. Formats: {string.Join(", ", formats.Take(5))}", isError: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImagePaste] Error: {ex}");
            ShowStatus($"Paste failed: {ex.Message}", isError: true);
        }
    }

    private static string GetExtensionForFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "image/png" or "png" => "png",
            "image/jpeg" or "jpeg" or "jpg" => "jpg",
            "image/gif" or "gif" => "gif",
            "image/bmp" or "bmp" => "bmp",
            "image/webp" or "webp" => "webp",
            _ => "png"
        };
    }

    private void ShowStatus(string message, bool isError)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PasteStatusText.Text = message;
            PasteStatusText.Foreground = isError
                ? new SolidColorBrush(Color.Parse("#FF6B6B"))
                : new SolidColorBrush(Color.Parse("#4EC9B0"));
            PasteStatusBorder.IsVisible = true;

            // Auto-hide after 3 seconds
            _statusTimer?.Stop();
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _statusTimer.Tick += (_, _) =>
            {
                PasteStatusBorder.IsVisible = false;
                _statusTimer.Stop();
            };
            _statusTimer.Start();
        });
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
