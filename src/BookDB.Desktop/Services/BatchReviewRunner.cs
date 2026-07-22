using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BookDB.Desktop.ViewModels;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;
using Serilog;

namespace BookDB.Desktop.Services;

/// <summary>How a review item was resolved; NoPayload means the item had nothing reviewable stored.</summary>
public enum BatchReviewOutcome { NoPayload, Saved, Skipped }

public interface IBatchReviewRunner
{
    /// <summary>
    /// Runs the review flow for one PendingReview item: parses the stored payload, auto-accepts
    /// stale zero-diff items, routes new-ISBN duplicates through the duplicate dialog, builds the
    /// cover slots (cache hits fill synchronously, the rest stream in), shows the merge-review
    /// dialog, and persists the resulting status. <paramref name="collectionId"/> becomes the
    /// review dialog's collection default for new books.
    /// </summary>
    Task<BatchReviewOutcome> ReviewItemAsync(BatchQueueItem item, int? collectionId = null);
}

/// <summary>
/// The single review path shared by the batch window's review loop and the guided add-book flow —
/// one item, one dialog, status persisted on resolution.
/// </summary>
public sealed class BatchReviewRunner : IBatchReviewRunner
{
    private readonly BatchQueueService _queueService;
    private readonly IBookService _bookService;
    private readonly IBookMetadataService _bookMetadataService;
    private readonly IBookImageService _bookImageService;
    private readonly IWindowService _windowService;
    private readonly ICoverFetcher _coverFetcher;
    private readonly ICoverCache _coverCache;

    public BatchReviewRunner(
        BatchQueueService queueService,
        IBookService bookService,
        IBookMetadataService bookMetadataService,
        IBookImageService bookImageService,
        IWindowService windowService,
        ICoverFetcher coverFetcher,
        ICoverCache coverCache)
    {
        _queueService = queueService;
        _bookService = bookService;
        _bookMetadataService = bookMetadataService;
        _bookImageService = bookImageService;
        _windowService = windowService;
        _coverFetcher = coverFetcher;
        _coverCache = coverCache;
    }

    public async Task<BatchReviewOutcome> ReviewItemAsync(BatchQueueItem item, int? collectionId = null)
    {
        if (item.ResultJson is null) return BatchReviewOutcome.NoPayload;

        // Try to deserialize as the new BatchReviewPayload (includes ExistingBookId and CurrentBookMetadata).
        // Fall back to old plain List<BookMetadata> format for backwards compatibility.
        List<BookMetadata>? sources = null;
        int? existingBookId = item.BookId;
        BookMetadata? currentBook = null;
        bool wasNewIsbn = false;
        IReadOnlyList<string> rateLimitedSources = [];
        IReadOnlyList<string> noResultSources = [];
        IReadOnlyList<string> erroredSources = [];

        try
        {
            var payload = JsonSerializer.Deserialize<BatchReviewPayload>(item.ResultJson);
            if (payload?.Sources is not null)
            {
                sources = [.. payload.Sources];
                existingBookId = payload.ExistingBookId ?? item.BookId;
                currentBook = payload.CurrentBookMetadata;
                wasNewIsbn = payload.WasNewIsbn;
                rateLimitedSources = payload.RateLimitedSources ?? [];
                noResultSources = payload.NoResultSources ?? [];
                erroredSources = payload.ErroredSources ?? [];
            }
        }
        catch
        {
            // Ignore — try old format below
        }

        if (sources is null)
        {
            try
            {
                sources = JsonSerializer.Deserialize<List<BookMetadata>>(item.ResultJson);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to deserialize ResultJson for item {ItemId}", item.BatchQueueItemId);
                return BatchReviewOutcome.NoPayload;
            }
        }

        if (sources is null || sources.Count == 0) return BatchReviewOutcome.NoPayload;

        // Safety net: if the stored diffs are empty when recomputed against the stored
        // current-book baseline, auto-accept without showing the dialog at all — the item
        // predates the processor writing a resolvable payload. A force-review item is exempt:
        // it landed here with zero diffs on purpose, to keep the guided flow's confirm promise.
        var diffs = FieldDiffComputer.ComputeDiffs(sources, currentBook);
        if (diffs.Count == 0 && !item.ForceReview)
        {
            try
            {
                if (existingBookId.HasValue)
                    await _bookMetadataService.UpdateBookFromMetadataAsync(existingBookId.Value, sources[0], null);
                else
                    await _bookMetadataService.AddBookFromMetadataAsync(sources[0], null, collectionId);

                await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Done, null);
                _coverCache.RemoveItem(item.BatchQueueItemId);
                return BatchReviewOutcome.Saved;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-accept failed for zero-diff item {ItemId}", item.BatchQueueItemId);
                await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null);
                _coverCache.RemoveItem(item.BatchQueueItemId);
                return BatchReviewOutcome.Skipped;
            }
        }

        // A new-ISBN lookup that mapped onto an existing book gets the duplicate dialog before review.
        if (wasNewIsbn && existingBookId.HasValue)
        {
            var existingBook = await _bookService.GetBookByIdAsync(existingBookId.Value);
            var existingTitle = existingBook?.Title ?? sources.FirstOrDefault()?.Title ?? "(unknown)";
            var isbn = sources.Select(s => s.Isbn).FirstOrDefault(i => !string.IsNullOrEmpty(i)) ?? item.Isbn;

            var choice = await _windowService.ShowDuplicateIsbnDialogAsync(isbn, existingTitle);

            if (choice == DuplicateIsbnResult.Cancel)
            {
                await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null);
                _coverCache.RemoveItem(item.BatchQueueItemId);
                return BatchReviewOutcome.Skipped;
            }
            if (choice == DuplicateIsbnResult.AddAsNew)
            {
                // Treat as new book — clear existingBookId so dialog creates a fresh record
                existingBookId = null;
            }
            // DuplicateIsbnResult.UpdateExisting: keep existingBookId, fall through to review dialog
        }

        // The dialog must open without waiting on the network: covers prefetched into the cache
        // during the scan fill their slots synchronously, the rest open as loading placeholders
        // and stream in while the dialog is already visible.
        var coverIsbn = sources.Select(s => s.Isbn).FirstOrDefault(i => !string.IsNullOrEmpty(i)) ?? item.Isbn;
        var coverOptions = BuildCoverSlots(
            sources,
            sourceName => _coverCache.TryGet(item.BatchQueueItemId, sourceName),
            out var pendingSlots);
        foreach (var slot in pendingSlots)
        {
            _ = FillCoverSlotAsync(_coverFetcher, slot, coverIsbn,
                action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        }

        // Load existing book cover so the user can compare and keep it if preferred.
        // Prepend as "Current" to match the AllColumnNames ordering in MergeReviewViewModel.
        if (existingBookId.HasValue)
        {
            try
            {
                var existingCoverBytes = await _bookImageService.GetBookPrimaryCoverBytesAsync(existingBookId.Value);
                if (existingCoverBytes is not null && existingCoverBytes.Length > 0)
                {
                    var existingBitmap = CoverFetchService.DecodeBitmap(existingCoverBytes);
                    coverOptions.Insert(0, new CoverOption
                    {
                        SourceName = "Current",
                        ImageData = existingCoverBytes,
                        ThumbnailBitmap = existingBitmap,
                        FullBitmap = existingBitmap
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load existing cover for book {BookId}", existingBookId.Value);
            }
        }

        // Pass the stored current-book metadata so the dialog computes the same diffs
        // as the processor did (not against null, which would give different results).
        var result = await _windowService.ShowMergeReviewDialogAsync(
            sources: sources,
            currentBook: currentBook,
            coverOptions: coverOptions,
            existingBookId: existingBookId,
            collectionId: collectionId,
            rateLimitedSources: rateLimitedSources,
            noResultSources: noResultSources,
            erroredSources: erroredSources);

        // The review is resolved either way — release the prefetched covers.
        _coverCache.RemoveItem(item.BatchQueueItemId);

        if (result == true)
        {
            await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Done, null);
            return BatchReviewOutcome.Saved;
        }

        // User cancelled or dialog closed with an error — mark as Skipped so the
        // same item does not reappear in the review queue on the next run.
        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null);
        return BatchReviewOutcome.Skipped;
    }

    /// <summary>
    /// Builds one cover slot per source that advertises a cover URL. Cached covers (scan-time
    /// prefetch) are decoded and filled immediately; the rest come back via
    /// <paramref name="pendingSlots"/> as loading placeholders for
    /// <see cref="FillCoverSlotAsync"/> to stream in after the dialog has opened.
    /// </summary>
    internal static List<CoverOption> BuildCoverSlots(
        IReadOnlyList<BookMetadata> sources,
        Func<string, byte[]?> cachedCover,
        out List<CoverOption> pendingSlots)
    {
        var slots = new List<CoverOption>();
        pendingSlots = [];
        foreach (var source in sources.Where(s => !string.IsNullOrEmpty(s.CoverImageUrl)))
        {
            var sourceName = source.SourceName ?? "Unknown";
            var cached = cachedCover(sourceName);
            if (cached is not null)
            {
                var bitmap = CoverFetchService.DecodeBitmap(cached);
                slots.Add(new CoverOption
                {
                    SourceName = sourceName,
                    ImageData = cached,
                    RemoteUrl = source.CoverImageUrl,
                    ThumbnailBitmap = bitmap,
                    FullBitmap = bitmap
                });
            }
            else
            {
                var slot = new CoverOption
                {
                    SourceName = sourceName,
                    RemoteUrl = source.CoverImageUrl,
                    IsLoading = true
                };
                slots.Add(slot);
                pendingSlots.Add(slot);
            }
        }
        return slots;
    }

    /// <summary>
    /// Downloads one streaming cover slot and publishes the result through
    /// <paramref name="dispatch"/> (the UI-thread dispatcher in production; inline in tests).
    /// A failed download just clears the slot's loading state — the placeholder stays empty
    /// and unselectable, matching a source that never had a cover.
    /// </summary>
    internal static async Task FillCoverSlotAsync(
        ICoverFetcher coverFetcher, CoverOption slot, string isbn, Action<Action> dispatch)
    {
        byte[]? imageBytes = null;
        try
        {
            imageBytes = await coverFetcher.DownloadCoverAsync(
                slot.RemoteUrl!, isbn, slot.SourceName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cover fetch failed for source {Source} ISBN {Isbn}",
                slot.SourceName, isbn);
        }

        var bitmap = imageBytes is not null ? CoverFetchService.DecodeBitmap(imageBytes) : null;
        dispatch(() =>
        {
            if (imageBytes is not null)
            {
                slot.ThumbnailBitmap = bitmap;
                slot.FullBitmap = bitmap;
                slot.ImageData = imageBytes;
            }
            slot.IsLoading = false;
        });
    }
}
