using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Logic.Helpers;
using BookDB.Models;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class BookService : IBookService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public record BookListRow(
        int BookId,
        string Title,
        string? AuthorDisplay,
        string? SeriesDisplay,
        string? PublisherName,
        string? Year,
        string? FormatName,
        bool HasCoverImage,
        int? FormatId,
        int? SeriesId,
        int? PublisherId,
        int? LanguageId,
        int? RatingId,
        int? StatusId,
        int? LocationId,
        int? OwnerId,
        IReadOnlyList<int> AuthorPersonIds,
        IReadOnlyList<int> CategoryIds,
        string? RatingDisplay,
        string? StatusDisplay,
        int? CollectionId,
        string? Isbn,
        bool HasDuplicateImageTypes,
        bool IsLoaned,
        bool IsOverdue,
        string? LoanedToName);

    public BookService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    private static string FormatSeriesNumber(string? seriesNumber)
    {
        if (string.IsNullOrWhiteSpace(seriesNumber)) return string.Empty;
        if (decimal.TryParse(seriesNumber, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) && d % 1 == 0)
            return ((int)d).ToString();
        return seriesNumber;
    }

    private static BookListRow MapToListRow(
        Book b,
        HashSet<int> coverSet,
        HashSet<int> duplicateTypeSet,
        Dictionary<int, (string DisplayName, DateTime? DueDate)> loanDict)
    {
        var isLoaned = loanDict.TryGetValue(b.BookId, out var loan);
        return new(
            b.BookId,
            b.Title,
            string.Join(", ", b.Contributors
                .Where(c => c.ContributorRole?.Code == "Author")
                .Select(c => c.Person?.DisplayName ?? string.Empty)),
            b.Series != null ? $"{b.Series.Name} #{FormatSeriesNumber(b.SeriesNumber)}" : null,
            b.Publisher?.Name,
            b.PubDate,
            b.Format?.Name,
            coverSet.Contains(b.BookId),
            b.FormatId,
            b.SeriesId,
            b.PublisherId,
            b.LanguageId,
            b.RatingId,
            b.StatusId,
            b.LocationId,
            b.OwnerId,
            b.Contributors
                .Where(c => c.ContributorRole?.Code == "Author" && c.PersonId != 0)
                .Select(c => c.PersonId)
                .ToList(),
            b.Categories
                .Select(c => c.CategoryId)
                .ToList(),
            b.Rating?.Name,
            b.Status?.Name,
            b.CollectionId,
            b.Isbn,
            duplicateTypeSet.Contains(b.BookId),
            isLoaned,
            isLoaned && loan.DueDate.HasValue && loan.DueDate.Value.Date < DateTime.UtcNow.Date,
            isLoaned ? loan.DisplayName : null
        );
    }

    // ---------------------------------------------------------------------------
    // CRUD
    // ---------------------------------------------------------------------------

    public async Task<Book> AddBookAsync(Book book, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        book.Added = now;
        book.Updated = now;
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync(ct);
        return book;
    }

    private static async Task<Person> CreatePersonAsync(
        BookDbContext db, string displayName, CancellationToken ct)
    {
        displayName = PersonNameHelper.DeriveDisplayName(displayName);
        var sortName = PersonNameHelper.DeriveSortName(displayName);
        var person = new Person { DisplayName = displayName, SortName = sortName };
        db.People.Add(person);
        await db.SaveChangesAsync(ct);
        return person;
    }

    private static readonly Regex _roleSuffixRegex = new(@"\s*[\(\[]\s*(?<role>[^\)\]]+?)\s*[\)\]]\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static IEnumerable<(string Name, string? RoleHint)> ExpandAuthorNames(IReadOnlyList<string> raw)
    {
        foreach (var entry in raw)
        {
            foreach (var fragment in PersonNameHelper.SplitSquished(entry))
            {
                var (name, roleHint) = ParseNameAndRoleHint(fragment);
                if (!string.IsNullOrEmpty(name))
                    yield return (name, roleHint);
            }
        }
    }

    private static (string Name, string? RoleHint) ParseNameAndRoleHint(string fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            return (string.Empty, null);

        var trimmed = fragment.Trim();
        var match = _roleSuffixRegex.Match(trimmed);
        if (match.Success)
        {
            var rawName = trimmed[..match.Index].Trim();
            var roleHint = match.Groups["role"].Value.Trim();
            return (PersonNameHelper.DeriveDisplayName(rawName), string.IsNullOrWhiteSpace(roleHint) ? null : roleHint);
        }

        return (PersonNameHelper.DeriveDisplayName(trimmed), null);
    }

    private static ContributorRole? ResolveContributorRoleHint(
        string? roleHint,
        IEnumerable<ContributorRole> roles)
    {
        if (string.IsNullOrWhiteSpace(roleHint))
            return null;

        return roles.FirstOrDefault(r => string.Equals(r.Code, roleHint, StringComparison.OrdinalIgnoreCase))
            ?? roles.FirstOrDefault(r => string.Equals(r.DisplayName, roleHint, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Book> AddBookWithContributorsAsync(
        Book book,
        IReadOnlyList<string> authorNames,
        CancellationToken ct = default)
    {
        book.Added = DateTime.UtcNow;
        book.Updated = DateTime.UtcNow;
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync(ct);

        if (authorNames.Count > 0)
        {
            var contributorRoles = await dbContext.ContributorRoles
                .AsNoTracking()
                .ToListAsync(ct);
            var authorRole = contributorRoles
                .First(r => r.Code == "Author");

            int i = 0;
            foreach (var (name, roleHint) in ExpandAuthorNames(authorNames))
            {
                var person = await PersonQueries.FindByDisplayNameAsync(dbContext, name, ct)
                    ?? await CreatePersonAsync(dbContext, name, ct);

                var resolvedRole = ResolveContributorRoleHint(roleHint, contributorRoles);
                var roleId = resolvedRole?.ContributorRoleId ?? authorRole.ContributorRoleId;

                dbContext.BookContributors.Add(new BookContributor
                {
                    BookId = book.BookId,
                    PersonId = person.PersonId,
                    ContributorRoleId = roleId,
                    SortOrder = i++
                });
            }
            await dbContext.SaveChangesAsync(ct);
        }
        return book;
    }

    public async Task UpdateBookContributorsAsync(
        int bookId,
        IReadOnlyList<string> authorNames,
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        var contributorRoles = await dbContext.ContributorRoles
            .AsNoTracking()
            .ToListAsync(ct);
        var authorRole = contributorRoles
            .First(r => r.Code == "Author");

        var existing = await dbContext.BookContributors
            .Where(bc => bc.BookId == bookId && bc.ContributorRoleId == authorRole.ContributorRoleId)
            .ToListAsync(ct);
        dbContext.BookContributors.RemoveRange(existing);

        int i = 0;
        foreach (var (name, roleHint) in ExpandAuthorNames(authorNames))
        {
            var person = await PersonQueries.FindByDisplayNameAsync(dbContext, name, ct)
                ?? await CreatePersonAsync(dbContext, name, ct);

            var resolvedRole = ResolveContributorRoleHint(roleHint, contributorRoles);
            var roleId = resolvedRole?.ContributorRoleId ?? authorRole.ContributorRoleId;

            dbContext.BookContributors.Add(new BookContributor
            {
                BookId = bookId,
                PersonId = person.PersonId,
                ContributorRoleId = roleId,
                SortOrder = i++
            });
        }
        await dbContext.SaveChangesAsync(ct);
    }

    /// <summary>Per D-A05: delete-then-insert all contributor rows for a book (not Author-only). Legacy Author-only overload retained for back-compat.</summary>
    public async Task UpdateBookContributorsAsync(
        int bookId,
        IReadOnlyList<(string personName, int? roleId)> contributors,
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        var existing = await dbContext.BookContributors
            .Where(bc => bc.BookId == bookId)
            .ToListAsync(ct);
        dbContext.BookContributors.RemoveRange(existing);

        int sortIndex = 0;
        foreach (var (personName, roleId) in contributors)
        {
            if (string.IsNullOrWhiteSpace(personName) || roleId == null)
                continue;

            var trimmed = personName.Trim();
            var person = await PersonQueries.FindByDisplayNameAsync(dbContext, trimmed, ct)
                ?? await CreatePersonAsync(dbContext, trimmed, ct);

            dbContext.BookContributors.Add(new BookContributor
            {
                BookId = bookId,
                PersonId = person.PersonId,
                ContributorRoleId = roleId.Value,
                SortOrder = sortIndex
            });
            sortIndex++;
        }
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateBookCategoriesAsync(int bookId, IReadOnlyList<int> categoryIds, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        // The delete+re-insert runs as one retriable transactional unit — a free-standing user transaction
        // throws under the Postgres retrying execution strategy. The change tracker is reset each attempt.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
            await dbContext.BookCategories
                .Where(bc => bc.BookId == bookId)
                .ExecuteDeleteAsync(ct);
            foreach (var catId in categoryIds)
            {
                dbContext.BookCategories.Add(new BookCategory { BookId = bookId, CategoryId = catId });
            }
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    public async Task<IReadOnlyList<Person>> GetPeopleAsync(CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.People
            .AsNoTracking()
            .OrderBy(p => p.SortName)
            .ThenBy(p => p.PersonId)
            .ToListAsync(ct);
    }

    public async Task<Book?> GetBookByIdAsync(int bookId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.Books
            .AsTracking()
            .Include(b => b.Collection)
            .Include(b => b.Publisher)
            .Include(b => b.Format)
            .Include(b => b.Edition)
            .Include(b => b.Language)
            .Include(b => b.Series)
            .Include(b => b.Rating)
            .Include(b => b.Condition)
            .Include(b => b.Location)
            .Include(b => b.Owner)
            .Include(b => b.Status)
            .Include(b => b.PurchasePlace)
            .Include(b => b.Source)
            .Include(b => b.ReadingLevel)
            .Include(b => b.Contributors).ThenInclude(bc => bc.Person)
            .Include(b => b.Contributors).ThenInclude(bc => bc.ContributorRole)
            .Include(b => b.Categories).ThenInclude(bc => bc.Category)
            .Include(b => b.Images)
            // Multiple collection includes — split into separate queries to avoid a cartesian explosion.
            .AsSplitQuery()
            .SingleOrDefaultAsync(b => b.BookId == bookId, ct);
    }

    public async Task<IReadOnlyList<BookListRow>> GetBooksForCollectionsAsync(
        IReadOnlySet<int> collectionIds,
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        // An empty selection means no collection filter (show all); a non-empty one scopes to it.
        IQueryable<Book> source = collectionIds is { Count: > 0 }
            ? dbContext.Books.Where(CollectionFilter.Predicate(collectionIds))
            : dbContext.Books;
        var books = await source
            .Include(b => b.Publisher)
            .Include(b => b.Format)
            .Include(b => b.Series)
            .Include(b => b.Language)
            .Include(b => b.Rating)
            .Include(b => b.Status)
            .Include(b => b.Location)
            .Include(b => b.Owner)
            .Include(b => b.Contributors).ThenInclude(bc => bc.ContributorRole)
            .Include(b => b.Contributors).ThenInclude(bc => bc.Person)
            .Include(b => b.Categories)
            // Multiple collection includes — split into separate queries to avoid a cartesian explosion.
            .AsSplitQuery()
            .ToListAsync(ct);

        // Check cover presence via a separate ID-only query to avoid loading BLOB data
        var bookIds = books.Select(b => b.BookId).ToList();
        var booksWithCovers = await dbContext.BookImages
            .Where(bi => bookIds.Contains(bi.BookId))
            .Select(bi => bi.BookId)
            .Distinct()
            .ToListAsync(ct);
        var coverSet = new HashSet<int>(booksWithCovers);

        var duplicateTypeBookIds = await dbContext.BookImages
            .Where(bi => bookIds.Contains(bi.BookId))
            .GroupBy(bi => new { bi.BookId, bi.BookImageTypeId })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.BookId)
            .Distinct()
            .ToListAsync(ct);
        var duplicateTypeSet = new HashSet<int>(duplicateTypeBookIds);

        var emptyLoanDict = new Dictionary<int, (string DisplayName, DateTime? DueDate)>();
        return books.Select(b => MapToListRow(b, coverSet, duplicateTypeSet, emptyLoanDict)).ToList();
    }

    /// <summary>
    /// Returns a paginated, filtered, sorted page of books with filtered and grand totals.
    /// All filtering and sorting happens in SQL. searchBookIds is applied as an IN clause;
    /// note SQLite SQLITE_MAX_VARIABLE_NUMBER is 32766, so callers should not exceed that limit.
    /// </summary>
    public async Task<(IReadOnlyList<BookListRow> Books, int FilteredTotal, int GrandTotal)> GetBooksAsync(
        IReadOnlySet<int>? collectionIds,
        IReadOnlyList<int>? searchBookIds,
        Dictionary<string, HashSet<int>>? facetFilters,
        string? sortColumn,
        bool sortAscending,
        int skip,
        int take,
        bool isLoanedOut = false,
        CancellationToken ct = default)
    {
        // Guard against negative skip/take and unreasonable page sizes
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 1000);

        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        // Base collection query
        IQueryable<Book> query = dbContext.Books;
        if (collectionIds is { Count: > 0 })
            query = query.Where(CollectionFilter.Predicate(collectionIds));

        // GrandTotal is collection-scoped count without search/facet filters
        var grandTotal = await query.CountAsync(ct);

        // Apply FTS/advanced search result IDs filter.
        // A non-null but empty list means the search matched nothing — filter to zero
        // rows rather than skipping the filter (null = no search active = show all).
        if (searchBookIds != null)
            query = query.Where(b => searchBookIds.Contains(b.BookId));

        // Apply facet filters — all translated to SQL by EF Core
        if (facetFilters is { Count: > 0 })
        {
            foreach (var (key, ids) in facetFilters)
            {
                if (ids.Count == 0) continue;
                query = key switch
                {
                    "Format"    => query.Where(b => b.FormatId != null && ids.Contains(b.FormatId.Value)),
                    "Series"    => query.Where(b => b.SeriesId != null && ids.Contains(b.SeriesId.Value)),
                    "Publisher" => query.Where(b => b.PublisherId != null && ids.Contains(b.PublisherId.Value)),
                    "Language"  => query.Where(b => b.LanguageId != null && ids.Contains(b.LanguageId.Value)),
                    "Rating"    => query.Where(b => b.RatingId != null && ids.Contains(b.RatingId.Value)),
                    "Status"    => query.Where(b => b.StatusId != null && ids.Contains(b.StatusId.Value)),
                    "Location"  => query.Where(b => b.LocationId != null && ids.Contains(b.LocationId.Value)),
                    "Owner"     => query.Where(b => b.OwnerId != null && ids.Contains(b.OwnerId.Value)),
                    "Author"    => query.Where(b => b.Contributors.Any(c => c.ContributorRole != null && c.ContributorRole.Code == "Author" && ids.Contains(c.PersonId))),
                    "Category"  => query.Where(b => b.Categories.Any(c => ids.Contains(c.CategoryId))),
                    _           => query  // unknown facet key — no-op; EF Core parameterizes all values
                };
            }
        }

        // Apply loaned-out pseudo-filter
        if (isLoanedOut)
            query = query.Where(b => dbContext.Loans.Any(l => l.BookId == b.BookId && l.ReturnedDate == null));

        // FilteredTotal is after search + facet filters but before pagination
        var filteredTotal = await query.CountAsync(ct);

        // Apply sort in SQL before pagination
        query = (sortColumn, sortAscending) switch
        {
            ("Title", true)          => query.OrderBy(b => b.Title),
            ("Title", false)         => query.OrderByDescending(b => b.Title),
            ("AuthorDisplay", true)  => query.OrderBy(b => b.Contributors
                .Where(c => c.ContributorRole != null && c.ContributorRole.Code == "Author")
                .Select(c => c.Person != null ? c.Person.DisplayName : null)
                .FirstOrDefault()),
            ("AuthorDisplay", false) => query.OrderByDescending(b => b.Contributors
                .Where(c => c.ContributorRole != null && c.ContributorRole.Code == "Author")
                .Select(c => c.Person != null ? c.Person.DisplayName : null)
                .FirstOrDefault()),
            ("SeriesDisplay", true)  => query.OrderBy(b => b.Series != null ? b.Series.Name : null),
            ("SeriesDisplay", false) => query.OrderByDescending(b => b.Series != null ? b.Series.Name : null),
            ("PublisherName", true)  => query.OrderBy(b => b.Publisher != null ? b.Publisher.Name : null),
            ("PublisherName", false) => query.OrderByDescending(b => b.Publisher != null ? b.Publisher.Name : null),
            ("Year", true)           => query.OrderBy(b => b.PubDate),
            ("Year", false)          => query.OrderByDescending(b => b.PubDate),
            ("FormatName", true)     => query.OrderBy(b => b.Format != null ? b.Format.Name : null),
            ("FormatName", false)    => query.OrderByDescending(b => b.Format != null ? b.Format.Name : null),
            _                        => query.OrderBy(b => b.Title)
        };

        // Apply pagination
        query = query.Skip(skip).Take(take);

        // Load page with navigation properties — use Include-based loading and map in memory
        // (string.Join and complex projections in EF Core Select may not translate to SQL)
        var books = await query
            .Include(b => b.Publisher)
            .Include(b => b.Format)
            .Include(b => b.Series)
            .Include(b => b.Language)
            .Include(b => b.Rating)
            .Include(b => b.Status)
            .Include(b => b.Location)
            .Include(b => b.Owner)
            .Include(b => b.Contributors).ThenInclude(bc => bc.ContributorRole)
            .Include(b => b.Contributors).ThenInclude(bc => bc.Person)
            .Include(b => b.Categories)
            // Multiple collection includes — split into separate queries to avoid a cartesian explosion.
            .AsSplitQuery()
            .ToListAsync(ct);

        // Check cover presence via ID-only query to avoid loading BLOB data
        var bookIds = books.Select(b => b.BookId).ToList();
        var booksWithCovers = await dbContext.BookImages
            .Where(bi => bookIds.Contains(bi.BookId))
            .Select(bi => bi.BookId)
            .Distinct()
            .ToListAsync(ct);
        var coverSet = new HashSet<int>(booksWithCovers);

        var duplicateTypeBookIds = await dbContext.BookImages
            .Where(bi => bookIds.Contains(bi.BookId))
            .GroupBy(bi => new { bi.BookId, bi.BookImageTypeId })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.BookId)
            .Distinct()
            .ToListAsync(ct);
        var duplicateTypeSet = new HashSet<int>(duplicateTypeBookIds);

        var activeLoans = await dbContext.Loans
            .Where(l => bookIds.Contains(l.BookId) && l.ReturnedDate == null)
            .Include(l => l.Borrower)
            .Select(l => new {
                l.BookId,
                DisplayName = (l.Borrower!.FirstName ?? "") + (l.Borrower.LastName != null ? " " + l.Borrower.LastName : ""),
                l.DueDate
            })
            .ToListAsync(ct);
        var loanDict = activeLoans.ToDictionary(l => l.BookId, l => (l.DisplayName, l.DueDate));

        var rows = books.Select(b => MapToListRow(b, coverSet, duplicateTypeSet, loanDict)).ToList();

        return (rows, filteredTotal, grandTotal);
    }

    public async Task UpdateBookAsync(Book book, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        book.Updated = DateTime.UtcNow;

        // Null out reference navigations before Attach. EF Core relationship fix-up during
        // Attach can override FK values with the stale navigation's PK, reverting any FK
        // changes made by the caller (e.g. changing Publisher). The caller reloads CurrentBook
        // immediately after this call, so these mutations are harmless.
        book.Collection = null;
        book.Publisher = null;
        book.Format = null;
        book.Edition = null;
        book.Language = null;
        book.Series = null;
        book.Rating = null;
        book.Condition = null;
        book.Location = null;
        book.Owner = null;
        book.Status = null;
        book.PurchasePlace = null;
        book.Source = null;
        book.ReadingLevel = null;

        dbContext.Books.Attach(book);
        var entry = dbContext.Entry(book);
        foreach (var prop in entry.Properties)
        {
            if (prop.Metadata.Name != nameof(Book.BookId) && prop.Metadata.Name != nameof(Book.Added))
                prop.IsModified = true;
        }
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteBooksAsync(IReadOnlyList<int> bookIds, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        // Loan.BookId is ON DELETE RESTRICT (loan history is protected from cascade), so its rows
        // must be removed explicitly before the book, or the DELETE fails the FK constraint.
        await dbContext.Loans
            .Where(l => bookIds.Contains(l.BookId))
            .ExecuteDeleteAsync(ct);
        await dbContext.Books
            .Where(b => bookIds.Contains(b.BookId))
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
    }

    public async Task<Book> DuplicateBookAsync(int bookId, string? titlePrefix = null, CancellationToken ct = default)
    {
        var original = await GetBookByIdAsync(bookId, ct)
            ?? throw new InvalidOperationException($"Book {bookId} not found.");

        var dup = new Book
        {
            CollectionId = original.CollectionId,
            Title = titlePrefix + original.Title,
            Subtitle = original.Subtitle,
            PublisherId = original.PublisherId,
            PubPlace = original.PubPlace,
            PubDate = original.PubDate,
            CopyrightDate = original.CopyrightDate,
            FormatId = original.FormatId,
            EditionId = original.EditionId,
            Pages = original.Pages,
            Copies = 1,
            Isbn = null, // UX_Book_Isbn partial unique index prevents copying a non-null ISBN
            LanguageId = original.LanguageId,
            SeriesId = original.SeriesId,
            SeriesNumber = original.SeriesNumber,
            ReadCount = 0,
            RatingId = original.RatingId,
            ConditionId = original.ConditionId,
            LocationId = original.LocationId,
            OwnerId = original.OwnerId,
            StatusId = original.StatusId,
            Signed = original.Signed,
            OutOfPrint = original.OutOfPrint,
            Favorite = original.Favorite,
            Keywords = original.Keywords,
            Comments = original.Comments,
            BookInfo = original.BookInfo,
            PurchasePrice = original.PurchasePrice,
            PurchaseCurrency = original.PurchaseCurrency,
            PurchasePlaceId = original.PurchasePlaceId,
            ListPrice = original.ListPrice,
            ListPriceCurrency = original.ListPriceCurrency,
            SourceId = original.SourceId,
            ExternalId = original.ExternalId,
            MediaLink = original.MediaLink,
            Display = original.Display,
            ReadingLevelId = original.ReadingLevelId,
            Added = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };

        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        dbContext.Books.Add(dup);
        await dbContext.SaveChangesAsync(ct);

        // Copy contributors
        foreach (var c in original.Contributors)
        {
            dbContext.BookContributors.Add(new BookContributor
            {
                BookId = dup.BookId,
                PersonId = c.PersonId,
                ContributorRoleId = c.ContributorRoleId,
                SortOrder = c.SortOrder
            });
        }

        // Copy categories
        foreach (var cat in original.Categories)
        {
            dbContext.BookCategories.Add(new BookCategory
            {
                BookId = dup.BookId,
                CategoryId = cat.CategoryId
            });
        }

        // Copy images from already-loaded Images collection (via .Include in GetBookByIdAsync)
        foreach (var img in original.Images)
        {
            dbContext.BookImages.Add(new BookImage
            {
                BookId = dup.BookId,
                ImageData = img.ImageData,
                MimeType = img.MimeType,
                IsPrimary = img.IsPrimary,
                DisplayOrder = img.DisplayOrder,
                BookImageTypeId = img.BookImageTypeId,
                Added = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(ct);
        return dup;
    }

    // ---------------------------------------------------------------------------
    // Bulk Edit
    // ---------------------------------------------------------------------------

    private async Task BulkSetPropertyAsync<T>(
        IReadOnlyList<int> bookIds,
        Expression<Func<Book, T?>> selector,
        T? value,
        CancellationToken ct)
    {
        if (bookIds.Count == 0) return;
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.Books
            .Where(b => bookIds.Contains(b.BookId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(selector, value)
                .SetProperty(b => b.Updated, DateTime.UtcNow), ct);
    }

    public Task BulkSetStatusAsync(IReadOnlyList<int> bookIds,
        int? newStatusId, CancellationToken ct = default)
        => BulkSetPropertyAsync(bookIds, b => b.StatusId, newStatusId, ct);

    public Task BulkSetLocationAsync(IReadOnlyList<int> bookIds,
        int? newLocationId, CancellationToken ct = default)
        => BulkSetPropertyAsync(bookIds, b => b.LocationId, newLocationId, ct);

    public Task BulkSetRatingAsync(IReadOnlyList<int> bookIds,
        int? newRatingId, CancellationToken ct = default)
        => BulkSetPropertyAsync(bookIds, b => b.RatingId, newRatingId, ct);

    public Task BulkSetFormatAsync(IReadOnlyList<int> bookIds,
        int? newFormatId, CancellationToken ct = default)
        => BulkSetPropertyAsync(bookIds, b => b.FormatId, newFormatId, ct);

    public Task BulkSetLanguageAsync(IReadOnlyList<int> bookIds,
        int? newLanguageId, CancellationToken ct = default)
        => BulkSetPropertyAsync(bookIds, b => b.LanguageId, newLanguageId, ct);

    public Task BulkSetOwnerAsync(IReadOnlyList<int> bookIds,
        int? newOwnerId, CancellationToken ct = default)
        => BulkSetPropertyAsync(bookIds, b => b.OwnerId, newOwnerId, ct);

    public Task BulkSetCollectionAsync(IReadOnlyList<int> bookIds,
        int collectionId, CancellationToken ct = default)
        => BulkSetPropertyAsync(bookIds, b => b.CollectionId, (int?)collectionId, ct);

    // ---------------------------------------------------------------------------
    // SavedSearch CRUD
    // ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<SavedSearch>> GetSavedSearchesAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.SavedSearches.OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task AddSavedSearchAsync(SavedSearch search, CancellationToken ct = default)
    {
        search.CreatedAt = DateTime.UtcNow;
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        dbContext.SavedSearches.Add(search);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateSavedSearchAsync(SavedSearch search, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.SavedSearches
            .Where(s => s.SavedSearchId == search.SavedSearchId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Name, search.Name)
                .SetProperty(x => x.QueryJson, search.QueryJson), ct);
    }

    public async Task DeleteSavedSearchAsync(int savedSearchId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.SavedSearches
            .Where(s => s.SavedSearchId == savedSearchId)
            .ExecuteDeleteAsync(ct);
    }
}
