using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Data.DbContexts;
using BookDB.Models;
using BookDB.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Services;

public record BatchQueueSummary(
    int Saved,
    int AutoAccepted,
    int NotFound,
    int PendingReview,
    int Skipped);

/// <summary>How many failed items share a failure code (null = failed before codes existed).</summary>
public sealed record BatchFailureCount(string? FailureCode, int Count);

public sealed class BatchQueueService
{
    private readonly IDbContextFactory<BookDbContext> _factory;

    public BatchQueueService(IDbContextFactory<BookDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<BatchQueueItem> EnqueueAsync(
        string isbn, int? bookId, bool forceReview = false, CancellationToken ct = default)
    {
        var normalized = IsbnNormalizer.Normalize(isbn);

        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        // Dedup: skip insert if a Pending or Processing item already exists for this ISBN
        var existing = await dbContext.BatchQueueItems
            .Where(i => i.Isbn == normalized &&
                        (i.Status == BatchStatus.Pending || i.Status == BatchStatus.Processing))
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            // A force-review enqueue must keep its confirm promise even when it dedups onto an
            // item queued without the flag — upgrade the row rather than dropping the demand.
            if (forceReview && !existing.ForceReview)
            {
                await dbContext.BatchQueueItems
                    .Where(i => i.BatchQueueItemId == existing.BatchQueueItemId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.ForceReview, true)
                        .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);
                existing.ForceReview = true;
            }
            return existing;
        }

        var item = new BatchQueueItem
        {
            Isbn = normalized,
            BookId = bookId,
            Status = BatchStatus.Pending,
            ForceReview = forceReview,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.BatchQueueItems.Add(item);
        await dbContext.SaveChangesAsync(ct);
        return item;
    }

    public async Task<IReadOnlyList<BatchQueueItem>> EnqueueBatchAsync(
        IReadOnlyList<string> isbns, CancellationToken ct = default)
    {
        var normalized = isbns.Select(IsbnNormalizer.Normalize).Distinct().ToList();

        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        // Dedup: ISBNs that already have a Pending or Processing item (already in channel)
        var existingActiveItems = await dbContext.BatchQueueItems
            .Where(i => normalized.Contains(i.Isbn) &&
                        (i.Status == BatchStatus.Pending || i.Status == BatchStatus.Processing))
            .ToListAsync(ct);
        var existingActiveIsbns = existingActiveItems.Select(i => i.Isbn).ToHashSet();

        // Dedup: ISBNs that were completed recently (within 30 min) — do not re-queue them.
        // This prevents re-saving books that were just saved in the same session when the
        // user re-runs the same batch without restarting the app.
        var recentCutoff = DateTime.UtcNow.AddMinutes(-30);
        var recentlyCompletedIsbns = await dbContext.BatchQueueItems
            .Where(i => normalized.Contains(i.Isbn) &&
                        (i.Status == BatchStatus.Done || i.Status == BatchStatus.AutoAccepted ||
                         i.Status == BatchStatus.Skipped || i.Status == BatchStatus.PendingReview) &&
                        i.UpdatedAt >= recentCutoff)
            .Select(i => i.Isbn)
            .ToHashSetAsync(ct);

        var newItems = normalized
            .Where(isbn => !existingActiveIsbns.Contains(isbn) && !recentlyCompletedIsbns.Contains(isbn))
            .Select(isbn => new BatchQueueItem
            {
                Isbn = isbn,
                BookId = null,
                Status = BatchStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ToList();

        if (newItems.Count > 0)
        {
            dbContext.BatchQueueItems.AddRange(newItems);
            await dbContext.SaveChangesAsync(ct);
        }

        // Return new items + pre-existing active items so the caller can enqueue them all.
        // The processor's EnqueueItemsAsync uses _enqueuedItemIds to deduplicate items that
        // were already loaded into the channel at startup — no double-processing occurs.
        return newItems.Concat(existingActiveItems).ToList();
    }

    public async Task<IReadOnlyList<BatchQueueItem>> EnqueueRecatalogAsync(
        IReadOnlyList<int> bookIds, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);

        var books = await dbContext.Books
            .Where(b => bookIds.Contains(b.BookId) && b.Isbn != null)
            .Select(b => new { b.BookId, b.Isbn })
            .ToListAsync(ct);

        var normalizedBooks = books
            .Select(b => new { b.BookId, Isbn = IsbnNormalizer.Normalize(b.Isbn!) })
            .ToList();

        // Dedup: skip ISBNs that already have a Pending or Processing item (already in channel).
        // Re-catalog intentionally bypasses the recently-completed guard — the user has explicitly
        // requested re-cataloging even if the book was just processed.
        var candidateIsbns = normalizedBooks.Select(b => b.Isbn).Distinct().ToList();
        var existingActiveItems = await dbContext.BatchQueueItems
            .Where(i => candidateIsbns.Contains(i.Isbn) &&
                        (i.Status == BatchStatus.Pending || i.Status == BatchStatus.Processing))
            .ToListAsync(ct);
        var existingActiveIsbns = existingActiveItems.Select(i => i.Isbn).ToHashSet();

        var newItems = normalizedBooks
            .Where(b => !existingActiveIsbns.Contains(b.Isbn))
            .Select(b => new BatchQueueItem
            {
                Isbn = b.Isbn,
                BookId = b.BookId,
                Status = BatchStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ToList();

        if (newItems.Count > 0)
        {
            dbContext.BatchQueueItems.AddRange(newItems);
            await dbContext.SaveChangesAsync(ct);
        }

        // Return new items + pre-existing active items.
        // The processor's EnqueueItemsAsync uses _enqueuedItemIds to deduplicate items that
        // were already loaded into the channel at startup — no double-processing occurs.
        return newItems.Concat(existingActiveItems).ToList();
    }

    /// <summary>
    /// Resets "Processing" items to "Pending" so they are reloaded after a crash/restart.
    /// Should be called once during startup before ReloadPendingFromDatabaseAsync.
    /// </summary>
    public async Task ResetProcessingItemsAsync(CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.BatchQueueItems
            .Where(i => i.Status == BatchStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, BatchStatus.Pending)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task<IReadOnlyList<BatchQueueItem>> GetPendingItemsAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.BatchQueueItems
            .Where(i => i.Status == BatchStatus.Pending)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// <paramref name="failureCode"/> is a <see cref="BatchFailureReason"/> name for Failed items; leaving it
    /// null on any other transition clears a stale code, so a retried item never keeps its old reason.
    /// </summary>
    public async Task UpdateStatusAsync(
        int itemId, string status, string? resultJson,
        string? failureCode = null, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.BatchQueueItems
            .Where(i => i.BatchQueueItemId == itemId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, status)
                .SetProperty(i => i.ResultJson, resultJson)
                .SetProperty(i => i.FailureCode, failureCode)
                .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), ct);
    }

    /// <summary>
    /// Failed-item counts grouped by failure code. Codes are raw <see cref="BatchQueueItem.FailureCode"/>
    /// values (null for rows that predate failure codes); the caller localizes them.
    /// </summary>
    public async Task<IReadOnlyList<BatchFailureCount>> GetFailureReasonCountsAsync(
        CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.BatchQueueItems
            .Where(i => i.Status == BatchStatus.Failed)
            .GroupBy(i => i.FailureCode)
            .Select(g => new BatchFailureCount(g.Key, g.Count()))
            .ToListAsync(ct);
    }

    public async Task<BatchQueueItem?> GetItemAsync(int itemId, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.BatchQueueItems
            .FirstOrDefaultAsync(i => i.BatchQueueItemId == itemId, ct);
    }

    public async Task<IReadOnlyList<BatchQueueItem>> GetItemsByStatusAsync(
        string status, CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        return await dbContext.BatchQueueItems
            .Where(i => i.Status == status)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task ClearCompletedAsync(CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.BatchQueueItems
            .Where(i => i.Status == BatchStatus.Done || i.Status == BatchStatus.AutoAccepted ||
                        i.Status == BatchStatus.Skipped || i.Status == "Failed")
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Deletes terminal-status items (Done, Skipped, Failed, PendingReview) older than 7 days.
    /// Called on startup to prevent unbounded DB growth across sessions.
    /// </summary>
    public async Task CleanupOldCompletedAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        await dbContext.BatchQueueItems
            .Where(i => (i.Status == BatchStatus.Done || i.Status == BatchStatus.AutoAccepted ||
                         i.Status == BatchStatus.Skipped || i.Status == "Failed" ||
                         i.Status == BatchStatus.PendingReview) &&
                        i.UpdatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<BatchQueueSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var dbContext = await _factory.CreateDbContextAsync(ct);
        var groups = await dbContext.BatchQueueItems
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int GetCount(string status) =>
            groups.FirstOrDefault(g => g.Status == status)?.Count ?? 0;

        return new BatchQueueSummary(
            Saved: GetCount("Done"),
            AutoAccepted: GetCount("AutoAccepted"),
            NotFound: GetCount("Failed"),
            PendingReview: GetCount("PendingReview"),
            Skipped: GetCount("Skipped"));
    }
}
