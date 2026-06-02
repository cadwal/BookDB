using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Serilog;

namespace BookDB.Desktop.Services;

/// <summary>
/// Downloads cover images from remote URLs and returns the raw bytes.
/// Implements ICoverFetcher so it can be injected into BatchQueueProcessor (Logic layer).
/// </summary>
public sealed class CoverFetchService : BookDB.Logic.Services.ICoverFetcher
{
    private readonly HttpClient _http;

    public CoverFetchService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Downloads the cover image at <paramref name="coverUrl"/> and returns the raw bytes,
    /// or null on failure.
    /// </summary>
    public async Task<byte[]?> DownloadCoverAsync(
        string coverUrl,
        string isbn,
        string sourceName,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync(coverUrl, ct);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download cover from {Url} for ISBN {Isbn}", coverUrl, isbn);
            return null;
        }
    }

    /// <summary>
    /// Decodes a bitmap from raw image bytes. Returns null if bytes are null or decoding fails.
    /// </summary>
    public static Bitmap? DecodeBitmap(byte[] imageData)
    {
        try
        {
            using var ms = new MemoryStream(imageData);
            return new Bitmap(ms);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to decode bitmap from image data");
            return null;
        }
    }
}
