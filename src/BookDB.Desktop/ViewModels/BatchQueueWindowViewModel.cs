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

/// <summary>One row of the completed-batch failure breakdown: a localized reason and how many items hit it.</summary>
public sealed record FailureReasonCount(string Reason, int Count);

public partial class BatchQueueWindowViewModel :
    ObservableObject,
    ICloseGuard,
    IRecipient<BookDB.Logic.Messages.BatchQueueProgressMessage>
{
    private readonly IBatchQueueProcessor _processor;
    private readonly BatchQueueService _queueService;
    private readonly IWindowService _windowService;
    private readonly IMessenger _messenger;
    private readonly IBatchReviewRunner _reviewRunner;

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

    /// <summary>
    /// Failed-item counts by localized reason, shown as a breakdown under the summary — a failure is
    /// never just "failed". Legacy rows without a stored code surface as the generic reason.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<FailureReasonCount> _failureReasons = [];

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
        IWindowService windowService,
        IMessenger messenger,
        IBatchReviewRunner reviewRunner)
    {
        _processor = processor;
        _queueService = queueService;
        _windowService = windowService;
        _messenger = messenger;
        _reviewRunner = reviewRunner;

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
        FailureReasons = [];
        IsComplete = false;
    }

    /// <summary>Localized text for a per-item progress stage; empty for stages with no text of their own.</summary>
    internal static string DescribeStatus(
        BookDB.Logic.Messages.BatchProgressStatus statusCode, string? currentIsbn, int resultCount) =>
        statusCode switch
        {
            BookDB.Logic.Messages.BatchProgressStatus.QueryingSources =>
                string.Format(Localization.Resources.BatchQueue_StatusQuerying, currentIsbn),
            BookDB.Logic.Messages.BatchProgressStatus.ProcessingResults =>
                string.Format(Localization.Resources.BatchQueue_StatusProcessing, resultCount),
            BookDB.Logic.Messages.BatchProgressStatus.FetchingCovers =>
                Localization.Resources.BatchQueue_StatusFetchingCovers,
            BookDB.Logic.Messages.BatchProgressStatus.Saving =>
                Localization.Resources.BatchQueue_StatusSaving,
            BookDB.Logic.Messages.BatchProgressStatus.Complete =>
                Localization.Resources.BatchQueue_StatusComplete,
            _ => string.Empty
        };

    public void Receive(BookDB.Logic.Messages.BatchQueueProgressMessage message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsRunning = message.IsRunning;
            ProcessedCount = message.Current;
            TotalCount = message.Total;
            CurrentIsbn = message.CurrentIsbn ?? string.Empty;
            if (message.StatusCode != BookDB.Logic.Messages.BatchProgressStatus.None)
                StatusText = DescribeStatus(message.StatusCode, message.CurrentIsbn, message.ResultCount);
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

            // Distinct codes can share a localized text (null and unknown both fall back), so group
            // by the displayed string to avoid duplicate rows.
            var failureCounts = await _queueService.GetFailureReasonCountsAsync();
            FailureReasons = failureCounts
                .GroupBy(f => Localization.BatchFailureText.DescribeCode(f.FailureCode))
                .Select(g => new FailureReasonCount(g.Key, g.Sum(f => f.Count)))
                .OrderByDescending(r => r.Count)
                .ToList();
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
                var outcome = await _reviewRunner.ReviewItemAsync(item);
                if (outcome == BatchReviewOutcome.NoPayload) continue;

                PendingReviewCount = Math.Max(0, PendingReviewCount - 1);
                if (outcome == BatchReviewOutcome.Saved) SavedCount++;
                else SkippedCount++;
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
        FailureReasons = [];
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

    public Task StartRecatalogAsync(IReadOnlyList<int> bookIds) =>
        StartRecatalogCoreAsync(() => _queueService.EnqueueRecatalogAsync(bookIds));

    /// <summary>
    /// Re-catalog for a single book whose record has no ISBN: the item is enqueued under the
    /// explicitly supplied ISBN, and its BookId routes the lookup result onto the existing book.
    /// </summary>
    public Task StartRecatalogAsync(int bookId, string isbn) =>
        StartRecatalogCoreAsync(async () => [await _queueService.EnqueueAsync(isbn, bookId)]);

    private async Task StartRecatalogCoreAsync(Func<Task<IReadOnlyList<BatchQueueItem>>> enqueue)
    {
        try
        {
            await _processor.CancelBatchAsync();
            ResetState();
            var items = await enqueue();
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
