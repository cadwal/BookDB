using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Logic.Helpers;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public sealed class BookMetadataService : IBookMetadataService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public BookMetadataService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<Book?> FindBookByIsbnAsync(string isbn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(isbn)) return null;

        var stripped = NormalizeIsbn(isbn);
        var isbn13 = NormalizeToIsbn13(isbn);
        var isbn10 = NormalizeToIsbn10(isbn);

        var searchVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stripped };
        if (isbn13 is not null) searchVariants.Add(isbn13);
        if (isbn10 is not null) searchVariants.Add(isbn10);

        if (stripped.Length == 10)
            searchVariants.Add("978" + stripped);
        else if (stripped.Length == 13 && stripped.StartsWith("978", StringComparison.Ordinal))
            searchVariants.Add(stripped.Substring(3));

        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var matchId = await dbContext.Books
            .Where(b => b.Isbn != null && searchVariants.Contains(b.Isbn))
            .Select(b => (int?)b.BookId)
            .FirstOrDefaultAsync(ct);

        if (matchId is null) return null;

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
            .SingleOrDefaultAsync(b => b.BookId == matchId.Value, ct);
    }

    public async Task<Book> AddBookFromMetadataAsync(
        BookMetadata merged,
        byte[]? cover,
        int? collectionId,
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var publisherId = await FindOrCreatePublisherAsync(dbContext, merged.Publisher, ct);
        var languageId = await FindOrCreateLanguageAsync(dbContext, merged.Language, ct);
        var seriesId = await FindOrCreateSeriesAsync(dbContext, merged.Series, ct);

        var book = new Book
        {
            Title = merged.Title ?? string.Empty,
            Subtitle = merged.Subtitle,
            Isbn = merged.Isbn is not null ? NormalizeToIsbn13(merged.Isbn) ?? NormalizeIsbn(merged.Isbn) : null,
            PublisherId = publisherId,
            PubDate = FieldDiffComputer.NormalizePubDate(merged.PubDate),
            LanguageId = languageId,
            SeriesId = seriesId,
            SeriesNumber = merged.SeriesNumber,
            Pages = merged.Pages,
            Comments = merged.Description,
            CollectionId = collectionId,
            Added = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        };

        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync(ct);

        if (cover is { Length: > 0 })
        {
            dbContext.BookImages.Add(new BookImage
            {
                BookId = book.BookId,
                ImageData = cover,
                MimeType = ImageHelpers.DetectMimeType(cover),
                IsPrimary = true,
                DisplayOrder = 0,
                Added = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(ct);
        }

        if (merged.Authors.Count > 0)
        {
            var contributorRoles = await dbContext.ContributorRoles
                .AsNoTracking()
                .ToListAsync(ct);
            var authorRole = contributorRoles
                .First(r => r.Code == "Author");

            int sortOrder = 0;
            foreach (var (name, roleHint) in ExpandAuthorNames(merged.Authors))
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
                    SortOrder = sortOrder++
                });
            }
            await dbContext.SaveChangesAsync(ct);
        }

        return book;
    }

    public async Task UpdateBookFromMetadataAsync(
        int bookId,
        BookMetadata merged,
        byte[]? cover,
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var book = await dbContext.Books.AsTracking().FirstOrDefaultAsync(b => b.BookId == bookId, ct);
        if (book is null)
            throw new InvalidOperationException($"Book {bookId} not found.");

        if (merged.Title is not null) book.Title = merged.Title;
        if (merged.Subtitle is not null) book.Subtitle = merged.Subtitle;
        if (merged.Isbn is not null) book.Isbn = NormalizeToIsbn13(merged.Isbn) ?? NormalizeIsbn(merged.Isbn);
        if (merged.PubDate is not null)
            book.PubDate = FieldDiffComputer.NormalizePubDate(merged.PubDate);
        if (merged.Pages is not null) book.Pages = merged.Pages;
        if (merged.Description is not null) book.Comments = merged.Description;
        if (merged.SeriesNumber is not null) book.SeriesNumber = merged.SeriesNumber;

        if (cover is { Length: > 0 })
        {
            var existingPrimary = await dbContext.BookImages
                .Where(bi => bi.BookId == bookId && bi.IsPrimary)
                .FirstOrDefaultAsync(ct);
            if (existingPrimary is not null)
                dbContext.BookImages.Remove(existingPrimary);

            dbContext.BookImages.Add(new BookImage
            {
                BookId = bookId,
                ImageData = cover,
                MimeType = ImageHelpers.DetectMimeType(cover),
                IsPrimary = true,
                DisplayOrder = 0,
                Added = DateTime.UtcNow
            });
        }

        if (merged.Publisher is not null)
            book.PublisherId = await FindOrCreatePublisherAsync(dbContext, merged.Publisher, ct);
        if (merged.Language is not null)
            book.LanguageId = await FindOrCreateLanguageAsync(dbContext, merged.Language, ct);
        if (merged.Series is not null)
            book.SeriesId = await FindOrCreateSeriesAsync(dbContext, merged.Series, ct);

        book.Updated = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        if (merged.Authors.Count > 0)
        {
            var contributorRoles = await dbContext.ContributorRoles
                .AsNoTracking()
                .ToListAsync(ct);
            var authorRole = contributorRoles
                .First(r => r.Code == "Author");
            var existing = await dbContext.BookContributors
                .Where(bc => bc.BookId == bookId && bc.ContributorRoleId == authorRole.ContributorRoleId)
                .ToListAsync(ct);
            dbContext.BookContributors.RemoveRange(existing);

            int sortOrder = 0;
            foreach (var (name, roleHint) in ExpandAuthorNames(merged.Authors))
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
                    SortOrder = sortOrder++
                });
            }
            await dbContext.SaveChangesAsync(ct);
        }
    }

    public static bool TryResolveLanguageName(string isoCode, out string fullName)
    {
        return _isoCodeToName.TryGetValue(isoCode.Trim(), out fullName!);
    }

    private static string NormalizeIsbn(string isbn) =>
        isbn.Replace("-", string.Empty).Replace(" ", string.Empty).Trim();

    private static string? NormalizeToIsbn13(string isbn)
    {
        var stripped = NormalizeIsbn(isbn);
        if (stripped.Length == 13) return stripped;
        if (stripped.Length == 10)
        {
            var base12 = "978" + stripped[..9];
            int sum = 0;
            for (int i = 0; i < 12; i++)
            {
                if (!char.IsDigit(base12[i])) return null;
                int d = base12[i] - '0';
                sum += i % 2 == 0 ? d : d * 3;
            }
            int check = (10 - (sum % 10)) % 10;
            return base12 + (char)('0' + check);
        }
        return null;
    }

    private static string? NormalizeToIsbn10(string isbn)
    {
        var stripped = NormalizeIsbn(isbn);
        if (stripped.Length == 10) return stripped;
        if (stripped.Length == 13 && stripped.StartsWith("978", StringComparison.Ordinal))
        {
            var nineDigits = stripped.Substring(3, 9);
            int sum = 0;
            for (int i = 0; i < 9; i++)
            {
                if (!char.IsDigit(nineDigits[i])) return null;
                sum += (nineDigits[i] - '0') * (10 - i);
            }
            int check = (11 - (sum % 11)) % 11;
            var checkChar = check == 10 ? 'X' : (char)('0' + check);
            return nineDigits + checkChar;
        }
        return null;
    }

    private static async Task<int?> FindOrCreatePublisherAsync(
        BookDbContext dbContext, string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var publisher = await dbContext.Publishers.FirstOrDefaultAsync(p => p.Name == name, ct);
        if (publisher is null)
        {
            publisher = new Publisher { Name = name };
            dbContext.Publishers.Add(publisher);
            await dbContext.SaveChangesAsync(ct);
        }
        return publisher.PublisherId;
    }

    private static readonly Dictionary<string, string> _isoCodeToName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English",
        ["sv"] = "Swedish",
        ["fr"] = "French",
        ["de"] = "German",
        ["es"] = "Spanish",
        ["it"] = "Italian",
        ["ja"] = "Japanese",
        ["no"] = "Norwegian",
        ["da"] = "Danish",
        ["fi"] = "Finnish",
    };

    private static async Task<int?> FindOrCreateLanguageAsync(
        BookDbContext dbContext, string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var resolvedName = _isoCodeToName.TryGetValue(name.Trim(), out var mappedName)
            ? mappedName
            : name.Trim();

        var languages = await dbContext.Languages.ToListAsync(ct);
        var language = languages.FirstOrDefault(l =>
            string.Equals(l.Name, resolvedName, StringComparison.OrdinalIgnoreCase));
        return language?.LanguageId;
    }

    private static async Task<int?> FindOrCreateSeriesAsync(
        BookDbContext dbContext, string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var series = await dbContext.Series.FirstOrDefaultAsync(s => s.Name == name, ct);
        if (series is null)
        {
            series = new Series { Name = name };
            dbContext.Series.Add(series);
            await dbContext.SaveChangesAsync(ct);
        }
        return series.SeriesId;
    }

    // Intentional duplicate of BookService.CreatePersonAsync — keep in sync
    private static async Task<Person> CreatePersonAsync(
        BookDbContext dbContext, string displayName, CancellationToken ct)
    {
        displayName = PersonNameHelper.DeriveDisplayName(displayName);
        var sortName = PersonNameHelper.DeriveSortName(displayName);
        var person = new Person { DisplayName = displayName, SortName = sortName };
        dbContext.People.Add(person);
        await dbContext.SaveChangesAsync(ct);
        return person;
    }

    // Intentional duplicate of BookService._roleSuffixRegex — keep in sync
    private static readonly Regex _roleSuffixRegex = new(@"\s*[\(\[]\s*(?<role>[^\)\]]+?)\s*[\)\]]\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Intentional duplicate of BookService.ExpandAuthorNames — keep in sync
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

    // Intentional duplicate of BookService.ParseNameAndRoleHint — keep in sync
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

    // Intentional duplicate of BookService.ResolveContributorRoleHint — keep in sync
    private static ContributorRole? ResolveContributorRoleHint(
        string? roleHint,
        IEnumerable<ContributorRole> roles)
    {
        if (string.IsNullOrWhiteSpace(roleHint))
            return null;

        return roles.FirstOrDefault(r => string.Equals(r.Code, roleHint, StringComparison.OrdinalIgnoreCase))
            ?? roles.FirstOrDefault(r => string.Equals(r.DisplayName, roleHint, StringComparison.OrdinalIgnoreCase));
    }
}
