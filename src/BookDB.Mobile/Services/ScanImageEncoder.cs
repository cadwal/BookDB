using System;
using BookDB.Contracts;
using SkiaSharp;

namespace BookDB.Mobile.Services;

/// <summary>
/// Applies the computer's capture settings to a crop before it is staged. The platform scanner returns
/// full-resolution pages; sizing them here — once, on accept — is what keeps a batch of covers small enough
/// to cross a home LAN and small enough to sit in app-private storage while offline.
/// </summary>
public static class ScanImageEncoder
{
    /// <summary>Used when the handshake never happened or left the settings unset. Matches the desktop's own
    /// defaults, so a phone that has never connected stages the same sizes as one that has.</summary>
    public const int DefaultMaxLongEdgePx = 1600;

    public const int DefaultJpegQuality = 80;

    /// <summary>
    /// Returns a JPEG no larger than the settings' long edge, re-encoded at their quality. Re-encoding is
    /// unconditional: quality has to be applied even to a crop that was already small enough. Bytes that
    /// cannot be decoded are handed back untouched — an image the desktop can try to read beats no image.
    /// </summary>
    public static byte[] Encode(byte[] page, CaptureSettings? settings)
    {
        if (page.Length == 0)
        {
            return page;
        }

        int maxLongEdge = settings is { MaxLongEdgePx: > 0 } ? settings.MaxLongEdgePx : DefaultMaxLongEdgePx;
        int quality = settings is { JpegQuality: > 0 } ? settings.JpegQuality : DefaultJpegQuality;

        // Asking the codec first: SKBitmap.Decode(byte[]) throws rather than returning null when the bytes are
        // not an image at all.
        using var codec = SKCodec.Create(new SKMemoryStream(page));
        if (codec is null)
        {
            return page;
        }

        using var source = SKBitmap.Decode(codec);
        if (source is null)
        {
            return page;
        }

        using var sized = Resize(source, maxLongEdge);
        using var encoded = (sized ?? source).Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        return encoded?.ToArray() ?? page;
    }

    private static SKBitmap? Resize(SKBitmap source, int maxLongEdgePx)
    {
        int longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maxLongEdgePx)
        {
            return null;
        }

        double scale = (double)maxLongEdgePx / longEdge;
        var size = new SKSizeI(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));

        return source.Resize(size, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }
}
