using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class FullDetailsWindowViewModel : BookEditViewModelBase, ICloseGuard, IRecipient<LookupsChangedMessage>
{
    private readonly IMessenger _messenger;
    private readonly ILoanService _loanService;

    public string WindowTitle => string.Format(Localization.Resources.FullDetails_WindowTitle, EditTitle);

    private readonly ObservableCollection<LoanHistoryRowViewModel> _loanHistory = [];
    public override IEnumerable<LoanHistoryRowViewModel> LoanHistory => _loanHistory;
    public override bool LoanHistoryIsEmpty => _loanHistory.Count == 0;

    public FullDetailsWindowViewModel(
        IBookService bookService,
        IBookImageService bookImageService,
        ILookupService lookupService,
        IFilePickerService filePickerService,
        IMessenger messenger,
        IWindowService windowService,
        IRemoteWriteGuard writeGuard,
        IHttpClientFactory httpClientFactory,
        ILoanService loanService,
        IConnectionHealthMonitor connectionMonitor,
        IConnectionFailureClassifier connectionClassifier)
        : base(bookService, bookImageService, lookupService, filePickerService, windowService, writeGuard, httpClientFactory, connectionMonitor, connectionClassifier)
    {
        _messenger = messenger;
        _loanService = loanService;
        _messenger.RegisterAll(this);
        // Wire PropertyChanged to notify WindowTitle whenever EditTitle changes.
        // Cannot use partial void OnEditTitleChanged here — that hook belongs to the declaring class
        // (BookEditViewModelBase). PropertyChanged subscription is the correct pattern for subclasses.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditTitle))
                OnPropertyChanged(nameof(WindowTitle));
        };
        // Notify LoanHistoryIsEmpty whenever the loan history collection changes.
        _loanHistory.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LoanHistoryIsEmpty));
        // Note: Contributors.CollectionChanged is wired in base constructor — do NOT re-add here
    }

    public void Receive(LookupsChangedMessage message)
    {
        // FullDetailsWindow is always open in edit mode — refresh lookups immediately
        // so newly added categories/purchase places appear without re-opening the window.
        UIThreadHelper.PostAsync(() => ReloadLookupsOnChangeAsync(), "reload lookups after change");
    }

    // ICloseGuard implementation

    public Action? CloseWindow { get; set; }

    public bool ShouldGuardClose => HasUnsavedChanges;

    protected override async Task CancelEditAsync()
    {
        var hadUnsavedChanges = HasUnsavedChanges;
        await base.CancelEditAsync();
        // If HasUnsavedChanges is still true after base returns, user chose KeepEditing — stay open.
        if (hadUnsavedChanges && HasUnsavedChanges)
            return;
        CloseWindow?.Invoke();
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (!HasUnsavedChanges) return true;

        var result = await _windowService.ShowUnsavedChangesDialogAsync(EditTitle);
        switch (result)
        {
            case UnsavedChangesResult.Save:
                await SaveAsync();
                return true;

            case UnsavedChangesResult.Discard:
                // WARNING: Do NOT call CancelEditAsync() here, even though it also reverts fields.
                // Reason: CancelEditAsync() checks HasUnsavedChanges first and would show the
                // Save/Discard/Keep Editing dialog AGAIN (double-dialog). We are already INSIDE
                // the dialog result handler — HasUnsavedChanges is still true at this point.
                // Use the inline revert below instead.
                await ImageEditor.ResetToSaved();
                if (CurrentBook != null) CopyBookToEditFields();
                HasUnsavedChanges = false;
                return true;

            default:
                return false; // KeepEditing — window stays open
        }
    }

    /// <summary>
    /// Loads the book; returns false when nothing could be loaded (book missing, or the remote connection is
    /// down — reported to the status indicator) so the caller can avoid opening a blank window.
    /// </summary>
    public async Task<bool> LoadBookAsync(int bookId)
    {
        try
        {
            var book = await _bookService.GetBookByIdAsync(bookId);
            CurrentBook = book;
            if (book == null) return false;

            await LoadLookupsAsync();
            CopyBookToEditFields();
            HasUnsavedChanges = false;

            await ImageEditor.LoadForBookAsync(book);

            var history = await _loanService.GetLoanHistoryAsync(bookId);
            _loanHistory.Clear();
            foreach (var row in history)
                _loanHistory.Add(LoanHistoryRowViewModel.FromLoanHistoryRow(row));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "FullDetailsWindowViewModel: Failed to load book {BookId}", bookId);
            ReportIfConnectionLoss(ex);
            return false;
        }
    }

    public override async Task OnLoanHistoryTabActivatingAsync()
    {
        if (CurrentBook == null) return;
        try
        {
            var history = await _loanService.GetLoanHistoryAsync(CurrentBook.BookId);
            _loanHistory.Clear();
            foreach (var row in history)
                _loanHistory.Add(LoanHistoryRowViewModel.FromLoanHistoryRow(row));
        }
        catch (Exception ex)
        {
            ReportIfConnectionLoss(ex);
            Log.Error(ex, "FullDetailsWindowViewModel: Failed to reload loan history for book {BookId}", CurrentBook.BookId);
        }
    }
}
