using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.MetadataSources.Services;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// The scanned-ISBN preview against a real temp-file SQLite library. The library is always asked fresh —
/// a book added between two scans has to show up — while the rate-limited metadata sources are asked once.
/// </summary>
public sealed class CompanionPreviewServiceTests : IDisposable
{
    private const string Isbn13 = "9780306406157";
    private const string Isbn10 = "0306406152";

    private sealed class RecordingLookup : IMetadataLookupService
    {
        private readonly Func<string, MetadataLookupResult> _answer;

        public RecordingLookup(Func<string, MetadataLookupResult> answer) => _answer = answer;

        public List<string> Calls { get; } = [];

        public Task<MetadataLookupResult> FetchAllSourcesAsync(string isbn, CancellationToken ct = default)
        {
            Calls.Add(isbn);
            return Task.FromResult(_answer(isbn));
        }
    }

    private sealed class HangingLookup : IMetadataLookupService
    {
        public async Task<MetadataLookupResult> FetchAllSourcesAsync(string isbn, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static MetadataLookupResult Found(string title, params string[] authors)
        => new([new BookMetadata(title, null, authors, null, null, null, null, null, null, null, null, null, "Test")], 1, 0);

    private static MetadataLookupResult NothingFound() => new([], 1, 0);

    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly BookMetadataService _metadata;
    private readonly BookService _books;

    public CompanionPreviewServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_companion_preview_{Guid.NewGuid():N}.db");
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
        _metadata = new BookMetadataService(_factory);
        _books = new BookService(_factory);
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

    private CompanionPreviewService Sut(IMetadataLookupService lookup)
        => new(_metadata, _books, lookup);

    private async Task<int> SeedBookAsync(string title, string isbn)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = title, Isbn = isbn };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return book.BookId;
    }

    [Fact]
    public async Task AnOwnedIsbnAnswersFromTheLibraryWithoutAskingAnySource()
    {
        int bookId = await SeedBookAsync("Concrete Mathematics", Isbn13);
        var lookup = new RecordingLookup(_ => Found("should not be asked"));

        var preview = await Sut(lookup).PreviewIsbnAsync(Isbn13, TestContext.Current.CancellationToken);

        Assert.True(preview.InLibrary);
        Assert.Equal(bookId, preview.BookId);
        Assert.Equal("Concrete Mathematics", preview.Title);
        Assert.Equal(IsbnPreviewOrigin.Library, preview.Origin);
        Assert.Empty(lookup.Calls);
    }

    [Fact]
    public async Task AnOwnedBookIsRecognisedThroughAnIsbn10Spelling()
    {
        await SeedBookAsync("Concrete Mathematics", Isbn13);

        var preview = await Sut(new RecordingLookup(_ => NothingFound()))
            .PreviewIsbnAsync(Isbn10, TestContext.Current.CancellationToken);

        Assert.True(preview.InLibrary);
    }

    [Fact]
    public async Task AnUnownedIsbnFallsBackToTheMetadataSources()
    {
        var lookup = new RecordingLookup(_ => Found("Kallocain", "Karin Boye"));

        var preview = await Sut(lookup).PreviewIsbnAsync(Isbn13, TestContext.Current.CancellationToken);

        Assert.False(preview.InLibrary);
        Assert.Null(preview.BookId);
        Assert.Equal("Kallocain", preview.Title);
        Assert.Equal("Karin Boye", preview.Authors);
        Assert.Equal(IsbnPreviewOrigin.Lookup, preview.Origin);
    }

    [Fact]
    public async Task AnIsbnNobodyKnowsIsReportedAsUnknownRatherThanFailing()
    {
        var preview = await Sut(new RecordingLookup(_ => NothingFound()))
            .PreviewIsbnAsync(Isbn13, TestContext.Current.CancellationToken);

        Assert.False(preview.InLibrary);
        Assert.Null(preview.Title);
        Assert.Equal(IsbnPreviewOrigin.None, preview.Origin);
    }

    [Fact]
    public async Task RepeatedScansOfTheSameIsbnAskTheSourcesOnlyOnce()
    {
        var lookup = new RecordingLookup(_ => Found("Kallocain"));
        var sut = Sut(lookup);

        await sut.PreviewIsbnAsync(Isbn13, TestContext.Current.CancellationToken);
        var second = await sut.PreviewIsbnAsync(Isbn13, TestContext.Current.CancellationToken);

        Assert.Equal("Kallocain", second.Title);
        Assert.Single(lookup.Calls);
    }

    [Fact]
    public async Task DifferentSpellingsOfTheSameIsbnShareTheOneLookup()
    {
        var lookup = new RecordingLookup(_ => Found("Kallocain"));
        var sut = Sut(lookup);

        await sut.PreviewIsbnAsync("978-0-306-40615-7", TestContext.Current.CancellationToken);
        await sut.PreviewIsbnAsync(Isbn13, TestContext.Current.CancellationToken);

        Assert.Single(lookup.Calls);
    }

    [Fact]
    public async Task ABookAddedAfterAScanIsSeenByTheNextOne()
    {
        var lookup = new RecordingLookup(_ => Found("Concrete Mathematics"));
        var sut = Sut(lookup);
        Assert.False((await sut.PreviewIsbnAsync(Isbn13, TestContext.Current.CancellationToken)).InLibrary);

        await SeedBookAsync("Concrete Mathematics", Isbn13);

        // The library answer must never come from the cache; only the source lookup is remembered.
        Assert.True((await sut.PreviewIsbnAsync(Isbn13, TestContext.Current.CancellationToken)).InLibrary);
    }

    [Fact]
    public async Task AnEmptyIsbnIsAnsweredWithoutTouchingAnything()
    {
        var lookup = new RecordingLookup(_ => Found("should not be asked"));

        var preview = await Sut(lookup).PreviewIsbnAsync("   ", TestContext.Current.CancellationToken);

        Assert.Equal(IsbnPreviewOrigin.None, preview.Origin);
        Assert.Empty(lookup.Calls);
    }

    [Fact]
    public async Task ACallerWhoGivesUpIsNotLeftWaitingOnTheSources()
    {
        using var caller = new CancellationTokenSource();
        var pending = Sut(new HangingLookup()).PreviewIsbnAsync(Isbn13, caller.Token);

        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }
}
