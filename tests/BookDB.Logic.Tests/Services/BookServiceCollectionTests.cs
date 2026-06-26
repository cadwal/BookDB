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
/// Integration tests for BookService.BulkSetCollectionAsync.
/// Uses file-based SQLite so DbUp migrations (including FTS5) apply correctly.
/// </summary>
public sealed class BookServiceCollectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookService _sut;

    public BookServiceCollectionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_coll_test_{Guid.NewGuid():N}.db");
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

    private async Task<int> SeedCollectionAsync(string name)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var collection = new Collection { Name = name, SortOrder = 0 };
        db.Collections.Add(collection);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return collection.CollectionId;
    }

    private async Task<List<int>> SeedBooksAsync(int count, int? collectionId = null)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var ids = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            var book = new Book { Title = $"Book {i:D4}", CollectionId = collectionId };
            db.Books.Add(book);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            ids.Add(book.BookId);
        }
        return ids;
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task BulkSetCollectionAsync_SetsCollectionIdOnAllGivenBooks()
    {
        var collectionId = await SeedCollectionAsync("MyCollection");
        var bookIds = await SeedBooksAsync(2);

        await _sut.BulkSetCollectionAsync(bookIds, collectionId, TestContext.Current.CancellationToken);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var books = await db.Books.Where(b => bookIds.Contains(b.BookId)).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, books.Count);
        Assert.All(books, b => Assert.Equal(collectionId, b.CollectionId));
    }

    [Fact]
    public async Task BulkSetCollectionAsync_WithEmptyList_DoesNotThrow()
    {
        await _sut.BulkSetCollectionAsync([], 1, TestContext.Current.CancellationToken);
    }
}
