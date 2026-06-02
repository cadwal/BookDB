using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Models.Entities;

namespace BookDB.Logic.Services;

public interface IBookService
{
    Task<Book> AddBookAsync(Book book, CancellationToken ct = default);

    Task<Book> AddBookWithContributorsAsync(
        Book book,
        IReadOnlyList<string> authorNames,
        CancellationToken ct = default);

    /// <summary>Author-only overload: replaces Author contributor rows for the book.</summary>
    Task UpdateBookContributorsAsync(
        int bookId,
        IReadOnlyList<string> authorNames,
        CancellationToken ct = default);

    /// <summary>Per D-A05: delete-then-insert all contributor rows for a book (not Author-only). Legacy Author-only overload retained for back-compat.</summary>
    Task UpdateBookContributorsAsync(
        int bookId,
        IReadOnlyList<(string personName, int? roleId)> contributors,
        CancellationToken ct = default);

    Task UpdateBookCategoriesAsync(int bookId, IReadOnlyList<int> categoryIds, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetPeopleNamesAsync(
        string? prefix = null,
        CancellationToken ct = default);

    Task<Book?> GetBookByIdAsync(int bookId, CancellationToken ct = default);

    Task<IReadOnlyList<BookService.BookListRow>> GetBooksForCollectionsAsync(
        IReadOnlySet<int> collectionIds,
        CancellationToken ct = default);

    Task<(IReadOnlyList<BookService.BookListRow> Books, int FilteredTotal, int GrandTotal)> GetBooksAsync(
        IReadOnlySet<int>? collectionIds,
        IReadOnlyList<int>? searchBookIds,
        Dictionary<string, HashSet<int>>? facetFilters,
        string? sortColumn,
        bool sortAscending,
        int skip,
        int take,
        bool isLoanedOut = false,
        CancellationToken ct = default);

    Task UpdateBookAsync(Book book, CancellationToken ct = default);

    Task DeleteBooksAsync(IReadOnlyList<int> bookIds, CancellationToken ct = default);

    Task<Book> DuplicateBookAsync(int bookId, string? titlePrefix = null, CancellationToken ct = default);

    Task BulkSetStatusAsync(IReadOnlyList<int> bookIds, int? newStatusId, CancellationToken ct = default);

    Task BulkSetLocationAsync(IReadOnlyList<int> bookIds, int? newLocationId, CancellationToken ct = default);

    Task BulkSetRatingAsync(IReadOnlyList<int> bookIds, int? newRatingId, CancellationToken ct = default);

    Task BulkSetFormatAsync(IReadOnlyList<int> bookIds, int? newFormatId, CancellationToken ct = default);

    Task BulkSetLanguageAsync(IReadOnlyList<int> bookIds, int? newLanguageId, CancellationToken ct = default);

    Task BulkSetOwnerAsync(IReadOnlyList<int> bookIds, int? newOwnerId, CancellationToken ct = default);

    Task BulkSetCollectionAsync(IReadOnlyList<int> bookIds, int collectionId, CancellationToken ct = default);

    Task<IReadOnlyList<SavedSearch>> GetSavedSearchesAsync(CancellationToken ct = default);

    Task AddSavedSearchAsync(SavedSearch search, CancellationToken ct = default);

    Task UpdateSavedSearchAsync(SavedSearch search, CancellationToken ct = default);

    Task DeleteSavedSearchAsync(int savedSearchId, CancellationToken ct = default);
}
