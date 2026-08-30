using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Browsing as the phone does it, against a real temp-file SQLite library: text search, collection filter,
/// the exact-ISBN "do I own this?" question, paging, and reading one image out by type.
/// </summary>
public sealed class CompanionBrowseServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly CompanionBrowseService _sut;
    private readonly BookImageService _images;

    public CompanionBrowseServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_companion_browse_{Guid.NewGuid():N}.db");
        string connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDB.Data.Sqlite.SqliteDbUpRunner))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");
        }

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        _factory = new TestBookDbContextFactory(options);
        _images = new BookImageService(_factory);
        _sut = new CompanionBrowseService(
            new BookService(_factory),
            new BookSearchService(_factory, new BookDB.Data.Sqlite.SqliteBookSearchProvider(_factory)),
            new BookMetadataService(_factory),
            _images,
            new LookupService(_factory),
            new LookupManagementService(_factory, new BookDB.Data.Sqlite.SqliteLookupNameMatcher()));
    }

    public void Dispose()
    {
        // This database's pool only — ClearAllPools() is process-wide and disposes the native
        // handle of connections other test classes are using in parallel.
        using (var poolKey = new SqliteConnection($"Data Source={_dbPath}"))
            SqliteConnection.ClearPool(poolKey);
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best effort: a temp file left behind must not fail the run.
        }
    }

    private async Task<int> SeedBookAsync(string title, string? isbn = null, int? collectionId = null, string? pubDate = null)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = title, Isbn = isbn, CollectionId = collectionId, PubDate = pubDate };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return book.BookId;
    }

    private async Task<int> SeedCollectionAsync(string name, int sortOrder)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var collection = new Collection { Name = name, SortOrder = sortOrder };
        db.Collections.Add(collection);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return collection.CollectionId;
    }

    private Task<BrowsePage> BrowseAsync(
        string? search = null, int? collectionId = null, string? isbn = null, int skip = 0, int take = 0)
        => _sut.BrowseAsync(search, collectionId, isbn, skip, take, TestContext.Current.CancellationToken);

    [Fact]
    public async Task AnEmptyQueryListsTheWholeLibrary()
    {
        await SeedBookAsync("Kallocain");
        await SeedBookAsync("Aniara");

        var page = await BrowseAsync();

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Books.Count);
    }

    [Fact]
    public async Task SearchNarrowsToMatchingBooks()
    {
        await SeedBookAsync("Kallocain");
        await SeedBookAsync("Aniara");

        var page = await BrowseAsync(search: "Kallocain");

        Assert.Equal("Kallocain", Assert.Single(page.Books).Title);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task ASearchThatMatchesNothingReturnsAnEmptyPage()
    {
        await SeedBookAsync("Kallocain");

        var page = await BrowseAsync(search: "nothing like this");

        Assert.Empty(page.Books);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task TheCollectionFilterExcludesOtherCollections()
    {
        int wanted = await SeedCollectionAsync("Companion wanted", sortOrder: 90);
        int other = await SeedCollectionAsync("Companion other", sortOrder: 91);
        await SeedBookAsync("Kallocain", collectionId: wanted);
        await SeedBookAsync("Atlas", collectionId: other);

        var page = await BrowseAsync(collectionId: wanted);

        Assert.Equal("Kallocain", Assert.Single(page.Books).Title);
    }

    [Fact]
    public async Task AnExactIsbnFindsTheOneBookThatHasIt()
    {
        await SeedBookAsync("Concrete Mathematics", isbn: "9780201558029");
        await SeedBookAsync("Kallocain");

        var page = await BrowseAsync(isbn: "9780201558029");

        var found = Assert.Single(page.Books);
        Assert.Equal("Concrete Mathematics", found.Title);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task AnIsbnTheLibraryDoesNotHaveAnswersEmptyRatherThanEverything()
    {
        await SeedBookAsync("Kallocain");

        var page = await BrowseAsync(isbn: "9780306406157");

        Assert.Empty(page.Books);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task TheIsbnQuestionIgnoresHyphensAndAnIsbn10Spelling()
    {
        await SeedBookAsync("Concrete Mathematics", isbn: "9780306406157");

        Assert.Single((await BrowseAsync(isbn: "978-0-306-40615-7")).Books);
        Assert.Single((await BrowseAsync(isbn: "0306406152")).Books);
    }

    [Fact]
    public async Task PagingWalksTheLibraryWithoutRepeatingOrLosingBooks()
    {
        for (int i = 0; i < 5; i++)
        {
            await SeedBookAsync($"Book {i:D2}");
        }

        var first = await BrowseAsync(skip: 0, take: 2);
        var second = await BrowseAsync(skip: 2, take: 2);
        var third = await BrowseAsync(skip: 4, take: 2);

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(2, first.Books.Count);
        Assert.Equal(2, second.Books.Count);
        Assert.Single(third.Books);
        Assert.Equal(5, first.Books.Concat(second.Books).Concat(third.Books).Select(b => b.BookId).Distinct().Count());
    }

    [Fact]
    public async Task AnUnaskedPageSizeBecomesTheServerDefault()
    {
        for (int i = 0; i < CompanionBrowseService.DefaultPageSize + 10; i++)
        {
            await SeedBookAsync($"Book {i:D3}");
        }

        var page = await BrowseAsync(take: 0);

        Assert.Equal(CompanionBrowseService.DefaultPageSize, page.Books.Count);
        Assert.Equal(CompanionBrowseService.DefaultPageSize + 10, page.TotalCount);
    }

    [Fact]
    public async Task AGreedyPageSizeIsCappedRatherThanHonoured()
    {
        for (int i = 0; i < CompanionBrowseService.MaxPageSize + 5; i++)
        {
            await SeedBookAsync($"Book {i:D3}");
        }

        var page = await BrowseAsync(take: 5000);

        Assert.Equal(CompanionBrowseService.MaxPageSize, page.Books.Count);
    }

    [Fact]
    public async Task ABookCarriesWhatABrowseRowNeeds()
    {
        int bookId = await SeedBookAsync("Kallocain", isbn: "9780299133245", pubDate: "1940");
        await _images.SavePrimaryBookImageAsync(bookId, [1, 2, 3], TestContext.Current.CancellationToken);

        var book = Assert.Single((await BrowseAsync(search: "Kallocain")).Books);

        Assert.Equal(bookId, book.BookId);
        Assert.Equal("Kallocain", book.Title);
        Assert.Equal("9780299133245", book.Isbn);
        Assert.Equal(1940, book.Year);
        Assert.True(book.HasCover);
    }

    [Fact]
    public async Task ABookWithoutACoverSaysSo()
    {
        await SeedBookAsync("Kallocain");

        Assert.False(Assert.Single((await BrowseAsync()).Books).HasCover);
    }

    [Fact]
    public async Task AnUnparseableYearIsReportedAsNoYearRatherThanFailing()
    {
        await SeedBookAsync("Odd", pubDate: "MCMXL");

        Assert.Null(Assert.Single((await BrowseAsync()).Books).Year);
    }

    [Fact]
    public async Task TheFrontCoverIsReadBackByType()
    {
        int bookId = await SeedBookAsync("Kallocain");
        await _images.SavePrimaryBookImageAsync(bookId, [4, 5, 6], TestContext.Current.CancellationToken);

        byte[]? cover = await _sut.GetImageAsync(
            bookId, BookImageTypeId.FrontCover, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 4, 5, 6 }, cover);
    }

    [Fact]
    public async Task OtherImageTypesAreReadBackByType()
    {
        int bookId = await SeedBookAsync("Kallocain");
        await _images.SaveBookImageByTypeAsync(
            bookId, BookImageTypeId.Spine, [7, 8], TestContext.Current.CancellationToken);

        Assert.Equal(
            new byte[] { 7, 8 },
            await _sut.GetImageAsync(bookId, BookImageTypeId.Spine, TestContext.Current.CancellationToken));
        Assert.Null(
            await _sut.GetImageAsync(bookId, BookImageTypeId.BackCover, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TheCollectionFilterListsEachCollectionWithItsBookCount()
    {
        int wanted = await SeedCollectionAsync("Companion counted", sortOrder: 92);
        await SeedBookAsync("Kallocain", collectionId: wanted);
        await SeedBookAsync("Aniara", collectionId: wanted);
        await SeedBookAsync("Uncollected");

        var collections = await _sut.GetCollectionsAsync(TestContext.Current.CancellationToken);

        var counted = Assert.Single(collections, c => c.CollectionId == wanted);
        Assert.Equal("Companion counted", counted.Name);
        Assert.Equal(2, counted.BookCount);
    }

    [Fact]
    public async Task DetailResolvesTheLookupsThePhoneCannot()
    {
        int collectionId = await SeedCollectionAsync("Companion detail", sortOrder: 93);
        var seeded = await SeedDetailedBookAsync(collectionId);

        var detail = await _sut.GetDetailAsync(seeded.BookId, TestContext.Current.CancellationToken);

        Assert.NotNull(detail);
        Assert.Equal("Kallocain", detail!.Title);
        Assert.Equal("En roman om framtiden", detail.Subtitle);
        Assert.Equal("Karin Boye", detail.Authors);
        Assert.Equal($"{seeded.Series} #2", detail.Series);
        Assert.Equal(seeded.Publisher, detail.Publisher);
        Assert.Equal("1940", detail.PubDate);
        Assert.Equal(seeded.Format, detail.Format);
        Assert.Equal(seeded.Language, detail.Language);
        Assert.Equal(191, detail.Pages);
        Assert.Equal("9780299133245", detail.Isbn);
        Assert.Equal("Companion detail", detail.Collection);
        Assert.Equal("Signed", detail.Comments);
    }

    /// <summary>A lookup row the library seeded carries the key its translations hang off, so a companion
    /// that has the same words can say them in its own language. One the user typed has no key to carry, and
    /// the name they typed is the only word there is.</summary>
    [Fact]
    public async Task ASeededLookupCarriesItsKey_AndAUserAddedOneCarriesNone()
    {
        int collectionId = await SeedCollectionAsync("Companion keys", sortOrder: 94);
        var seeded = await SeedDetailedBookAsync(
            collectionId, formatKey: "Format_Hardcover", languageKey: "Language_Swedish");
        var typed = await SeedDetailedBookAsync(collectionId, isbn: "9780306406157");

        var withKeys = await _sut.GetDetailAsync(seeded.BookId, TestContext.Current.CancellationToken);
        var withoutKeys = await _sut.GetDetailAsync(typed.BookId, TestContext.Current.CancellationToken);

        Assert.Equal("Format_Hardcover", withKeys!.FormatKey);
        Assert.Equal("Language_Swedish", withKeys.LanguageKey);
        Assert.Equal(seeded.Format, withKeys.Format);

        Assert.Null(withoutKeys!.FormatKey);
        Assert.Null(withoutKeys.LanguageKey);
    }

    [Fact]
    public async Task ASeriesWithoutANumberIsNotFollowedByABareHash()
    {
        var (bookId, seriesName) = await SeedSeriesBookAsync(seriesNumber: null);

        var detail = await _sut.GetDetailAsync(bookId, TestContext.Current.CancellationToken);

        Assert.Equal(seriesName, detail!.Series);
    }

    [Fact]
    public async Task ASeriesNumberIsShownWithTheSeries()
    {
        var (bookId, seriesName) = await SeedSeriesBookAsync(seriesNumber: "2");

        var detail = await _sut.GetDetailAsync(bookId, TestContext.Current.CancellationToken);

        Assert.Equal($"{seriesName} #2", detail!.Series);
    }

    /// <summary>The lookup tables come seeded, so every name a test invents is made unique to avoid the
    /// unique-name constraints.</summary>
    private async Task<(int BookId, string SeriesName)> SeedSeriesBookAsync(string? seriesNumber)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        var series = new Series { Name = $"Companion series {Guid.NewGuid():N}" };
        db.Series.Add(series);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var book = new Book
        {
            Title = "Kallocain",
            SeriesId = series.SeriesId,
            SeriesNumber = seriesNumber,
        };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (book.BookId, series.Name);
    }

    [Fact]
    public async Task DetailNamesOnlyTheImagesTheBookHas()
    {
        int bookId = await SeedBookAsync("Kallocain");
        await _images.SavePrimaryBookImageAsync(bookId, [1, 2], TestContext.Current.CancellationToken);
        await _images.SaveBookImageByTypeAsync(
            bookId, BookImageTypeId.Spine, [3, 4], TestContext.Current.CancellationToken);

        var detail = await _sut.GetDetailAsync(bookId, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { BookImageTypeId.FrontCover, BookImageTypeId.Spine }, detail!.ImageTypeIds);
    }

    [Fact]
    public async Task AnEmptyFieldTravelsAsNothingRatherThanAsAnEmptyLine()
    {
        int bookId = await SeedBookAsync("Kallocain");

        var detail = await _sut.GetDetailAsync(bookId, TestContext.Current.CancellationToken);

        Assert.Null(detail!.Authors);
        Assert.Null(detail.Subtitle);
        Assert.Null(detail.Isbn);
        Assert.Empty(detail.ImageTypeIds);
    }

    [Fact]
    public async Task ABookThatIsGoneHasNoDetail()
    {
        Assert.Null(await _sut.GetDetailAsync(4242, TestContext.Current.CancellationToken));
    }

    /// <summary>The ISBN is a parameter because it is unique in the schema, so a test that seeds two detailed
    /// books has to give them different ones.</summary>
    private async Task<SeededBook> SeedDetailedBookAsync(
        int collectionId,
        string? formatKey = null,
        string? languageKey = null,
        string isbn = "9780299133245")
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        var publisher = new Publisher { Name = $"Companion publisher {Guid.NewGuid():N}" };
        var series = new Series { Name = $"Companion detail series {Guid.NewGuid():N}" };
        var format = new Format { Name = $"Companion format {Guid.NewGuid():N}", ResourceKey = formatKey };
        var language = new Language { Name = $"Companion language {Guid.NewGuid():N}", ResourceKey = languageKey };
        var person = new Person { DisplayName = "Karin Boye", SortName = "Boye, Karin" };
        db.AddRange(publisher, series, format, language, person);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var role = await db.ContributorRoles.SingleAsync(
            r => r.Code == "Author", TestContext.Current.CancellationToken);

        var book = new Book
        {
            Title = "Kallocain",
            Subtitle = "En roman om framtiden",
            CollectionId = collectionId,
            PublisherId = publisher.PublisherId,
            SeriesId = series.SeriesId,
            SeriesNumber = "2",
            FormatId = format.FormatId,
            LanguageId = language.LanguageId,
            Pages = 191,
            PubDate = "1940",
            Isbn = isbn,
            Comments = "Signed",
        };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.BookContributors.Add(new BookContributor
        {
            BookId = book.BookId,
            PersonId = person.PersonId,
            ContributorRoleId = role.ContributorRoleId,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new SeededBook(book.BookId, publisher.Name, series.Name, format.Name, language.Name);
    }

    private sealed record SeededBook(int BookId, string Publisher, string Series, string Format, string Language);
}
