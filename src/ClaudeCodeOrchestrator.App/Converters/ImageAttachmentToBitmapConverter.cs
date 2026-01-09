using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using ClaudeCodeOrchestrator.App.Models;

namespace ClaudeCodeOrchestrator.App.Converters;

/// <summary>
/// Converts an ImageAttachment to a Bitmap for display in an Image control.
/// </summary>
public class ImageAttachmentToBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ImageAttachment attachment)
            return null;

        try
        {
            using var stream = new MemoryStream(attachment.ImageBytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
