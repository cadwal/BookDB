using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Messages;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class BatchQueueWindowViewModel :
    ObservableObject,
    ICloseGuard,
    IRecipient<BookDB.Logic.Messages.BatchQueueProgressMessage>
{
    private readonly IBatchQueueProcessor _processor;
    private readonly BatchQueueService _queueService;
    private readonly IBookService _bookService;
    private readonly IBookMetadataService _bookMetadataService;
    private readonly IBookImageService _bookImageService;
    private readonly IWindowService _windowService;
    private readonly IMessenger _messenger;
    private readonly CoverFetchService _coverFetcher;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isComplete;

    /// <summary>
    /// True when neither running nor complete — shows the idle "No batch in progress" message.
    /// </summary>
    public bool IsIdle => !IsRunning && !IsComplete;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int _processedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private int _totalCount;

    [ObservableProperty]
    private string _currentIsbn = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <summary>
    /// Current operation status text, e.g. "Querying Google Books…"
    /// </summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _savedCount;

    [ObservableProperty]
    private int _autoAcceptedCount;

    [ObservableProperty]
    private int _notFoundCount;

    [ObservableProperty]
    private int _pendingReviewCount;

    [ObservableProperty]
    private int _skippedCount;

    public double ProgressPercent =>
        TotalCount > 0 ? (double)ProcessedCount / TotalCount * 100.0 : 0.0;

    /// <summary>
    /// Set by WindowService or code-behind to close the window.
    /// </summary>
    public Action? CloseWindow { get; set; }

    /// <summary>
    /// Set by WindowService to minimize the window.
    /// </summary>
    public Action? MinimizeWindow { get; set; }

    public BatchQueueWindowViewModel(
        IBatchQueueProcessor processor,
        BatchQueueService queueService,
        IBookService bookService,
        IBookMetadataService bookMetadataService,
        IBookImageService bookImageService,
        IWindowService windowService,
        IMessenger messenger,
        CoverFetchService coverFetcher)
    {
        _processor = processor;
        _queueService = queueService;
        _bookService = bookService;
        _bookMetadataService = bookMetadataService;
        _bookImageService = bookImageService;
        _windowService = windowService;
        _messenger = messenger;
        _coverFetcher = coverFetcher;

        messenger.RegisterAll(this);
    }

    /// <summary>
    /// Resets all stats counters to zero. Called when the window is opened after a completed or idle run.
    /// Only blocked when a batch is actively running to avoid clearing live progress.
    /// </summary>
    public void ResetStats()
    {
        if (IsRunning) return;

        ProcessedCount = 0;
        TotalCount = 0;
        CurrentIsbn = string.Empty;
        ProgressText = string.Empty;
        StatusText = string.Empty;
        SavedCount = 0;
        AutoAcceptedCount = 0;
        NotFoundCount = 0;
        PendingReviewCount = 0;
        SkippedCount = 0;
        IsComplete = false;
    }

    public void Receive(BookDB.Logic.Messages.BatchQueueProgressMessage message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsRunning = message.IsRunning;
            ProcessedCount = message.Current;
            TotalCount = message.Total;
            CurrentIsbn = message.CurrentIsbn ?? string.Empty;
            if (message.StatusCode != BookDB.Logic.Messages.BatchProgressStatus.None)
                StatusText = message.StatusCode switch
                {
                    BookDB.Logic.Messages.BatchProgressStatus.QueryingSources =>
                        string.Format(Localization.Resources.BatchQueue_StatusQuerying, message.CurrentIsbn),
                    BookDB.Logic.Messages.BatchProgressStatus.ProcessingResults =>
                        string.Format(Localization.Resources.BatchQueue_StatusProcessing, message.ResultCount),
                    BookDB.Logic.Messages.BatchProgressStatus.Saving =>
                        Localization.Resources.BatchQueue_StatusSaving,
                    BookDB.Logic.Messages.BatchProgressStatus.Complete =>
                        Localization.Resources.BatchQueue_StatusComplete,
                    _ => string.Empty
                };
            ProgressText = TotalCount > 0
                ? string.Format(
                    Localization.Resources.BatchQueue_ProgressCounts,
                    message.Current,
                    message.Total)
                : string.Empty;

            if (!message.IsRunning && (message.Total > 0 || IsComplete))
            {
                IsComplete = true;
                IsRunning = false;
                StatusText = string.Empty;
                _ = LoadSummaryAsync();
                // Refresh book list so newly downloaded covers appear without restarting the app.
                _messenger.Send(new BookSavedMessage(0));
            }
        });
    }

    private async Task LoadSummaryAsync()
    {
        try
        {
            var summary = await _queueService.GetSummaryAsync();
            SavedCount = summary.Saved;
            AutoAcceptedCount = summary.AutoAccepted;
            NotFoundCount = summary.NotFound;
            PendingReviewCount = summary.PendingReview;
            SkippedCount = summary.Skipped;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load batch queue summary");
        }
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync()
    {
        await _processor.PauseAsync();
        IsPaused = true;
    }

    private bool CanPause() => IsRunning && !IsPaused;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        _processor.Resume();
        IsPaused = false;
    }

    private bool CanResume() => IsPaused;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task Cancel()
    {
        try
        {
            await _processor.CancelBatchAsync();
            IsRunning = false;
            IsPaused = false;
            IsComplete = true;
            StatusText = string.Empty;
            await LoadSummaryAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cancelling batch processor");
        }
    }

    private bool CanCancel() => IsRunning;

    [RelayCommand]
    private async Task StartReview()
    {
        try
        {
            var pendingItems = await _queueService.GetItemsByStatusAsync(BatchStatus.PendingReview);
            foreach (var item in pendingItems)
            {
                if (item.ResultJson is null) continue;

                // Try to deserialize as the new BatchReviewPayload (includes ExistingBookId and CurrentBookMetadata).
                // Fall back to old plain List<BookMetadata> format for backwards compatibility.
                List<BookMetadata>? sources = null;
                int? existingBookId = item.BookId;
                BookMetadata? currentBook = null;
                bool wasNewIsbn = false;

                try
                {
                    var payload = JsonSerializer.Deserialize<BatchReviewPayload>(item.ResultJson);
                    if (payload?.Sources is not null)
                    {
                        sources = [.. payload.Sources];
                        existingBookId = payload.ExistingBookId ?? item.BookId;
                        currentBook = payload.CurrentBookMetadata;
                        wasNewIsbn = payload.WasNewIsbn;
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
                        continue;
                    }
                }

                if (sources is null || sources.Count == 0) continue;

                // Safety net (Bug 5): if the stored diffs are empty when recomputed against the
                // stored current-book baseline, auto-accept without showing the dialog at all.
                // This should not happen after Bug 1 is fixed in the processor, but protects
                // against stale PendingReview items from before the fix.
                var diffs = FieldDiffComputer.ComputeDiffs(sources, currentBook);
                if (diffs.Count == 0)
                {
                    try
                    {
                        if (existingBookId.HasValue)
                            await _bookMetadataService.UpdateBookFromMetadataAsync(existingBookId.Value, sources[0], null);
                        else
                            await _bookMetadataService.AddBookFromMetadataAsync(sources[0], null, null);

                        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Done, null);
                        PendingReviewCount = Math.Max(0, PendingReviewCount - 1);
                        SavedCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Auto-accept failed for zero-diff item {ItemId}", item.BatchQueueItemId);
                        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null);
                        PendingReviewCount = Math.Max(0, PendingReviewCount - 1);
                        SkippedCount++;
                    }
                    continue;
                }

                // Gap 3: Show duplicate ISBN dialog when a LookupWizard item maps to an existing book.
                if (wasNewIsbn && existingBookId.HasValue)
                {
                    var existingBook = await _bookService.GetBookByIdAsync(existingBookId.Value);
                    var existingTitle = existingBook?.Title ?? sources.FirstOrDefault()?.Title ?? "(unknown)";
                    var isbn = sources.Select(s => s.Isbn).FirstOrDefault(i => !string.IsNullOrEmpty(i)) ?? item.Isbn;

                    var choice = await _windowService.ShowDuplicateIsbnDialogAsync(isbn, existingTitle);

                    if (choice == DuplicateIsbnResult.Cancel)
                    {
                        await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null);
                        PendingReviewCount = Math.Max(0, PendingReviewCount - 1);
                        SkippedCount++;
                        continue;
                    }
                    if (choice == DuplicateIsbnResult.AddAsNew)
                    {
                        // Treat as new book — clear existingBookId so dialog creates a fresh record
                        existingBookId = null;
                    }
                    // DuplicateIsbnResult.UpdateExisting: keep existingBookId, fall through to review dialog
                }

                // Gap 2: Fetch cover images from sources before opening the review dialog.
                var coverOptions = new List<CoverOption>();
                var coverIsbn = sources.Select(s => s.Isbn).FirstOrDefault(i => !string.IsNullOrEmpty(i)) ?? item.Isbn;
                foreach (var source in sources.Where(s => !string.IsNullOrEmpty(s.CoverImageUrl)))
                {
                    try
                    {
                        var imageBytes = await _coverFetcher.DownloadCoverAsync(
                            source.CoverImageUrl!, coverIsbn, source.SourceName ?? "Unknown");
                        if (imageBytes is not null)
                        {
                            // Decode bitmap from bytes for thumbnail and full-size display.
                            var bitmap = CoverFetchService.DecodeBitmap(imageBytes);
                            coverOptions.Add(new CoverOption
                            {
                                SourceName = source.SourceName ?? "Unknown",
                                ImageData = imageBytes,
                                RemoteUrl = source.CoverImageUrl,
                                ThumbnailBitmap = bitmap,
                                FullBitmap = bitmap
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Cover fetch failed for source {Source} ISBN {Isbn}",
                            source.SourceName, coverIsbn);
                    }
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

                // Show dialog with the batch window as owner so it appears on top.
                // Pass the stored current-book metadata so the dialog computes the same diffs
                // as the processor did (not against null, which would give different results).
                var result = await _windowService.ShowMergeReviewDialogAsync(
                    sources: sources,
                    currentBook: currentBook,
                    coverOptions: coverOptions,
                    existingBookId: existingBookId,
                    collectionId: null);

                if (result == true)
                {
                    await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Done, null);
                    PendingReviewCount = Math.Max(0, PendingReviewCount - 1);
                    SavedCount++;
                }
                else
                {
                    // User cancelled or dialog closed with an error — mark as Skipped so the
                    // same item does not reappear in the review queue on the next run.
                    await _queueService.UpdateStatusAsync(item.BatchQueueItemId, BatchStatus.Skipped, null);
                    PendingReviewCount = Math.Max(0, PendingReviewCount - 1);
                    SkippedCount++;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during start review");
        }
    }

    public bool ShouldGuardClose => IsRunning;

    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsRunning) return true;

        var result = await _windowService.ShowBatchShutdownWarningAsync();
        if (result == true)
        {
            PauseCommand.Execute(null);
            return true;
        }

        return false;
    }

    [RelayCommand]
    private async Task Close()
    {
        try
        {
            await _queueService.ClearCompletedAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear completed batch items on close");
        }
        CloseWindow?.Invoke();
    }

    [RelayCommand]
    private void Minimize()
    {
        MinimizeWindow?.Invoke();
    }

    /// <summary>
    /// Resets all UI state so a new batch can be started cleanly.
    /// </summary>
    private void ResetState()
    {
        IsRunning = false;
        IsPaused = false;
        IsComplete = false;
        ProcessedCount = 0;
        TotalCount = 0;
        CurrentIsbn = string.Empty;
        StatusText = string.Empty;
        ProgressText = string.Empty;
        SavedCount = 0;
        AutoAcceptedCount = 0;
        NotFoundCount = 0;
        PendingReviewCount = 0;
        SkippedCount = 0;
    }

    public async Task StartBatchAsync(IReadOnlyList<string> isbns)
    {
        try
        {
            await _processor.CancelBatchAsync(); // cancel any running batch first
            ResetState();
            var items = await _queueService.EnqueueBatchAsync(isbns);
            if (items.Count == 0)
            {
                // All ISBNs were deduplicated (recently completed or already active).
                // Show the summary from the DB immediately — no processing to do.
                IsComplete = true;
                await LoadSummaryAsync();
                return;
            }
            IsRunning = true;
            IsComplete = false;
            ProcessedCount = 0;
            TotalCount = items.Count;
            _ = _processor.StartBatch(items); // fire-and-forget; progress via IMessenger
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start batch lookup");
        }
    }

    public async Task StartRecatalogAsync(IReadOnlyList<int> bookIds)
    {
        try
        {
            await _processor.CancelBatchAsync();
            ResetState();
            var items = await _queueService.EnqueueRecatalogAsync(bookIds);
            if (items.Count == 0)
            {
                // All books already have an active Pending/Processing item in the queue.
                // Show the current summary — nothing new to enqueue.
                IsComplete = true;
                await LoadSummaryAsync();
                return;
            }
            IsRunning = true;
            IsComplete = false;
            ProcessedCount = 0;
            TotalCount = items.Count;
            _ = _processor.StartBatch(items); // fire-and-forget; progress via IMessenger
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start re-catalog");
        }
    }
}
