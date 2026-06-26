using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

/// <summary>
/// Integration tests for BookService.GetBooksAsync using a real temp-file SQLite
/// database. FTS5 requires a file-based SQLite, not in-memory.
/// </summary>
public sealed class BookServicePaginationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookService _sut;

    public BookServicePaginationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_pag_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, _connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDB.Data.Sqlite.SqliteDbUpRunner))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(_connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        _factory = new TestBookDbContextFactory(options);
        _sut = new BookService(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private async Task<List<int>> SeedBooksAsync(int count, int? collectionId = null, int? formatId = null)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var ids = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            var book = new Book
            {
                Title = $"Book {i:D4}",
                CollectionId = collectionId,
                FormatId = formatId
            };
            db.Books.Add(book);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            ids.Add(book.BookId);
        }
        return ids;
    }

    private async Task<int> SeedAuthorAsync(string name)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var sortName = parts.Length > 1
            ? $"{parts[^1]}, {string.Join(" ", parts[..^1])}"
            : name;
        var person = new Person { DisplayName = name, SortName = sortName };
        db.People.Add(person);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return person.PersonId;
    }

    private async Task<int> SeedBookWithAuthorAsync(string title, int personId, int? collectionId = null)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var authorRole = await db.ContributorRoles.FirstAsync(r => r.Code == "Author", TestContext.Current.CancellationToken);
        var book = new Book { Title = title, CollectionId = collectionId };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.BookContributors.Add(new BookContributor
        {
            BookId = book.BookId,
            PersonId = personId,
            ContributorRoleId = authorRole.ContributorRoleId
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return book.BookId;
    }

    private async Task<int> SeedBookWithCategoryAsync(string title, int categoryId)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = title };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.BookCategories.Add(new BookCategory { BookId = book.BookId, CategoryId = categoryId });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return book.BookId;
    }

    private async Task<int> SeedCategoryAsync(string name)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var category = new Category { Name = name, SortOrder = 999 };
        db.Categories.Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return category.CategoryId;
    }

    private async Task<int> SeedFormatAsync(string name)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var format = new Format { Name = name };
        db.Formats.Add(format);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return format.FormatId;
    }

    private async Task<int> SeedCollectionAsync(string name)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var collection = new Collection { Name = name, SortOrder = 99 };
        db.Collections.Add(collection);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return collection.CollectionId;
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetBooksAsync_ReturnsPageOfBooks()
    {
        await SeedBooksAsync(150);

        var (books, filteredTotal, _) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: null,
            sortColumn: "Title",
            sortAscending: true,
            skip: 0,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(100, books.Count);
        Assert.Equal(150, filteredTotal);
    }

    [Fact]
    public async Task GetBooksAsync_SecondPage()
    {
        await SeedBooksAsync(150);

        var (books, _, _) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: null,
            sortColumn: "Title",
            sortAscending: true,
            skip: 100,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(50, books.Count);
    }

    [Fact]
    public async Task GetBooksAsync_CollectionFilter()
    {
        var col1 = await SeedCollectionAsync("CollectionA");
        var col2 = await SeedCollectionAsync("CollectionB");
        await SeedBooksAsync(10, collectionId: col1);
        await SeedBooksAsync(5, collectionId: col2);

        var (books, filteredTotal, _) = await _sut.GetBooksAsync(
            collectionIds: new HashSet<int> { col1 },
            searchBookIds: null,
            facetFilters: null,
            sortColumn: null,
            sortAscending: true,
            skip: 0,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(10, filteredTotal);
        Assert.Equal(10, books.Count);
        Assert.All(books, b => Assert.DoesNotContain(b.Title, books
            .Where(x => x.BookId != b.BookId)
            .Select(x => x.Title)));
    }

    [Fact]
    public async Task GetBooksAsync_FacetFilter_Format()
    {
        var formatA = await SeedFormatAsync("FormatPagA");
        var formatB = await SeedFormatAsync("FormatPagB");
        await SeedBooksAsync(7, formatId: formatA);
        await SeedBooksAsync(3, formatId: formatB);

        var (books, filteredTotal, grandTotal) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: new Dictionary<string, HashSet<int>>
            {
                ["Format"] = [formatA]
            },
            sortColumn: null,
            sortAscending: true,
            skip: 0,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(7, filteredTotal);
        Assert.Equal(7, books.Count);
        Assert.All(books, b => Assert.Equal(formatA, b.FormatId));
    }

    [Fact]
    public async Task GetBooksAsync_FacetFilter_Author()
    {
        var authorAId = await SeedAuthorAsync("Author Alpha");
        var authorBId = await SeedAuthorAsync("Author Beta");
        await SeedBookWithAuthorAsync("Book by Alpha 1", authorAId);
        await SeedBookWithAuthorAsync("Book by Alpha 2", authorAId);
        await SeedBookWithAuthorAsync("Book by Beta 1", authorBId);

        // Critical: validates EF Core translates Contributors.Any(c => c.ContributorRole.Code == "Author" && ids.Contains(c.PersonId)) to SQL
        var (books, filteredTotal, _) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: new Dictionary<string, HashSet<int>>
            {
                ["Author"] = [authorAId]
            },
            sortColumn: null,
            sortAscending: true,
            skip: 0,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, filteredTotal);
        Assert.Equal(2, books.Count);
        Assert.All(books, b => Assert.Contains(authorAId, b.AuthorPersonIds));
    }

    [Fact]
    public async Task GetBooksAsync_FacetFilter_Category()
    {
        var catA = await SeedCategoryAsync("CategoryPagA");
        var catB = await SeedCategoryAsync("CategoryPagB");
        await SeedBookWithCategoryAsync("CatA Book 1", catA);
        await SeedBookWithCategoryAsync("CatA Book 2", catA);
        await SeedBookWithCategoryAsync("CatB Book 1", catB);

        var (books, filteredTotal, _) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: new Dictionary<string, HashSet<int>>
            {
                ["Category"] = [catA]
            },
            sortColumn: null,
            sortAscending: true,
            skip: 0,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, filteredTotal);
        Assert.Equal(2, books.Count);
        Assert.All(books, b => Assert.Contains(catA, b.CategoryIds));
    }

    [Fact]
    public async Task GetBooksAsync_SearchBookIds()
    {
        var ids = await SeedBooksAsync(50);
        var searchIds = ids.Take(10).ToList();

        var (books, filteredTotal, _) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: searchIds,
            facetFilters: null,
            sortColumn: null,
            sortAscending: true,
            skip: 0,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(10, filteredTotal);
        Assert.Equal(10, books.Count);
        Assert.All(books, b => Assert.Contains(b.BookId, searchIds));
    }

    [Fact]
    public async Task GetBooksAsync_SortByTitle()
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Books.Add(new Book { Title = "Zebra" });
        db.Books.Add(new Book { Title = "Apple" });
        db.Books.Add(new Book { Title = "Mango" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (books, _, _) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: null,
            sortColumn: "Title",
            sortAscending: true,
            skip: 0,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        var titles = books.Select(b => b.Title).ToList();
        var sortedTitles = titles.OrderBy(t => t).ToList();
        Assert.Equal(sortedTitles, titles);
    }

    [Fact]
    public async Task GetBooksAsync_GrandTotal_ExcludesFilters()
    {
        var formatA = await SeedFormatAsync("FormatGrand");
        await SeedBooksAsync(40, formatId: null);    // 40 books without this format
        await SeedBooksAsync(10, formatId: formatA); // 10 books with format

        var (_, filteredTotal, grandTotal) = await _sut.GetBooksAsync(
            collectionIds: null,
            searchBookIds: null,
            facetFilters: new Dictionary<string, HashSet<int>>
            {
                ["Format"] = [formatA]
            },
            sortColumn: null,
            sortAscending: true,
            skip: 0,
            take: 100,
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(10, filteredTotal);
        Assert.Equal(50, grandTotal);
        Assert.NotEqual(filteredTotal, grandTotal);
    }
}
