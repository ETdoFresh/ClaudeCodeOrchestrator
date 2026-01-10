using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ClaudeCodeOrchestrator.App.Helpers;
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

    /// <summary>
    /// Focuses the task description input box.
    /// </summary>
    public void FocusInput()
    {
        TaskDescriptionBox.Focus();
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
            Debug.WriteLine($"[ImagePaste] Available clipboard formats: {string.Join(", ", formats)}");

            // First pass: collect all available formats and their data
            // This is needed because some formats report as available but return no data
            foreach (var format in ClipboardImageHelper.ImageFormats)
            {
                // Use case-insensitive matching since Windows clipboard format names can vary in case
                // Also get the actual format name from clipboard to use for GetDataAsync
                var actualFormat = formats.FirstOrDefault(f => f.Equals(format, StringComparison.OrdinalIgnoreCase));
                if (actualFormat == null) continue;

                var data = await clipboard.GetDataAsync(actualFormat);
                Debug.WriteLine($"[ImagePaste] Got data for format {actualFormat}: {data?.GetType().Name ?? "null"}, Length={(data as byte[])?.Length ?? -1}");

                byte[]? imageBytes = null;

                if (data is byte[] bytes && bytes.Length > 0)
                {
                    imageBytes = bytes;
                }
                else if (data is Stream stream)
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    if (ms.Length > 0)
                    {
                        imageBytes = ms.ToArray();
                    }
                }

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    Debug.WriteLine($"[ImagePaste] Format {actualFormat} reported available but returned no data, trying next format");
                    continue;
                }

                // Process DIB formats with special conversion (PNG first, then BMP fallback)
                var (processedBytes, mediaType, extension) = ClipboardImageHelper.ProcessDibImage(imageBytes, actualFormat);
                if (processedBytes.Length > 0)
                {
                    AddImageAttachment(processedBytes, mediaType, $"pasted-image.{extension}");
                    Debug.WriteLine($"[ImagePaste] Successfully added image from format {actualFormat}");
                    return true;
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

            Debug.WriteLine($"[ImagePaste] No image found in clipboard");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImagePaste] Error: {ex}");
            return false;
        }
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

    private void TaskDescriptionBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Ctrl+Enter (or Cmd+Enter on macOS) submits from anywhere in the text
        if (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta)
        {
            e.Handled = true;
            Create_Click(sender, e);
            return;
        }

        // Plain Enter only submits when cursor is at the end of text
        // (Shift+Enter should still add newlines)
        if (e.KeyModifiers != KeyModifiers.None)
            return;

        if (sender is not TextBox textBox)
            return;

        var text = textBox.Text ?? string.Empty;
        var caretIndex = textBox.CaretIndex;

        if (caretIndex != text.Length)
            return;

        e.Handled = true;
        Create_Click(sender, e);
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
