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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Logic.Tests;

/// <summary>
/// Taking a scanned item into the library, against a real temp-file SQLite database: a new ISBN becomes a
/// book queued for cataloguing, a known ISBN gets its photos appended without anything being replaced, and
/// a write that fails leaves nothing behind.
/// </summary>
public sealed class CompanionIntakeServiceTests : IDisposable
{
    private const string Isbn13 = "9780306406157";
    private const string Isbn10 = "0306406152";

    private sealed class RecordingProcessor : IBatchQueueProcessor
    {
        public List<IReadOnlyList<BatchQueueItem>> Enqueued { get; } = [];

        public bool IsPaused => false;

        public Task EnqueueAsync(IReadOnlyList<BatchQueueItem> items, bool priority = false)
        {
            Enqueued.Add(items);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BatchQueueItem>> ReloadPendingFromDatabaseAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BatchQueueItem>>([]);

        public Task CancelBatchAsync() => Task.CompletedTask;
        public Task PauseAsync() => Task.CompletedTask;
        public void Resume() { }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private readonly string _dbPath;
    private readonly TestBookDbContextFactory _factory;
    private readonly RecordingProcessor _processor = new();
    private readonly CompanionIntakeService _sut;

    public CompanionIntakeServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_companion_intake_{Guid.NewGuid():N}.db");
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
        _sut = new CompanionIntakeService(
            _factory,
            new BookMetadataService(_factory),
            new BatchQueueService(_factory),
            _processor,
            NullLogger<CompanionIntakeService>.Instance);
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

    private static CompanionScanImage Image(int typeId, params byte[] bytes) => new(typeId, bytes);

    private Task<CompanionIntakeResult> IntakeAsync(string isbn, params CompanionScanImage[] images)
        => _sut.IntakeAsync(new CompanionScanItem("item-1", isbn, images), TestContext.Current.CancellationToken);

    private async Task<int> SeedBookAsync(string title, string isbn)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var book = new Book { Title = title, Isbn = isbn };
        db.Books.Add(book);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return book.BookId;
    }

    private async Task<List<BookImage>> ImagesOfAsync(int bookId)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await db.BookImages
            .Where(i => i.BookId == bookId)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Book?> BookAsync(int bookId)
    {
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await db.Books.FirstOrDefaultAsync(b => b.BookId == bookId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnUnknownIsbnBecomesABookQueuedForCataloguing()
    {
        var result = await IntakeAsync(Isbn13, Image(BookImageTypeId.FrontCover, 1, 2, 3));

        Assert.Equal(CompanionIntakeOutcome.Created, result.Outcome);
        Assert.NotNull(result.BookId);
        var book = await BookAsync(result.BookId!.Value);
        Assert.Equal(Isbn13, book!.Isbn);
        Assert.Equal(Isbn13, book.Title);
    }

    [Fact]
    public async Task TheQueuedItemPointsAtTheBookItCameFrom()
    {
        var result = await IntakeAsync(Isbn13);

        var queued = Assert.Single(Assert.Single(_processor.Enqueued));
        Assert.Equal(Isbn13, queued.Isbn);
        Assert.Equal(result.BookId, queued.BookId);
        Assert.Equal(queued.BatchQueueItemId, result.BatchQueueItemId);
    }

    [Fact]
    public async Task ABookCanBeTakenInFromItsIsbnAlone()
    {
        var result = await IntakeAsync(Isbn13);

        Assert.Equal(CompanionIntakeOutcome.Created, result.Outcome);
        Assert.Empty(await ImagesOfAsync(result.BookId!.Value));
    }

    [Fact]
    public async Task EveryScannedPhotoIsStoredUnderItsOwnType()
    {
        var result = await IntakeAsync(
            Isbn13,
            Image(BookImageTypeId.FrontCover, 1),
            Image(BookImageTypeId.BackCover, 2),
            Image(BookImageTypeId.Spine, 3),
            Image(BookImageTypeId.DustJacket, 4));

        var images = await ImagesOfAsync(result.BookId!.Value);
        Assert.Equal(
            [BookImageTypeId.FrontCover, BookImageTypeId.BackCover, BookImageTypeId.Spine, BookImageTypeId.DustJacket],
            images.Select(i => i.BookImageTypeId));
        Assert.Equal([1, 2, 3, 4], images.Select(i => i.ImageData[0]));
    }

    [Fact]
    public async Task TheScannedFrontCoverBecomesTheNewBooksPrimaryImage()
    {
        var result = await IntakeAsync(Isbn13, Image(BookImageTypeId.BackCover, 2), Image(BookImageTypeId.FrontCover, 1));

        var primary = Assert.Single(await ImagesOfAsync(result.BookId!.Value), i => i.IsPrimary);
        Assert.Equal(BookImageTypeId.FrontCover, primary.BookImageTypeId);
    }

    [Fact]
    public async Task AnIsbnTheLibraryAlreadyHasGetsThePhotosRatherThanASecondBook()
    {
        int existing = await SeedBookAsync("Concrete Mathematics", Isbn13);

        var result = await IntakeAsync(Isbn13, Image(BookImageTypeId.FrontCover, 9));

        Assert.Equal(CompanionIntakeOutcome.AddedToExisting, result.Outcome);
        Assert.Equal(existing, result.BookId);
        Assert.Equal(new byte[] { 9 }, Assert.Single(await ImagesOfAsync(existing)).ImageData);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, await db.Books.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AttachingPhotosDoesNotQueueTheBookAgain()
    {
        await SeedBookAsync("Concrete Mathematics", Isbn13);

        await IntakeAsync(Isbn13, Image(BookImageTypeId.FrontCover, 9));

        Assert.Empty(_processor.Enqueued);
    }

    [Fact]
    public async Task AnExistingCoverIsKeptAndTheScannedOneAddedBesideIt()
    {
        int existing = await SeedBookAsync("Concrete Mathematics", Isbn13);
        await using (var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            db.BookImages.Add(new BookImage
            {
                BookId = existing,
                ImageData = [7],
                BookImageTypeId = BookImageTypeId.FrontCover,
                IsPrimary = true,
                DisplayOrder = 0,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await IntakeAsync(Isbn13, Image(BookImageTypeId.FrontCover, 8));

        var images = await ImagesOfAsync(existing);
        Assert.Equal(2, images.Count);
        Assert.Equal([7, 8], images.Select(i => i.ImageData[0]));
        // The user's own cover stays primary; the scan lands beside it.
        Assert.True(images[0].IsPrimary);
        Assert.False(images[1].IsPrimary);
    }

    [Fact]
    public async Task AScannedCoverBecomesPrimaryWhenTheBookHadNoneAtAll()
    {
        int existing = await SeedBookAsync("Concrete Mathematics", Isbn13);

        await IntakeAsync(Isbn13, Image(BookImageTypeId.FrontCover, 8));

        Assert.True(Assert.Single(await ImagesOfAsync(existing)).IsPrimary);
    }

    [Fact]
    public async Task AnIsbnOnlyScanOfAnOwnedBookChangesNothing()
    {
        int existing = await SeedBookAsync("Concrete Mathematics", Isbn13);

        var result = await IntakeAsync(Isbn13);

        Assert.Equal(CompanionIntakeOutcome.AlreadyOwned, result.Outcome);
        Assert.Equal(existing, result.BookId);
        Assert.Empty(await ImagesOfAsync(existing));
        Assert.Empty(_processor.Enqueued);
    }

    [Fact]
    public async Task AnIsbn10ScanFindsTheBookStoredUnderItsIsbn13()
    {
        int existing = await SeedBookAsync("Concrete Mathematics", Isbn13);

        var result = await IntakeAsync(Isbn10, Image(BookImageTypeId.FrontCover, 5));

        Assert.Equal(CompanionIntakeOutcome.AddedToExisting, result.Outcome);
        Assert.Equal(existing, result.BookId);
    }

    [Fact]
    public async Task AHyphenatedIsbnIsStoredNormalized()
    {
        var result = await IntakeAsync("978-0-306-40615-7");

        Assert.Equal(Isbn13, (await BookAsync(result.BookId!.Value))!.Isbn);
    }

    [Fact]
    public async Task AnItemWithNoIsbnIsRefusedWithoutCreatingAnything()
    {
        var result = await IntakeAsync("   ", Image(BookImageTypeId.FrontCover, 1));

        Assert.Equal(CompanionIntakeOutcome.Failed, result.Outcome);
        Assert.Equal(CompanionIntakeFailure.InvalidIsbn, result.Failure);
        Assert.Null(result.BookId);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await db.Books.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AWrongCheckDigitIsStillTakenInBecauseMisprintedIsbnsExist()
    {
        var result = await IntakeAsync("9780306406158");

        Assert.Equal(CompanionIntakeOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task AnItemThatFailsToStoreLeavesNoBookAndNoPhotosBehind()
    {
        var result = await _sut.IntakeAsync(
            new CompanionScanItem(
                "item-1",
                Isbn13,
                [Image(BookImageTypeId.FrontCover, 1), new CompanionScanImage(BookImageTypeId.BackCover, null!)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(CompanionIntakeOutcome.Failed, result.Outcome);
        Assert.Equal(CompanionIntakeFailure.SaveFailed, result.Failure);

        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await db.Books.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, await db.BookImages.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_processor.Enqueued);
    }

    [Fact]
    public async Task AFailedAttachLeavesTheExistingBooksPhotosUntouched()
    {
        int existing = await SeedBookAsync("Concrete Mathematics", Isbn13);
        await IntakeAsync(Isbn13, Image(BookImageTypeId.FrontCover, 4));

        var result = await _sut.IntakeAsync(
            new CompanionScanItem("item-2", Isbn13, [new CompanionScanImage(BookImageTypeId.Spine, null!)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(CompanionIntakeFailure.SaveFailed, result.Failure);
        Assert.Equal(new byte[] { 4 }, Assert.Single(await ImagesOfAsync(existing)).ImageData);
    }
}
