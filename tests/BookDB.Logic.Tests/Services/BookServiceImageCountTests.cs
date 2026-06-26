using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests.Services;

/// <summary>
/// Integration tests verifying that GetBooksAsync and GetBooksForCollectionsAsync
/// correctly detect same-type duplicate images (HasDuplicateImageTypes).
/// Uses a temp-file SQLite database (FTS5 requires a real file).
/// </summary>
public sealed class BookServiceImageCountTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookService _sut;

    public BookServiceImageCountTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_imgcount_test_{Guid.NewGuid():N}.db");
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
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private async Task<int> SeedBookAsync(string title = "Test Book")
    {
        await using var dbContext = _factory.CreateDbContext();
        var book = new Book { Title = title };
        dbContext.Books.Add(book);
        await dbContext.SaveChangesAsync();
        return book.BookId;
    }

    private async Task SeedImageAsync(int bookId, int bookImageTypeId)
    {
        await using var dbContext = _factory.CreateDbContext();
        dbContext.BookImages.Add(new BookImage
        {
            BookId = bookId,
            ImageData = new byte[] { 0xFF, 0xD8, 0xFF },
            MimeType = "image/jpeg",
            IsPrimary = bookImageTypeId == 0,
            DisplayOrder = bookImageTypeId,
            BookImageTypeId = bookImageTypeId,
            Added = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    // ---------------------------------------------------------------------------
    // GetBooksAsync — HasDuplicateImageTypes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetBooksAsync_BookWithSameTypeImages_ReturnsDuplicateImageTypes()
    {
        int bookId = await SeedBookAsync("Same Type Book");
        await SeedImageAsync(bookId, bookImageTypeId: 2); // Back Cover #1
        await SeedImageAsync(bookId, bookImageTypeId: 2); // Back Cover #2 — duplicate type

        var (books, _, _) = await _sut.GetBooksAsync(
            null, null, null, null, true, 0, 100, ct: TestContext.Current.CancellationToken);

        var row = Assert.Single(books, b => b.BookId == bookId);
        Assert.True(row.HasDuplicateImageTypes);
    }

    [Fact]
    public async Task GetBooksAsync_BookWithAllDifferentTypeImages_ReturnsNoDuplicateImageTypes()
    {
        int bookId = await SeedBookAsync("Different Types Book");
        await SeedImageAsync(bookId, bookImageTypeId: 0); // Front Cover
        await SeedImageAsync(bookId, bookImageTypeId: 2); // Back Cover — different type

        var (books, _, _) = await _sut.GetBooksAsync(
            null, null, null, null, true, 0, 100, ct: TestContext.Current.CancellationToken);

        var row = Assert.Single(books, b => b.BookId == bookId);
        Assert.False(row.HasDuplicateImageTypes);
    }

    [Fact]
    public async Task GetBooksAsync_BookWithNoImages_ReturnsNoDuplicateImageTypes()
    {
        int bookId = await SeedBookAsync("No Images Book");

        var (books, _, _) = await _sut.GetBooksAsync(
            null, null, null, null, true, 0, 100, ct: TestContext.Current.CancellationToken);

        var row = Assert.Single(books, b => b.BookId == bookId);
        Assert.False(row.HasDuplicateImageTypes);
    }

    // ---------------------------------------------------------------------------
    // GetBooksForCollectionsAsync — HasDuplicateImageTypes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetBooksForCollectionsAsync_BookWithSameTypeImages_ReturnsDuplicateImageTypes()
    {
        int bookId = await SeedBookAsync("Collections Same Type Book");
        await SeedImageAsync(bookId, bookImageTypeId: 1); // Thumbnail #1
        await SeedImageAsync(bookId, bookImageTypeId: 1); // Thumbnail #2 — duplicate type

        var books = await _sut.GetBooksForCollectionsAsync(
            new HashSet<int>(), TestContext.Current.CancellationToken);

        var row = Assert.Single(books, b => b.BookId == bookId);
        Assert.True(row.HasDuplicateImageTypes);
    }
}
