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
/// Tests for BatchQueueService using a real temp-file SQLite database.
/// </summary>
public sealed class BatchQueueServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly TestBookDbContextFactory _factory;
    private readonly BatchQueueService _sut;

    public BatchQueueServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"bookdb_bq_test_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";

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
        _sut = new BatchQueueService(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task EnqueueAsync_InsertsPendingItemAndReturnsIt()
    {
        var item = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        Assert.NotNull(item);
        Assert.True(item.BatchQueueItemId > 0);
        Assert.Equal("Pending", item.Status);
        Assert.NotEqual(default, item.CreatedAt);
        Assert.NotEqual(default, item.UpdatedAt);
    }

    [Fact]
    public async Task EnqueueAsync_NormalizesIsbn()
    {
        var item = await _sut.EnqueueAsync("978-0-451-52653-8", bookId: null, TestContext.Current.CancellationToken);

        Assert.Equal("9780451526538", item.Isbn);
    }

    [Fact]
    public async Task GetPendingItemsAsync_ReturnsOnlyPendingItems()
    {
        await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        await _sut.EnqueueAsync("9780062315007", bookId: null, TestContext.Current.CancellationToken);

        // Manually set one as Done
        var pending = await _sut.GetPendingItemsAsync(TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(pending[0].BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);

        var result = await _sut.GetPendingItemsAsync(TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("Pending", result[0].Status);
    }

    [Fact]
    public async Task GetPendingItemsAsync_ReturnsOrderedByCreatedAt()
    {
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        var item2 = await _sut.EnqueueAsync("9780062315007", bookId: null, TestContext.Current.CancellationToken);

        var result = await _sut.GetPendingItemsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(item1.BatchQueueItemId, result[0].BatchQueueItemId);
        Assert.Equal(item2.BatchQueueItemId, result[1].BatchQueueItemId);
    }

    [Fact]
    public async Task UpdateStatusAsync_ChangesStatusAndSetsUpdatedAt()
    {
        var item = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        var originalUpdatedAt = item.UpdatedAt;

        await Task.Delay(10, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item.BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);

        var doneItems = await _sut.GetItemsByStatusAsync("Done", TestContext.Current.CancellationToken);
        var updated = doneItems.Count > 0 ? doneItems[0] : null;
        Assert.NotNull(updated);
        Assert.Equal("Done", updated.Status);
        Assert.True(updated.UpdatedAt >= originalUpdatedAt);
    }

    [Fact]
    public async Task UpdateStatusAsync_StoresResultJson()
    {
        var item = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        const string json = "{\"test\":true}";

        await _sut.UpdateStatusAsync(item.BatchQueueItemId, "PendingReview", json, TestContext.Current.CancellationToken);

        var items = await _sut.GetItemsByStatusAsync("PendingReview", TestContext.Current.CancellationToken);
        Assert.Single(items);
        Assert.Equal(json, items[0].ResultJson);
    }

    [Fact]
    public async Task ClearCompletedAsync_RemovesDoneSkippedFailedItems()
    {
        await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        var done = await _sut.EnqueueAsync("9780062315007", bookId: null, TestContext.Current.CancellationToken);
        var skipped = await _sut.EnqueueAsync("9780141439600", bookId: null, TestContext.Current.CancellationToken);
        var failed = await _sut.EnqueueAsync("9780316769174", bookId: null, TestContext.Current.CancellationToken);

        await _sut.UpdateStatusAsync(done.BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(skipped.BatchQueueItemId, "Skipped", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(failed.BatchQueueItemId, "Failed", null, TestContext.Current.CancellationToken);

        // Backdate UpdatedAt so the 30-minute guard considers these rows old enough to delete
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.BatchQueueItems
            .Where(i => i.Status == "Done" || i.Status == "Skipped" || i.Status == "Failed")
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, DateTime.UtcNow.AddMinutes(-31)), TestContext.Current.CancellationToken);

        await _sut.ClearCompletedAsync(TestContext.Current.CancellationToken);

        var pending = await _sut.GetPendingItemsAsync(TestContext.Current.CancellationToken);
        Assert.Single(pending);

        var doneItems = await _sut.GetItemsByStatusAsync("Done", TestContext.Current.CancellationToken);
        Assert.Empty(doneItems);

        var skippedItems = await _sut.GetItemsByStatusAsync("Skipped", TestContext.Current.CancellationToken);
        Assert.Empty(skippedItems);

        var failedItems = await _sut.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Empty(failedItems);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsCountsByStatus()
    {
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        var item2 = await _sut.EnqueueAsync("9780062315007", bookId: null, TestContext.Current.CancellationToken);
        var item3 = await _sut.EnqueueAsync("9780141439600", bookId: null, TestContext.Current.CancellationToken);
        var item4 = await _sut.EnqueueAsync("9780316769174", bookId: null, TestContext.Current.CancellationToken);

        await _sut.UpdateStatusAsync(item1.BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item2.BatchQueueItemId, "Skipped", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item3.BatchQueueItemId, "PendingReview", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item4.BatchQueueItemId, "Failed", null, TestContext.Current.CancellationToken);

        var summary = await _sut.GetSummaryAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.Saved);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(1, summary.PendingReview);
        Assert.Equal(1, summary.NotFound);
    }

    [Fact]
    public async Task EnqueueBatchAsync_InsertsAllItems()
    {
        var isbns = new List<string> { "9780451526538", "9780062315007", "9780141439600" };

        var items = await _sut.EnqueueBatchAsync(isbns, TestContext.Current.CancellationToken);

        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.Equal("Pending", i.Status));
    }

    [Fact]
    public async Task EnqueueAsync_Deduplicates_WhenPendingItemAlreadyExists()
    {
        // First enqueue creates a Pending item
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        // Second enqueue with same ISBN returns existing item — no new row
        var item2 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        Assert.Equal(item1.BatchQueueItemId, item2.BatchQueueItemId);

        var pending = await _sut.GetPendingItemsAsync(TestContext.Current.CancellationToken);
        Assert.Single(pending);
    }

    [Fact]
    public async Task EnqueueAsync_Deduplicates_WhenProcessingItemExists()
    {
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item1.BatchQueueItemId, "Processing", null, TestContext.Current.CancellationToken);

        // Second enqueue with same ISBN should return existing Processing item
        var item2 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        Assert.Equal(item1.BatchQueueItemId, item2.BatchQueueItemId);

        // Only one item in DB
        var all = await _sut.GetItemsByStatusAsync("Processing", TestContext.Current.CancellationToken);
        Assert.Single(all);
    }

    [Fact]
    public async Task EnqueueAsync_AllowsRequeue_WhenPreviousItemIsCompleted()
    {
        // First item completes
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item1.BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);

        // New enqueue should create a fresh Pending item (no dedup on terminal status)
        var item2 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        Assert.NotEqual(item1.BatchQueueItemId, item2.BatchQueueItemId);
        Assert.Equal("Pending", item2.Status);
    }

    [Fact]
    public async Task EnqueueBatchAsync_SkipsDuplicateIsbns()
    {
        // Pre-insert one ISBN as Pending
        await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);

        // Batch with 2 ISBNs, one already pending
        var isbns = new List<string> { "9780451526538", "9780062315007" };
        var items = await _sut.EnqueueBatchAsync(isbns, TestContext.Current.CancellationToken);

        // Should return 2 items (1 pre-existing + 1 new)
        Assert.Equal(2, items.Count);

        // Only 2 total rows in DB (not 3)
        var pending = await _sut.GetPendingItemsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task ResetProcessingItemsAsync_ResetsProcessingToPending()
    {
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        _ = await _sut.EnqueueAsync("9780062315007", bookId: null, TestContext.Current.CancellationToken);

        // Simulate crash: item1 was Processing when app died
        await _sut.UpdateStatusAsync(item1.BatchQueueItemId, "Processing", null, TestContext.Current.CancellationToken);

        await _sut.ResetProcessingItemsAsync(TestContext.Current.CancellationToken);

        var pending = await _sut.GetPendingItemsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, pending.Count); // both should be Pending now

        var processing = await _sut.GetItemsByStatusAsync("Processing", TestContext.Current.CancellationToken);
        Assert.Empty(processing);
    }

    [Fact]
    public async Task GetSummaryAsync_CountsSumEqualsTotalItems()
    {
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        var item2 = await _sut.EnqueueAsync("9780062315007", bookId: null, TestContext.Current.CancellationToken);
        var item3 = await _sut.EnqueueAsync("9780141439600", bookId: null, TestContext.Current.CancellationToken);
        var item4 = await _sut.EnqueueAsync("9780316769174", bookId: null, TestContext.Current.CancellationToken);

        await _sut.UpdateStatusAsync(item1.BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item2.BatchQueueItemId, "Skipped", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item3.BatchQueueItemId, "PendingReview", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item4.BatchQueueItemId, "Failed", null, TestContext.Current.CancellationToken);

        var summary = await _sut.GetSummaryAsync(TestContext.Current.CancellationToken);
        int total = summary.Saved + summary.AutoAccepted + summary.Skipped +
                    summary.PendingReview + summary.NotFound;

        // AutoAccepted is always 0 in current design (Done covers auto-accept).
        // Total must equal the 4 items we processed.
        Assert.Equal(4, total);
    }

    [Fact]
    public async Task EnqueueBatchAsync_SkipsRecentlyCompletedIsbns()
    {
        // Pre-complete an ISBN (Done, updated just now)
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item1.BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);

        // Re-batch with same ISBN + one new ISBN
        var isbns = new List<string> { "9780451526538", "9780062315007" };
        var items = await _sut.EnqueueBatchAsync(isbns, TestContext.Current.CancellationToken);

        // Recently-completed ISBN skipped — only new ISBN returned
        Assert.Single(items);
        Assert.Equal("9780062315007", items[0].Isbn);

        // Only one new Pending row in DB (not a second row for 9780451526538)
        var pending = await _sut.GetPendingItemsAsync(TestContext.Current.CancellationToken);
        Assert.Single(pending);
        Assert.Equal("9780062315007", pending[0].Isbn);
    }

    [Fact]
    public async Task EnqueueBatchAsync_AllowsRequeueLong_AfterCompletionGuardExpires()
    {
        // Pre-complete an ISBN, but with UpdatedAt forced to be 31 minutes ago (outside guard window)
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item1.BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);

        // Force the UpdatedAt to be 31 minutes ago
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.BatchQueueItems
            .Where(i => i.BatchQueueItemId == item1.BatchQueueItemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow.AddMinutes(-31)), TestContext.Current.CancellationToken);

        // Should allow new Pending item since old completion is outside the 30-min guard
        var items = await _sut.EnqueueBatchAsync(["9780451526538"], TestContext.Current.CancellationToken);

        Assert.Single(items);
        Assert.Equal("Pending", items[0].Status);
        Assert.NotEqual(item1.BatchQueueItemId, items[0].BatchQueueItemId);
    }

    [Fact]
    public async Task CleanupOldCompletedAsync_RemovesOldTerminalItems()
    {
        // Insert items and manually set old UpdatedAt
        var item1 = await _sut.EnqueueAsync("9780451526538", bookId: null, TestContext.Current.CancellationToken);
        var item2 = await _sut.EnqueueAsync("9780062315007", bookId: null, TestContext.Current.CancellationToken);

        await _sut.UpdateStatusAsync(item1.BatchQueueItemId, "Done", null, TestContext.Current.CancellationToken);
        await _sut.UpdateStatusAsync(item2.BatchQueueItemId, "Failed", null, TestContext.Current.CancellationToken);

        // Force UpdatedAt to be old (8 days ago) by direct DB update
        await using var db = await _factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await db.BatchQueueItems
            .Where(i => i.BatchQueueItemId == item1.BatchQueueItemId ||
                        i.BatchQueueItemId == item2.BatchQueueItemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow.AddDays(-8)), TestContext.Current.CancellationToken);

        await _sut.CleanupOldCompletedAsync(TestContext.Current.CancellationToken);

        var done = await _sut.GetItemsByStatusAsync("Done", TestContext.Current.CancellationToken);
        Assert.Empty(done);
        var failed = await _sut.GetItemsByStatusAsync("Failed", TestContext.Current.CancellationToken);
        Assert.Empty(failed);
    }
}
