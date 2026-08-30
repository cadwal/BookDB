using System;
using SkiaSharp;

namespace BookDB.Companion.Host;

/// <summary>
/// Shrinks stored images for the phone. The library keeps only full-size covers — nothing generates
/// thumbnails — so a browse page would otherwise ship megabytes per screen over the LAN.
/// </summary>
public static class ImageDownscaler
{
    /// <summary>Big enough for a browse row on a dense screen, small enough that a page of them is cheap.</summary>
    public const int ThumbnailLongEdgePx = 240;

    /// <summary>
    /// Returns a JPEG no larger than <paramref name="maxLongEdgePx"/> on its long edge. The original is
    /// handed back untouched when it is already small enough, when no limit is asked for, or when it
    /// cannot be decoded — an image the phone can try to render beats no image at all.
    /// </summary>
    public static byte[] Downscale(byte[] image, int maxLongEdgePx, int quality = 80)
    {
        if (image.Length == 0 || maxLongEdgePx <= 0)
        {
            return image;
        }

        // Asking the codec first: SKBitmap.Decode(byte[]) throws rather than returning null when the bytes
        // are not an image at all, and a stored blob that Skia cannot read must not fail the call.
        using var codec = SKCodec.Create(new SKMemoryStream(image));
        if (codec is null)
        {
            return image;
        }

        using var source = SKBitmap.Decode(codec);
        if (source is null)
        {
            return image;
        }

        int longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maxLongEdgePx)
        {
            return image;
        }

        double scale = (double)maxLongEdgePx / longEdge;
        var size = new SKSizeI(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));

        using var resized = source.Resize(size, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized is null)
        {
            return image;
        }

        using var encoded = resized.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        return encoded?.ToArray() ?? image;
    }
}
