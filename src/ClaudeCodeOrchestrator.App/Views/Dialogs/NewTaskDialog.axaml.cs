using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ClaudeCodeOrchestrator.App.Models;
using ClaudeCodeOrchestrator.App.Views.Controls;
using ClaudeCodeOrchestrator.Core.Services;

namespace ClaudeCodeOrchestrator.App.Views.Dialogs;

public partial class NewTaskDialog : Window
{
    private readonly List<ImageAttachment> _attachments = new();
    private readonly ITitleGeneratorService? _titleGeneratorService;

    private static readonly FilePickerFileType ImageFileTypes = new("Images")
    {
        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" },
        MimeTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/bmp", "image/webp" }
    };

    public NewTaskDialog() : this(null)
    {
    }

    public NewTaskDialog(ITitleGeneratorService? titleGeneratorService)
    {
        _titleGeneratorService = titleGeneratorService;
        InitializeComponent();
        Opened += OnOpened;

        // Subscribe to paste event on the task description box - this is the proper way to intercept paste in Avalonia
        TaskDescriptionBox.PastingFromClipboard += TaskDescriptionBox_PastingFromClipboard;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        TaskDescriptionBox.Focus();
    }

    private async void TaskDescriptionBox_PastingFromClipboard(object? sender, RoutedEventArgs e)
    {
        // This event fires when content is being pasted - check for images first
        var pastedImage = await TryPasteImageFromClipboard();
        if (pastedImage)
        {
            e.Handled = true; // Prevent default text paste if we handled an image
        }
    }

    /// <summary>
    /// Attempts to paste an image from the clipboard.
    /// </summary>
    /// <returns>True if an image was pasted, false otherwise (allowing text paste to proceed).</returns>
    private async Task<bool> TryPasteImageFromClipboard()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return false;

            var formats = await clipboard.GetFormatsAsync();
            Debug.WriteLine($"[ImagePaste] Available clipboard formats: {string.Join(", ", formats)}");

            // Check for various image formats (different platforms use different names)
            // Include macOS UTI formats (public.png, public.jpeg, etc.)
            // Include Windows clipboard formats (DeviceIndependentBitmap, Bitmap)
            string[] imageFormats = {
                "image/png", "PNG", "public.png",
                "image/jpeg", "JPEG", "public.jpeg",
                "image/bmp", "BMP", "public.bmp", "DeviceIndependentBitmap", "Bitmap",
                "image/gif", "GIF", "public.gif",
                "image/tiff", "TIFF", "public.tiff"
            };

            foreach (var format in imageFormats)
            {
                if (!formats.Contains(format)) continue;

                var data = await clipboard.GetDataAsync(format);
                Debug.WriteLine($"[ImagePaste] Got data for format {format}: {data?.GetType().Name ?? "null"}");

                if (data is byte[] imageBytes && imageBytes.Length > 0)
                {
                    // For Windows DIB format, we need to add BITMAPFILEHEADER to make it a valid BMP
                    var processedBytes = IsDibFormat(format) ? ConvertDibToBmp(imageBytes) : imageBytes;
                    if (processedBytes.Length > 0)
                    {
                        var mediaType = GetMediaTypeForFormat(format);
                        AddImageAttachment(processedBytes, mediaType, $"pasted-image.{GetExtensionForFormat(format)}");
                        return true;
                    }
                }

                // Some platforms return a stream instead of bytes
                if (data is Stream stream)
                {
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    if (bytes.Length > 0)
                    {
                        var processedBytes = IsDibFormat(format) ? ConvertDibToBmp(bytes) : bytes;
                        if (processedBytes.Length > 0)
                        {
                            var mediaType = GetMediaTypeForFormat(format);
                            AddImageAttachment(processedBytes, mediaType, $"pasted-image.{GetExtensionForFormat(format)}");
                            return true;
                        }
                    }
                }
            }

            // Try to get image files from clipboard (for copied image files)
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

            // No image found in clipboard - let TextBox handle text paste
            Debug.WriteLine($"[ImagePaste] No image found in clipboard");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImagePaste] Error: {ex}");
            // Clipboard access failed - let TextBox try its default paste
            return false;
        }
    }

    /// <summary>
    /// Checks if the clipboard format is a Windows DIB format that needs conversion.
    /// </summary>
    private static bool IsDibFormat(string format)
    {
        return format.Equals("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("Bitmap", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts Windows DIB (Device Independent Bitmap) data to a proper BMP file by adding BITMAPFILEHEADER.
    /// DIB data from the clipboard is missing the 14-byte file header that BMP files require.
    /// </summary>
    private static byte[] ConvertDibToBmp(byte[] dibData)
    {
        try
        {
            if (dibData.Length < 40) // Minimum BITMAPINFOHEADER size
            {
                Debug.WriteLine($"[ImagePaste] DIB data too small: {dibData.Length} bytes");
                return dibData;
            }

            // Read BITMAPINFOHEADER to calculate offsets
            // biSize is at offset 0 (4 bytes) - size of the header
            var biSize = BitConverter.ToInt32(dibData, 0);
            // biBitCount is at offset 14 (2 bytes) - bits per pixel
            var biBitCount = BitConverter.ToInt16(dibData, 14);
            // biClrUsed is at offset 32 (4 bytes) - number of colors in palette (0 means default)
            var biClrUsed = BitConverter.ToInt32(dibData, 32);

            Debug.WriteLine($"[ImagePaste] DIB header: biSize={biSize}, biBitCount={biBitCount}, biClrUsed={biClrUsed}");

            // Calculate color table size
            // For <= 8 bit images, there's a color table
            var colorTableSize = 0;
            if (biBitCount <= 8)
            {
                colorTableSize = (biClrUsed == 0 ? (1 << biBitCount) : biClrUsed) * 4;
            }

            // BITMAPFILEHEADER is 14 bytes
            const int fileHeaderSize = 14;
            var pixelDataOffset = fileHeaderSize + biSize + colorTableSize;
            var totalSize = fileHeaderSize + dibData.Length;

            Debug.WriteLine($"[ImagePaste] Creating BMP: fileHeaderSize={fileHeaderSize}, pixelDataOffset={pixelDataOffset}, totalSize={totalSize}");

            // Create the complete BMP file
            var bmpData = new byte[totalSize];

            // Write BITMAPFILEHEADER (14 bytes)
            // bfType: "BM" signature (2 bytes)
            bmpData[0] = 0x42; // 'B'
            bmpData[1] = 0x4D; // 'M'
            // bfSize: total file size (4 bytes)
            BitConverter.GetBytes(totalSize).CopyTo(bmpData, 2);
            // bfReserved1: 0 (2 bytes) - already zero
            // bfReserved2: 0 (2 bytes) - already zero
            // bfOffBits: offset to pixel data (4 bytes)
            BitConverter.GetBytes(pixelDataOffset).CopyTo(bmpData, 10);

            // Copy DIB data after the file header
            Array.Copy(dibData, 0, bmpData, fileHeaderSize, dibData.Length);

            return bmpData;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImagePaste] DIB conversion failed: {ex.Message}");
            return dibData; // Return original if conversion fails
        }
    }

    private static string GetMediaTypeForFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "image/png" or "png" or "public.png" => "image/png",
            "image/jpeg" or "jpeg" or "jpg" or "public.jpeg" => "image/jpeg",
            "image/gif" or "gif" or "public.gif" => "image/gif",
            "image/bmp" or "bmp" or "public.bmp" or "deviceindependentbitmap" or "bitmap" => "image/bmp",
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
            "image/bmp" or "bmp" or "public.bmp" or "deviceindependentbitmap" or "bitmap" => "bmp",
            "image/tiff" or "tiff" or "public.tiff" => "tiff",
            "image/webp" or "webp" or "public.webp" => "webp",
            _ => "png"
        };
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

    private void TaskDescriptionBox_KeyDown(object? sender, KeyEventArgs e)
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

        // Trigger the Create button click
        e.Handled = true;
        Create_Click(sender, e);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        var description = TaskDescriptionBox.Text?.Trim();

        if (string.IsNullOrEmpty(description))
        {
            ErrorText.Text = "Please enter a task description.";
            ErrorText.IsVisible = true;
            return;
        }

        ErrorText.IsVisible = false;
        string? title = null;
        string? branch = null;

        if (_titleGeneratorService != null)
        {
            // Show creating indicator and disable button
            CreatingIndicator.IsVisible = true;
            CreateButton.IsEnabled = false;

            try
            {
                var generated = await _titleGeneratorService.GenerateTitleAsync(description);
                title = generated.Title;
                branch = generated.BranchName;
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Failed to generate title: {ex.Message}";
                ErrorText.IsVisible = true;
                CreatingIndicator.IsVisible = false;
                CreateButton.IsEnabled = true;
                return;
            }
        }

        var result = TaskInput.Create(description, _attachments.ToList(), title, branch);
        Close(result);
    }
}
