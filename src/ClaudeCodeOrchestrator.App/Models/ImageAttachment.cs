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

    /// <summary>
    /// Creates an ImageAttachment from raw bytes.
    /// </summary>
    public static ImageAttachment FromBytes(byte[] bytes, string mediaType, string? fileName = null)
    {
        return new ImageAttachment
        {
            FileName = fileName,
            MediaType = mediaType,
            Base64Data = Convert.ToBase64String(bytes),
            ImageBytes = bytes
        };
    }

    /// <summary>
    /// Creates an ImageAttachment from a base64 string.
    /// </summary>
    public static ImageAttachment FromBase64(string base64, string mediaType, string? fileName = null)
    {
        var bytes = Convert.FromBase64String(base64);
        return new ImageAttachment
        {
            FileName = fileName,
            MediaType = mediaType,
            Base64Data = base64,
            ImageBytes = bytes
        };
    }
}
