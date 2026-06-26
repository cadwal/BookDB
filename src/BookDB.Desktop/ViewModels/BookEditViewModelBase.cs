using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public abstract partial class BookEditViewModelBase : ObservableObject
{
    protected readonly IBookService _bookService;
    protected readonly ILookupService _lookupService;
    protected readonly IFilePickerService _filePickerService;
    protected readonly IWindowService _windowService;
    private readonly IRemoteWriteGuard _writeGuard;
    private readonly IConnectionHealthMonitor _connectionMonitor;
    private readonly IConnectionFailureClassifier _connectionClassifier;
    private bool _loadingInProgress;
    private bool _suppressContributorDirty;

    [ObservableProperty] private Book? _currentBook;
    [ObservableProperty] private bool _hasUnsavedChanges;

    public ImageEditorViewModel ImageEditor { get; }

    // Basic Info tab
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editSubtitle = string.Empty;
    [ObservableProperty] private string _editAltTitle = string.Empty;
    [ObservableProperty] private int? _editPublisherId;
    [ObservableProperty] private string _editPubDate = string.Empty;
    [ObservableProperty] private int? _editFormatId;
    [ObservableProperty] private int? _editEditionId;
    [ObservableProperty] private int? _editLanguageId;
    [ObservableProperty] private int? _editSeriesId;
    [ObservableProperty] private string _editSeriesNumber = string.Empty;
    [ObservableProperty] private string _editIsbn = string.Empty;
    [ObservableProperty] private string _editExternalId = string.Empty;

    // Details tab
    [ObservableProperty] private int? _editPages;
    [ObservableProperty] private int _editCopies = 1;
    [ObservableProperty] private int _editReadCount = 0;
    [ObservableProperty] private int? _editRatingId;
    [ObservableProperty] private int? _editConditionId;
    [ObservableProperty] private int? _editStatusId;
    [ObservableProperty] private int? _editReadingLevelId;
    [ObservableProperty] private string _editKeywords = string.Empty;
    [ObservableProperty] private string _editComments = string.Empty;
    [ObservableProperty] private string _editBookInfo = string.Empty;
    [ObservableProperty] private bool _editFavorite;
    [ObservableProperty] private bool _editSigned;
    [ObservableProperty] private bool _editOutOfPrint;

    // Acquisition tab
    [ObservableProperty] private decimal? _editPurchasePrice;
    [ObservableProperty] private string _editPurchaseCurrency = string.Empty;
    [ObservableProperty] private decimal? _editListPrice;
    [ObservableProperty] private string _editListPriceCurrency = string.Empty;
    [ObservableProperty] private int? _editPurchasePlaceId;
    [ObservableProperty] private string _editPurchaseDate = string.Empty;
    [ObservableProperty] private string _editCopyrightDate = string.Empty;
    [ObservableProperty] private string _editPubPlace = string.Empty;
    [ObservableProperty] private int? _editSourceId;

    // Contributors / misc tab
    [ObservableProperty] private int? _editLocationId;
    [ObservableProperty] private int? _editOwnerId;
    [ObservableProperty] private string _editMediaLink = string.Empty;
    [ObservableProperty] private bool _editDisplay = true;

    // Additional Info tab
    [ObservableProperty] private string? _issn;
    [ObservableProperty] private string? _lccn;
    [ObservableProperty] private string? _deweyDecimal;
    [ObservableProperty] private string? _callNumber;
    [ObservableProperty] private string? _dimensions;
    [ObservableProperty] private decimal? _weight;
    [ObservableProperty] private decimal? _itemValue;
    [ObservableProperty] private string? _valuationDate;
    [ObservableProperty] private decimal? _amazonNewValue;
    [ObservableProperty] private decimal? _amazonUsedValue;
    [ObservableProperty] private decimal? _amazonCollectibleValue;
    [ObservableProperty] private int? _amazonNewCount;
    [ObservableProperty] private int? _amazonUsedCount;
    [ObservableProperty] private int? _amazonCollectibleCount;
    [ObservableProperty] private int? _salesRank;
    [ObservableProperty] private int? _lexileLevel;

    // Lookup collections for dropdowns
    public ObservableCollection<Format> Formats { get; } = [];
    public ObservableCollection<Publisher> Publishers { get; } = [];
    public ObservableCollection<Series> SeriesList { get; } = [];
    public ObservableCollection<Language> Languages { get; } = [];
    public ObservableCollection<Edition> Editions { get; } = [];
    public ObservableCollection<Rating> Ratings { get; } = [];
    public ObservableCollection<Condition> Conditions { get; } = [];
    public ObservableCollection<Status> Statuses { get; } = [];
    public ObservableCollection<Location> Locations { get; } = [];
    public ObservableCollection<Owner> Owners { get; } = [];
    public ObservableCollection<ReadingLevel> ReadingLevels { get; } = [];
    public ObservableCollection<PurchasePlace> PurchasePlaces { get; } = [];
    public ObservableCollection<Source> Sources { get; } = [];
    public ObservableCollection<CategorySelectionItem> CategoryRows { get; } = [];
    public ObservableCollection<ContributorRole> ContributorRoles { get; } = [];
    public ObservableCollection<ContributorRowViewModel> Contributors { get; } = [];
    public ObservableCollection<string> AvailablePersonNames { get; } = [];

    // Loan History tab — overridden in FullDetailsWindowViewModel with real data.
    // Base returns empty so BookDetailViewModel (which also uses BookEditForm) compiles cleanly.
    public virtual IEnumerable<LoanHistoryRowViewModel> LoanHistory => [];
    public virtual bool LoanHistoryIsEmpty => true;
    public virtual Task OnLoanHistoryTabActivatingAsync() => Task.CompletedTask;

    protected BookEditViewModelBase(
        IBookService bookService,
        IBookImageService bookImageService,
        ILookupService lookupService,
        IFilePickerService filePickerService,
        IWindowService windowService,
        IRemoteWriteGuard writeGuard,
        IHttpClientFactory httpClientFactory,
        IConnectionHealthMonitor connectionMonitor,
        IConnectionFailureClassifier connectionClassifier)
    {
        _bookService = bookService;
        _lookupService = lookupService;
        _filePickerService = filePickerService;
        _windowService = windowService;
        _writeGuard = writeGuard;
        _connectionMonitor = connectionMonitor;
        _connectionClassifier = connectionClassifier;
        ImageEditor = new ImageEditorViewModel(bookImageService, filePickerService, httpClientFactory);
        ImageEditor.PropertyChanged += (_, _) =>
        {
            // Propagate image pending state into parent HasUnsavedChanges.
            // MarkDirty checks _loadingInProgress; image changes are never during loading.
            if (ImageEditor.HasPendingChanges)
                HasUnsavedChanges = true;
        };
        Contributors.CollectionChanged += OnContributorsCollectionChanged;
        CategoryRows.CollectionChanged += OnCategoryRowsCollectionChanged;
    }

    // Shared by this base and its subclasses (BookDetail, FullDetails). Returns true when the failure was a
    // connection loss so callers can react (e.g. close a window that would otherwise sit blank).
    protected bool ReportIfConnectionLoss(Exception ex) =>
        _connectionMonitor.ReportIfConnectionLoss(_connectionClassifier, ex);

    [RelayCommand(CanExecute = nameof(CanSave))]
    protected virtual async Task SaveAsync()
    {
        if (CurrentBook == null || string.IsNullOrWhiteSpace(EditTitle)) return;
        try
        {
            // The write is wrapped so a mid-session connection loss prompts Retry / Discard instead of
            // silently dropping the edit; ordinary errors fall through to the catch below.
            var outcome = await _writeGuard.ExecuteAsync(async ct =>
            {
                CopyEditFieldsToBook(CurrentBook);
                await _bookService.UpdateBookAsync(CurrentBook, ct);
                var contributorPairs = Contributors
                    .Where(r => !string.IsNullOrWhiteSpace(r.PersonName) && r.RoleId.HasValue)
                    .Select(r => (r.PersonName.Trim(), r.RoleId))
                    .ToList();
                await _bookService.UpdateBookContributorsAsync(CurrentBook.BookId, contributorPairs, ct);
                var categoryIds = CategoryRows.Where(r => r.IsSelected).Select(r => r.CategoryId).ToList();
                await _bookService.UpdateBookCategoriesAsync(CurrentBook.BookId, categoryIds, ct);
                await ImageEditor.FlushPendingAsync(CurrentBook.BookId);
            });

            // Either way the in-memory edit is resolved: saved, or discarded by the user's choice.
            HasUnsavedChanges = false;
            if (outcome == WriteResult.Discarded)
                return;

            // Reload book to refresh navigation properties (Publisher, Format, etc.)
            var refreshed = await _bookService.GetBookByIdAsync(CurrentBook.BookId);
            if (refreshed != null)
                CurrentBook = refreshed;

            WeakReferenceMessenger.Default.Send(new BookSavedMessage(CurrentBook.BookId));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save book {BookId}", CurrentBook?.BookId);
        }
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(EditTitle);

    // Named CancelEditAsync (not CancelAsync) so CommunityToolkit.Mvvm generates CancelEditCommand.
    // This keeps the AXAML binding name consistent: both BDV and FDW bind CancelEditCommand.
    // BDV overrides this method to add IsEditMode = false after calling base.CancelEditAsync().
    [RelayCommand]
    protected virtual async Task CancelEditAsync()
    {
        if (HasUnsavedChanges)
        {
            var result = await _windowService.ShowUnsavedChangesDialogAsync(EditTitle);
            if (result == UnsavedChangesResult.Save) { await SaveAsync(); return; }
            else if (result == UnsavedChangesResult.KeepEditing) { return; }
            // Discard — fall through to inline revert below
        }
        await ImageEditor.ResetToSaved();
        if (CurrentBook != null) CopyBookToEditFields();
        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private async Task OpenManageLookupsAsync(string? tabName)
    {
        await _windowService.ShowManageLookupsAsync(tabName);
    }

    /// <summary>
    /// Called by subclasses when a <see cref="Messages.LookupsChangedMessage"/> is received.
    /// Refreshes the Categories and PurchasePlaces collections so that items newly added
    /// via ManageLookupsWindow appear in the book edit form without requiring a re-open.
    /// </summary>
    protected async Task ReloadLookupsOnChangeAsync()
    {
        try
        {
            var reloadCats = await _lookupService.GetAllAsync<Category>();
            var keepSelected = CategoryRows.Where(r => r.IsSelected).Select(r => r.CategoryId).ToHashSet();
            var reloadSorted = reloadCats.OrderByDescending(c => keepSelected.Contains(c.CategoryId)).ThenBy(c => c.Name).ToList();
            CategoryRows.Clear();
            foreach (var c in reloadSorted)
            {
                var item = new CategorySelectionItem { CategoryId = c.CategoryId, Name = c.Name };
                item.IsSelected = keepSelected.Contains(c.CategoryId);
                CategoryRows.Add(item);
            }
            await PopulateAsync(PurchasePlaces, () => _lookupService.GetAllAsync<PurchasePlace>());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reload lookup data after LookupsChangedMessage");
        }
    }

    protected async Task LoadLookupsAsync()
    {
        try
        {
            await PopulateAsync(Formats, () => _lookupService.GetAllAsync<Format>());
            await PopulateAsync(Publishers, () => _lookupService.GetAllAsync<Publisher>());
            await PopulateAsync(SeriesList, () => _lookupService.GetAllAsync<Series>());
            await PopulateAsync(Languages, () => _lookupService.GetAllAsync<Language>());
            await PopulateAsync(Editions, () => _lookupService.GetAllAsync<Edition>());
            await PopulateAsync(Ratings, () => _lookupService.GetAllAsync<Rating>());
            await PopulateAsync(Conditions, () => _lookupService.GetAllAsync<Condition>());
            await PopulateAsync(Statuses, () => _lookupService.GetAllAsync<Status>());
            await PopulateAsync(Locations, () => _lookupService.GetAllAsync<Location>());
            await PopulateAsync(Owners, () => _lookupService.GetAllAsync<Owner>());
            await PopulateAsync(ReadingLevels, () => _lookupService.GetAllAsync<ReadingLevel>());
            await PopulateAsync(PurchasePlaces, () => _lookupService.GetAllAsync<PurchasePlace>());
            var cats = await _lookupService.GetAllAsync<Category>();
            CategoryRows.Clear();
            foreach (var c in cats.OrderBy(c => c.Name))
                CategoryRows.Add(new CategorySelectionItem { CategoryId = c.CategoryId, Name = c.Name });
            await PopulateAsync(Sources, () => _lookupService.GetAllAsync<Source>());
            await PopulateAsync(ContributorRoles, () => _lookupService.GetContributorRolesAsync());

            var personNames = await _bookService.GetPeopleNamesAsync();
            AvailablePersonNames.Clear();
            foreach (var name in personNames)
                AvailablePersonNames.Add(name);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load lookup data");
        }
    }

    private static async Task PopulateAsync<T>(ObservableCollection<T> target, Func<Task<IReadOnlyList<T>>> fetch)
    {
        var items = await fetch();
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    // Marked virtual so BookDetailViewModel can override to use its own _editingInProgress flag.
    protected virtual void CopyBookToEditFields()
    {
        if (CurrentBook == null) return;
        _loadingInProgress = true;
        try
        {
            CopyBasicInfoTabToFields();
            CopyDetailsTabToFields();
            CopyAcquisitionTabToFields();
            CopyContribsAndAdminTabToFields();
            CopyAdditionalInfoTabToFields();
        }
        finally
        {
            _loadingInProgress = false;
        }
        // Suppress the spurious dirty notification that fires when Avalonia's AutoCompleteBox
        // defers binding initialization until the Contributors tab is first rendered.
        // The flag is cleared after one Loaded-priority dispatcher cycle, which is guaranteed
        // to run after the tab's first layout pass.
        _suppressContributorDirty = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => _suppressContributorDirty = false,
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Called by ContributorsTabActivationBehavior when the Contributors tab is selected,
    /// BEFORE Avalonia renders the tab content. Sets the dirty suppression flag and schedules
    /// its clearance at ApplicationIdle priority — after all lazy tab rendering completes,
    /// including AutoCompleteBox binding initialization.
    /// </summary>
    public void OnContributorsTabActivating()
    {
        _suppressContributorDirty = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => _suppressContributorDirty = false,
            Avalonia.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void CopyBasicInfoTabToFields()
    {
        EditTitle = CurrentBook!.Title;
        Contributors.CollectionChanged -= OnContributorsCollectionChanged;
        foreach (var prev in Contributors)
            prev.PropertyChanged -= OnContributorRowPropertyChanged;
        Contributors.Clear();
        foreach (var bc in CurrentBook.Contributors.OrderBy(c => c.SortOrder))
        {
            var row = new ContributorRowViewModel
            {
                PersonName = bc.Person?.DisplayName ?? string.Empty,
                RoleId = bc.ContributorRoleId,
                IsNew = false
            };
            row.PropertyChanged += OnContributorRowPropertyChanged;
            Contributors.Add(row);
        }
        Contributors.CollectionChanged += OnContributorsCollectionChanged;
        EditSubtitle = CurrentBook.Subtitle ?? string.Empty;
        EditAltTitle = CurrentBook.AltTitle ?? string.Empty;
        EditPublisherId = CurrentBook.PublisherId;
        EditPubDate = CurrentBook.PubDate ?? string.Empty;
        EditFormatId = CurrentBook.FormatId;
        EditEditionId = CurrentBook.EditionId;
        EditLanguageId = CurrentBook.LanguageId;
        EditSeriesId = CurrentBook.SeriesId;
        EditSeriesNumber = FormatSeriesNumberForEdit(CurrentBook.SeriesNumber);
        EditIsbn = CurrentBook.Isbn ?? string.Empty;
        EditExternalId = CurrentBook.ExternalId ?? string.Empty;
    }

    private void CopyDetailsTabToFields()
    {
        EditPages = CurrentBook!.Pages;
        EditCopies = CurrentBook.Copies;
        EditReadCount = CurrentBook.ReadCount;
        EditRatingId = CurrentBook.RatingId;
        EditConditionId = CurrentBook.ConditionId;
        EditStatusId = CurrentBook.StatusId;
        EditReadingLevelId = CurrentBook.ReadingLevelId;
        EditKeywords = CurrentBook.Keywords ?? string.Empty;
        EditComments = CurrentBook.Comments ?? string.Empty;
        EditBookInfo = CurrentBook.BookInfo ?? string.Empty;
        EditFavorite = CurrentBook.Favorite;
        EditSigned = CurrentBook.Signed;
        EditOutOfPrint = CurrentBook.OutOfPrint;
        var selectedIds = CurrentBook.Categories.Select(bc => bc.CategoryId).ToHashSet();
        var sorted = CategoryRows.OrderByDescending(r => selectedIds.Contains(r.CategoryId)).ThenBy(r => r.Name).ToList();
        CategoryRows.Clear();
        foreach (var r in sorted)
        {
            r.IsSelected = selectedIds.Contains(r.CategoryId);
            CategoryRows.Add(r);
        }
    }

    private void CopyAcquisitionTabToFields()
    {
        EditPurchasePrice = CurrentBook!.PurchasePrice;
        EditPurchaseCurrency = CurrentBook.PurchaseCurrency ?? string.Empty;
        EditListPrice = CurrentBook.ListPrice;
        EditListPriceCurrency = CurrentBook.ListPriceCurrency ?? string.Empty;
        EditPurchasePlaceId = CurrentBook.PurchasePlaceId;
        EditPurchaseDate = CurrentBook.PurchaseDate?.ToString("yyyy-MM-dd") ?? string.Empty;
        EditCopyrightDate = CurrentBook.CopyrightDate ?? string.Empty;
        EditPubPlace = CurrentBook.PubPlace ?? string.Empty;
        EditSourceId = CurrentBook.SourceId;
    }

    private void CopyContribsAndAdminTabToFields()
    {
        EditLocationId = CurrentBook!.LocationId;
        EditOwnerId = CurrentBook.OwnerId;
        EditMediaLink = CurrentBook.MediaLink ?? string.Empty;
        EditDisplay = CurrentBook.Display;
    }

    private void CopyAdditionalInfoTabToFields()
    {
        Issn = CurrentBook!.Issn;
        Lccn = CurrentBook.Lccn;
        DeweyDecimal = CurrentBook.DeweyDecimal;
        CallNumber = CurrentBook.CallNumber;
        Dimensions = CurrentBook.Dimensions;
        Weight = CurrentBook.Weight;
        ItemValue = CurrentBook.ItemValue;
        ValuationDate = CurrentBook.ValuationDate?.ToString("yyyy-MM-dd");
        AmazonNewValue = CurrentBook.AmazonNewValue;
        AmazonUsedValue = CurrentBook.AmazonUsedValue;
        AmazonCollectibleValue = CurrentBook.AmazonCollectibleValue;
        AmazonNewCount = CurrentBook.AmazonNewCount;
        AmazonUsedCount = CurrentBook.AmazonUsedCount;
        AmazonCollectibleCount = CurrentBook.AmazonCollectibleCount;
        SalesRank = CurrentBook.SalesRank;
        LexileLevel = CurrentBook.LexileLevel;
    }

    protected void CopyEditFieldsToBook(Book book)
    {
        CopyBasicInfoTabFromFields(book);
        CopyDetailsTabFromFields(book);
        CopyAcquisitionTabFromFields(book);
        CopyContribsAndAdminTabFromFields(book);
        CopyAdditionalInfoTabFromFields(book);
        book.Updated = DateTime.UtcNow;
    }

    private void CopyBasicInfoTabFromFields(Book book)
    {
        book.Title = EditTitle;
        book.Subtitle = EditSubtitle.NullIfEmpty();
        book.AltTitle = EditAltTitle.NullIfEmpty();
        book.PublisherId = EditPublisherId;
        book.PubDate = EditPubDate.NullIfEmpty();
        book.FormatId = EditFormatId;
        book.EditionId = EditEditionId;
        book.LanguageId = EditLanguageId;
        book.SeriesId = EditSeriesId;
        book.SeriesNumber = EditSeriesNumber.NullIfEmpty();
        book.Isbn = EditIsbn.NullIfEmpty();
        book.ExternalId = EditExternalId.NullIfEmpty();
    }

    private void CopyDetailsTabFromFields(Book book)
    {
        book.Pages = EditPages;
        book.Copies = EditCopies;
        book.ReadCount = EditReadCount;
        book.RatingId = EditRatingId;
        book.ConditionId = EditConditionId;
        book.StatusId = EditStatusId;
        book.ReadingLevelId = EditReadingLevelId;
        book.Keywords = EditKeywords.NullIfEmpty();
        book.Comments = EditComments.NullIfEmpty();
        book.BookInfo = EditBookInfo.NullIfEmpty();
        book.Favorite = EditFavorite;
        book.Signed = EditSigned;
        book.OutOfPrint = EditOutOfPrint;
    }

    private void CopyAcquisitionTabFromFields(Book book)
    {
        book.PurchasePrice = EditPurchasePrice;
        book.PurchaseCurrency = EditPurchaseCurrency.NullIfEmpty();
        book.ListPrice = EditListPrice;
        book.ListPriceCurrency = EditListPriceCurrency.NullIfEmpty();
        book.PurchasePlaceId = EditPurchasePlaceId;
        book.PurchaseDate = DateTime.TryParse(EditPurchaseDate, out var pd) ? pd : (DateTime?)null;
        book.CopyrightDate = EditCopyrightDate.NullIfEmpty();
        book.PubPlace = EditPubPlace.NullIfEmpty();
        book.SourceId = EditSourceId;
    }

    private void CopyContribsAndAdminTabFromFields(Book book)
    {
        book.LocationId = EditLocationId;
        book.OwnerId = EditOwnerId;
        book.MediaLink = EditMediaLink.NullIfEmpty();
        book.Display = EditDisplay;
    }

    private void CopyAdditionalInfoTabFromFields(Book book)
    {
        book.Issn = Issn;
        book.Lccn = Lccn;
        book.DeweyDecimal = DeweyDecimal;
        book.CallNumber = CallNumber;
        book.Dimensions = Dimensions;
        book.Weight = Weight;
        book.ItemValue = ItemValue;
        book.ValuationDate = DateTime.TryParse(ValuationDate, out var vd) ? vd : (DateTime?)null;
        book.AmazonNewValue = AmazonNewValue;
        book.AmazonUsedValue = AmazonUsedValue;
        book.AmazonCollectibleValue = AmazonCollectibleValue;
        book.AmazonNewCount = AmazonNewCount;
        book.AmazonUsedCount = AmazonUsedCount;
        book.AmazonCollectibleCount = AmazonCollectibleCount;
        book.SalesRank = SalesRank;
        book.LexileLevel = LexileLevel;
    }

    // Marked virtual so BookDetailViewModel can override to add IsEditMode check.
    protected virtual void MarkDirty()
    {
        if (!_loadingInProgress)
            HasUnsavedChanges = true;
    }

    private void OnCategoryRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (CategorySelectionItem item in e.NewItems)
                item.PropertyChanged += OnCategorySelectionItemChanged;
        if (e.OldItems != null)
            foreach (CategorySelectionItem item in e.OldItems)
                item.PropertyChanged -= OnCategorySelectionItemChanged;
    }

    private void OnCategorySelectionItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CategorySelectionItem.IsSelected))
            MarkDirty();
    }

    private void OnContributorsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (ContributorRowViewModel row in e.NewItems)
                row.PropertyChanged += OnContributorRowPropertyChanged;
        if (e.OldItems != null)
            foreach (ContributorRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnContributorRowPropertyChanged;
        MarkDirty();
    }

    private void OnContributorRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_suppressContributorDirty)
            MarkDirty();
    }

    [RelayCommand]
    private void AddContributor()
    {
        Contributors.Add(new ContributorRowViewModel { IsNew = true });
    }

    [RelayCommand]
    private void RemoveContributor(ContributorRowViewModel row)
    {
        Contributors.Remove(row);
    }

    partial void OnEditTitleChanged(string value) => MarkDirty();
    partial void OnEditSubtitleChanged(string value) => MarkDirty();
    partial void OnEditAltTitleChanged(string value) => MarkDirty();
    partial void OnEditPublisherIdChanged(int? value) => MarkDirty();
    partial void OnEditPubDateChanged(string value) => MarkDirty();
    partial void OnEditFormatIdChanged(int? value) => MarkDirty();
    partial void OnEditEditionIdChanged(int? value) => MarkDirty();
    partial void OnEditLanguageIdChanged(int? value) => MarkDirty();
    partial void OnEditSeriesIdChanged(int? value) => MarkDirty();
    partial void OnEditSeriesNumberChanged(string value) => MarkDirty();
    partial void OnEditIsbnChanged(string value) => MarkDirty();
    partial void OnEditExternalIdChanged(string value) => MarkDirty();
    partial void OnEditPagesChanged(int? value) => MarkDirty();
    partial void OnEditCopiesChanged(int value) => MarkDirty();
    partial void OnEditReadCountChanged(int value) => MarkDirty();
    partial void OnEditRatingIdChanged(int? value) => MarkDirty();
    partial void OnEditConditionIdChanged(int? value) => MarkDirty();
    partial void OnEditStatusIdChanged(int? value) => MarkDirty();
    partial void OnEditReadingLevelIdChanged(int? value) => MarkDirty();
    partial void OnEditKeywordsChanged(string value) => MarkDirty();
    partial void OnEditCommentsChanged(string value) => MarkDirty();
    partial void OnEditBookInfoChanged(string value) => MarkDirty();
    partial void OnEditFavoriteChanged(bool value) => MarkDirty();
    partial void OnEditSignedChanged(bool value) => MarkDirty();
    partial void OnEditOutOfPrintChanged(bool value) => MarkDirty();
    partial void OnEditPurchasePriceChanged(decimal? value) => MarkDirty();
    partial void OnEditPurchaseCurrencyChanged(string value) => MarkDirty();
    partial void OnEditListPriceChanged(decimal? value) => MarkDirty();
    partial void OnEditListPriceCurrencyChanged(string value) => MarkDirty();
    partial void OnEditPurchasePlaceIdChanged(int? value) => MarkDirty();
    partial void OnEditPurchaseDateChanged(string value) => MarkDirty();
    partial void OnEditCopyrightDateChanged(string value) => MarkDirty();
    partial void OnEditPubPlaceChanged(string value) => MarkDirty();
    partial void OnEditSourceIdChanged(int? value) => MarkDirty();
    partial void OnEditLocationIdChanged(int? value) => MarkDirty();
    partial void OnEditOwnerIdChanged(int? value) => MarkDirty();
    partial void OnEditMediaLinkChanged(string value) => MarkDirty();
    partial void OnEditDisplayChanged(bool value) => MarkDirty();
    partial void OnIssnChanged(string? value) => MarkDirty();
    partial void OnLccnChanged(string? value) => MarkDirty();
    partial void OnDeweyDecimalChanged(string? value) => MarkDirty();
    partial void OnCallNumberChanged(string? value) => MarkDirty();
    partial void OnDimensionsChanged(string? value) => MarkDirty();
    partial void OnWeightChanged(decimal? value) => MarkDirty();
    partial void OnItemValueChanged(decimal? value) => MarkDirty();
    partial void OnValuationDateChanged(string? value) => MarkDirty();
    partial void OnAmazonNewValueChanged(decimal? value) => MarkDirty();
    partial void OnAmazonUsedValueChanged(decimal? value) => MarkDirty();
    partial void OnAmazonCollectibleValueChanged(decimal? value) => MarkDirty();
    partial void OnAmazonNewCountChanged(int? value) => MarkDirty();
    partial void OnAmazonUsedCountChanged(int? value) => MarkDirty();
    partial void OnAmazonCollectibleCountChanged(int? value) => MarkDirty();
    partial void OnSalesRankChanged(int? value) => MarkDirty();
    partial void OnLexileLevelChanged(int? value) => MarkDirty();

    /// <summary>
    /// Formats a series number for display in the edit field, stripping the ".0" suffix
    /// from whole numbers so "5.0" appears as "5".
    /// </summary>
    private static string FormatSeriesNumberForEdit(string? seriesNumber)
    {
        if (string.IsNullOrWhiteSpace(seriesNumber)) return string.Empty;
        if (decimal.TryParse(seriesNumber, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) && d % 1 == 0)
            return ((int)d).ToString();
        return seriesNumber;
    }
}

// File-scoped extension helper — moved from BookDetailViewModel.cs
internal static class BookEditStringExtensions
{
    public static string? NullIfEmpty(this string s) =>
        string.IsNullOrEmpty(s) ? null : s;
}
