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
using BookDB.Models.Enums;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Uses a real temp-file SQLite database so FTS5 is available (in-memory SQLite
/// does not support the FTS5 module). DbUp runs all migrations before each test.
/// </summary>
public sealed class BookServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookService _sut;
    private readonly BookSearchService _searchSut;

    public BookServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

        // Run all migrations (V001–V005) via DbUp using same pattern as DatabaseStartupService
        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, _connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDbContext))!,
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
        _searchSut = new BookSearchService(_factory);
    }

    public void Dispose()
    {
        // Give SQLite a chance to release the file
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    // ---------------------------------------------------------------------------
    // CRUD
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task AddBook_SetsBookIdAndTimestamps()
    {
        var book = new Book { Title = "Test" };
        var added = await _sut.AddBookAsync(book, TestContext.Current.CancellationToken);

        Assert.True(added.BookId > 0);
        Assert.NotEqual(default, added.Added);
        Assert.NotEqual(default, added.Updated);
    }

    [Fact]
    public async Task GetBookById_ReturnsWithNavigationProperties()
    {
        // Seed a publisher
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var publisher = new Publisher { Name = "O'Reilly" };
        setup.Publishers.Add(publisher);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var book = new Book { Title = "Nav Test", PublisherId = publisher.PublisherId };
        await _sut.AddBookAsync(book, TestContext.Current.CancellationToken);

        var retrieved = await _sut.GetBookByIdAsync(book.BookId, TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved!.Publisher);
        Assert.Equal("O'Reilly", retrieved.Publisher!.Name);
    }

    [Fact]
    public async Task GetBooksForCollections_FiltersCorrectly()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var c1 = new Collection { Name = "FilterC1", SortOrder = 90 };
        var c2 = new Collection { Name = "FilterC2", SortOrder = 91 };
        setup.Collections.Add(c1);
        setup.Collections.Add(c2);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.AddBookAsync(new Book { Title = "Book A", CollectionId = c1.CollectionId }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "Book B", CollectionId = c2.CollectionId }, TestContext.Current.CancellationToken);

        var rows = await _sut.GetBooksForCollectionsAsync(new HashSet<int> { c1.CollectionId }, TestContext.Current.CancellationToken);

        Assert.Single(rows);
        Assert.Equal("Book A", rows[0].Title);
    }

    [Fact]
    public async Task UpdateBook_PersistsChanges()
    {
        await _sut.AddBookAsync(new Book { Title = "Original" }, TestContext.Current.CancellationToken);

        // Re-load and modify within the same context
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var loaded = await db.Books.FirstAsync(TestContext.Current.CancellationToken);
        loaded.Title = "Updated";
        db.Books.Update(loaded);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reloaded = await _sut.GetBookByIdAsync(loaded.BookId, TestContext.Current.CancellationToken);
        Assert.Equal("Updated", reloaded!.Title);
    }

    [Fact]
    public async Task DeleteBooks_RemovesMultiple()
    {
        var b1 = await _sut.AddBookAsync(new Book { Title = "Del1" }, TestContext.Current.CancellationToken);
        var b2 = await _sut.AddBookAsync(new Book { Title = "Del2" }, TestContext.Current.CancellationToken);
        _ = await _sut.AddBookAsync(new Book { Title = "Keep" }, TestContext.Current.CancellationToken);

        await _sut.DeleteBooksAsync([b1.BookId, b2.BookId], TestContext.Current.CancellationToken);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var remaining = await db.Books.ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(remaining);
        Assert.Equal("Keep", remaining[0].Title);
    }

    [Fact]
    public async Task DuplicateBook_CreatesNewWithFreshId()
    {
        var original = await _sut.AddBookAsync(new Book { Title = "Dup" }, TestContext.Current.CancellationToken);

        var dup = await _sut.DuplicateBookAsync(original.BookId, ct: TestContext.Current.CancellationToken);

        Assert.NotEqual(original.BookId, dup.BookId);
        Assert.Equal("Dup", dup.Title);
        // Fresh timestamps (dup.Added >= original.Added)
        Assert.True(dup.Added >= original.Added);
    }

    // ---------------------------------------------------------------------------
    // FTS5 Search
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FtsSearch_FindsByTitle()
    {
        await _sut.AddBookAsync(new Book { Title = "Hitchhiker" }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "Other Book" }, TestContext.Current.CancellationToken);

        var ids = await _searchSut.SearchBookIdsAsync("Hitchhiker", TestContext.Current.CancellationToken);

        Assert.Single(ids);
    }

    [Fact]
    public async Task FtsSearch_FindsByKeywords()
    {
        await _sut.AddBookAsync(new Book { Title = "Sci-Fi", Keywords = "space travel" }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "Fantasy" }, TestContext.Current.CancellationToken);

        var ids = await _searchSut.SearchBookIdsAsync("space", TestContext.Current.CancellationToken);

        Assert.Single(ids);
    }

    // ---------------------------------------------------------------------------
    // Facet Counts
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FacetCounts_ReturnsByFormat()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var col = new Collection { Name = "FacetCol", SortOrder = 92 };
        setup.Collections.Add(col);
        // Use unique names to avoid collision with V002 seed data
        var hardcover = new Format { Name = "HardcoverTest" };
        var paperback = new Format { Name = "PaperbackTest" };
        setup.Formats.Add(hardcover);
        setup.Formats.Add(paperback);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.AddBookAsync(new Book { Title = "H1", CollectionId = col.CollectionId, FormatId = hardcover.FormatId }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "H2", CollectionId = col.CollectionId, FormatId = hardcover.FormatId }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "P1", CollectionId = col.CollectionId, FormatId = paperback.FormatId }, TestContext.Current.CancellationToken);

        var facets = await _searchSut.GetFacetCountsAsync(new HashSet<int> { col.CollectionId }, "Format", TestContext.Current.CancellationToken);

        Assert.Equal(2, facets.Count);
        var hc = facets.First(f => f.Name == "HardcoverTest");
        var pb = facets.First(f => f.Name == "PaperbackTest");
        Assert.Equal(2, hc.Count);
        Assert.Equal(1, pb.Count);
    }

    // ---------------------------------------------------------------------------
    // Bulk Edit
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task BulkSetStatus_UpdatesMultiple()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var status = new Status { Name = "Read" };
        setup.Statuses.Add(status);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        var b1 = await _sut.AddBookAsync(new Book { Title = "Bulk1" }, TestContext.Current.CancellationToken);
        var b2 = await _sut.AddBookAsync(new Book { Title = "Bulk2" }, TestContext.Current.CancellationToken);
        var b3 = await _sut.AddBookAsync(new Book { Title = "Bulk3" }, TestContext.Current.CancellationToken);

        await _sut.BulkSetStatusAsync([b1.BookId, b2.BookId], status.StatusId, TestContext.Current.CancellationToken);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var updated = await db.Books.Where(b => b.StatusId == status.StatusId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, updated.Count);
        Assert.DoesNotContain(updated, b => b.BookId == b3.BookId);
    }

    // ---------------------------------------------------------------------------
    // SavedSearch
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SavedSearch_PersistAndRetrieve()
    {
        var search = new SavedSearch
        {
            Name = "My Search",
            QueryJson = "{\"facets\":{}}"
        };
        await _sut.AddSavedSearchAsync(search, TestContext.Current.CancellationToken);

        var all = await _sut.GetSavedSearchesAsync(TestContext.Current.CancellationToken);

        Assert.Single(all);
        Assert.Equal("My Search", all[0].Name);
        Assert.Equal("{\"facets\":{}}", all[0].QueryJson);
    }

    // ---------------------------------------------------------------------------
    // SearchByConditions
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchByConditions_TitleContains()
    {
        await _sut.AddBookAsync(new Book { Title = "Dragon Quest" }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "Space Odyssey" }, TestContext.Current.CancellationToken);

        var conditions = new List<SearchCondition>
        {
            new(SearchField.Title, SearchOperator.Contains, "Dragon")
        };

        var ids = await _searchSut.SearchByConditionsAsync(conditions, "AND", TestContext.Current.CancellationToken);

        Assert.Single(ids);
    }

    [Fact]
    public async Task SearchByConditions_OrCombinator()
    {
        var b1 = await _sut.AddBookAsync(new Book { Title = "Alpha Book" }, TestContext.Current.CancellationToken);
        var b2 = await _sut.AddBookAsync(new Book { Title = "Beta Book" }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "Gamma Book" }, TestContext.Current.CancellationToken);

        var conditions = new List<SearchCondition>
        {
            new(SearchField.Title, SearchOperator.Contains, "Alpha"),
            new(SearchField.Title, SearchOperator.Contains, "Beta")
        };

        var ids = await _searchSut.SearchByConditionsAsync(conditions, "OR", TestContext.Current.CancellationToken);

        Assert.Equal(2, ids.Count);
        Assert.Contains((long)b1.BookId, ids);
        Assert.Contains((long)b2.BookId, ids);
    }

    [Fact]
    public async Task SearchByConditions_NotContains_ExcludesMatchingTitles()
    {
        await _sut.AddBookAsync(new Book { Title = "Dragon Quest" }, TestContext.Current.CancellationToken);
        var keep = await _sut.AddBookAsync(new Book { Title = "Space Odyssey" }, TestContext.Current.CancellationToken);

        var conditions = new List<SearchCondition>
        {
            new(SearchField.Title, SearchOperator.NotContains, "Dragon")
        };

        var ids = await _searchSut.SearchByConditionsAsync(conditions, "AND", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { (long)keep.BookId }, ids);
    }

    [Fact]
    public async Task SearchByConditions_NotEquals_ExcludesExactTitleMatch()
    {
        await _sut.AddBookAsync(new Book { Title = "Alpha" }, TestContext.Current.CancellationToken);
        var keep = await _sut.AddBookAsync(new Book { Title = "Alpha Two" }, TestContext.Current.CancellationToken);

        var conditions = new List<SearchCondition>
        {
            new(SearchField.Title, SearchOperator.NotEquals, "Alpha")
        };

        var ids = await _searchSut.SearchByConditionsAsync(conditions, "AND", TestContext.Current.CancellationToken);

        // "Alpha" is excluded; "Alpha Two" (different value) is kept.
        Assert.Equal(new[] { (long)keep.BookId }, ids);
    }

    [Fact]
    public async Task SearchByConditions_NotContains_ExcludesEmptyAndNullTextFields()
    {
        var keep = await _sut.AddBookAsync(new Book { Title = "A", Comments = "a useful note" }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "B", Comments = "" }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "C", Comments = null }, TestContext.Current.CancellationToken);

        var conditions = new List<SearchCondition>
        {
            new(SearchField.Comments, SearchOperator.NotContains, "xyz")
        };

        var ids = await _searchSut.SearchByConditionsAsync(conditions, "AND", TestContext.Current.CancellationToken);

        // Only the book that actually has comments (not containing "xyz") matches;
        // empty-string and null comment fields are excluded.
        Assert.Equal(new[] { (long)keep.BookId }, ids);
    }

    [Fact]
    public async Task SearchByConditions_NotContains_OnNavigationField_ExcludesBooksWithNoValue()
    {
        await using var setup = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var penguin = new Publisher { Name = "Penguin" };
        var other = new Publisher { Name = "Random House" };
        setup.Publishers.AddRange(penguin, other);
        await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.AddBookAsync(new Book { Title = "Has Penguin", PublisherId = penguin.PublisherId }, TestContext.Current.CancellationToken);
        var otherPub = await _sut.AddBookAsync(new Book { Title = "Has Other", PublisherId = other.PublisherId }, TestContext.Current.CancellationToken);
        await _sut.AddBookAsync(new Book { Title = "No Publisher" }, TestContext.Current.CancellationToken);

        var conditions = new List<SearchCondition>
        {
            new(SearchField.Publisher, SearchOperator.NotContains, "Penguin")
        };

        var ids = await _searchSut.SearchByConditionsAsync(conditions, "AND", TestContext.Current.CancellationToken);

        // Excludes the Penguin book (matches) AND the book with no publisher (empty field):
        // only the book with a different, non-matching publisher remains.
        Assert.Equal(new[] { (long)otherPub.BookId }, ids);
    }

    // -----------------------------------------------------------------------
    // UpdateBookCategoriesAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateBookCategoriesAsync_ReplacesExistingCategories()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed two categories
        await using var setup = await _factory.CreateDbContextAsync(ct);
        var cat1 = new Category { Name = "Fiction" };
        var cat2 = new Category { Name = "Fantasy" };
        setup.Categories.AddRange(cat1, cat2);
        await setup.SaveChangesAsync(ct);

        var book = await _sut.AddBookAsync(new Book { Title = "Test Book" }, ct);

        // Initially assign cat1
        await _sut.UpdateBookCategoriesAsync(book.BookId, [cat1.CategoryId], ct);

        // Now replace with cat2
        await _sut.UpdateBookCategoriesAsync(book.BookId, [cat2.CategoryId], ct);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var cats = await db.BookCategories
            .Where(bc => bc.BookId == book.BookId)
            .ToListAsync(ct);
        Assert.Single(cats);
        Assert.Equal(cat2.CategoryId, cats[0].CategoryId);
    }

    [Fact]
    public async Task UpdateBookCategoriesAsync_EmptyList_ClearsAllCategories()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var setup = await _factory.CreateDbContextAsync(ct);
        var cat = new Category { Name = "SciFi" };
        setup.Categories.Add(cat);
        await setup.SaveChangesAsync(ct);

        var book = await _sut.AddBookAsync(new Book { Title = "SciFi Book" }, ct);
        await _sut.UpdateBookCategoriesAsync(book.BookId, [cat.CategoryId], ct);

        // Clear all categories
        await _sut.UpdateBookCategoriesAsync(book.BookId, [], ct);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var count = await db.BookCategories.CountAsync(bc => bc.BookId == book.BookId, ct);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateBookCategoriesAsync_MultipleCategories_PersistsAll()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var setup = await _factory.CreateDbContextAsync(ct);
        var cat1 = new Category { Name = "Thriller" };
        var cat2 = new Category { Name = "Mystery" };
        var cat3 = new Category { Name = "Crime" };
        setup.Categories.AddRange(cat1, cat2, cat3);
        await setup.SaveChangesAsync(ct);

        var book = await _sut.AddBookAsync(new Book { Title = "Multi-Genre Book" }, ct);

        await _sut.UpdateBookCategoriesAsync(book.BookId, [cat1.CategoryId, cat2.CategoryId, cat3.CategoryId], ct);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var catIds = await db.BookCategories
            .Where(bc => bc.BookId == book.BookId)
            .Select(bc => bc.CategoryId)
            .ToListAsync(ct);
        Assert.Equal(3, catIds.Count);
        Assert.Contains(cat1.CategoryId, catIds);
        Assert.Contains(cat2.CategoryId, catIds);
        Assert.Contains(cat3.CategoryId, catIds);
    }

}
