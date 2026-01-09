using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClaudeCodeOrchestrator.App.Models;

namespace ClaudeCodeOrchestrator.App.Views.Controls;

/// <summary>
/// Inline control for creating new tasks, designed to be embedded directly in panels.
/// </summary>
public partial class NewTaskInputControl : UserControl
{
    private readonly List<ImageAttachment> _attachments = new();

    private static readonly FilePickerFileType ImageFileTypes = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" },
        MimeTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp" }
    };

    /// <summary>
    /// Raised when the user requests to create a task.
    /// The event args contain the TaskInput with text and images.
    /// </summary>
    public event EventHandler<TaskInput>? TaskCreationRequested;

    /// <summary>
    /// Gets or sets whether the control is currently in a creating state.
    /// </summary>
    public bool IsCreating
    {
        get => CreatingIndicator.IsVisible;
        set
        {
            CreatingIndicator.IsVisible = value;
            CreateButton.IsEnabled = !value;
            TaskDescriptionBox.IsEnabled = !value;
            AttachButton.IsEnabled = !value;
        }
    }

    public NewTaskInputControl()
    {
        InitializeComponent();

        // Subscribe to paste event on the task description box
        TaskDescriptionBox.PastingFromClipboard += TaskDescriptionBox_PastingFromClipboard;
    }

    /// <summary>
    /// Shows an error message in the control.
    /// </summary>
    public void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    /// <summary>
    /// Clears any error message.
    /// </summary>
    public void ClearError()
    {
        ErrorText.IsVisible = false;
    }

    /// <summary>
    /// Clears the input and resets the control.
    /// </summary>
    public void Clear()
    {
        TaskDescriptionBox.Text = string.Empty;
        _attachments.Clear();
        UpdateAttachmentsDisplay();
        ClearError();
        IsCreating = false;
    }

    private async void TaskDescriptionBox_PastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        var pastedImage = await TryPasteImageFromClipboard();
        if (pastedImage)
        {
            e.Handled = true;
        }
    }

    private async Task<bool> TryPasteImageFromClipboard()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return false;

            var formats = await clipboard.GetFormatsAsync();

            string[] imageFormats = {
                "image/png", "PNG", "public.png",
                "image/jpeg", "JPEG", "public.jpeg",
                "image/bmp", "BMP", "public.bmp",
                "image/gif", "GIF", "public.gif",
                "image/tiff", "TIFF", "public.tiff"
            };

            foreach (var format in imageFormats)
            {
                if (!formats.Contains(format)) continue;

                var data = await clipboard.GetDataAsync(format);

                if (data is byte[] imageBytes && imageBytes.Length > 0)
                {
                    var mediaType = GetMediaTypeForFormat(format);
                    AddImageAttachment(imageBytes, mediaType, $"pasted-image.{GetExtensionForFormat(format)}");
                    return true;
                }

                if (data is Stream stream)
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    if (bytes.Length > 0)
                    {
                        var mediaType = GetMediaTypeForFormat(format);
                        AddImageAttachment(bytes, mediaType, $"pasted-image.{GetExtensionForFormat(format)}");
                        return true;
                    }
                }
            }

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
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string GetMediaTypeForFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "image/png" or "png" or "public.png" => "image/png",
            "image/jpeg" or "jpeg" or "jpg" or "public.jpeg" => "image/jpeg",
            "image/gif" or "gif" or "public.gif" => "image/gif",
            "image/bmp" or "bmp" or "public.bmp" => "image/bmp",
            "image/tiff" or "tiff" or "public.tiff" => "image/tiff",
            "image/webp" or "webp" or "public.webp" => "image/webp",
            _ when format.StartsWith("image/") => format,
            _ => "image/png"
        };
    }

    private static string GetExtensionForFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "image/png" or "png" or "public.png" => "png",
            "image/jpeg" or "jpeg" or "jpg" or "public.jpeg" => "jpg",
            "image/gif" or "gif" or "public.gif" => "gif",
            "image/bmp" or "bmp" or "public.bmp" => "bmp",
            "image/tiff" or "tiff" or "public.tiff" => "tiff",
            "image/webp" or "webp" or "public.webp" => "webp",
            _ => "png"
        };
    }

    private async void AttachButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
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

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        var description = TaskDescriptionBox.Text?.Trim();

        if (string.IsNullOrEmpty(description))
        {
            ShowError("Please enter a task description.");
            return;
        }

        ClearError();

        // Create TaskInput with text and images (title/branch will be generated by caller)
        var taskInput = TaskInput.Create(description, _attachments.ToList());
        TaskCreationRequested?.Invoke(this, taskInput);
    }
}
