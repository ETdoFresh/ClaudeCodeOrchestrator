using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
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
            Debug.WriteLine($"[ImagePaste] Available clipboard formats: {string.Join(", ", formats)}");

            // Check for various image formats (different platforms use different names)
            // Include macOS UTI formats (public.png, public.jpeg, etc.)
            // Include Windows clipboard formats (DeviceIndependentBitmap, Bitmap, CF_DIB, CF_DIBV5)
            // IMPORTANT: Order matters - prefer standard formats first, then Windows DIB formats last
            // because some apps report multiple formats but only DIB has actual data
            string[] imageFormats = {
                "image/png", "PNG", "public.png",
                "image/jpeg", "JPEG", "public.jpeg",
                "image/gif", "GIF", "public.gif",
                "image/tiff", "TIFF", "public.tiff",
                "image/bmp", "BMP", "public.bmp",
                // Windows DIB formats - try these last as they need conversion
                "DeviceIndependentBitmap", "Bitmap", "CF_DIB", "CF_DIBV5", "DIB"
            };

            // First pass: collect all available formats and their data
            // This is needed because some formats report as available but return no data
            foreach (var format in imageFormats)
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

                // For Windows DIB format, try converting to PNG first (most reliable), then fall back to BMP
                byte[] processedBytes;
                string mediaType;
                string extension;

                if (IsDibFormat(actualFormat))
                {
                    // Try direct PNG conversion first - handles more DIB variants reliably
                    var pngBytes = ConvertDibToPng(imageBytes);
                    if (pngBytes != null && pngBytes.Length > 0)
                    {
                        processedBytes = pngBytes;
                        mediaType = "image/png";
                        extension = "png";
                        Debug.WriteLine($"[ImagePaste] DIB converted to PNG successfully");
                    }
                    else
                    {
                        // Fall back to BMP conversion
                        processedBytes = ConvertDibToBmp(imageBytes);
                        mediaType = "image/bmp";
                        extension = "bmp";
                        Debug.WriteLine($"[ImagePaste] DIB converted to BMP (PNG conversion failed)");
                    }
                }
                else
                {
                    processedBytes = imageBytes;
                    mediaType = GetMediaTypeForFormat(actualFormat);
                    extension = GetExtensionForFormat(actualFormat);
                }

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

    /// <summary>
    /// Checks if the clipboard format is a Windows DIB format that needs conversion.
    /// </summary>
    private static bool IsDibFormat(string format)
    {
        return format.Equals("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("Bitmap", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("CF_DIB", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("CF_DIBV5", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("DIB", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts Windows DIB (Device Independent Bitmap) data to a proper BMP file by adding BITMAPFILEHEADER.
    /// DIB data from the clipboard is missing the 14-byte file header that BMP files require.
    /// Handles BITMAPINFOHEADER (40 bytes), BITMAPV4HEADER (108 bytes), and BITMAPV5HEADER (124 bytes).
    /// </summary>
    private static byte[] ConvertDibToBmp(byte[] dibData)
    {
        try
        {
            if (dibData.Length < 12) // Minimum to read biSize
            {
                Debug.WriteLine($"[ImagePaste] DIB data too small: {dibData.Length} bytes");
                return dibData;
            }

            // Read the header size to determine which header type we have
            // biSize is at offset 0 (4 bytes) - size of the header
            var biSize = BitConverter.ToInt32(dibData, 0);

            Debug.WriteLine($"[ImagePaste] DIB header size: {biSize}");

            // Validate header size - must be at least BITMAPINFOHEADER (40) or could be
            // BITMAPV4HEADER (108) or BITMAPV5HEADER (124)
            if (biSize < 40 || biSize > 256 || dibData.Length < biSize)
            {
                Debug.WriteLine($"[ImagePaste] Invalid DIB header size: {biSize}, data length: {dibData.Length}");
                return dibData;
            }

            // Read common header fields (present in all header types)
            // biWidth is at offset 4 (4 bytes)
            var biWidth = BitConverter.ToInt32(dibData, 4);
            // biHeight is at offset 8 (4 bytes) - can be negative for top-down bitmaps
            var biHeight = BitConverter.ToInt32(dibData, 8);
            // biPlanes is at offset 12 (2 bytes)
            var biPlanes = BitConverter.ToInt16(dibData, 12);
            // biBitCount is at offset 14 (2 bytes) - bits per pixel
            var biBitCount = BitConverter.ToInt16(dibData, 14);
            // biCompression is at offset 16 (4 bytes)
            var biCompression = BitConverter.ToInt32(dibData, 16);

            Debug.WriteLine($"[ImagePaste] DIB info: width={biWidth}, height={biHeight}, planes={biPlanes}, bpp={biBitCount}, compression={biCompression}");

            // Check for BI_BITFIELDS compression (3) which uses color masks
            // The masks come right after the header
            var colorMasksSize = 0;
            if (biCompression == 3) // BI_BITFIELDS
            {
                colorMasksSize = 12; // 3 x 4-byte color masks (RGB)
                // BITMAPV4/V5 headers include the masks in the header itself
                if (biSize >= 52)
                {
                    colorMasksSize = 0; // Masks are part of the header
                }
            }
            else if (biCompression == 6) // BI_ALPHABITFIELDS (rare)
            {
                colorMasksSize = 16; // 4 x 4-byte color masks (RGBA)
                if (biSize >= 56)
                {
                    colorMasksSize = 0;
                }
            }

            // Calculate color table size
            // biClrUsed is at offset 32 (4 bytes) - number of colors in palette (0 means default)
            var biClrUsed = dibData.Length >= 36 ? BitConverter.ToInt32(dibData, 32) : 0;

            var colorTableSize = 0;
            // For <= 8 bit images without bitfields compression, there's a color table
            if (biBitCount <= 8 && biCompression != 3 && biCompression != 6)
            {
                colorTableSize = (biClrUsed == 0 ? (1 << biBitCount) : biClrUsed) * 4;
            }

            Debug.WriteLine($"[ImagePaste] colorMasksSize={colorMasksSize}, colorTableSize={colorTableSize}, biClrUsed={biClrUsed}");

            // BITMAPFILEHEADER is 14 bytes
            const int fileHeaderSize = 14;
            var pixelDataOffset = fileHeaderSize + biSize + colorMasksSize + colorTableSize;
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

    /// <summary>
    /// Converts DIB data directly to PNG format using WriteableBitmap.
    /// This handles cases where standard BMP decoders fail, especially for 32-bit BGRA DIBs.
    /// </summary>
    private static byte[]? ConvertDibToPng(byte[] dibData)
    {
        try
        {
            if (dibData.Length < 40)
            {
                Debug.WriteLine($"[ImagePaste] DIB data too small for PNG conversion: {dibData.Length} bytes");
                return null;
            }

            // Read header info
            var biSize = BitConverter.ToInt32(dibData, 0);
            var biWidth = BitConverter.ToInt32(dibData, 4);
            var biHeight = BitConverter.ToInt32(dibData, 8);
            var biBitCount = BitConverter.ToInt16(dibData, 14);
            var biCompression = BitConverter.ToInt32(dibData, 16);

            // Only handle 32-bit and 24-bit uncompressed or bitfields DIBs
            if (biBitCount != 32 && biBitCount != 24)
            {
                Debug.WriteLine($"[ImagePaste] Unsupported bit depth for direct PNG conversion: {biBitCount}");
                return null;
            }

            if (biCompression != 0 && biCompression != 3 && biCompression != 6)
            {
                Debug.WriteLine($"[ImagePaste] Unsupported compression for direct PNG conversion: {biCompression}");
                return null;
            }

            var isTopDown = biHeight < 0;
            var height = Math.Abs(biHeight);
            var width = biWidth;

            Debug.WriteLine($"[ImagePaste] Direct PNG conversion: {width}x{height}, {biBitCount}bpp, topDown={isTopDown}");

            // Calculate the pixel data offset
            var biClrUsed = dibData.Length >= 36 ? BitConverter.ToInt32(dibData, 32) : 0;
            var colorMasksSize = 0;
            if (biCompression == 3 && biSize < 52)
            {
                colorMasksSize = 12;
            }
            else if (biCompression == 6 && biSize < 56)
            {
                colorMasksSize = 16;
            }

            var colorTableSize = 0;
            if (biBitCount <= 8)
            {
                colorTableSize = (biClrUsed == 0 ? (1 << biBitCount) : biClrUsed) * 4;
            }

            var pixelDataOffset = biSize + colorMasksSize + colorTableSize;

            // Calculate row stride (rows are padded to 4-byte boundary)
            var bytesPerPixel = biBitCount / 8;
            var rowStride = ((width * bytesPerPixel) + 3) & ~3;

            Debug.WriteLine($"[ImagePaste] Pixel data offset: {pixelDataOffset}, row stride: {rowStride}");

            if (pixelDataOffset + (rowStride * height) > dibData.Length)
            {
                Debug.WriteLine($"[ImagePaste] DIB data truncated, expected {pixelDataOffset + (rowStride * height)} bytes, got {dibData.Length}");
                return null;
            }

            // Read color masks for BI_BITFIELDS
            uint redMask = 0x00FF0000, greenMask = 0x0000FF00, blueMask = 0x000000FF, alphaMask = 0xFF000000;
            if (biCompression == 3)
            {
                var maskOffset = biSize < 52 ? biSize : 40;
                redMask = BitConverter.ToUInt32(dibData, maskOffset);
                greenMask = BitConverter.ToUInt32(dibData, maskOffset + 4);
                blueMask = BitConverter.ToUInt32(dibData, maskOffset + 8);
                if (biCompression == 6 || (biSize >= 56 && dibData.Length >= maskOffset + 16))
                {
                    alphaMask = BitConverter.ToUInt32(dibData, maskOffset + 12);
                }
                Debug.WriteLine($"[ImagePaste] Color masks: R={redMask:X8}, G={greenMask:X8}, B={blueMask:X8}, A={alphaMask:X8}");
            }

            // Create BGRA pixel buffer first
            var destStride = width * 4;
            var pixelBuffer = new byte[destStride * height];

            // Track if all alpha values are 0 (indicates alpha channel is unused)
            var allAlphaZero = true;

            for (var y = 0; y < height; y++)
            {
                // DIB rows are stored bottom-up unless height is negative
                var srcY = isTopDown ? y : (height - 1 - y);
                var srcRowOffset = pixelDataOffset + (srcY * rowStride);
                var destRowOffset = y * destStride;

                for (var x = 0; x < width; x++)
                {
                    var srcPixelOffset = srcRowOffset + (x * bytesPerPixel);

                    byte r, g, b, a;

                    if (biBitCount == 32)
                    {
                        if (biCompression == 0)
                        {
                            // Standard BGRX or BGRA
                            b = dibData[srcPixelOffset];
                            g = dibData[srcPixelOffset + 1];
                            r = dibData[srcPixelOffset + 2];
                            a = dibData[srcPixelOffset + 3];

                            if (a != 0) allAlphaZero = false;
                        }
                        else
                        {
                            // BI_BITFIELDS - need to apply masks
                            var pixel = BitConverter.ToUInt32(dibData, srcPixelOffset);
                            r = ExtractColorComponent(pixel, redMask);
                            g = ExtractColorComponent(pixel, greenMask);
                            b = ExtractColorComponent(pixel, blueMask);
                            a = alphaMask != 0 ? ExtractColorComponent(pixel, alphaMask) : (byte)255;

                            if (a != 0 || alphaMask == 0) allAlphaZero = false;
                        }
                    }
                    else // 24-bit
                    {
                        b = dibData[srcPixelOffset];
                        g = dibData[srcPixelOffset + 1];
                        r = dibData[srcPixelOffset + 2];
                        a = 255;
                        allAlphaZero = false;
                    }

                    // Write to buffer as BGRA
                    var destPixelOffset = destRowOffset + (x * 4);
                    pixelBuffer[destPixelOffset] = b;
                    pixelBuffer[destPixelOffset + 1] = g;
                    pixelBuffer[destPixelOffset + 2] = r;
                    pixelBuffer[destPixelOffset + 3] = a;
                }
            }

            // If all alpha values were 0, set them all to 255 (common in screenshots)
            if (allAlphaZero && biBitCount == 32)
            {
                Debug.WriteLine($"[ImagePaste] All alpha values are 0, treating as opaque");
                for (var i = 3; i < pixelBuffer.Length; i += 4)
                {
                    pixelBuffer[i] = 255;
                }
            }

            // Create WriteableBitmap and copy pixel data
            var writeableBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Unpremul);

            using (var frameBuffer = writeableBitmap.Lock())
            {
                Marshal.Copy(pixelBuffer, 0, frameBuffer.Address, pixelBuffer.Length);
            }

            // Save as PNG
            using var outputStream = new MemoryStream();
            writeableBitmap.Save(outputStream);
            Debug.WriteLine($"[ImagePaste] Successfully converted DIB to PNG: {outputStream.Length} bytes");
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ImagePaste] DIB to PNG conversion failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts a color component from a pixel value using the given mask.
    /// </summary>
    private static byte ExtractColorComponent(uint pixel, uint mask)
    {
        if (mask == 0) return 0;

        var value = pixel & mask;

        // Find the position of the first set bit in the mask
        var shift = 0;
        var tempMask = mask;
        while ((tempMask & 1) == 0)
        {
            tempMask >>= 1;
            shift++;
        }

        value >>= shift;

        // Count bits in shifted mask to scale to 8 bits
        var bits = 0;
        while (tempMask != 0)
        {
            bits += (int)(tempMask & 1);
            tempMask >>= 1;
        }

        // Scale to 8 bits
        if (bits < 8)
        {
            value <<= (8 - bits);
        }
        else if (bits > 8)
        {
            value >>= (bits - 8);
        }

        return (byte)value;
    }

    private static string GetMediaTypeForFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "image/png" or "png" or "public.png" => "image/png",
            "image/jpeg" or "jpeg" or "jpg" or "public.jpeg" => "image/jpeg",
            "image/gif" or "gif" or "public.gif" => "image/gif",
            "image/bmp" or "bmp" or "public.bmp" or "deviceindependentbitmap" or "bitmap" or "cf_dib" or "cf_dibv5" or "dib" => "image/bmp",
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
            "image/bmp" or "bmp" or "public.bmp" or "deviceindependentbitmap" or "bitmap" or "cf_dib" or "cf_dibv5" or "dib" => "bmp",
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
