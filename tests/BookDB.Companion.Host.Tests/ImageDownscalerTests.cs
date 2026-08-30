using BookDB.Companion.Host;
using SkiaSharp;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// Downscaling covers for the phone. The library stores full-size images only, so this is what stands
/// between a browse page and megabytes over the LAN.
/// </summary>
public sealed class ImageDownscalerTests
{
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Firebrick);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    private static (int Width, int Height) SizeOf(byte[] jpeg)
    {
        using var decoded = SKBitmap.Decode(jpeg);
        return (decoded.Width, decoded.Height);
    }

    [Fact]
    public void ATallCoverIsBoundedByItsHeight()
    {
        var size = SizeOf(ImageDownscaler.Downscale(Jpeg(600, 900), 300));

        Assert.Equal(300, size.Height);
        Assert.Equal(200, size.Width);
    }

    [Fact]
    public void AWideImageIsBoundedByItsWidth()
    {
        var size = SizeOf(ImageDownscaler.Downscale(Jpeg(900, 600), 300));

        Assert.Equal(300, size.Width);
        Assert.Equal(200, size.Height);
    }

    [Fact]
    public void ShrinkingActuallySavesBytes()
    {
        byte[] full = Jpeg(1600, 2400);

        Assert.True(ImageDownscaler.Downscale(full, 240).Length < full.Length / 4);
    }

    [Fact]
    public void AnImageAlreadySmallEnoughIsHandedBackAsItIs()
    {
        byte[] small = Jpeg(200, 240);

        Assert.Same(small, ImageDownscaler.Downscale(small, 240));
    }

    [Fact]
    public void AskingForNoLimitLeavesTheImageAlone()
    {
        byte[] full = Jpeg(1000, 1000);

        Assert.Same(full, ImageDownscaler.Downscale(full, 0));
    }

    [Fact]
    public void SomethingThatIsNotAnImageIsPassedThroughRatherThanThrowing()
    {
        byte[] notAnImage = [1, 2, 3, 4, 5];

        Assert.Same(notAnImage, ImageDownscaler.Downscale(notAnImage, 240));
    }

    [Fact]
    public void AVeryWideImageStillHasAtLeastOnePixelOfHeight()
    {
        var size = SizeOf(ImageDownscaler.Downscale(Jpeg(2000, 4), 240));

        Assert.Equal(240, size.Width);
        Assert.True(size.Height >= 1);
    }
}
