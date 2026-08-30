using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

public enum IsbnPreviewOrigin
{
    None,
    Library,
    Lookup,
}

/// <summary>
/// What a scanned ISBN turned out to be. Creates nothing — this is the answer to "what is this, and do I
/// already own it?", asked while the user is still standing in front of the shelf.
/// </summary>
public sealed record IsbnPreviewResult(
    bool InLibrary,
    int? BookId,
    string? Title,
    string? Authors,
    IsbnPreviewOrigin Origin);

public interface ICompanionPreviewService
{
    Task<IsbnPreviewResult> PreviewIsbnAsync(string isbn, CancellationToken ct = default);
}
