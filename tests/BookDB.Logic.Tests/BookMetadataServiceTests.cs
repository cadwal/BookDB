using System;
using System.IO;
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
/// Integration tests for BookMetadataService.FindBookByIsbnAsync using the temp-file SQLite +
/// DbUp migration fixture. Covers ISBN normalisation and ISBN-10 ↔ ISBN-13 variant matching.
/// </summary>
public sealed class BookMetadataServiceTests : IDisposable
{
    // Canonical equivalent pair: ISBN-13 9780306406157 == ISBN-10 0306406152.
    private const string Isbn13 = "9780306406157";
    private const string Isbn10 = "0306406152";

    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookMetadataService _sut;

    public BookMetadataServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_metadata_test_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={_dbPath}";

        var upgrader = SqliteExtensions.SqliteDatabase(DeployChanges.To, connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(BookDbContext))!,
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
        _sut = new BookMetadataService(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private async Task SeedBookAsync(string title, string isbn)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.Books.Add(new Book { Title = title, Isbn = isbn });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FindByIsbn_ExactIsbn13_ReturnsBook()
    {
        await SeedBookAsync("Concrete Mathematics", Isbn13);

        var book = await _sut.FindBookByIsbnAsync(Isbn13, TestContext.Current.CancellationToken);

        Assert.NotNull(book);
        Assert.Equal("Concrete Mathematics", book!.Title);
    }

    [Fact]
    public async Task FindByIsbn_Isbn10Query_MatchesIsbn13Stored()
    {
        // Stored as ISBN-13, queried with the equivalent ISBN-10 — exercises the conversion path.
        await SeedBookAsync("Concrete Mathematics", Isbn13);

        var book = await _sut.FindBookByIsbnAsync(Isbn10, TestContext.Current.CancellationToken);

        Assert.NotNull(book);
        Assert.Equal("Concrete Mathematics", book!.Title);
    }

    [Fact]
    public async Task FindByIsbn_HyphenatedQuery_IsNormalised()
    {
        await SeedBookAsync("Concrete Mathematics", Isbn13);

        var book = await _sut.FindBookByIsbnAsync("978-0-306-40615-7", TestContext.Current.CancellationToken);

        Assert.NotNull(book);
        Assert.Equal("Concrete Mathematics", book!.Title);
    }

    [Fact]
    public async Task FindByIsbn_NoMatch_ReturnsNull()
    {
        await SeedBookAsync("Concrete Mathematics", Isbn13);

        var book = await _sut.FindBookByIsbnAsync("9781234567897", TestContext.Current.CancellationToken);

        Assert.Null(book);
    }

    [Fact]
    public async Task FindByIsbn_BlankInput_ReturnsNull()
    {
        await SeedBookAsync("Concrete Mathematics", Isbn13);

        var book = await _sut.FindBookByIsbnAsync("   ", TestContext.Current.CancellationToken);

        Assert.Null(book);
    }
}
