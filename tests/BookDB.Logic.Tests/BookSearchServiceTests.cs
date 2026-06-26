using System;
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

namespace BookDB.Logic.Tests;

/// <summary>
/// Integration tests for BookSearchService.SearchBookIdsAsync FTS5 query building.
/// Covers prefix matching, multi-word AND semantics, and no-match — behaviours beyond the
/// exact title/keyword matches covered in BookServiceTests. Uses temp-file SQLite because the
/// FTS5 module is unavailable on in-memory SQLite; DbUp builds the schema and FTS triggers.
/// </summary>
public sealed class BookSearchServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookSearchService _sut;

    public BookSearchServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_search_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDB.Data.Sqlite.SqliteDbUpRunner))!,
                name => name.Contains(".Migrations."))
            .LogToNowhere()
            .Build();
        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
            throw new InvalidOperationException($"DbUp migration failed: {result.Error}");

        var options = new DbContextOptionsBuilder<BookDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;
        _factory = new TestBookDbContextFactory(options);
        _sut = new BookSearchService(_factory, new BookDB.Data.Sqlite.SqliteBookSearchProvider(_factory));
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private async Task<int> SeedBookAsync(string title)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = title };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return book.BookId;
    }

    [Fact]
    public async Task SingleToken_PrefixMatches()
    {
        var hitchId = await SeedBookAsync("Hitchhiker");
        await SeedBookAsync("Other Book");

        // "Hitch" is a prefix of "Hitchhiker" — FTS query appends a prefix wildcard.
        var ids = await _sut.SearchBookIdsAsync("Hitch", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { hitchId }, ids);
    }

    [Fact]
    public async Task MultiWord_RequiresAllTokens()
    {
        var match = await SeedBookAsync("Deep Space Nine");
        await SeedBookAsync("Deep Ocean");

        // Both tokens are ANDed: only the book matching every token prefix is returned.
        var ids = await _sut.SearchBookIdsAsync("deep space", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { match }, ids);
    }

    [Fact]
    public async Task NoMatch_ReturnsEmpty()
    {
        await SeedBookAsync("Hitchhiker");

        var ids = await _sut.SearchBookIdsAsync("nonexistent", TestContext.Current.CancellationToken);

        Assert.Empty(ids);
    }
}
