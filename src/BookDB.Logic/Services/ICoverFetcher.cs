using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

/// <summary>
/// Abstraction for downloading a cover image and returning its raw bytes.
/// Implemented in the Desktop layer (CoverFetchService) and injected into BatchQueueProcessor
/// so the Logic layer does not depend on the Desktop layer.
/// Returns the image bytes, or null on failure.
/// </summary>
public interface ICoverFetcher
{
    Task<byte[]?> DownloadCoverAsync(
        string coverUrl,
        string isbn,
        string sourceName,
        CancellationToken ct = default);
}
