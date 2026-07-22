using System;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Messages;
using BookDB.Logic.Services;
using BookDB.Models;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// The guided add-book entry stage: identify the book by ISBN. The lookup runs through the same
/// intake seam as the batch queue — enqueue (force-review) → processor → review — so there is no
/// parallel single-book pipeline; the duplicate check runs before anything is enqueued, and a
/// failed lookup offers the manual path with the ISBN carried over.
/// </summary>
public sealed partial class AddBookIdentifyViewModel :
    ObservableObject,
    ICloseGuard,
    IRecipient<BatchQueueProgressMessage>
{
    private readonly BatchQueueService _queueService;
    private readonly IBatchQueueProcessor _processor;
    private readonly IBookMetadataService _bookMetadataService;
    private readonly IWindowService _windowService;
    private readonly IBatchReviewRunner _reviewRunner;
    private readonly IMessenger _messenger;

    private int? _collectionId;

    /// <summary>Set by WindowService to close the dialog with a result.</summary>
    public Action<bool?>? CloseDialog { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManualEntryCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _isbnText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure))]
    private string _failureText = string.Empty;

    /// <summary>True after a failed lookup — shows the localized reason and highlights the manual path.</summary>
    public bool HasFailure => FailureText.Length > 0;

    public AddBookIdentifyViewModel(
        BatchQueueService queueService,
        IBatchQueueProcessor processor,
        IBookMetadataService bookMetadataService,
        IWindowService windowService,
        IBatchReviewRunner reviewRunner,
        IMessenger messenger)
    {
        _queueService = queueService;
        _processor = processor;
        _bookMetadataService = bookMetadataService;
        _windowService = windowService;
        _reviewRunner = reviewRunner;
        _messenger = messenger;

        messenger.RegisterAll(this);
    }

    /// <summary>The collection the flow files a new book into by default (the caller's current selection).</summary>
    public void Initialize(int? collectionId) => _collectionId = collectionId;

    private bool CanLookUp() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanLookUp))]
    private async Task LookUpAsync()
    {
        var raw = IsbnText.Trim();
        if (raw.Length == 0) return;
        var isbn = IsbnNormalizer.Normalize(raw);
        FailureText = string.Empty;

        try
        {
            // Duplicate check before anything is enqueued — earlier than the batch flow's
            // post-lookup check, so a known ISBN never wastes a lookup round.
            int? bookId = null;
            var existing = await _bookMetadataService.FindBookByIsbnAsync(isbn);
            if (existing is not null)
            {
                var choice = await _windowService.ShowDuplicateIsbnDialogAsync(
                    isbn, existing.Title ?? isbn);
                if (choice == DuplicateIsbnResult.Cancel) return;
                if (choice == DuplicateIsbnResult.UpdateExisting) bookId = existing.BookId;
                // AddAsNew: bookId stays null — the lookup creates a fresh record
            }

            var item = await _queueService.EnqueueAsync(isbn, bookId, forceReview: true);
            IsRunning = true;
            await _processor.StartBatch([item]);

            // The in-memory item is pre-processing state — re-read for status and payload.
            var processed = await _queueService.GetItemAsync(item.BatchQueueItemId);
            IsRunning = false;
            if (processed is null) return;

            if (processed.Status == BatchStatus.PendingReview)
            {
                CloseDialog?.Invoke(true);
                await _reviewRunner.ReviewItemAsync(processed, _collectionId);
            }
            else if (processed.Status == BatchStatus.Failed)
            {
                FailureText = string.Format(
                    Resources.AddBookIdentify_LookupFailed,
                    BatchFailureText.DescribeCode(processed.FailureCode));
            }
            else if (processed.Status is BatchStatus.Done or BatchStatus.AutoAccepted or BatchStatus.Skipped)
            {
                // Resolved without review (should not happen with the force-review flag, but a
                // terminal status means the item is done) — close so the list refreshes.
                CloseDialog?.Invoke(true);
            }
            // Pending/Processing: the lookup was cancelled (close guard) — the dialog is closing.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ISBN lookup failed in the identify dialog");
            FailureText = string.Format(
                Resources.AddBookIdentify_LookupFailed,
                BatchFailureText.DescribeCode(null));
        }
        finally
        {
            IsRunning = false;
            ProgressText = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLookUp))]
    private async Task ManualEntryAsync()
    {
        CloseDialog?.Invoke(false);
        var raw = IsbnText.Trim();
        var saved = await _windowService.ShowAddBookDialogAsync(
            _collectionId, prefillIsbn: raw.Length > 0 ? IsbnNormalizer.Normalize(raw) : null);
        // The identify dialog already returned false to its caller, so the refresh
        // the caller would have sent on true has to come from here.
        if (saved == true)
            _messenger.Send(new BookSavedMessage(0));
    }

    [RelayCommand]
    private async Task OpenWizardAsync()
    {
        CloseDialog?.Invoke(false);
        await _windowService.ShowLookupWizardDialogAsync();
    }

    [RelayCommand]
    private void Cancel() => CloseDialog?.Invoke(false);

    public void Receive(BatchQueueProgressMessage message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsRunning) return;
            if (message.StatusCode is not BatchProgressStatus.None and not BatchProgressStatus.Complete)
                ProgressText = BatchQueueWindowViewModel.DescribeStatus(
                    message.StatusCode, message.CurrentIsbn, message.ResultCount);
        });
    }

    public bool ShouldGuardClose => IsRunning;

    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsRunning) return true;

        var confirmed = await _windowService.ShowConfirmAsync(
            Resources.AddBookIdentify_CancelLookup_Title,
            Resources.AddBookIdentify_CancelLookup_Body);
        if (confirmed != true) return false;

        await _processor.CancelBatchAsync();
        return true;
    }
}
