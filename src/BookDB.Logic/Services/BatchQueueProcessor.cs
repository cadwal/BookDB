using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Logic.Messages;
using BookDB.Models.Metadata;
using BookDB.MetadataSources.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace BookDB.Logic.Services;

/// <summary>
/// Processes batch ISBN lookups sequentially.
/// Supports pause/resume, cancellation, and persistence across restarts.
/// </summary>
public sealed class BatchQueueProcessor : IBatchQueueProcessor, IDisposable
{
    private readonly BatchQueueService _queueService;
    private readonly IMetadataLookupService _lookupService;
    private readonly IBookService _bookService;
    private readonly IBookMetadataService _bookMetadataService;
    private readonly IBookImageService _bookImageService;
    private readonly ICoverFetcher? _coverFetcher;
    private readonly IMessenger _messenger;
    private readonly ILogger<BatchQueueProcessor> _logger;
    private readonly TimeSpan _itemDelay;

    private readonly SemaphoreSlim _pauseGate = new(1, 1);
    private bool _isPaused;
    private int _processedCount;
    private int _totalCount;

    private CancellationTokenSource? _batchCts;
    private Task? _batchTask;

    public bool IsPaused => _isPaused;
    public int ProcessedCount => _processedCount;
    public int TotalCount => _totalCount;

    public BatchQueueProcessor(
        BatchQueueService queueService,
        IMetadataLookupService lookupService,
        IBookService bookService,
        IBookMetadataService bookMetadataService,
        IBookImageService bookImageService,
        IMessenger messenger,
        ILogger<BatchQueueProcessor> logger,
        TimeSpan? itemDelay = null,
        ICoverFetcher? coverFetcher = null)
    {
        _queueService = queueService;
        _lookupService = lookupService;
        _bookService = bookService;
        _bookMetadataService = bookMetadataService;
        _bookImageService = bookImageService;
        _coverFetcher = coverFetcher;
        _messenger = messenger;
        _logger = logger;
        _itemDelay = itemDelay ?? TimeSpan.FromSeconds(1.5);
    }

    /// <summary>
    /// Reloads any pending items from DB that survived a previous app shutdown.
    /// Items stuck in BatchStatus.Processing (from a crashed session) are reset to "Pending" first.
    /// Returns the items so the caller can pass them to StartBatch.
    /// </summary>
    public async Task<IReadOnlyList<BatchQueueItem>> ReloadPendingFromDatabaseAsync(
        CancellationToken ct = default)
    {
        await _queueService.ResetProcessingItemsAsync(ct);
        return await _queueService.GetPendingItemsAsync(ct);
    }

    /// <summary>
    /// Starts processing a batch of items in the background.
    /// Any currently running batch is cancelled first.
    /// Returns a Task that completes when all items are processed or the batch is cancelled.
    /// </summary>
    public async Task StartBatch(IReadOnlyList<BatchQueueItem> items)
    {
        // Cancel any existing batch and wait for it to stop
        if (_batchCts is not null)
        {
            _batchCts.Cancel();
            if (_batchTask is not null)
            {
                try { await _batchTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            _batchCts.Dispose();
        }

        _processedCount = 0;
        _totalCount = items.Count;

        _batchCts = new CancellationTokenSource();

        var token = _batchCts.Token;
        // Deduplicate by item ID to prevent double-processing if the same row is passed twice
        var deduped = items.DistinctBy(x => x.BatchQueueItemId).ToList();
        _batchTask = ProcessBatchInternalAsync(deduped, token);

        await _batchTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels the current batch and waits for it to stop cleanly.
    /// </summary>
    public async Task CancelBatchAsync()
    {
        Resume(); // ensure not paused before cancelling
        _batchCts?.Cancel();
        if (_batchTask is not null)
        {
            try { await _batchTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected — batch was cancelled */ }
        }
    }

    /// <summary>
    /// Graceful shutdown — cancels current batch and waits up to ct timeout.
    /// Called on application exit.
    /// </summary>
    public async Task StopAsync(CancellationToken ct)
    {
        Resume();
        _batchCts?.Cancel();
        if (_batchTask is not null)
        {
            try
            {
                await Task.WhenAny(_batchTask, Task.Delay(Timeout.Infinite, ct))
                          .ConfigureAwait(false);
            }
            catch { }
        }
    }

    private async Task ProcessBatchInternalAsync(
        IReadOnlyList<BatchQueueItem> items, CancellationToken ct)
    {
        try
        {
            foreach (var item in items)
            {
                if (ct.IsCancellationRequested) break;

                // Blocking pause: wait until gate is available then release immediately.
                // When paused, Pause() holds the gate; processor blocks here until Resume() releases it.
                await _pauseGate.WaitAsync(ct).ConfigureAwait(false);
                _pauseGate.Release();

                // Send pre-processing status (1-indexed so UI shows "N/Total" while Nth item fetches)
                _messenger.Send(new BatchQueueProgressMessage
                {
                    Current = _processedCount + 1,
                    Total = _totalCount,
                    CurrentIsbn = item.Isbn,
                    IsRunning = true,
                    StatusCode = BatchProgressStatus.QueryingSources
                });

                try
                {
                    await ProcessItemAsync(item, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process batch item {ItemId} ISBN {Isbn}",
                        item.BatchQueueItemId, item.Isbn);
                    await _queueService.UpdateStatusAsync(
                        item.BatchQueueItemId, BatchStatus.Failed, null, ct).ConfigureAwait(false);
                }

                _processedCount++;

                bool batchComplete = _totalCount > 0 && _processedCount >= _totalCount;

                _messenger.Send(new BatchQueueProgressMessage
                {
                    Current = _processedCount,
                    Total = _totalCount,
                    CurrentIsbn = item.Isbn,
                    IsRunning = !batchComplete,
                    StatusCode = batchComplete ? BatchProgressStatus.Complete : BatchProgressStatus.None
                });

                if (!batchComplete && !ct.IsCancellationRequested)
                    await Task.Delay(_itemDelay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation — send final message below
        }
        finally
        {
            // Always send IsRunning=false on exit so UI updates correctly
            _messenger.Send(new BatchQueueProgressMessage
            {
                Current = _processedCount,
                Total = _totalCount,
                CurrentIsbn = null,
                IsRunning = false
            });
        }
    }

    private async Task ProcessItemAsync(BatchQueueItem item, CancellationToken ct)
    {
        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Processing, null, ct);

        // 30-second per-item timeout guard — prevents hangs if sources are unreachable
        using var itemTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, itemTimeout.Token);

        var results = await _lookupService.FetchAllSourcesAsync(item.Isbn, linked.Token);

        if (results.Count == 0)
        {
            await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Failed, null, ct);
            return;
        }

        _messenger.Send(new BatchQueueProgressMessage
        {
            Current = _processedCount,
            Total = _totalCount,
            CurrentIsbn = item.Isbn,
            IsRunning = true,
            StatusCode = BatchProgressStatus.ProcessingResults,
            ResultCount = results.Count
        });

        // Always look up existing book by ISBN (handles ISBN-10/13 cross-format matching).
        // item.BookId is only set for explicit re-catalog; for new ISBNs we still need to
        // check whether the book was already saved in a previous run.
        BookMetadata? currentBookMetadata = null;
        int? existingBookId = item.BookId;
        bool existingHasCover = false;

        if (existingBookId.HasValue)
        {
            var book = await _bookService.GetBookByIdAsync(existingBookId.Value, ct);
            if (book is not null)
            {
                currentBookMetadata = BookToMetadata(book);
                existingHasCover = book.Images.Any(i => i.IsPrimary);
            }
        }
        else
        {
            // Try to find an existing book with the same ISBN (cross-format)
            var existingBook = await _bookMetadataService.FindBookByIsbnAsync(item.Isbn, ct);
            if (existingBook is not null)
            {
                existingBookId = existingBook.BookId;
                currentBookMetadata = BookToMetadata(existingBook);
                existingHasCover = existingBook.Images.Any(i => i.IsPrimary);
            }
        }

        // Normalize ISO language codes to full names before computing diffs.
        // API sources return ISO 639-1 codes (e.g. "sv") while the book database stores
        // full names (e.g. "Swedish"), which would otherwise produce false diff conflicts.
        results = NormalizeSourceLanguages(results);

        var diffs = FieldDiffComputer.ComputeDiffs(results, currentBookMetadata);

        if (diffs.Count == 0)
        {
            // No conflicts — auto-accept
            if (existingBookId.HasValue)
            {
                if (currentBookMetadata is not null)
                {
                    // All source values match current book.
                    if (!existingHasCover)
                    {
                        // No existing cover — download one from the best available source and save directly.
                        var coverBytes = await DownloadBestCoverAsync(results, item.Isbn, ct);
                        if (coverBytes is not null)
                        {
                            await _bookMetadataService.UpdateBookFromMetadataAsync(
                                existingBookId.Value, results[0], coverBytes, ct);
                        }
                        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null, ct);
                    }
                    else
                    {
                        // Book already has a cover — compare API cover bytes with existing cover.
                        // Only route to PendingReview if the covers differ; skip silently if identical.
                        var apiCoverBytes = await DownloadBestCoverAsync(results, item.Isbn, ct);
                        if (apiCoverBytes is { Length: > 0 })
                        {
                            var existingCoverBytes = await _bookImageService.GetBookPrimaryCoverBytesAsync(
                                existingBookId.Value, ct);
                            bool coversAreIdentical = existingCoverBytes is not null
                                && existingCoverBytes.AsSpan().SequenceEqual(apiCoverBytes);
                            if (!coversAreIdentical)
                            {
                                // Different cover — route to PendingReview so user can accept/reject.
                                var resultJson = JsonSerializer.Serialize(
                                    new BatchReviewPayload(results, existingBookId, currentBookMetadata,
                                        WasNewIsbn: item.BookId is null));
                                await _queueService.UpdateStatusAsync(
                                    item.BatchQueueItemId, BatchStatus.PendingReview, resultJson, ct);
                                return;
                            }
                        }
                        // Identical cover (or no API cover) — skip silently.
                        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null, ct);
                    }
                }
                else
                {
                    // Existing book ID provided but book not loadable (unusual edge case)
                    var coverBytes = await DownloadBestCoverAsync(results, item.Isbn, ct);
                    await _bookMetadataService.UpdateBookFromMetadataAsync(
                        existingBookId.Value, results[0], coverBytes, ct);
                    await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Done, null, ct);
                }
            }
            else
            {
                // New book — save using first source that has the most data
                _messenger.Send(new BatchQueueProgressMessage
                {
                    Current = _processedCount,
                    Total = _totalCount,
                    CurrentIsbn = item.Isbn,
                    IsRunning = true,
                    StatusCode = BatchProgressStatus.Saving
                });
                var coverBytes = await DownloadBestCoverAsync(results, item.Isbn, ct);
                // Ensure the stored book has an ISBN so it can be found by FindBookByIsbnAsync
                // on future runs. If the source returned a null ISBN, fall back to item.Isbn.
                var metadata = string.IsNullOrEmpty(results[0].Isbn)
                    ? results[0] with { Isbn = item.Isbn }
                    : results[0];
                await _bookMetadataService.AddBookFromMetadataAsync(metadata, coverBytes, null, ct);
                await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.AutoAccepted, null, ct);
            }
        }
        else
        {
            // Conflicts — queue for review.
            // Store the current book metadata snapshot alongside the source results so the review
            // dialog can recompute the same diffs against the same baseline (not against null).
            var resultJson = JsonSerializer.Serialize(
                new BatchReviewPayload(results, existingBookId, currentBookMetadata,
                    WasNewIsbn: item.BookId is null));
            await _queueService.UpdateStatusAsync(
                item.BatchQueueItemId, BatchStatus.PendingReview, resultJson, ct);
        }
    }

    /// <summary>
    /// Downloads cover from the first source that has a CoverImageUrl.
    /// Returns raw image bytes, or null if no cover URL is available or download fails.
    /// </summary>
    private async Task<byte[]?> DownloadBestCoverAsync(
        IReadOnlyList<BookMetadata> results, string isbn, CancellationToken ct)
    {
        if (_coverFetcher is null) return null;

        var sourceWithCover = results.FirstOrDefault(r => !string.IsNullOrEmpty(r.CoverImageUrl));
        if (sourceWithCover?.CoverImageUrl is null) return null;

        return await _coverFetcher.DownloadCoverAsync(
            sourceWithCover.CoverImageUrl, isbn, sourceWithCover.SourceName, ct);
    }

    private static BookMetadata BookToMetadata(BookDB.Models.Entities.Book book)
    {
        var authors = new List<string>();
        foreach (var contributor in book.Contributors)
        {
            if (contributor.ContributorRole?.Code == "Author"
                && contributor.Person?.DisplayName is not null)
            {
                authors.Add(contributor.Person.DisplayName);
            }
        }

        return new BookMetadata(
            Title: book.Title,
            Subtitle: book.Subtitle,
            Authors: authors,
            Publisher: book.Publisher?.Name,
            PubDate: book.PubDate,
            Language: book.Language?.Name,
            Isbn: book.Isbn,
            Pages: book.Pages,
            Description: book.Comments,
            CoverImageUrl: null,
            Series: book.Series?.Name,
            SeriesNumber: book.SeriesNumber,
            SourceName: "Current");
    }

    /// <summary>
    /// Normalizes ISO 639-1 language codes in source metadata to full language names
    /// so that diff comparison against the current book (which stores full names) does
    /// not produce false positives (e.g. "sv" vs "Swedish").
    /// </summary>
    private static IReadOnlyList<BookMetadata> NormalizeSourceLanguages(IReadOnlyList<BookMetadata> sources)
    {
        var normalized = new List<BookMetadata>(sources.Count);
        foreach (var source in sources)
        {
            if (source.Language is not null
                && source.Language.Length <= 3
                && BookMetadataService.TryResolveLanguageName(source.Language, out var fullName))
            {
                normalized.Add(source with { Language = fullName });
            }
            else
            {
                normalized.Add(source);
            }
        }
        return normalized;
    }

    /// <summary>Pauses processing after the current item finishes.</summary>
    public async Task PauseAsync()
    {
        if (_isPaused) return;
        await _pauseGate.WaitAsync(); // take the gate; processor blocks at WaitAsync on next iteration
        _isPaused = true;
    }

    /// <summary>Resumes processing after a Pause.</summary>
    public void Resume()
    {
        if (!_isPaused) return;
        _isPaused = false;
        _pauseGate.Release();
    }

    public void Dispose()
    {
        _batchCts?.Dispose();
        _pauseGate.Dispose();
    }
}

/// <summary>
/// Payload stored in BatchQueueItem.ResultJson for PendingReview items.
/// Includes the source results, the ID of the existing book (if found during processing),
/// and the current book's metadata snapshot (used to reconstruct the same diff in the review dialog).
/// </summary>
public record BatchReviewPayload(
    IReadOnlyList<BookMetadata> Sources,
    int? ExistingBookId,
    BookMetadata? CurrentBookMetadata = null,
    bool WasNewIsbn = false);
