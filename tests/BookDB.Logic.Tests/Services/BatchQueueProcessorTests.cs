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
using BookDB.Logic.Messages;
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
        TimeSpan? itemDelay = null,
        ICoverFetcher? coverFetcher = null,
        TimeSpan? itemTimeout = null,
        ICoverCache? coverCache = null)
        => new BatchQueueProcessor(
            _queueService,
            lookupService,
            bookService,
            bookMetadataService,
            bookImageService,
            _messenger,
            NullLogger<BatchQueueProcessor>.Instance,
            itemDelay ?? TimeSpan.FromMilliseconds(10),
            coverFetcher,
            itemTimeout,
            coverCache);

    [Fact]
    public async Task Processor_MarksNotFound_WhenNoSourcesReturnResults()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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
        Assert.Equal(nameof(BatchFailureReason.NoResults), failed[0].FailureCode);
    }

    [Fact]
    public async Task Processor_MarksNetworkError_WhenEveryQueriedSourceFails()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

        var lookupService = new MockMetadataLookupService(results: [], sourcesQueried: 2, sourcesFailed: 2);
        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var failed = await _queueService.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Single(failed);
        Assert.Equal(nameof(BatchFailureReason.NetworkError), failed[0].FailureCode);
    }

    [Fact]
    public async Task Processor_MarksRateLimited_WhenNoResultsAndASourceWasRateLimited()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

        // No source had the book, and one was rate-limited (429) — surface "rate limited, retry later"
        // rather than a generic network error, so the reason is not lost.
        var lookupService = new MockMetadataLookupService(
            results: [], sourcesQueried: 2, sourcesFailed: 2, rateLimitedSources: ["GoogleBooks"]);
        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var failed = await _queueService.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Single(failed);
        Assert.Equal(nameof(BatchFailureReason.RateLimited), failed[0].FailureCode);
    }

    [Fact]
    public async Task Processor_MarksNoResults_WhenSomeSourcesAnswerButNoneMatch()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

        // One source errored, but the other answered "no hit" — that is a real no-result, not a network problem.
        var lookupService = new MockMetadataLookupService(results: [], sourcesQueried: 2, sourcesFailed: 1);
        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var failed = await _queueService.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Single(failed);
        Assert.Equal(nameof(BatchFailureReason.NoResults), failed[0].FailureCode);
    }

    [Fact]
    public async Task Processor_MarksAllSourcesDisabled_WhenNothingWasQueried()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

        var lookupService = new MockMetadataLookupService(results: [], sourcesQueried: 0);
        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var failed = await _queueService.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Single(failed);
        Assert.Equal(nameof(BatchFailureReason.AllSourcesDisabled), failed[0].FailureCode);
    }

    [Fact]
    public async Task Processor_MarksNetworkError_WhenTheLookupHitsThePerItemTimeout()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

        var lookupService = new MockMetadataLookupService(
            results: [MakeMetadata("Never Delivered")], delay: TimeSpan.FromSeconds(30));
        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory),
            itemTimeout: TimeSpan.FromMilliseconds(100));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var failed = await _queueService.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Single(failed);
        Assert.Equal(nameof(BatchFailureReason.NetworkError), failed[0].FailureCode);
    }

    [Fact]
    public async Task Processor_MarksUnexpected_WhenProcessingThrows()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

        var lookupService = new MockMetadataLookupService(
            results: [], throwOnFetch: new InvalidOperationException("boom"));
        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var failed = await _queueService.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Single(failed);
        Assert.Equal(nameof(BatchFailureReason.Unexpected), failed[0].FailureCode);
    }

    [Fact]
    public async Task Processor_ClearsFailureCode_WhenARetrySucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: ct);

        var emptyLookup = new MockMetadataLookupService(results: []);
        var bookService = new BookService(_factory);
        var bookMetadataService = new BookMetadataService(_factory);
        var bookImageService = new BookImageService(_factory);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);

        var failingProcessor = MakeProcessor(emptyLookup, bookService, bookMetadataService, bookImageService);
        await failingProcessor.StartBatch([item]).WaitAsync(linkedCts.Token);
        Assert.NotNull((await _queueService.GetItemsByStatusAsync("Failed", ct))[0].FailureCode);

        var succeedingLookup = new MockMetadataLookupService(results: [MakeMetadata("Found Now")]);
        var retryProcessor = MakeProcessor(succeedingLookup, bookService, bookMetadataService, bookImageService);
        await retryProcessor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var accepted = await _queueService.GetItemsByStatusAsync("AutoAccepted", ct);
        Assert.Single(accepted);
        Assert.Null(accepted[0].FailureCode);
    }

    [Fact]
    public async Task Processor_ReportsFetchingCovers_WhileDownloadingACover()
    {
        var ct = TestContext.Current.CancellationToken;
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: ct);

        var metadata = MakeMetadata("Book With Cover") with { CoverImageUrl = "https://covers.example/1.jpg" };
        var lookupService = new MockMetadataLookupService(results: [metadata]);
        var observedStatusCodes = new List<BatchProgressStatus>();
        _messenger.Register<BatchQueueProgressMessage>(this, (_, m) =>
        {
            lock (observedStatusCodes) observedStatusCodes.Add(m.StatusCode);
        });

        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory),
            coverFetcher: new StubCoverFetcher());

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        lock (observedStatusCodes)
            Assert.Contains(BatchProgressStatus.FetchingCovers, observedStatusCodes);
    }

    [Fact]
    public async Task Processor_AutoAcceptsAndMarksDone_WhenNoConflicts()
    {
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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
    public async Task Processor_StoresPendingReview_WhenForceReviewIsSet_EvenWithoutConflicts()
    {
        var ct = TestContext.Current.CancellationToken;
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, forceReview: true, ct: ct);

        var metadata = MakeMetadata("Consistent Title");
        var lookupService = new MockMetadataLookupService(results: [metadata]);
        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var pendingReview = await _queueService.GetItemsByStatusAsync("PendingReview", ct);
        Assert.Single(pendingReview);
        Assert.Equal(item.BatchQueueItemId, pendingReview[0].BatchQueueItemId);
        Assert.NotNull(pendingReview[0].ResultJson);
        var autoAccepted = await _queueService.GetItemsByStatusAsync("AutoAccepted", ct);
        Assert.Empty(autoAccepted);
    }

    [Fact]
    public async Task Processor_PrefetchesEverySourceCoverIntoTheCache_WhenItemLandsInPendingReview()
    {
        var ct = TestContext.Current.CancellationToken;
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: ct);

        var metadata1 = MakeMetadata("Title A", "Source1") with { CoverImageUrl = "https://covers.example/a.jpg" };
        var metadata2 = MakeMetadata("Title B", "Source2") with { CoverImageUrl = "https://covers.example/b.jpg" };
        var lookupService = new MockMetadataLookupService(results: [metadata1, metadata2]);
        var cache = new BoundedCoverCache();
        var fetcher = new PerUrlCoverFetcher();
        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory),
            coverFetcher: fetcher, coverCache: cache);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        var pendingReview = await _queueService.GetItemsByStatusAsync("PendingReview", ct);
        Assert.Single(pendingReview);
        Assert.Equal(fetcher.BytesFor("https://covers.example/a.jpg"),
            cache.TryGet(item.BatchQueueItemId, "Source1"));
        Assert.Equal(fetcher.BytesFor("https://covers.example/b.jpg"),
            cache.TryGet(item.BatchQueueItemId, "Source2"));
    }

    [Fact]
    public async Task Processor_ReportsFetchingCovers_WhilePrefetchingForReview()
    {
        var ct = TestContext.Current.CancellationToken;
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: ct);

        var metadata1 = MakeMetadata("Title A", "Source1") with { CoverImageUrl = "https://covers.example/a.jpg" };
        var metadata2 = MakeMetadata("Title B", "Source2");
        var lookupService = new MockMetadataLookupService(results: [metadata1, metadata2]);
        var observedStatusCodes = new List<BatchProgressStatus>();
        _messenger.Register<BatchQueueProgressMessage>(this, (_, m) =>
        {
            lock (observedStatusCodes) observedStatusCodes.Add(m.StatusCode);
        });

        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory),
            coverFetcher: new StubCoverFetcher(), coverCache: new BoundedCoverCache());

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        await processor.StartBatch([item]).WaitAsync(linkedCts.Token);

        lock (observedStatusCodes)
            Assert.Contains(BatchProgressStatus.FetchingCovers, observedStatusCodes);
    }

    [Fact]
    public async Task Processor_ProgressMessages_CarryTheRunningToReviewCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var item1 = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: ct);
        await Task.Delay(5, ct); // ensure ordering by CreatedAt
        var item2 = await _queueService.EnqueueAsync("9780062315007", bookId: null, ct: ct);

        // Conflicting titles route every item to PendingReview.
        var metadata1 = MakeMetadata("Title A", "Source1");
        var metadata2 = MakeMetadata("Title B", "Source2");
        var lookupService = new MockMetadataLookupService(results: [metadata1, metadata2]);
        var messages = new List<BatchQueueProgressMessage>();
        _messenger.Register<BatchQueueProgressMessage>(this, (_, m) =>
        {
            lock (messages) messages.Add(m);
        });

        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        await processor.StartBatch([item1, item2]).WaitAsync(linkedCts.Token);

        lock (messages)
        {
            // The count is visible mid-run (after the first item) and complete at the end.
            Assert.Contains(messages, m => m.IsRunning && m.ToReviewCount == 1);
            var final = messages.Last(m => !m.IsRunning);
            Assert.Equal(2, final.ToReviewCount);
            Assert.Equal(0, final.FailedCount);
        }
    }

    [Fact]
    public async Task Processor_ProgressMessages_CarryTheRunningFailedCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var item1 = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: ct);
        await Task.Delay(5, ct);
        var item2 = await _queueService.EnqueueAsync("9780062315007", bookId: null, ct: ct);

        var lookupService = new MockMetadataLookupService(results: []);
        var messages = new List<BatchQueueProgressMessage>();
        _messenger.Register<BatchQueueProgressMessage>(this, (_, m) =>
        {
            lock (messages) messages.Add(m);
        });

        var processor = MakeProcessor(lookupService, new BookService(_factory),
            new BookMetadataService(_factory), new BookImageService(_factory));

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);
        await processor.StartBatch([item1, item2]).WaitAsync(linkedCts.Token);

        lock (messages)
        {
            Assert.Contains(messages, m => m.IsRunning && m.FailedCount == 1);
            var final = messages.Last(m => !m.IsRunning);
            Assert.Equal(2, final.FailedCount);
            Assert.Equal(0, final.ToReviewCount);
        }
    }

    [Fact]
    public async Task Processor_ProcessesItemsSequentially()
    {
        var item1 = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);
        await Task.Delay(5, TestContext.Current.CancellationToken); // ensure ordering by CreatedAt
        var item2 = await _queueService.EnqueueAsync("9780062315007", bookId: null, ct: TestContext.Current.CancellationToken);

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
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);
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
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);
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
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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
        var item = await _queueService.EnqueueAsync("0451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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
        var item1 = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);
        var lookupService = new MockMetadataLookupService(results: [metadata]);
        var processor = MakeProcessor(lookupService, bookService, bookMetadataService, bookImageService);

        using var timeoutCts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts1 = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts1.Token, TestContext.Current.CancellationToken);
        await processor.StartBatch([item1]).WaitAsync(linkedCts1.Token);

        var done = await _queueService.GetItemsByStatusAsync("AutoAccepted", TestContext.Current.CancellationToken);
        Assert.Single(done);

        // Second run: same ISBN, same metadata — should be Skipped (no new data)
        var item2 = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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
            items.Add(await _queueService.EnqueueAsync($"978045152653{i}", bookId: null, ct: TestContext.Current.CancellationToken));

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
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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
        var item = await _queueService.EnqueueAsync("9780451526538", bookId: null, ct: TestContext.Current.CancellationToken);

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

/// <summary>Returns fixed bytes for any cover URL without touching the network.</summary>
internal sealed class StubCoverFetcher : ICoverFetcher
{
    public Task<byte[]?> DownloadCoverAsync(
        string coverUrl, string isbn, string sourceName, CancellationToken ct = default)
        => Task.FromResult<byte[]?>([0xFF, 0xD8, 0xFF, 0x42]);
}

/// <summary>Returns bytes derived from the requested URL, so tests can tell covers apart.</summary>
internal sealed class PerUrlCoverFetcher : ICoverFetcher
{
    public byte[] BytesFor(string coverUrl) =>
        System.Text.Encoding.UTF8.GetBytes(coverUrl);

    public Task<byte[]?> DownloadCoverAsync(
        string coverUrl, string isbn, string sourceName, CancellationToken ct = default)
        => Task.FromResult<byte[]?>(BytesFor(coverUrl));
}

/// <summary>
/// Minimal mock for IMetadataLookupService that doesn't call real HTTP. Defaults to one queried,
/// non-failing source; the counts, a fetch delay (for timeout paths), and a thrown exception are
/// configurable so every processor failure path can be driven.
/// </summary>
internal sealed class MockMetadataLookupService : IMetadataLookupService
{
    private readonly IReadOnlyList<BookMetadata> _results;
    private readonly Action<string>? _onFetch;
    private readonly int _sourcesQueried;
    private readonly int _sourcesFailed;
    private readonly TimeSpan _delay;
    private readonly Exception? _throwOnFetch;
    private readonly IReadOnlyList<string> _rateLimitedSources;

    public MockMetadataLookupService(
        IReadOnlyList<BookMetadata> results,
        Action<string>? onFetch = null,
        int? sourcesQueried = null,
        int sourcesFailed = 0,
        TimeSpan? delay = null,
        Exception? throwOnFetch = null,
        IReadOnlyList<string>? rateLimitedSources = null)
    {
        _results = results;
        _onFetch = onFetch;
        _sourcesQueried = sourcesQueried ?? Math.Max(1, results.Count);
        _sourcesFailed = sourcesFailed;
        _delay = delay ?? TimeSpan.Zero;
        _throwOnFetch = throwOnFetch;
        _rateLimitedSources = rateLimitedSources ?? [];
    }

    public async Task<MetadataLookupResult> FetchAllSourcesAsync(
        string isbn, CancellationToken ct = default)
    {
        _onFetch?.Invoke(isbn);
        if (_throwOnFetch is not null)
            throw _throwOnFetch;
        if (_delay > TimeSpan.Zero)
            await Task.Delay(_delay, ct);
        var statuses = _rateLimitedSources
            .Select(s => new SourceLookupStatus(s, SourceLookupOutcome.RateLimited))
            .ToList();
        return new MetadataLookupResult(_results, _sourcesQueried, _sourcesFailed, statuses);
    }
}
