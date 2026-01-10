using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;

namespace ClaudeCodeOrchestrator.App.Helpers;

/// <summary>
/// Helper class for handling clipboard image operations, including DIB format conversion.
/// </summary>
public static class ClipboardImageHelper
{
    /// <summary>
    /// Image formats to check in the clipboard, in priority order.
    /// Standard formats are checked first, Windows DIB formats last as they need conversion.
    /// </summary>
    public static readonly string[] ImageFormats =
    {
        "image/png", "PNG", "public.png",
        "image/jpeg", "JPEG", "public.jpeg",
        "image/gif", "GIF", "public.gif",
        "image/tiff", "TIFF", "public.tiff",
        "image/bmp", "BMP", "public.bmp",
        // Windows DIB formats - try these last as they need conversion
        "DeviceIndependentBitmap", "Bitmap", "CF_DIB", "CF_DIBV5", "DIB"
    };

    /// <summary>
    /// Checks if the clipboard format is a Windows DIB format that needs conversion.
    /// </summary>
    public static bool IsDibFormat(string format)
    {
        return format.Equals("DeviceIndependentBitmap", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("Bitmap", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("CF_DIB", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("CF_DIBV5", StringComparison.OrdinalIgnoreCase) ||
               format.Equals("DIB", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Processes DIB image data and returns the converted bytes along with the appropriate media type and extension.
    /// For DIB formats, attempts PNG conversion first (most reliable), then falls back to BMP.
    /// </summary>
    public static (byte[] bytes, string mediaType, string extension) ProcessDibImage(byte[] imageBytes, string format)
    {
        if (!IsDibFormat(format))
        {
            return (imageBytes, GetMediaTypeForFormat(format), GetExtensionForFormat(format));
        }

        // Try direct PNG conversion first - handles more DIB variants reliably
        var pngBytes = ConvertDibToPng(imageBytes);
        if (pngBytes != null && pngBytes.Length > 0)
        {
            Debug.WriteLine($"[ImagePaste] DIB converted to PNG successfully");
            return (pngBytes, "image/png", "png");
        }

        // Fall back to BMP conversion
        var bmpBytes = ConvertDibToBmp(imageBytes);
        Debug.WriteLine($"[ImagePaste] DIB converted to BMP (PNG conversion failed)");
        return (bmpBytes, "image/bmp", "bmp");
    }

    /// <summary>
    /// Converts Windows DIB (Device Independent Bitmap) data to a proper BMP file by adding BITMAPFILEHEADER.
    /// DIB data from the clipboard is missing the 14-byte file header that BMP files require.
    /// Handles BITMAPINFOHEADER (40 bytes), BITMAPV4HEADER (108 bytes), and BITMAPV5HEADER (124 bytes).
    /// </summary>
    public static byte[] ConvertDibToBmp(byte[] dibData)
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
    public static byte[]? ConvertDibToPng(byte[] dibData)
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
                System.Runtime.InteropServices.Marshal.Copy(pixelBuffer, 0, frameBuffer.Address, pixelBuffer.Length);
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

    /// <summary>
    /// Gets the MIME type for a clipboard format.
    /// </summary>
    public static string GetMediaTypeForFormat(string format)
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

    /// <summary>
    /// Gets the file extension for a clipboard format.
    /// </summary>
    public static string GetExtensionForFormat(string format)
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
}
