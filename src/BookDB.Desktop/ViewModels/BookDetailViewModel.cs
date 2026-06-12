using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using BookDB.Models.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class BookDetailViewModel :
    BookEditViewModelBase,
    IRecipient<BookSelectedMessage>,
    IRecipient<BookSavedMessage>,
    IRecipient<LookupsChangedMessage>
{
    private readonly IMessenger _messenger;
    private readonly ILoanService _loanService;

    // Set synchronously in Receive(BookSavedMessage) when IsEditMode is true, so the
    // upcoming BookSelectedMessage (from list-row refresh) is suppressed before any
    // Dispatcher.Post callbacks can run and read a stale IsEditMode value.
    private bool _suppressNextBookSelectedForCurrentBook = false;

    // Tracks when CopyBookToEditFields is in progress so MarkDirty() can suppress
    // HasUnsavedChanges during field population.
    private bool _editingInProgress = false;

    [ObservableProperty]
    private bool _isEditMode = false;

    // --- Read-only display properties (computed from CurrentBook) ---

    public string BookTitle => CurrentBook?.Title ?? string.Empty;
    public string? AuthorDisplay
    {
        get
        {
            if (CurrentBook == null) return null;
            var names = CurrentBook.Contributors
                .Where(c => c.ContributorRole?.Code == "Author")
                .OrderBy(c => c.SortOrder)
                .Select(c => c.Person?.DisplayName)
                .Where(n => n != null)
                .ToList();
            return names.Count > 0 ? string.Join(", ", names) : null;
        }
    }
    public string? PublisherDisplay => CurrentBook?.Publisher?.Name;
    public string? YearDisplay => CurrentBook?.PubDate;
    public string? FormatDisplay => CurrentBook?.Format?.Name;
    public string? SeriesDisplay => CurrentBook?.Series?.Name;
    public string? IsbnDisplay => CurrentBook?.Isbn;
    public string? PagesDisplay => CurrentBook?.Pages?.ToString();
    public string? RatingDisplay => CurrentBook?.Rating?.Name;
    public string? StatusDisplay => CurrentBook?.Status?.Name;

    public string? AltTitleDisplay => CurrentBook?.AltTitle;

    public string? SeriesWithNumberDisplay
    {
        get
        {
            if (CurrentBook?.Series == null) return null;
            var name = CurrentBook.Series.Name;
            var number = CurrentBook.SeriesNumber;
            return string.IsNullOrEmpty(number) ? name : $"{name} #{number}";
        }
    }

    public string? EditionDisplay => CurrentBook?.Edition?.Name;

    public string? LanguageDisplay => CurrentBook?.Language?.Name;

    public string? CategoryDisplay
    {
        get
        {
            if (CurrentBook == null) return null;
            var names = CurrentBook.Categories
                .Select(c => c.Category?.Name)
                .Where(n => n != null)
                .ToList();
            return names.Count > 0 ? string.Join(", ", names) : null;
        }
    }

    public string? PublisherYearDisplay
    {
        get
        {
            var pub = PublisherDisplay;
            var year = YearDisplay;
            if (string.IsNullOrEmpty(pub) && string.IsNullOrEmpty(year)) return null;
            if (string.IsNullOrEmpty(year)) return pub;
            if (string.IsNullOrEmpty(pub)) return year;
            return $"{pub}, {year}";
        }
    }

    public string? ConditionDisplay => CurrentBook?.Condition?.Name;

    public string? LocationDisplay => CurrentBook?.Location?.Name;

    public string? OwnerDisplay => CurrentBook?.Owner?.Name;

    public string? BookInfoDisplay => CurrentBook?.BookInfo;

    public bool HasAnyImage => ImageEditor.ImageTypeButtons.Any(b => b.Bitmap != null);

    // --- Loan display properties ---
    public bool IsLoanVisible { get; private set; }
    public string LoanStatusDisplay { get; private set; } = string.Empty;
    public IBrush LoanStatusForeground { get; private set; } = Brushes.Transparent;

    // --- Loan history (inline edit panel mirrors FullDetailsWindowViewModel) ---
    private readonly System.Collections.ObjectModel.ObservableCollection<LoanHistoryRowViewModel> _loanHistory = [];
    public override System.Collections.Generic.IEnumerable<LoanHistoryRowViewModel> LoanHistory => _loanHistory;
    public override bool LoanHistoryIsEmpty => _loanHistory.Count == 0;

    public override async Task OnLoanHistoryTabActivatingAsync()
    {
        if (CurrentBook == null) return;
        var history = await _loanService.GetLoanHistoryAsync(CurrentBook.BookId);
        _loanHistory.Clear();
        foreach (var row in history)
            _loanHistory.Add(LoanHistoryRowViewModel.FromLoanHistoryRow(row));
        OnPropertyChanged(nameof(LoanHistoryIsEmpty));
    }

    public BookDetailViewModel(
        IMessenger messenger,
        IBookService bookService,
        IBookImageService bookImageService,
        ILookupService lookupService,
        IWindowService windowService,
        IFilePickerService filePickerService,
        IHttpClientFactory httpClientFactory,
        ILoanService loanService)
        : base(bookService, bookImageService, lookupService, filePickerService, windowService, httpClientFactory)
    {
        _messenger = messenger;
        _loanService = loanService;
        _messenger.RegisterAll(this);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CurrentBook))
                NotifyDisplayPropertiesChanged();
        };
    }

    public void Receive(BookSavedMessage message)
    {
        if (CurrentBook == null || IsEditMode)
        {
            // Set flag synchronously so the upcoming spurious BookSelectedMessage
            // (from list-row replacement triggered by the save) is suppressed
            // before any Dispatcher.Post callback can read a stale IsEditMode.
            if (IsEditMode && message.Value == CurrentBook?.BookId)
                _suppressNextBookSelectedForCurrentBook = true;
            return;
        }
        if (message.Value != CurrentBook.BookId) return;
        UIThreadHelper.PostAsync(() => LoadBookAsync(CurrentBook.BookId), "reload book after save");
    }

    public void Receive(LookupsChangedMessage message)
    {
        // Only refresh when the edit form is open — no point reloading when in read-only mode.
        if (!IsEditMode) return;
        UIThreadHelper.PostAsync(() => ReloadLookupsOnChangeAsync(), "reload lookups after change");
    }

    public void Receive(BookSelectedMessage message)
    {
        UIThreadHelper.PostAsync(async () =>
        {
            // Suppress spurious re-selection triggered by list-row replacement after an
            // image save. Flag is set synchronously in Receive(BookSavedMessage) so it
            // is always visible before this Post callback executes.
            if (_suppressNextBookSelectedForCurrentBook &&
                (message.Value == null || message.Value == CurrentBook?.BookId))
            {
                _suppressNextBookSelectedForCurrentBook = false;
                return;
            }
            _suppressNextBookSelectedForCurrentBook = false;

            // Belt-and-suspenders: also guard on IsEditMode for same-book re-selects
            // that arrive outside the flag window.
            if (IsEditMode && (message.Value == null || message.Value == CurrentBook?.BookId))
                return;

            if (HasUnsavedChanges)
            {
                var canNavigate = await TryNavigateAwayAsync();
                if (!canNavigate) return;
            }
            await LoadBookAsync(message.Value);
            if (message.OpenInEditMode && CurrentBook != null)
                await EnterEditModeAsync();
        }, "load book on selection");
    }

    // --- Commands ---

    [RelayCommand]
    private async Task EnterEditModeAsync()
    {
        await LoadLookupsAsync();
        CopyBookToEditFields();
        IsEditMode = true;
        HasUnsavedChanges = false;
        await ImageEditor.ResetToSaved();
    }

    protected override async Task SaveAsync()
    {
        if (CurrentBook == null) return;
        if (string.IsNullOrWhiteSpace(EditTitle)) return;  // guard matches base early-exit condition
        var bookId = CurrentBook.BookId; // capture before await — CurrentBook may be reassigned
        await base.SaveAsync();
        // Only exit edit mode if the base save actually cleared unsaved changes
        if (!HasUnsavedChanges)
        {
            IsEditMode = false;
            await LoadBookAsync(bookId);
        }
    }

    protected override async Task CancelEditAsync()
    {
        var hadUnsavedChanges = HasUnsavedChanges;
        await base.CancelEditAsync();
        // base.CancelEditAsync returns early (without clearing HasUnsavedChanges) on KeepEditing.
        // If there were unsaved changes and HasUnsavedChanges is still true after base returns,
        // the user chose KeepEditing — do NOT flip IsEditMode.
        if (hadUnsavedChanges && HasUnsavedChanges)
            return; // User chose KeepEditing
        IsEditMode = false;
        NotifyDisplayPropertiesChanged(); // recompute HasAnyImage after edit session
    }

    protected override void MarkDirty()
    {
        if (!_editingInProgress && IsEditMode)
            HasUnsavedChanges = true;
    }

    protected override void CopyBookToEditFields()
    {
        _editingInProgress = true;
        try { base.CopyBookToEditFields(); }
        finally { _editingInProgress = false; }
    }

    [RelayCommand]
    private void OpenFullDetails()
    {
        if (CurrentBook != null)
            _windowService.OpenFullDetailsWindow(CurrentBook.BookId);
    }

    [RelayCommand]
    private async Task RecatalogAsync()
    {
        if (CurrentBook == null) return;

        string? isbn = CurrentBook.Isbn;

        if (string.IsNullOrWhiteSpace(isbn))
        {
            isbn = await _windowService.ShowIsbnPromptDialogAsync();
            if (string.IsNullOrWhiteSpace(isbn)) return;
        }

        try
        {
            await _windowService.StartBatchRecatalogAsync(new[] { CurrentBook.BookId });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start re-catalog for book {BookId}", CurrentBook?.BookId);
        }
    }

    // --- Methods ---

    public async Task LoadBookAsync(int? bookId)
    {
        _loanHistory.Clear();
        OnPropertyChanged(nameof(LoanHistoryIsEmpty));
        if (bookId == null)
        {
            CurrentBook = null;
            IsEditMode = false;
            HasUnsavedChanges = false;
            IsLoanVisible = false;
            LoanStatusDisplay = string.Empty;
            LoanStatusForeground = Brushes.Transparent;
            ImageEditor.ClearForNoBook();
            NotifyDisplayPropertiesChanged();
            return;
        }

        try
        {
            var book = await _bookService.GetBookByIdAsync(bookId.Value);
            CurrentBook = book;
            IsEditMode = false;
            HasUnsavedChanges = false;

            if (book == null)
            {
                IsLoanVisible = false;
                LoanStatusDisplay = string.Empty;
                LoanStatusForeground = Brushes.Transparent;
                ImageEditor.ClearForNoBook();
                NotifyDisplayPropertiesChanged();
                return;
            }

            // Load active loan status
            var loan = await _loanService.GetActiveLoanAsync(book.BookId);
            if (loan == null)
            {
                IsLoanVisible = false;
                LoanStatusDisplay = string.Empty;
                LoanStatusForeground = Brushes.Transparent;
            }
            else
            {
                var (name, dueDate) = loan.Value;
                bool isOverdue = dueDate.HasValue && dueDate.Value.Date < DateTime.UtcNow.Date;
                IsLoanVisible = true;
                if (isOverdue)
                {
                    LoanStatusDisplay = string.Format(Resources.BookDetail_OverdueStatus_Format, name);
                    LoanStatusForeground = Helpers.Palette.Brush("BrushWarning", Brushes.Orange);
                }
                else
                {
                    LoanStatusDisplay = string.Format(Resources.BookDetail_LoanStatus_Format, name, dueDate?.ToLocalTime().ToShortDateString() ?? string.Empty);
                    LoanStatusForeground = Helpers.Palette.Brush("BrushTextSecondary", Brushes.Gray);
                }
            }

            await ImageEditor.LoadForBookAsync(book);

            NotifyDisplayPropertiesChanged();

            // Eagerly load loan history so the tab shows current data when the book changes
            // (tab-activation event does not re-fire when the book switches while tab stays open)
            var history = await _loanService.GetLoanHistoryAsync(book.BookId);
            foreach (var row in history)
                _loanHistory.Add(LoanHistoryRowViewModel.FromLoanHistoryRow(row));
            OnPropertyChanged(nameof(LoanHistoryIsEmpty));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load book {BookId}", bookId);
        }
    }

    public async Task<bool> TryNavigateAwayAsync()
    {
        if (!HasUnsavedChanges) return true;

        var result = await _windowService.ShowUnsavedChangesDialogAsync(BookTitle);
        return result switch
        {
            UnsavedChangesResult.Save => await SaveAndReturnTrueAsync(),
            UnsavedChangesResult.Discard => true,
            _ => false // KeepEditing
        };
    }

    private async Task<bool> SaveAndReturnTrueAsync()
    {
        await SaveAsync();
        return true;
    }

    private void NotifyDisplayPropertiesChanged()
    {
        OnPropertyChanged(nameof(BookTitle));
        OnPropertyChanged(nameof(AuthorDisplay));
        OnPropertyChanged(nameof(PublisherDisplay));
        OnPropertyChanged(nameof(YearDisplay));
        OnPropertyChanged(nameof(PublisherYearDisplay));
        OnPropertyChanged(nameof(FormatDisplay));
        OnPropertyChanged(nameof(SeriesDisplay));
        OnPropertyChanged(nameof(IsbnDisplay));
        OnPropertyChanged(nameof(PagesDisplay));
        OnPropertyChanged(nameof(RatingDisplay));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(AltTitleDisplay));
        OnPropertyChanged(nameof(SeriesWithNumberDisplay));
        OnPropertyChanged(nameof(EditionDisplay));
        OnPropertyChanged(nameof(LanguageDisplay));
        OnPropertyChanged(nameof(CategoryDisplay));
        OnPropertyChanged(nameof(ConditionDisplay));
        OnPropertyChanged(nameof(LocationDisplay));
        OnPropertyChanged(nameof(OwnerDisplay));
        OnPropertyChanged(nameof(BookInfoDisplay));
        OnPropertyChanged(nameof(HasAnyImage));
        OnPropertyChanged(nameof(IsLoanVisible));
        OnPropertyChanged(nameof(LoanStatusDisplay));
        OnPropertyChanged(nameof(LoanStatusForeground));
    }
}
