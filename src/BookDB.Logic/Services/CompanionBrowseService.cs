using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Isbn;
using BookDB.Models;
using BookDB.Models.Entities;

namespace BookDB.Logic.Services;

public sealed class CompanionBrowseService : ICompanionBrowseService
{
    public const int DefaultPageSize = 50;

    /// <summary>A page is fetched whole, covers and all, so the size a phone may ask for is bounded.</summary>
    public const int MaxPageSize = 100;

    private readonly IBookService _books;
    private readonly IBookSearchService _search;
    private readonly IBookMetadataService _metadata;
    private readonly IBookImageService _images;
    private readonly ILookupService _lookups;
    private readonly ILookupManagementService _lookupCounts;

    public CompanionBrowseService(
        IBookService books,
        IBookSearchService search,
        IBookMetadataService metadata,
        IBookImageService images,
        ILookupService lookups,
        ILookupManagementService lookupCounts)
    {
        _books = books;
        _search = search;
        _metadata = metadata;
        _images = images;
        _lookups = lookups;
        _lookupCounts = lookupCounts;
    }

    public async Task<BrowsePage> BrowseAsync(
        string? search,
        int? collectionId,
        string? isbn,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        take = take <= 0 ? DefaultPageSize : System.Math.Min(take, MaxPageSize);
        skip = System.Math.Max(skip, 0);

        IReadOnlyList<int>? bookIds = null;

        string normalizedIsbn = IsbnNormalizer.Normalize(isbn ?? "");
        if (normalizedIsbn.Length > 0)
        {
            // The exact-ISBN question has one answer or none; it is not a search that can be paged.
            var owned = await _metadata.FindBookByIsbnAsync(normalizedIsbn, ct).ConfigureAwait(false);
            if (owned is null)
            {
                return new BrowsePage([], 0);
            }

            bookIds = [owned.BookId];
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            bookIds = await _search.SearchBookIdsAsync(search!, ct).ConfigureAwait(false);
            if (bookIds.Count == 0)
            {
                return new BrowsePage([], 0);
            }
        }

        IReadOnlySet<int>? collections = collectionId is { } id ? new HashSet<int> { id } : null;

        var (rows, filteredTotal, _) = await _books
            .GetBooksAsync(collections, bookIds, null, null, true, skip, take, false, ct)
            .ConfigureAwait(false);

        return new BrowsePage([.. rows.Select(ToBrowseBook)], filteredTotal);
    }

    public async Task<IReadOnlyList<BrowseCollection>> GetCollectionsAsync(CancellationToken ct = default)
    {
        var collections = await _lookups.GetCollectionsAsync(ct).ConfigureAwait(false);

        var result = new List<BrowseCollection>(collections.Count);
        foreach (var collection in collections)
        {
            int count = await _lookupCounts
                .GetCollectionBookCountAsync(collection.CollectionId, ct).ConfigureAwait(false);

            result.Add(new BrowseCollection(collection.CollectionId, collection.Name, count));
        }

        return result;
    }

    public async Task<BrowseDetail?> GetDetailAsync(int bookId, CancellationToken ct = default)
    {
        var book = await _books.GetBookByIdAsync(bookId, ct).ConfigureAwait(false);
        if (book is null)
        {
            return null;
        }

        string authors = string.Join(", ", book.Contributors
            .Where(c => c.ContributorRole?.Code == "Author")
            .Select(c => c.Person?.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name)));

        return new BrowseDetail(
            book.BookId,
            book.Title,
            NullIfBlank(book.Subtitle),
            NullIfBlank(authors),
            SeriesOf(book),
            NullIfBlank(book.Publisher?.Name),
            NullIfBlank(book.PubDate),
            NullIfBlank(book.Format?.Name),
            NullIfBlank(book.Language?.Name),
            book.Pages,
            NullIfBlank(book.Isbn),
            NullIfBlank(book.Collection?.Name),
            NullIfBlank(book.Comments),
            [.. book.Images
                .Select(image => image.BookImageTypeId)
                .Where(typeId => typeId != BookImageTypeId.Thumbnail)
                .Distinct()
                .OrderBy(typeId => typeId)])
        {
            FormatKey = NullIfBlank(book.Format?.ResourceKey),
            LanguageKey = NullIfBlank(book.Language?.ResourceKey),
        };
    }

    public Task<byte[]?> GetImageAsync(int bookId, int imageTypeId, CancellationToken ct = default)
        => imageTypeId == BookImageTypeId.FrontCover
            ? _images.GetBookPrimaryCoverBytesAsync(bookId, ct)
            : GetByTypeAsync(bookId, imageTypeId, ct);

    private async Task<byte[]?> GetByTypeAsync(int bookId, int imageTypeId, CancellationToken ct)
    {
        var images = await _images.GetBookImagesAsync(bookId, ct).ConfigureAwait(false);
        var match = images.FirstOrDefault(i => i.BookImageTypeId == imageTypeId);

        return match is null
            ? null
            : await _images.GetBookImageBytesAsync(bookId, match.BookImageId, ct).ConfigureAwait(false);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Unlike the desktop list, a series with no number reads as the series alone rather than
    /// trailing a bare "#".</summary>
    private static string? SeriesOf(Book book)
    {
        if (book.Series is null)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(book.SeriesNumber)
            ? book.Series.Name
            : $"{book.Series.Name} #{book.SeriesNumber.Trim()}";
    }

    private static BrowseBook ToBrowseBook(BookService.BookListRow row) => new(
        row.BookId,
        row.Title,
        row.AuthorDisplay,
        row.Isbn,
        YearOf(row.Year),
        row.HasCoverImage);

    /// <summary>
    /// Publication dates are stored as free text — "1940", "1940-05-01", sometimes prose. The phone shows a
    /// year or nothing, so anything that does not start with four digits is simply no year.
    /// </summary>
    private static int? YearOf(string? pubDate)
    {
        if (pubDate is null || pubDate.Length < 4)
        {
            return null;
        }

        return int.TryParse(
            pubDate.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out int year) ? year : null;
    }
}
