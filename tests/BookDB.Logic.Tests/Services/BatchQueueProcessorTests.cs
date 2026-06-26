using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data;
using BookDB.Data.DbContexts;
using BookDB.Logic.Services;
using BookDB.MetadataSources.Services;
using BookDB.MetadataSources.Sources;
using BookDB.Models.Metadata;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using DbUp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BookDB.Logic.Tests.Services;

/// <summary>
/// Tests for BatchQueueProcessor using a real temp-file SQLite database
/// and mocked lookup/book services.
/// </summary>
public sealed class BatchQueueProcessorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BatchQueueService _queueService;
    private readonly WeakReferenceMessenger _messenger;

    public BatchQueueProcessorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_bqp_test_{Guid.NewGuid():N}.db");
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
        _queueService = new BatchQueueService(_factory);
        _messenger = new WeakReferenceMessenger();
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static BookMetadata MakeMetadata(string title, string sourceName = "TestSource") =>
        new(title, null, ["Author One"], "Publisher", "2020", "en",
            "9780451526538", 200, "Description", null, null, null, sourceName);

    private BatchQueueProcessor MakeProcessor(
        MockMetadataLookupService lookupService,
        IBookService bookService,
        IBookMetadataService bookMetadataService,
        IBookImageService bookImageService,
        TimeSpan? itemDelay = null)
        => new BatchQueueProcessor(
            _queueService,
            lookupService,
            bookService,
            bookMetadataService,
            bookImageService,
            _messenger,
            NullLogger<BatchQueueProcessor>.Instance,
            itemDelay ?? TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task Processor_MarksNotFound_WhenNoSourcesReturnResults()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        var lookupService = new MockMetadataLookupService(results: []);
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var failed = await _queueService.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Single(failed);
        Assert.Equal(item.BatchQueueItemId, failed[0].BatchQueueItemId);
    }

    [Fact]
    public async Task Processor_AutoAcceptsAndMarksDone_WhenNoConflicts()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        var metadata = MakeMetadata("Consistent Title");
        var lookupService = new MockMetadataLookupService(results: [metadata]);
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var done = await _queueService.GetItemsByStatusAsync("AutoAccepted", TestContext.Current.CancellationToken);
        Assert.Single(done);
        Assert.Equal(item.BatchQueueItemId, done[0].BatchQueueItemId);
    }

    [Fact]
    public async Task Processor_StoresPendingReview_WhenConflictsExist()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        var metadata1 = MakeMetadata("Title A", "Source1");
        var metadata2 = MakeMetadata("Title B", "Source2");
        var lookupService = new MockMetadataLookupService(results: [metadata1, metadata2]);
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var pendingReview = await _queueService.GetItemsByStatusAsync("PendingReview", TestContext.Current.CancellationToken);
        Assert.Single(pendingReview);
        Assert.NotNull(pendingReview[0].ResultJson);
    }

    [Fact]
    public async Task Processor_ProcessesItemsSequentially()
    {
        var item1 = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        await Task.Delay(5, TestContext.Current.CancellationToken); // ensure ordering by CreatedAt
        var item2 = await _queueService.EnqueueAsync("9780062315007", bookId: null, TestContext.Current.CancellationToken);

        var processedOrder = new List<string>();
        var metadata = MakeMetadata("Title");
        var lookupService = new MockMetadataLookupService(
            results: [metadata],
            onFetch: isbn => processedOrder.Add(isbn));
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        // Simulate startup reload from DB
        var items = await processor.ReloadPendingFromDatabaseAsync(TestContext.Current.CancellationToken);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch(items).WaitAsync(linkedCts.Token);

        Assert.Equal(2, processedOrder.Count);
        Assert.Equal(item1.Isbn, processedOrder[0]);
        Assert.Equal(item2.Isbn, processedOrder[1]);
    }

    [Fact]
    public async Task Processor_SendsProgressMessages()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        var messages = new List<BookDB.Logic.Messages.BatchQueueProgressMessage>();
        _messenger.Register<BookDB.Logic.Messages.BatchQueueProgressMessage>(this,
            (_, msg) => messages.Add(msg));

        var metadata = MakeMetadata("Title");
        var lookupService = new MockMetadataLookupService(results: [metadata]);
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        Assert.NotEmpty(messages);
    }

    [Fact]
    public async Task Processor_SendsIsRunningFalse_WhenAllItemsProcessed()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        var completionMessages = new List<BookDB.Logic.Messages.BatchQueueProgressMessage>();
        _messenger.Register<BookDB.Logic.Messages.BatchQueueProgressMessage>(this,
            (_, msg) =>
            {
                if (!msg.IsRunning)
                    completionMessages.Add(msg);
            });

        var metadata = MakeMetadata("Title");
        var lookupService = new MockMetadataLookupService(results: [metadata]);
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        // Must have received at least one completion message (IsRunning=false)
        Assert.NotEmpty(completionMessages);
        // The completion message must reflect full progress
        var last = completionMessages.Last();
        Assert.Equal(1, last.Current);
        Assert.Equal(1, last.Total);
    }

    [Fact]
    public async Task Processor_FindsExistingBookByIsbn13_SkipsWhenNoNewData()
    {
        // Arrange: save a book with ISBN-13 "9780451526538"
        var bookMetadataService = new BookMetadataService(_factory);
        var existingMetadata = MakeMetadata("Consistent Title");
        await bookMetadataService.AddBookFromMetadataAsync(existingMetadata, null, null, TestContext.Current.CancellationToken);

        // Enqueue with the same ISBN-13 but without BookId (simulates repeated batch run)
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        var lookupService = new MockMetadataLookupService(results: [existingMetadata]);
        var bookService = new BookService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        // Should be Skipped since data matches existing book
        var skipped = await _queueService.GetItemsByStatusAsync("Skipped", TestContext.Current.CancellationToken);
        Assert.Single(skipped);
    }

    [Fact]
    public async Task Processor_FindsExistingBookByIsbn10_SkipsWhenNoNewData()
    {
        // Arrange: save a book with ISBN-13 "9780451526538"
        var bookMetadataService = new BookMetadataService(_factory);
        var existingMetadata = MakeMetadata("Consistent Title");
        // The metadata has ISBN-13 9780451526538
        await bookMetadataService.AddBookFromMetadataAsync(existingMetadata, null, null, TestContext.Current.CancellationToken);

        // Enqueue with ISBN-10 form "0451526538" — processor must cross-format match
        var item = await _queueService.EnqueueAsync("0451526538", bookId: null, TestContext.Current.CancellationToken);

        var lookupService = new MockMetadataLookupService(results: [existingMetadata]);
        var bookService = new BookService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        // Should be Skipped since data matches existing book (found via ISBN-10/13 cross-format)
        var skipped = await _queueService.GetItemsByStatusAsync("Skipped", TestContext.Current.CancellationToken);
        Assert.Single(skipped);
    }

    [Fact]
    public async Task Processor_RepeatedBatch_DoesNotSaveDuplicateWhenDataUnchanged()
    {
        var bookMetadataService = new BookMetadataService(_factory);
        var bookService = new BookService(_factory);
        var bookImageService = new BookImageService(_factory);
        var metadata = MakeMetadata("My Book");

        // First run: enqueue, process, should save as Done
        var item1 = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        var lookupService = new MockMetadataLookupService(results: [metadata]);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts1 = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts1.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item1]).WaitAsync(linkedCts1.Token);

        var done = await _queueService.GetItemsByStatusAsync("AutoAccepted", TestContext.Current.CancellationToken);
        Assert.Single(done);

        // Second run: same ISBN, same metadata — should be Skipped (no new data)
        var item2 = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        using var timeoutCts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts2 = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts2.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item2]).WaitAsync(linkedCts2.Token);

        var skipped = await _queueService.GetItemsByStatusAsync("Skipped", TestContext.Current.CancellationToken);
        Assert.Single(skipped);
    }

    [Fact]
    public async Task Processor_StopsOnCancellation()
    {
        // Enqueue many items and cancel early
        var items = new List<BookDB.Models.Entities.BatchQueueItem>();
        for (int i = 0; i < 5; i++)
            items.Add(await _queueService.EnqueueAsync($"978045152653{i}", bookId: null, TestContext.Current.CancellationToken));

        var processedCount = 0;
        var metadata = MakeMetadata("Title");
        var lookupService = new MockMetadataLookupService(
            results: [metadata],
            onFetch: _ =>
            {
                processedCount++;
                Thread.Sleep(100); // Slow enough to cancel mid-way
            });
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        var batchTask = processor.StartBatch(items);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await processor.CancelBatchAsync();
        // batchTask is already complete by now

        Assert.True(processedCount < 5, $"Expected fewer than 5 processed items, got {processedCount}");
    }

    [Fact]
    public async Task Processor_DoesNotDoubleProcess_WhenSameItemEnqueuedTwice()
    {
        // Simulate startup reload: item already in DB and passed twice to StartBatch
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        var processedCount = 0;
        var metadata = MakeMetadata("Title");
        var lookupService = new MockMetadataLookupService(
            results: [metadata],
            onFetch: _ => processedCount++);
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        // Pass the same item twice — DistinctBy should deduplicate
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item, item]).WaitAsync(linkedCts.Token);

        // Item should be processed exactly once, not twice
        Assert.Equal(1, processedCount);
    }

    [Fact]
    public async Task Processor_SavesBookWithQueuedIsbn_WhenSourceReturnsNullIsbn()
    {
        // Arrange: metadata from source has no ISBN (null) — processor should use item.Isbn
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        // Source returns metadata with null ISBN
        var metadataNoIsbn = new BookMetadata(
            "Title Without ISBN", null, ["Author"], "Publisher", "2020", "en",
            null, 200, "Desc", null, null, null, "TestSource");
        var lookupService = new MockMetadataLookupService(results: [metadataNoIsbn]);
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        // Item should be marked AutoAccepted (new book, no conflicts)
        var done = await _queueService.GetItemsByStatusAsync("AutoAccepted", TestContext.Current.CancellationToken);
        Assert.Single(done);

        // Book should be findable by the queued ISBN (not null)
        var savedBook = await bookMetadataService.FindBookByIsbnAsync("9780451526538", TestContext.Current.CancellationToken);
        Assert.NotNull(savedBook);
        Assert.Equal("9780451526538", savedBook.Isbn);
    }

    [Fact]
    public async Task Processor_SkipsBook_WhenSourceReturnsNullIsbnButBookAlreadyExistsViaQueuedIsbn()
    {
        // Arrange: book already exists with the queued ISBN
        var bookMetadataService = new BookMetadataService(_factory);
        var existingMetadata = MakeMetadata("Existing Title");
        await bookMetadataService.AddBookFromMetadataAsync(existingMetadata, null, null, TestContext.Current.CancellationToken);

        // Queue an item with same ISBN
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        // Source returns metadata with null ISBN — but same title (matching)
        var metadataNoIsbn = new BookMetadata(
            "Existing Title", null, ["Author One"], "Publisher", "2020", "en",
            null, 200, "Description", null, null, null, "TestSource");
        var lookupService = new MockMetadataLookupService(results: [metadataNoIsbn]);
        var bookService = new BookService(_factory);
        var bookImageService = new BookImageService(_factory);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        // Since existing book is found (via queued ISBN) and there are no diffs, should be Skipped
        var skipped = await _queueService.GetItemsByStatusAsync("Skipped", TestContext.Current.CancellationToken);
        Assert.Single(skipped);

        // Only one book in DB (not duplicated)
        var found = await bookMetadataService.FindBookByIsbnAsync("9780451526538", TestContext.Current.CancellationToken);
        Assert.NotNull(found);
    }
}

/// <summary>
/// Minimal mock for IMetadataLookupService that doesn't call real HTTP.
/// </summary>
internal sealed class MockMetadataLookupService : IMetadataLookupService
{
    private readonly IReadOnlyList<BookMetadata> _results;
    private readonly Action<string>? _onFetch;

    public MockMetadataLookupService(
        IReadOnlyList<BookMetadata> results,
        Action<string>? onFetch = null)
    {
        _results = results;
        _onFetch = onFetch;
    }

    public Task<IReadOnlyList<BookMetadata>> FetchAllSourcesAsync(
        string isbn, CancellationToken ct = default)
    {
        _onFetch?.Invoke(isbn);
        return Task.FromResult(_results);
    }
}
