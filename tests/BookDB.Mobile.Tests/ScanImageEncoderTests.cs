using BookDB.Contracts;
using BookDB.Mobile.Services;
using SkiaSharp;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>Sizing a captured cover to what the computer asked for. The platform scanner hands back
/// full-resolution pages, so this is the only place the handshake's capture settings are applied.</summary>
public class ScanImageEncoderTests
{
    /// <summary>A real JPEG of a known size — the encoder decodes what it is given, so a stand-in will not do.</summary>
    internal static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        canvas.DrawRect(0, 0, width / 2f, height / 2f, new SKPaint { Color = SKColors.Goldenrod });

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return encoded.ToArray();
    }

    private static (int Width, int Height) SizeOf(byte[] jpeg)
    {
        using var bitmap = SKBitmap.Decode(jpeg);
        return (bitmap.Width, bitmap.Height);
    }

    [Fact]
    public void ALargeCrop_IsShrunkToTheLongEdgeTheComputerAskedFor()
    {
        var encoded = ScanImageEncoder.Encode(
            Jpeg(3000, 2000),
            new CaptureSettings { MaxLongEdgePx = 900, JpegQuality = 80 });

        Assert.Equal((900, 600), SizeOf(encoded));
    }

    [Fact]
    public void ThePortraitSide_IsMeasuredToo()
    {
        var encoded = ScanImageEncoder.Encode(
            Jpeg(1000, 2000),
            new CaptureSettings { MaxLongEdgePx = 500, JpegQuality = 80 });

        Assert.Equal((250, 500), SizeOf(encoded));
    }

    [Fact]
    public void ACropThatIsAlreadySmallEnough_KeepsItsSizeButTakesTheQuality()
    {
        var page = Jpeg(400, 300);

        var encoded = ScanImageEncoder.Encode(page, new CaptureSettings { MaxLongEdgePx = 1600, JpegQuality = 20 });

        Assert.Equal((400, 300), SizeOf(encoded));
        Assert.True(encoded.Length < page.Length, "the quality setting should have been applied");
    }

    [Fact]
    public void UnsetSettings_FallBackToTheAppsOwnClamps()
    {
        var unset = ScanImageEncoder.Encode(Jpeg(4000, 3000), new CaptureSettings());
        var absent = ScanImageEncoder.Encode(Jpeg(4000, 3000), settings: null);

        Assert.Equal((ScanImageEncoder.DefaultMaxLongEdgePx, 1200), SizeOf(unset));
        Assert.Equal((ScanImageEncoder.DefaultMaxLongEdgePx, 1200), SizeOf(absent));
    }

    [Fact]
    public void BytesThatAreNotAnImage_ComeBackUntouched()
    {
        byte[] nonsense = [1, 2, 3, 4, 5];

        Assert.Same(nonsense, ScanImageEncoder.Encode(nonsense, new CaptureSettings { MaxLongEdgePx = 800 }));
    }

    [Fact]
    public void NothingCaptured_StaysNothing()
    {
        Assert.Empty(ScanImageEncoder.Encode([], new CaptureSettings { MaxLongEdgePx = 800 }));
    }
}
