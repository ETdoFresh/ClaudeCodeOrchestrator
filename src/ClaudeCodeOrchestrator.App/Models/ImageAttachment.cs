using Avalonia.Media.Imaging;
using ClaudeCodeOrchestrator.Core.Services;

namespace ClaudeCodeOrchestrator.App.Models;

/// <summary>
/// Represents an image attachment that can be included with text input.
/// </summary>
public sealed class ImageAttachment : IImageData
{
    /// <summary>
    /// Unique identifier for this attachment.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Original file name, if available.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// MIME type of the image (e.g., "image/png", "image/jpeg").
    /// </summary>
    public required string MediaType { get; init; }

    /// <summary>
    /// Base64-encoded image data.
    /// </summary>
    public required string Base64Data { get; init; }

    /// <summary>
    /// Raw image bytes.
    /// </summary>
    public required byte[] ImageBytes { get; init; }

    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Gets the data URI for displaying in HTML or as a source.
    /// </summary>
    public string DataUri => $"data:{MediaType};base64,{Base64Data}";

    private const int MaxImageDimension = 1024;

    /// <summary>
    /// Creates an ImageAttachment from raw bytes, resizing if larger than 1024x1024.
    /// </summary>
    public static ImageAttachment FromBytes(byte[] bytes, string mediaType, string? fileName = null)
    {
        var (processedBytes, width, height) = ProcessImage(bytes, mediaType);

        return new ImageAttachment
        {
            FileName = fileName,
            MediaType = mediaType,
            Base64Data = Convert.ToBase64String(processedBytes),
            ImageBytes = processedBytes,
            Width = width,
            Height = height
        };
    }

    /// <summary>
    /// Processes an image, resizing it if either dimension exceeds 1024 pixels.
    /// Maintains aspect ratio when resizing.
    /// </summary>
    private static (byte[] bytes, int width, int height) ProcessImage(byte[] originalBytes, string mediaType)
    {
        using var stream = new MemoryStream(originalBytes);
        var bitmap = new Bitmap(stream);

        var originalWidth = bitmap.PixelSize.Width;
        var originalHeight = bitmap.PixelSize.Height;

        // Check if resizing is needed
        if (originalWidth <= MaxImageDimension && originalHeight <= MaxImageDimension)
        {
            return (originalBytes, originalWidth, originalHeight);
        }

        // Calculate new dimensions maintaining aspect ratio
        var scale = Math.Min(
            (double)MaxImageDimension / originalWidth,
            (double)MaxImageDimension / originalHeight);

        var newWidth = (int)(originalWidth * scale);
        var newHeight = (int)(originalHeight * scale);

        // Resize the image
        var resizedBitmap = bitmap.CreateScaledBitmap(new Avalonia.PixelSize(newWidth, newHeight));

        // Convert back to bytes
        using var outputStream = new MemoryStream();
        resizedBitmap.Save(outputStream);

        return (outputStream.ToArray(), newWidth, newHeight);
    }

    /// <summary>
    /// Creates an ImageAttachment from a base64 string, resizing if larger than 1024x1024.
    /// </summary>
    public static ImageAttachment FromBase64(string base64, string mediaType, string? fileName = null)
    {
        var bytes = Convert.FromBase64String(base64);
        return FromBytes(bytes, mediaType, fileName);
    }
}
