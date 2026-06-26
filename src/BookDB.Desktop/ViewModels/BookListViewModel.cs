using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

public partial class BookListViewModel :
    ObservableRecipient,
    IRecipient<CollectionSelectionChangedMessage>,
    IRecipient<BookSavedMessage>,
    IRecipient<BooksDeletedMessage>,
    IRecipient<AdvancedSearchResultMessage>,
    IRecipient<FilterChangedMessage>,
    IRecipient<ClearFiltersRequestedMessage>,
    IRecipient<ImportCompleteMessage>
{
    private readonly IBookService _bookService;
    private readonly IBookSearchService _bookSearchService;
    private readonly IBookImageService _bookImageService;
    private readonly IWindowService _windowService;
    private readonly ISettingsService _settingsService;
    private readonly ILookupService _lookupService;
    private readonly IClipboardService _clipboardService;
    private readonly ILoanService _loanService;
    private readonly IConnectionHealthMonitor _connectionMonitor;
    private readonly IConnectionFailureClassifier _connectionClassifier;
    private IReadOnlyList<Collection> _cachedCollections = [];

    // Active filter state
    private IReadOnlySet<int> _activeCollectionIds = new HashSet<int>();
    private IReadOnlyList<int>? _activeSearchBookIds;
    private FilterState? _activeFilterState;
    private IReadOnlyList<long>? _activeAdvancedSearchBookIds;

    // Pagination state
    private const int PageSize = 100;
    private CancellationTokenSource? _loadCts;

    // Debounce timer for search text
    private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private string? _pendingSearchValue;

    // ColumnState is declared at namespace level (see bottom of this file) so the code-behind can access it

    // Runtime column states captured from the DataGrid by the code-behind
    private readonly Dictionary<string, (int DisplayIndex, double Width)> _runtimeColumnStates = [];
    // Loaded states waiting for the code-behind to apply display indices and widths
    private List<ColumnState>? _pendingColumnRestore;

    public ObservableCollection<BookRowViewModel> Books { get; } = [];
    public ObservableCollection<BookRowViewModel> SelectedBooks { get; } = [];
    public ObservableCollection<CollectionMenuEntry> CollectionMenuEntries { get; } = [];
    public bool HasBooks => Books.Count > 0;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _rowHeight = 24;

    [ObservableProperty]
    private bool _thumbnailColumnVisible = false;

    [ObservableProperty]
    private string _sortColumn = "Title";

    [ObservableProperty]
    private bool _sortAscending = true;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private bool _isAllLoaded;

    [ObservableProperty]
    private int _filteredTotal;

    [ObservableProperty]
    private int _grandTotal;

    // Column visibility properties
    [ObservableProperty]
    private bool _authorColumnVisible = true;

    [ObservableProperty]
    private bool _seriesColumnVisible = true;

    [ObservableProperty]
    private bool _publisherColumnVisible = true;

    [ObservableProperty]
    private bool _yearColumnVisible = true;

    [ObservableProperty]
    private bool _formatColumnVisible = true;

    [ObservableProperty]
    private bool _ratingColumnVisible = false;

    [ObservableProperty]
    private bool _statusColumnVisible = false;

    [ObservableProperty]
    private bool _loanedToColumnVisible = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedSearchStatusText))]
    private bool _isAdvancedSearchActive;

    // Signals the code-behind that saved column display indices / widths are ready to apply
    [ObservableProperty]
    private bool _columnStateRestoreReady;

    // Signals the code-behind to restore DataGrid selection after an in-place row replacement.
    // Set after Books[index] replace so the view can select the new instance.
    [ObservableProperty]
    private BookRowViewModel? _pendingSelectAfterUpdate;

    public string AdvancedSearchStatusText =>
        IsAdvancedSearchActive ? Localization.Resources.BookList_AdvancedSearch_ActiveStatus : string.Empty;

    /// <summary>Current active collection filter — used by ExportCsvCommand to scope export to current view.</summary>
    public IReadOnlySet<int> ActiveCollectionIds => _activeCollectionIds;

    /// <summary>Current active search book IDs (FTS + advanced search intersection) — null when no search active.</summary>
    public IReadOnlyList<int>? ActiveSearchBookIds => BuildSearchBookIds();

    /// <summary>Current active facet filter state — null when no facets selected.</summary>
    public Dictionary<string, HashSet<int>>? ActiveFacetFilters =>
        ToFacetDictionary(_activeFilterState?.FacetSelections);

    /// <summary>Called by code-behind after each reorder or resize to keep runtime state current.</summary>
    public void UpdateRuntimeColumnStates(IEnumerable<(string Header, int DisplayIndex, double Width)> states)
    {
        foreach (var (header, displayIndex, width) in states)
        {
            var name = HeaderToName(header);
            if (name != null)
                _runtimeColumnStates[name] = (displayIndex, width);
        }
    }

    /// <summary>Returns the column states loaded from settings for the code-behind to apply to the DataGrid. Consumed once.</summary>
    internal List<ColumnState>? ConsumeColumnRestoreStates()
    {
        var states = _pendingColumnRestore;
        _pendingColumnRestore = null;
        return states;
    }

    private static string? HeaderToName(string header) => header switch
    {
        "Cover" => "Thumbnail",
        "Title" => "Title",
        "Author(s)" => "Author",
        "Series" => "Series",
        "Publisher" => "Publisher",
        "Year" => "Year",
        "Format" => "Format",
        "Rating" => "Rating",
        "Status" => "Status",
        "Loaned To" => "LoanedTo",
        _ => null
    };

    public BookListViewModel(
        IMessenger messenger,
        IBookService bookService,
        IBookSearchService bookSearchService,
        IBookImageService bookImageService,
        IWindowService windowService,
        ISettingsService settingsService,
        ILookupService lookupService,
        IClipboardService clipboardService,
        ILoanService loanService,
        IConnectionHealthMonitor connectionMonitor,
        IConnectionFailureClassifier connectionClassifier)
        : base(messenger)
    {
        _bookService = bookService;
        _bookSearchService = bookSearchService;
        _bookImageService = bookImageService;
        _windowService = windowService;
        _settingsService = settingsService;
        _lookupService = lookupService;
        _clipboardService = clipboardService;
        _loanService = loanService;
        _connectionMonitor = connectionMonitor;
        _connectionClassifier = connectionClassifier;
        IsActive = true;

        Books.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBooks));

        SelectedBooks.CollectionChanged += (_, _) =>
        {
            AddBookCommand.NotifyCanExecuteChanged();
            EditBookCommand.NotifyCanExecuteChanged();
            DeleteBooksCommand.NotifyCanExecuteChanged();
            DuplicateBookCommand.NotifyCanExecuteChanged();
            OpenFullDetailsCommand.NotifyCanExecuteChanged();
            BulkEditCommand.NotifyCanExecuteChanged();
            RecatalogSelectedCommand.NotifyCanExecuteChanged();
            MoveToCollectionCommand.NotifyCanExecuteChanged();
            CopyIsbnCommand.NotifyCanExecuteChanged();
            CopyTitleCommand.NotifyCanExecuteChanged();
            CheckOutCommand.NotifyCanExecuteChanged();
            CheckInCommand.NotifyCanExecuteChanged();
            RefreshCollectionMenuCurrentState();
        };

        _searchDebounceTimer.Tick += OnSearchDebounceTimerTick;
    }

    // --- Message Handlers ---

    public void Receive(CollectionSelectionChangedMessage message)
    {
        _activeCollectionIds = message.Value;
        UIThreadHelper.PostAsync(() => LoadBooksAsync(), "load books for collection selection change");
    }

    public void Receive(BookSavedMessage message)
    {
        UIThreadHelper.PostAsync(async () =>
        {
            if (message.Value > 0)
                await UpdateSingleBookRowAsync(message.Value);
            else
                await LoadBooksAsync();
        }, "refresh book list after save");
    }

    public void Receive(BooksDeletedMessage message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var deletedIds = message.Value.ToHashSet();
            var toRemove = Books.Where(b => deletedIds.Contains(b.BookId)).ToList();
            foreach (var item in toRemove)
                Books.Remove(item);
        });
    }

    public void Receive(ImportCompleteMessage message)
    {
        UIThreadHelper.PostAsync(async () =>
        {
            await LoadBooksAsync();
            Log.Warning("Import complete: {Imported} imported, {Updated} updated. Book list refreshed.", message.ImportedCount, message.UpdatedCount);
        }, "refresh books after import complete");
    }

    public void Receive(AdvancedSearchResultMessage message)
    {
        // Keep the result list even when it is empty: an empty list means the search
        // ran and matched nothing (show no rows), which is distinct from null = no
        // advanced search active (show everything).
        _activeAdvancedSearchBookIds = message.Value;
        UIThreadHelper.PostAsync(async () =>
        {
            IsAdvancedSearchActive = _activeAdvancedSearchBookIds != null;
            await LoadBooksAsync();
        }, "re-filter books for advanced search result");
    }

    public void Receive(FilterChangedMessage message)
    {
        _activeFilterState = message.Value;
        UIThreadHelper.PostAsync(() => LoadBooksAsync(), "re-filter books for facet filter change");
    }

    public void Receive(ClearFiltersRequestedMessage message)
    {
        _activeFilterState = null;
    }

    // --- Commands ---

    [RelayCommand]
    private async Task AddBookAsync()
    {
        try
        {
            var defaultIdStr = await _settingsService.GetAsync("DefaultCollectionId");
            int? collectionId = int.TryParse(defaultIdStr, out var storedId) ? storedId : null;

            var result = await _windowService.ShowAddBookDialogAsync(collectionId);
            if (result == true)
                Messenger.Send(new BookSavedMessage(0));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Add book dialog failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditBook))]
    private void EditBook()
    {
        if (SelectedBooks.Count != 1) return;
        Messenger.Send(new BookSelectedMessage(SelectedBooks[0].BookId, openInEditMode: true));
    }

    private bool CanEditBook() => SelectedBooks.Count == 1;

    [RelayCommand(CanExecute = nameof(CanDeleteBooks))]
    private async Task DeleteBooksAsync()
    {
        if (SelectedBooks.Count == 0) return;
        try
        {
            var bookIds = SelectedBooks.Select(b => b.BookId).ToList();
            string message = SelectedBooks.Count == 1
                ? string.Format(Localization.Resources.Delete_SingleBook_Message, SelectedBooks[0].Title)
                : string.Format(Localization.Resources.Delete_MultipleBooks_Message, SelectedBooks.Count);

            var confirmed = await _windowService.ShowDeleteConfirmationAsync(message);
            if (confirmed == true)
            {
                await _bookService.DeleteBooksAsync(bookIds);
                Messenger.Send(new BooksDeletedMessage(bookIds));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Delete books failed");
        }
    }

    private bool CanDeleteBooks() => SelectedBooks.Count >= 1;

    [RelayCommand(CanExecute = nameof(CanDuplicateBook))]
    private async Task DuplicateBookAsync()
    {
        if (SelectedBooks.Count != 1) return;
        try
        {
            _ = await _bookService.DuplicateBookAsync(SelectedBooks[0].BookId, Resources.DuplicateBook_TitlePrefix);
            Messenger.Send(new BookSavedMessage(0));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Duplicate book failed");
        }
    }

    private bool CanDuplicateBook() => SelectedBooks.Count == 1;

    [RelayCommand(CanExecute = nameof(CanOpenFullDetails))]
    private async Task OpenFullDetails()
    {
        if (SelectedBooks.Count != 1) return;
        await _windowService.OpenFullDetailsWindowAsync(SelectedBooks[0].BookId);
    }

    private bool CanOpenFullDetails() => SelectedBooks.Count == 1;

    [RelayCommand(CanExecute = nameof(CanBulkEdit))]
    private async Task BulkEditAsync()
    {
        if (SelectedBooks.Count < 1) return;
        try
        {
            var bookIds = SelectedBooks.Select(b => b.BookId).ToList();
            await _windowService.ShowBulkEditDialogAsync(bookIds);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk edit failed");
        }
    }

    private bool CanBulkEdit() => SelectedBooks.Count >= 1;

    [RelayCommand]
    private async Task AdvancedSearchAsync()
    {
        try
        {
            await _windowService.ShowAdvancedSearchDialogAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Advanced search failed");
        }
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        SearchText = string.Empty;
        _activeAdvancedSearchBookIds = null;
        IsAdvancedSearchActive = false;
        _activeSearchBookIds = null;
        _activeFilterState = null;
        Messenger.Send(new ClearFiltersRequestedMessage());
        try
        {
            await LoadBooksAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Clear search failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecatalogSelected))]
    private async Task RecatalogSelectedAsync()
    {
        var selectedIds = SelectedBooks.Select(b => b.BookId).ToList();
        if (selectedIds.Count == 0) return;

        try
        {
            await _windowService.StartBatchRecatalogAsync(selectedIds);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start re-catalog for selected books");
        }
    }

    private bool CanRecatalogSelected() => SelectedBooks.Count >= 1;

    [RelayCommand(CanExecute = nameof(CanMoveToCollection))]
    private async Task MoveToCollectionAsync(int collectionId)
    {
        var bookIds = SelectedBooks.Select(b => b.BookId).ToList();
        if (bookIds.Count == 0) return;
        try
        {
            await _bookService.BulkSetCollectionAsync(bookIds, collectionId);
            Messenger.Send(new BookSavedMessage(0));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Move to collection failed");
        }
    }

    private bool CanMoveToCollection(int collectionId) => SelectedBooks.Count >= 1;

    private void RefreshCollectionMenuCurrentState()
    {
        var currentId = SelectedBooks.Count == 1 ? SelectedBooks[0].CollectionId : 0;
        CollectionMenuEntries.Clear();
        foreach (var c in _cachedCollections)
            CollectionMenuEntries.Add(new CollectionMenuEntry(
                c.CollectionId,
                c.Name,
                IsCurrentCollection: currentId != 0 && currentId == c.CollectionId));
    }

    [RelayCommand(CanExecute = nameof(CanCopyIsbn))]
    private async Task CopyIsbnAsync()
    {
        var text = string.Join("\n", SelectedBooks
            .Select(b => b.Isbn ?? string.Empty)
            .Where(s => s.Length > 0));
        if (text.Length == 0) return;
        try { await _clipboardService.SetTextAsync(text); }
        catch (Exception ex) { Log.Error(ex, "Copy ISBN failed"); }
    }

    private bool CanCopyIsbn() => SelectedBooks.Count >= 1;

    [RelayCommand(CanExecute = nameof(CanCopyTitle))]
    private async Task CopyTitleAsync()
    {
        var text = string.Join("\n", SelectedBooks.Select(b => b.Title));
        if (text.Length == 0) return;
        try { await _clipboardService.SetTextAsync(text); }
        catch (Exception ex) { Log.Error(ex, "Copy title failed"); }
    }

    private bool CanCopyTitle() => SelectedBooks.Count >= 1;

    [RelayCommand]
    private void ConfigureColumns()
    {
        // Placeholder — column config UI handled via View menu toggles
    }

    [RelayCommand(CanExecute = nameof(CanCheckOut))]
    private async Task CheckOutAsync()
    {
        if (SelectedBooks.Count != 1) return;
        try
        {
            var result = await _windowService.ShowCheckOutDialogAsync(SelectedBooks[0].BookId);
            if (result == true)
                await UpdateSingleBookRowAsync(SelectedBooks[0].BookId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CheckOut failed");
        }
    }

    private bool CanCheckOut() => SelectedBooks.Count == 1 && !SelectedBooks[0].IsLoaned;

    [RelayCommand(CanExecute = nameof(CanCheckIn))]
    private async Task CheckInAsync()
    {
        if (SelectedBooks.Count != 1) return;
        try
        {
            await _loanService.CheckInAsync(SelectedBooks[0].BookId);
            await UpdateSingleBookRowAsync(SelectedBooks[0].BookId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CheckIn failed");
        }
    }

    private bool CanCheckIn() => SelectedBooks.Count == 1 && SelectedBooks[0].IsLoaned;

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || IsAllLoaded) return;

        // Capture the current CTS reference to detect if a new LoadBooksAsync supersedes us
        var currentCts = _loadCts;
        var ct = currentCts?.Token ?? CancellationToken.None;

        IsLoadingMore = true;
        try
        {
            var searchIds = BuildSearchBookIds();
            var facetFilters = ToFacetDictionary(_activeFilterState?.FacetSelections);

            var result = await _bookService.GetBooksAsync(
                _activeCollectionIds,
                searchIds,
                facetFilters,
                SortColumn,
                SortAscending,
                Books.Count,
                PageSize,
                isLoanedOut: _activeFilterState?.IsLoanedOut ?? false,
                ct: ct);

            var newVms = result.Books.Select(BookRowViewModel.FromListRow).ToList();

            // If a new LoadBooksAsync started while we were fetching, discard stale results
            if (!ReferenceEquals(_loadCts, currentCts)) return;

            Dispatcher.UIThread.Post(() =>
            {
                int startIndex = Books.Count;  // capture before appending
                foreach (var vm in newVms)
                {
                    vm.RowNumber = ++startIndex;  // assign before Add so binding reads correct value
                    Books.Add(vm);
                }

                if (result.Books.Count < PageSize || Books.Count >= result.FilteredTotal)
                    IsAllLoaded = true;

                // Load thumbnails for newly appended VMs only
                if (ThumbnailColumnVisible)
                    _ = LoadThumbnailsAsync(newVms, CancellationToken.None);
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load more books");
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    // --- Methods ---

    public async Task LoadBooksAsync(CancellationToken ct = default)
    {
        // Cancel any in-flight load
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var linkedCt = _loadCts.Token;

        try
        {
            IsAllLoaded = false;
            IsLoadingMore = false;

            var searchIds = BuildSearchBookIds();
            var facetFilters = ToFacetDictionary(_activeFilterState?.FacetSelections);

            var result = await _bookService.GetBooksAsync(
                _activeCollectionIds,
                searchIds,
                facetFilters,
                SortColumn,
                SortAscending,
                0,
                PageSize,
                isLoanedOut: _activeFilterState?.IsLoanedOut ?? false,
                ct: linkedCt);

            foreach (var old in Books) { old.CoverThumbnail?.Dispose(); old.TooltipBitmap?.Dispose(); }
            Books.Clear();
            var newVms = new List<BookRowViewModel>();
            int rowNum = 0;
            foreach (var row in result.Books)
            {
                var vm = BookRowViewModel.FromListRow(row);
                vm.RowNumber = ++rowNum;  // assign before Add so binding reads correct value
                Books.Add(vm);
                newVms.Add(vm);
            }

            FilteredTotal = result.FilteredTotal;
            GrandTotal = result.GrandTotal;

            if (result.Books.Count < PageSize || result.FilteredTotal <= PageSize)
                IsAllLoaded = true;

            // Publish count for status bar
            Messenger.Send(new BookCountChangedMessage(FilteredTotal, GrandTotal));

            // Load thumbnails async if visible
            if (ThumbnailColumnVisible)
                _ = LoadThumbnailsAsync(newVms, linkedCt);
        }
        catch (OperationCanceledException)
        {
            // Expected when filter changes rapidly — ignore
        }
        catch (Exception ex)
        {
            // A read that fails on a dropped connection drives the health indicator; the monitor retries in
            // the background and refreshes the view on reconnection. Other errors just log.
            _connectionMonitor.ReportIfConnectionLoss(_connectionClassifier, ex);
            Log.Error(ex, "Failed to load books");
        }
    }

    private static Dictionary<string, HashSet<int>>? ToFacetDictionary(
        IReadOnlyDictionary<string, IReadOnlySet<int>>? source)
    {
        if (source == null) return null;
        return source.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToHashSet());
    }

    private IReadOnlyList<int>? BuildSearchBookIds()
    {
        // Merge FTS search IDs and advanced search IDs.
        // null  => that search dimension is not active.
        // empty => the search ran but matched nothing.
        // Returns null only when no search is active at all; an empty (but non-null)
        // list is preserved so callers filter the list down to zero rows rather than
        // treating "no matches" as "no filter" (which would show every book).
        bool ftsActive = _activeSearchBookIds != null;
        bool advancedActive = _activeAdvancedSearchBookIds != null;

        if (!ftsActive && !advancedActive)
            return null;

        var ftsIds = _activeSearchBookIds?.ToHashSet() ?? [];
        // _activeAdvancedSearchBookIds is IReadOnlyList<long> — cast to int
        var advancedIds = _activeAdvancedSearchBookIds?.Select(id => (int)id).ToHashSet() ?? [];

        HashSet<int> result;
        if (ftsActive && advancedActive)
        {
            result = ftsIds;
            result.IntersectWith(advancedIds);  // Both active: intersection
        }
        else
        {
            result = ftsActive ? ftsIds : advancedIds;
        }

        return result.ToList();
    }

    private async Task UpdateSingleBookRowAsync(int bookId)
    {
        var existingRow = Books.FirstOrDefault(b => b.BookId == bookId);
        if (existingRow == null)
        {
            await LoadBooksAsync();
            return;
        }

        var facetFilters = ToFacetDictionary(_activeFilterState?.FacetSelections);
        var result = await _bookService.GetBooksAsync(
            _activeCollectionIds,
            new List<int> { bookId },
            facetFilters,
            SortColumn,
            SortAscending,
            skip: 0,
            take: 1,
            isLoanedOut: false,
            ct: CancellationToken.None);
        var updatedRow = result.Books.FirstOrDefault();
        if (updatedRow == null)
        {
            Books.Remove(existingRow);
            return;
        }

        var index = Books.IndexOf(existingRow);
        existingRow.CoverThumbnail?.Dispose();
        existingRow.TooltipBitmap?.Dispose();
        var updatedRowViewModel = BookRowViewModel.FromListRow(updatedRow);
        Books[index] = updatedRowViewModel;

        // Restore DataGrid selection to the replacement instance — the ObservableCollection Replace
        // event clears the selection when the old instance is removed from the collection.
        // The view's OnVmPropertyChanged handler picks this up and sets BooksGrid.SelectedItem.
        PendingSelectAfterUpdate = updatedRowViewModel;

        if (ThumbnailColumnVisible)
            await LoadThumbnailsAsync([updatedRowViewModel], CancellationToken.None);
    }

    private async Task LoadThumbnailBitmapAsync(BookRowViewModel viewModel, CancellationToken ct)
    {
        const int thumbnailWidth = 36;
        // Prefer pre-sized thumbnail (BookImageTypeId=1), fall back to primary cover
        var imageData = await _bookImageService.GetBookThumbnailBytesAsync(viewModel.BookId, ct);
        if (imageData?.Length > 0)
        {
            viewModel.CoverThumbnail = await Task.Run(() =>
            {
                using var ms = new System.IO.MemoryStream(imageData);
                return Bitmap.DecodeToWidth(ms, thumbnailWidth);
            }, ct);
        }
    }

    private async Task LoadTooltipBitmapAsync(BookRowViewModel viewModel, CancellationToken ct)
    {
        const long maxTooltipBytes = 1 * 1024 * 1024;
        var allImages = await _bookImageService.GetBookImagesAsync(viewModel.BookId, ct);
        var bestImage =
            allImages.FirstOrDefault(i => i.BookImageTypeId == 0)
            ?? allImages.FirstOrDefault(i => i.BookImageTypeId == 1)
            ?? allImages.FirstOrDefault(i => i.IsPrimary);

        if (bestImage?.ImageData != null && bestImage.ImageData.LongLength <= maxTooltipBytes)
        {
            var tooltipBytes = bestImage.ImageData;
            var bmp = await Task.Run(() =>
            {
                using var ms = new System.IO.MemoryStream(tooltipBytes);
                return new Bitmap(ms);
            }, ct);

            if (bmp.PixelSize.Width > 3000 || bmp.PixelSize.Height > 4000)
            {
                bmp.Dispose();
                viewModel.TooltipBitmap = null;
                viewModel.TooltipBitmapSizeBytes = null;
            }
            else
            {
                viewModel.TooltipBitmap = bmp;
                viewModel.TooltipBitmapSizeBytes = tooltipBytes.LongLength;
            }
        }
        else
        {
            viewModel.TooltipBitmap = null;
            viewModel.TooltipBitmapSizeBytes = null;
        }
    }

    private async Task LoadThumbnailsAsync(List<BookRowViewModel> viewModels, CancellationToken ct)
    {
        foreach (var viewModel in viewModels)
        {
            if (ct.IsCancellationRequested) break;
            if (!viewModel.HasCoverImage) continue;
            try { await LoadThumbnailBitmapAsync(viewModel, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Debug(ex, "Failed to load thumbnail for book {BookId}", viewModel.BookId); }
            try { await LoadTooltipBitmapAsync(viewModel, ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Log.Debug(ex, "Failed to load tooltip image for book {BookId}", viewModel.BookId); }
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var colStateJson = await _settingsService.GetAsync("BookList_ColumnState", ct);
            if (colStateJson is not null)
            {
                var states = JsonSerializer.Deserialize<List<ColumnState>>(colStateJson);
                if (states != null)
                {
                    foreach (var s in states)
                    {
                        switch (s.Name)
                        {
                            case "Author": AuthorColumnVisible = s.IsVisible; break;
                            case "Series": SeriesColumnVisible = s.IsVisible; break;
                            case "Publisher": PublisherColumnVisible = s.IsVisible; break;
                            case "Year": YearColumnVisible = s.IsVisible; break;
                            case "Format": FormatColumnVisible = s.IsVisible; break;
                            case "Rating": RatingColumnVisible = s.IsVisible; break;
                            case "Status": StatusColumnVisible = s.IsVisible; break;
                        }
                        // Seed runtime state so first persist (before any reorder/resize) uses saved values
                        if (s.DisplayIndex > 0 || s.Width > 0)
                            _runtimeColumnStates[s.Name] = (s.DisplayIndex, s.Width);
                    }
                    // Store for code-behind to apply display indices and widths to the DataGrid
                    _pendingColumnRestore = states;
                    ColumnStateRestoreReady = true;
                }
            }

            var sortStateJson = await _settingsService.GetAsync("BookList_SortState", ct);
            if (sortStateJson is not null)
            {
                var sortRecord = JsonSerializer.Deserialize<SortState>(sortStateJson);
                if (sortRecord != null)
                {
                    SortColumn = sortRecord.Column;
                    SortAscending = sortRecord.Ascending;
                }
            }

            var thumbStr = await _settingsService.GetAsync("BookList_ThumbnailVisible", ct);
            if (thumbStr != null && bool.TryParse(thumbStr, out var thumbVisible))
                ThumbnailColumnVisible = thumbVisible;

            // Read per-column visibility (overrides IsVisible from BookList_ColumnState JSON if present)
            var colVisKeys = new (string Key, Action<bool> Setter)[]
            {
                ("ColumnVisible.Author",    v => AuthorColumnVisible    = v),
                ("ColumnVisible.Series",    v => SeriesColumnVisible    = v),
                ("ColumnVisible.Publisher", v => PublisherColumnVisible = v),
                ("ColumnVisible.Year",      v => YearColumnVisible      = v),
                ("ColumnVisible.Format",    v => FormatColumnVisible    = v),
                ("ColumnVisible.Rating",    v => RatingColumnVisible    = v),
                ("ColumnVisible.Status",    v => StatusColumnVisible    = v),
                ("ColumnVisible.LoanedTo",  v => LoanedToColumnVisible  = v),
            };
            foreach (var (key, setter) in colVisKeys)
            {
                var str = await _settingsService.GetAsync(key, ct);
                if (str != null && bool.TryParse(str, out var vis))
                    setter(vis);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load book list settings");
        }

        try
        {
            var collections = await _lookupService.GetCollectionsAsync(ct);
            _cachedCollections = collections;
            RefreshCollectionMenuCurrentState();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load collections for context menu");
        }
    }

    public async Task PersistColumnStateAsync(CancellationToken ct = default)
    {
        try
        {
            (int DisplayIndex, double Width) GetRuntime(string name, int defaultDisplayIndex) =>
                _runtimeColumnStates.TryGetValue(name, out var rs) ? rs : (defaultDisplayIndex, 0.0);

            var state = new List<ColumnState>
            {
                new("Author",    AuthorColumnVisible,    GetRuntime("Author",    3).DisplayIndex, GetRuntime("Author",    3).Width),
                new("Series",    SeriesColumnVisible,    GetRuntime("Series",    4).DisplayIndex, GetRuntime("Series",    4).Width),
                new("Publisher", PublisherColumnVisible, GetRuntime("Publisher", 5).DisplayIndex, GetRuntime("Publisher", 5).Width),
                new("Year",      YearColumnVisible,      GetRuntime("Year",      6).DisplayIndex, GetRuntime("Year",      6).Width),
                new("Format",    FormatColumnVisible,    GetRuntime("Format",    7).DisplayIndex, GetRuntime("Format",    7).Width),
                new("Rating",    RatingColumnVisible,    GetRuntime("Rating",    8).DisplayIndex, GetRuntime("Rating",    8).Width),
                new("Status",    StatusColumnVisible,    GetRuntime("Status",    9).DisplayIndex, GetRuntime("Status",    9).Width),
            };
            var json = JsonSerializer.Serialize(state);
            await _settingsService.SetAsync("BookList_ColumnState", json, ct);

            var sortState = new SortState(SortColumn, SortAscending);
            var sortJson = JsonSerializer.Serialize(sortState);
            await _settingsService.SetAsync("BookList_SortState", sortJson, ct);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to persist column state");
        }
    }

    public void UpdateSelectedBooks(System.Collections.IList selectedItems)
    {
        SelectedBooks.Clear();
        foreach (var item in selectedItems)
        {
            if (item is BookRowViewModel bookRowViewModel)
                SelectedBooks.Add(bookRowViewModel);
        }
    }

    // --- Partial property change handlers ---

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer.Stop();
        _pendingSearchValue = value;
        _searchDebounceTimer.Start();
    }

    private async void OnSearchDebounceTimerTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        var value = _pendingSearchValue;
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _activeSearchBookIds = null;
            }
            else
            {
                _activeSearchBookIds = await _bookSearchService.SearchBookIdsAsync(value);
            }
            await LoadBooksAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Search failed for query: {Query}", value);
        }
    }

    partial void OnSortColumnChanged(string value) =>
        UIThreadHelper.PostAsync(() => LoadBooksAsync(), "reload books after sort column change");

    partial void OnSortAscendingChanged(bool value) =>
        UIThreadHelper.PostAsync(() => LoadBooksAsync(), "reload books after sort direction change");

    partial void OnThumbnailColumnVisibleChanged(bool value)
    {
        RowHeight = value ? 40 : 24;
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("BookList_ThumbnailVisible", value.ToString()),
            "persist thumbnail column visibility");

        // When the thumbnail column is turned on, load thumbnails for books already in the list.
        // Without this, covers only appear after the next LoadBooksAsync call (e.g. collection switch).
        if (value && Books.Count > 0)
        {
            var bookRowViewModels = Books.ToList();
            _ = LoadThumbnailsAsync(bookRowViewModels, CancellationToken.None);
        }
    }

    partial void OnAuthorColumnVisibleChanged(bool value) =>
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("ColumnVisible.Author", value.ToString()),
            "persist Author column visibility");

    partial void OnSeriesColumnVisibleChanged(bool value) =>
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("ColumnVisible.Series", value.ToString()),
            "persist Series column visibility");

    partial void OnPublisherColumnVisibleChanged(bool value) =>
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("ColumnVisible.Publisher", value.ToString()),
            "persist Publisher column visibility");

    partial void OnYearColumnVisibleChanged(bool value) =>
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("ColumnVisible.Year", value.ToString()),
            "persist Year column visibility");

    partial void OnFormatColumnVisibleChanged(bool value) =>
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("ColumnVisible.Format", value.ToString()),
            "persist Format column visibility");

    partial void OnRatingColumnVisibleChanged(bool value) =>
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("ColumnVisible.Rating", value.ToString()),
            "persist Rating column visibility");

    partial void OnStatusColumnVisibleChanged(bool value) =>
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("ColumnVisible.Status", value.ToString()),
            "persist Status column visibility");

    partial void OnLoanedToColumnVisibleChanged(bool value) =>
        UIThreadHelper.PostAsync(
            () => _settingsService.SetAsync("ColumnVisible.LoanedTo", value.ToString()),
            "persist LoanedTo column visibility");

    private record SortState(string Column, bool Ascending);
}

internal record ColumnState(string Name, bool IsVisible, int DisplayIndex, double Width);

public record CollectionMenuEntry(int CollectionId, string Name, bool IsCurrentCollection = false);
