using System;
using Net.Codecrete.QrCodeGenerator;
using SkiaSharp;

namespace BookDB.Companion.Host;

/// <summary>
/// Draws a pairing payload as a QR code the camera on a phone or tablet can read.
/// </summary>
public static class PairingQrRenderer
{
    /// <summary>Four modules of quiet zone is the QR specification's minimum for reliable detection.</summary>
    private const int QuietZoneModules = 4;

    /// <summary>
    /// Renders to a PNG about <paramref name="targetPixels"/> across. Always black on white regardless of
    /// the app's theme — a scanner needs the contrast, and an inverted QR is not reliably readable.
    /// </summary>
    public static byte[] ToPng(string payload, int targetPixels = 320)
    {
        // Low error correction keeps the module count down, which matters: the payload is ~1800 bytes and
        // a denser grid is a harder scan. Nothing here is damage-prone — it is on screen for two minutes.
        var qr = QrCode.EncodeText(payload, QrCode.Ecc.Low);

        int modules = qr.Size + (QuietZoneModules * 2);
        int scale = Math.Max(1, targetPixels / modules);
        int side = modules * scale;

        using var surface = SKSurface.Create(new SKImageInfo(side, side));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
        for (int y = 0; y < qr.Size; y++)
        {
            for (int x = 0; x < qr.Size; x++)
            {
                if (qr.GetModule(x, y))
                {
                    canvas.DrawRect(
                        (x + QuietZoneModules) * scale,
                        (y + QuietZoneModules) * scale,
                        scale,
                        scale,
                        paint);
                }
            }
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
