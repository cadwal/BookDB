using System;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using SkiaSharp;

namespace BookDB.Mobile.UITests;

/// <summary>An open connection to a stub desktop. The view-model tests each keep a private one of these; the
/// smokes need it across several screens, so it lives here.</summary>
internal sealed class StubConnection : ICompanionConnection
{
    public StubConnection(IBookScannerService service) => Service = service;

    public IBookScannerService Service { get; }

    public void Dispose() { }
}

/// <summary>An upload that reports one book saved and stops — enough for the sending screen to render a row in
/// a settled state. What the upload service actually does is the view-model tests' business.</summary>
internal sealed class StubUploadService : IUploadService
{
    public Task<UploadResult> RunAsync(Action<BatchItemStatus> onStatus, CancellationToken ct = default)
    {
        onStatus(new BatchItemStatus { ClientItemId = "a1", State = BatchItemState.Saved });
        return Task.FromResult(UploadResult.Completed);
    }
}

internal static class TestJpeg
{
    /// <summary>A real one-pixel JPEG, so a smoke that renders a photo decodes bytes the platform accepts
    /// rather than falling into the converter's empty-slot path.</summary>
    public static byte[] OnePixel()
    {
        using var bitmap = new SKBitmap(1, 1);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90)
            ?? throw new InvalidOperationException("SkiaSharp declined to encode a 1×1 JPEG.");
        return encoded.ToArray();
    }
}
