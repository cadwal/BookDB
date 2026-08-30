using System;
using System.Linq;
using BookDB.Companion.Host;
using BookDB.Contracts;
using SkiaSharp;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// Rendering the pairing code. What matters is that a real payload encodes at all, and that the result is
/// a square, high-contrast, quiet-zoned image a camera can actually read.
/// </summary>
public sealed class PairingQrRendererTests
{
    private static string RealPayload()
    {
        using var certificate = CertificateFactory.CreateDeviceCertificate(Guid.NewGuid().ToString("N"));
        return new PairingPayload
        {
            Endpoint = "192.168.1.20:7443",
            ServerThumbprints = [CertificateFactory.Sha256Thumbprint(certificate)],
            ClientPfxBase64 = Convert.ToBase64String(CertificateFactory.ExportPfx(certificate)),
            IssuedAtUtc = DateTimeOffset.UtcNow,
        }.ToQrString();
    }

    [Fact]
    public void AFullPairingPayloadEncodesToASquareImage()
    {
        using var bitmap = SKBitmap.Decode(PairingQrRenderer.ToPng(RealPayload()));

        Assert.NotNull(bitmap);
        Assert.Equal(bitmap!.Width, bitmap.Height);
        Assert.True(bitmap.Width >= 200, "the code must be big enough to scan on screen");
    }

    [Fact]
    public void TheCodeIsBlackOnWhiteWhateverTheAppTheme()
    {
        using var bitmap = SKBitmap.Decode(PairingQrRenderer.ToPng(RealPayload()));

        // The corner is inside the quiet zone, which is white by definition.
        Assert.Equal(SKColors.White, bitmap!.GetPixel(0, 0));

        var colours = new[] { bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2) }
            .Concat(Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x, bitmap.Height / 2)))
            .Distinct()
            .ToList();
        Assert.All(colours, c => Assert.True(c == SKColors.Black || c == SKColors.White));
        Assert.Contains(SKColors.Black, colours);
    }

    [Fact]
    public void AskingForABiggerImageGivesABiggerImage()
    {
        string payload = RealPayload();

        using var small = SKBitmap.Decode(PairingQrRenderer.ToPng(payload, targetPixels: 200));
        using var large = SKBitmap.Decode(PairingQrRenderer.ToPng(payload, targetPixels: 600));

        Assert.True(large!.Width > small!.Width);
    }
}
