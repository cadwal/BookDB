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
    private readonly ICoverCache? _coverCache;
    private readonly IMessenger _messenger;
    private readonly ILogger<BatchQueueProcessor> _logger;
    private readonly TimeSpan _itemDelay;
    private readonly TimeSpan _itemTimeout;

    private readonly SemaphoreSlim _pauseGate = new(1, 1);
    private readonly object _lock = new();
    private readonly LinkedList<PendingWork> _pending = new();
    private bool _isPaused;

    // Guarded by _lock: true while a drain loop is live, so an append joins the running session
    // (growing its running totals) instead of spinning up a second loop.
    private bool _draining;

    private int _processedCount;
    private int _totalCount;
    private int _toReviewCount;
    private int _failedCount;

    private CancellationTokenSource? _sessionCts;
    private Task? _drainTask;

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
        ICoverFetcher? coverFetcher = null,
        TimeSpan? itemTimeout = null,
        ICoverCache? coverCache = null)
    {
        _queueService = queueService;
        _lookupService = lookupService;
        _bookService = bookService;
        _bookMetadataService = bookMetadataService;
        _bookImageService = bookImageService;
        _coverFetcher = coverFetcher;
        _coverCache = coverCache;
        _messenger = messenger;
        _logger = logger;
        _itemDelay = itemDelay ?? TimeSpan.FromSeconds(1.5);
        _itemTimeout = itemTimeout ?? TimeSpan.FromSeconds(30);
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
    /// Appends items to the single continuous drain loop, starting the loop if it is idle. Items are
    /// already persisted <see cref="BatchQueueItem"/> rows; duplicates within the call are dropped.
    /// When <paramref name="priority"/> is set the items jump ahead of the pending backlog (the
    /// in-flight item still finishes first) — the interactive single-book add uses this so it isn't
    /// stuck behind a long bulk scan. The returned task completes when these specific items have
    /// drained (or the session is cancelled), so a producer can await its own submission without
    /// waiting on the rest of the loop.
    /// </summary>
    public Task EnqueueAsync(IReadOnlyList<BatchQueueItem> items, bool priority = false)
    {
        var deduped = items.DistinctBy(x => x.BatchQueueItemId).ToList();
        if (deduped.Count == 0)
            return Task.CompletedTask;

        var ticket = new EnqueueTicket(deduped.Select(x => x.BatchQueueItemId));
        lock (_lock)
        {
            bool newSession = !_draining;
            if (newSession)
            {
                _processedCount = 0;
                _totalCount = 0;
                _toReviewCount = 0;
                _failedCount = 0;
                _sessionCts?.Dispose();
                _sessionCts = new CancellationTokenSource();
                _draining = true;
            }

            _totalCount += deduped.Count;

            // Priority items insert ahead of the existing backlog in their given order; a null anchor
            // (empty queue) falls through to a plain append.
            var anchor = priority ? _pending.First : null;
            foreach (var item in deduped)
            {
                if (anchor is not null)
                    _pending.AddBefore(anchor, new PendingWork(item, ticket));
                else
                    _pending.AddLast(new PendingWork(item, ticket));
            }

            if (newSession)
            {
                var token = _sessionCts!.Token;
                _drainTask = Task.Run(() => DrainLoopAsync(token));
            }
        }

        return ticket.Tcs.Task;
    }

    /// <summary>
    /// Cancels the whole draining session and waits for it to stop cleanly. Items that never got to
    /// run stay Pending in the DB and resume on a later session; their awaiters are released here.
    /// </summary>
    public async Task CancelBatchAsync()
    {
        Resume(); // ensure not paused before cancelling
        Task? drain;
        lock (_lock)
        {
            _sessionCts?.Cancel();
            drain = _drainTask;
        }
        if (drain is not null)
        {
            try { await drain.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected — session was cancelled */ }
        }
        ReleasePendingTickets();
    }

    /// <summary>Clears the pending backlog and releases each cleared item's awaiter.</summary>
    private void ReleasePendingTickets()
    {
        lock (_lock)
        {
            foreach (var work in _pending)
                work.Ticket.MarkDone(work.Item.BatchQueueItemId);
            _pending.Clear();
        }
    }

    /// <summary>
    /// Graceful shutdown — cancels the session and waits up to the ct timeout. Called on app exit.
    /// </summary>
    public async Task StopAsync(CancellationToken ct)
    {
        Resume();
        Task? drain;
        lock (_lock)
        {
            _sessionCts?.Cancel();
            drain = _drainTask;
        }
        if (drain is not null)
        {
            try
            {
                await Task.WhenAny(drain, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
            }
            catch { }
        }
        ReleasePendingTickets();
    }

    /// <summary>Single funnel for progress messages so every send carries the running outcome counts.</summary>
    private void SendProgress(
        int current, string? isbn, bool isRunning,
        BatchProgressStatus statusCode = BatchProgressStatus.None, int resultCount = 0)
        => _messenger.Send(new BatchQueueProgressMessage
        {
            Current = current,
            Total = _totalCount,
            CurrentIsbn = isbn,
            IsRunning = isRunning,
            StatusCode = statusCode,
            ResultCount = resultCount,
            ToReviewCount = _toReviewCount,
            FailedCount = _failedCount
        });

    private async Task DrainLoopAsync(CancellationToken ct)
    {
        bool cancelled = false;
        try
        {
            while (true)
            {
                PendingWork work;
                lock (_lock)
                {
                    // _draining stays true here even when the queue is empty; the finally re-checks
                    // under the lock so an append racing the exit is never stranded.
                    if (ct.IsCancellationRequested || _pending.First is null)
                        break;
                    work = _pending.First.Value;
                    _pending.RemoveFirst();
                }

                // Blocking pause: wait until the gate is available then release immediately. When
                // paused, Pause() holds the gate; the loop blocks here until Resume() releases it.
                await _pauseGate.WaitAsync(ct).ConfigureAwait(false);
                _pauseGate.Release();

                // 1-indexed so the UI shows "N/Total" while the Nth item fetches.
                SendProgress(_processedCount + 1, work.Item.Isbn, isRunning: true,
                    BatchProgressStatus.QueryingSources);

                try
                {
                    await ProcessItemAsync(work.Item, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    work.Ticket.MarkDone(work.Item.BatchQueueItemId);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process batch item {ItemId} ISBN {Isbn}",
                        work.Item.BatchQueueItemId, work.Item.Isbn);
                    await _queueService.UpdateStatusAsync(
                        work.Item.BatchQueueItemId, BatchStatus.Failed, null,
                        BatchFailureReason.Unexpected.ToString(), ct).ConfigureAwait(false);
                    _failedCount++;
                }

                _processedCount++;
                work.Ticket.MarkDone(work.Item.BatchQueueItemId);

                SendProgress(_processedCount, work.Item.Isbn, isRunning: true);

                bool moreQueued;
                lock (_lock) moreQueued = _pending.First is not null;
                if (moreQueued && !ct.IsCancellationRequested)
                    await Task.Delay(_itemDelay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            bool relaunched = false;
            lock (_lock)
            {
                // An append that arrived as the loop was exiting keeps the session alive rather than
                // being stranded; on cancellation the session ends (Cancel clears the backlog).
                if (!cancelled && !ct.IsCancellationRequested && _pending.First is not null)
                {
                    _drainTask = Task.Run(() => DrainLoopAsync(ct));
                    relaunched = true;
                }
                else
                {
                    _draining = false;
                }
            }

            if (!relaunched)
                SendProgress(_processedCount, null, isRunning: false,
                    cancelled ? BatchProgressStatus.None : BatchProgressStatus.Complete);
        }
    }

    private async Task ProcessItemAsync(BatchQueueItem item, CancellationToken ct)
    {
        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Processing, null, ct: ct);

        // Per-item timeout guard — prevents hangs if sources are unreachable
        using var itemTimeout = new CancellationTokenSource(_itemTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, itemTimeout.Token);

        MetadataLookupResult lookup;
        try
        {
            lookup = await _lookupService.FetchAllSourcesAsync(item.Isbn, linked.Token);
        }
        catch (OperationCanceledException) when (itemTimeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // The per-item timeout fired while the sources hung — a network failure, not a user cancel.
            await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Failed, null,
                BatchFailureReason.NetworkError.ToString(), ct);
            _failedCount++;
            return;
        }

        var results = lookup.Results;
        if (results.Count == 0)
        {
            var reason =
                lookup.SourcesQueried == 0 ? BatchFailureReason.AllSourcesDisabled
                : lookup.RateLimitedSources.Count > 0 ? BatchFailureReason.RateLimited
                : lookup.SourcesFailed >= lookup.SourcesQueried ? BatchFailureReason.NetworkError
                : BatchFailureReason.NoResults;
            await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Failed, null,
                reason.ToString(), ct);
            _failedCount++;
            return;
        }

        SendProgress(_processedCount, item.Isbn, isRunning: true,
            BatchProgressStatus.ProcessingResults, results.Count);

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

        if (diffs.Count == 0 && !item.ForceReview)
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
                        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null, ct: ct);
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
                                        WasNewIsbn: item.BookId is null,
                                        RateLimitedSources: lookup.RateLimitedSources,
                                        NoResultSources: lookup.NoResultSources,
                                        ErroredSources: lookup.ErroredSources));
                                await _queueService.UpdateStatusAsync(
                                    item.BatchQueueItemId, BatchStatus.PendingReview, resultJson, ct: ct);
                                _toReviewCount++;
                                // Seed the cache with the bytes just downloaded for the comparison
                                // (DownloadBestCoverAsync uses the first source with a URL), then
                                // prefetch the remaining sources.
                                var comparedSource = results.First(r => !string.IsNullOrEmpty(r.CoverImageUrl));
                                _coverCache?.Set(item.BatchQueueItemId, comparedSource.SourceName, apiCoverBytes);
                                await PrefetchCoversAsync(results, item, ct);
                                return;
                            }
                        }
                        // Identical cover (or no API cover) — skip silently.
                        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null, ct: ct);
                    }
                }
                else
                {
                    // Existing book ID provided but book not loadable (unusual edge case)
                    var coverBytes = await DownloadBestCoverAsync(results, item.Isbn, ct);
                    await _bookMetadataService.UpdateBookFromMetadataAsync(
                        existingBookId.Value, results[0], coverBytes, ct);
                    await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Done, null, ct: ct);
                }
            }
            else
            {
                // New book — save using first source that has the most data
                SendProgress(_processedCount, item.Isbn, isRunning: true, BatchProgressStatus.Saving);
                var coverBytes = await DownloadBestCoverAsync(results, item.Isbn, ct);
                // Ensure the stored book has an ISBN so it can be found by FindBookByIsbnAsync
                // on future runs. If the source returned a null ISBN, fall back to item.Isbn.
                var metadata = string.IsNullOrEmpty(results[0].Isbn)
                    ? results[0] with { Isbn = item.Isbn }
                    : results[0];
                await _bookMetadataService.AddBookFromMetadataAsync(metadata, coverBytes, null, ct);
                await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.AutoAccepted, null, ct: ct);
            }
        }
        else
        {
            // Conflicts — or a force-review item, which lands here even when sources agree,
            // because the guided single-book add promised the user a confirm step.
            // Store the current book metadata snapshot alongside the source results so the review
            // dialog can recompute the same diffs against the same baseline (not against null).
            var resultJson = JsonSerializer.Serialize(
                new BatchReviewPayload(results, existingBookId, currentBookMetadata,
                    WasNewIsbn: item.BookId is null,
                    RateLimitedSources: lookup.RateLimitedSources,
                    NoResultSources: lookup.NoResultSources,
                    ErroredSources: lookup.ErroredSources));
            await _queueService.UpdateStatusAsync(
                item.BatchQueueItemId, BatchStatus.PendingReview, resultJson, ct: ct);
            _toReviewCount++;
            await PrefetchCoversAsync(results, item, ct);
        }
    }

    /// <summary>
    /// Downloads every source cover for an item that just landed in PendingReview into the
    /// in-memory cache, so the review dialog can show them without touching the network.
    /// Best-effort: the status is already persisted, and a miss simply streams into the
    /// dialog later — so failures are swallowed and cancellation returns quietly.
    /// </summary>
    private async Task PrefetchCoversAsync(
        IReadOnlyList<BookMetadata> results, BatchQueueItem item, CancellationToken ct)
    {
        if (_coverFetcher is null || _coverCache is null) return;

        var sourcesWithCovers = results
            .Where(r => !string.IsNullOrEmpty(r.CoverImageUrl))
            .Where(r => _coverCache.TryGet(item.BatchQueueItemId, r.SourceName) is null)
            .ToList();
        if (sourcesWithCovers.Count == 0) return;

        SendProgress(_processedCount, item.Isbn, isRunning: true, BatchProgressStatus.FetchingCovers);

        foreach (var source in sourcesWithCovers)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var bytes = await _coverFetcher.DownloadCoverAsync(
                    source.CoverImageUrl!, item.Isbn, source.SourceName, ct);
                if (bytes is not null)
                    _coverCache.Set(item.BatchQueueItemId, source.SourceName, bytes);
            }
            catch
            {
                // Best-effort per the summary above; the fetcher already logs its own failures.
            }
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

        SendProgress(_processedCount, isbn, isRunning: true, BatchProgressStatus.FetchingCovers);

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
        _sessionCts?.Dispose();
        _pauseGate.Dispose();
    }

    private readonly record struct PendingWork(BatchQueueItem Item, EnqueueTicket Ticket);

    /// <summary>
    /// Tracks one <see cref="EnqueueAsync"/> call's items so its returned task completes exactly when
    /// those items have drained — letting one producer await its own submission without waiting on the
    /// rest of the shared loop.
    /// </summary>
    private sealed class EnqueueTicket
    {
        private readonly HashSet<int> _remaining;
        private readonly object _gate = new();

        public EnqueueTicket(IEnumerable<int> itemIds) => _remaining = [.. itemIds];

        public TaskCompletionSource Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void MarkDone(int itemId)
        {
            lock (_gate)
            {
                if (_remaining.Remove(itemId) && _remaining.Count == 0)
                    Tcs.TrySetResult();
            }
        }
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
    bool WasNewIsbn = false,
    IReadOnlyList<string>? RateLimitedSources = null,
    IReadOnlyList<string>? NoResultSources = null,
    IReadOnlyList<string>? ErroredSources = null);
