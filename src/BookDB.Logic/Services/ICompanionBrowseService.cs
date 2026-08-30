using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookDB.Logic.Services;

public sealed record BrowseBook(int BookId, string Title, string? Authors, string? Isbn, int? Year, bool HasCover);

public sealed record BrowsePage(IReadOnlyList<BrowseBook> Books, int TotalCount);

public sealed record BrowseCollection(int CollectionId, string Name, int BookCount);

/// <summary>
/// One book with every lookup already resolved to text: the companion holds no lookup tables of its own.
/// A seeded lookup row also carries its resource key, so a companion that has the same word in its own
/// resources can use that instead of the stored name. <paramref name="ImageTypeIds"/> are the desktop's own
/// image-type numbers.
/// </summary>
public sealed record BrowseDetail(
    int BookId,
    string Title,
    string? Subtitle,
    string? Authors,
    string? Series,
    string? Publisher,
    string? PubDate,
    string? Format,
    string? Language,
    int? Pages,
    string? Isbn,
    string? Collection,
    string? Comments,
    IReadOnlyList<int> ImageTypeIds)
{
    public string? FormatKey { get; init; }

    public string? LanguageKey { get; init; }
}

/// <summary>
/// Read-only browsing for the companion: the same search, filtering and paging the desktop list uses, over
/// whichever backend the library happens to live on.
/// </summary>
public interface ICompanionBrowseService
{
    /// <summary>
    /// An <paramref name="isbn"/> asks the "do I own this?" question and wins over
    /// <paramref name="search"/>; paging is clamped server-side, so a phone need not know the limits.
    /// </summary>
    Task<BrowsePage> BrowseAsync(
        string? search,
        int? collectionId,
        string? isbn,
        int skip,
        int take,
        CancellationToken ct = default);

    /// <summary>The collections a browse query can be narrowed to, in the desktop's own order.</summary>
    Task<IReadOnlyList<BrowseCollection>> GetCollectionsAsync(CancellationToken ct = default);

    /// <summary>Everything the read-only detail view shows, or null when the book is no longer there.</summary>
    Task<BrowseDetail?> GetDetailAsync(int bookId, CancellationToken ct = default);

    /// <summary>The stored bytes of one image of a book, or null when it has none of that type.</summary>
    Task<byte[]?> GetImageAsync(int bookId, int imageTypeId, CancellationToken ct = default);
}
