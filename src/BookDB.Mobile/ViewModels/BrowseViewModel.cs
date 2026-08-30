using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Isbn;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace BookDB.Mobile.ViewModels;

/// <summary>
/// The read-only view of the library: type to search, narrow by collection, or scan a barcode to settle the
/// one question worth asking in a shop — do I already own this? Everything here comes from the computer, so
/// offline the screen says so instead of showing an empty library.
/// </summary>
public sealed partial class BrowseViewModel : PageViewModel, IDisposable
{
    /// <summary>Matches the desktop's own default; it clamps anything larger anyway.</summary>
    public const int PageSize = 50;

    /// <summary>Long enough that typing a title does not fire a query per keystroke, short enough that the
    /// list feels like it is following along.</summary>
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(400);

    private readonly IConnectionService _connection;
    private readonly IBarcodeScanner _scanner;
    private readonly Action<int, string> _openDetail;
    private readonly Func<CancellationToken, Task> _settle;

    private CancellationTokenSource? _pending;
    private int _loaded;

    /// <summary>Set while the filter list is being rebuilt, so restoring the selection does not fire a query
    /// the caller is about to make anyway.</summary>
    private bool _rebuildingFilter;

    public BrowseViewModel(
        IConnectionService connection,
        IBarcodeScanner scanner,
        IDeviceSettings deviceSettings,
        Action<int, string> openDetail,
        Func<CancellationToken, Task>? settle = null)
    {
        _connection = connection;
        _scanner = scanner;
        _openDetail = openDetail;
        _settle = settle ?? (ct => Task.Delay(SearchDelay, ct));
        CameraAccess = new CameraAccessViewModel(deviceSettings);

        Collections.Add(CollectionFilterViewModel.All());
        _selectedCollection = Collections[0];
    }

    public override string Title => Resources.Browse_Title;

    /// <summary>Searching by title still works with the camera refused, so the screen keeps working and only
    /// the barcode question goes away.</summary>
    public CameraAccessViewModel CameraAccess { get; }

    /// <summary>The shell disposes a page it navigates away from. A query still in the air is asking the
    /// computer for a screen nobody is looking at, and holds this page — with its covers — alive until it
    /// answers.</summary>
    public void Dispose()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }

    public ObservableCollection<BrowseRowViewModel> Results { get; } = [];

    public ObservableCollection<CollectionFilterViewModel> Collections { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CollectionFilterViewModel _selectedCollection;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Set when the computer could not be reached, which is the difference between "no books" and
    /// "no answer".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty))]
    private bool _isUnreachable;

    /// <summary>
    /// Set when the computer answered and refused this device. Offering *Try again* here would be a lie: the
    /// only thing that changes it is pairing again, so the screen says that instead.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty))]
    private bool _isRevoked;

    /// <summary>The number scanned for the ownership check, or null when browsing normally.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBanner))]
    [NotifyPropertyChangedFor(nameof(BannerIsbn))]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(ClearScanCommand))]
    private string? _scannedIsbn;

    [ObservableProperty]
    private bool _isOwned;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private int _totalCount;

    /// <summary>The answer to the scanned barcode, shown until browsing resumes.</summary>
    public bool ShowsBanner => ScannedIsbn is not null;

    public string BannerIsbn => string.Format(
        CultureInfo.CurrentCulture, Resources.Browse_ScannedIsbn, ScannedIsbn ?? string.Empty);

    public bool HasMore => _loaded < TotalCount;

    /// <summary>An empty list only reads as "nothing matched" when the computer actually answered — and never
    /// under the banner, which has already said the book is not there.</summary>
    public bool ShowsEmpty =>
        !IsUnreachable && !IsRevoked && !ShowsBanner && TotalCount == 0 && Results.Count == 0;

    public string ResultCount => string.Format(
        CultureInfo.CurrentCulture, Resources.Browse_ResultCount, TotalCount);

    /// <summary>Fetches the collection filter and the first page. Separate from the constructor because a
    /// screen that reaches the network on the way in cannot be built synchronously.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await LoadCollectionsAsync();
        await QueryAsync(reset: true);
    }

    [RelayCommand(CanExecute = nameof(HasMore))]
    private async Task LoadMoreAsync() => await QueryAsync(reset: false);

    [RelayCommand]
    private async Task RetryAsync() => await LoadAsync();

    /// <summary>The in-shop question. The desktop answers it from an exact ISBN match, so a book it holds
    /// comes back as the row itself and not merely a yes.</summary>
    [RelayCommand]
    private async Task ScanAsync()
    {
        var scan = await _scanner.ScanOnceAsync(BarcodeKind.BookNumber);
        if (CameraAccess.Explains(scan))
        {
            Message = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(scan.Text))
        {
            Message = Resources.Browse_NothingRead;
            return;
        }

        // A wrong check digit still asks — misprinted ISBNs exist — but a barcode that is not an ISBN at all
        // must not be answered with "not in your library", which would read as a fact about a book.
        var isbn = IsbnNormalizer.Normalize(scan.Text!);
        if (isbn.Length is not (10 or 13))
        {
            Message = Resources.Browse_NotAnIsbn;
            return;
        }

        ScannedIsbn = isbn;
        await QueryAsync(reset: true);
    }

    [RelayCommand(CanExecute = nameof(ShowsBanner))]
    private async Task ClearScanAsync()
    {
        ScannedIsbn = null;
        await QueryAsync(reset: true);
    }

    partial void OnSearchTextChanged(string value) => _ = SearchLaterAsync();

    partial void OnSelectedCollectionChanged(CollectionFilterViewModel value)
    {
        if (!_rebuildingFilter)
        {
            _ = QueryAsync(reset: true);
        }
    }

    /// <summary>Waits out the typing before asking. Each keystroke cancels the wait the one before it
    /// started, so only the pause at the end reaches the computer.</summary>
    private async Task SearchLaterAsync()
    {
        var token = Restart();

        try
        {
            await _settle(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunQueryAsync(reset: true, token);
    }

    private async Task QueryAsync(bool reset) => await RunQueryAsync(reset, Restart());

    private async Task RunQueryAsync(bool reset, CancellationToken token)
    {
        if (reset)
        {
            _loaded = 0;
        }

        IsBusy = true;
        Message = null;

        try
        {
            var connection = await _connection.EnsureConnectedAsync(token);
            if (connection is null)
            {
                Explain();
                return;
            }

            var page = await connection.Service.ListBooksAsync(
                new BookQuery
                {
                    Search = ScannedIsbn is null ? SearchText : null,
                    CollectionId = SelectedCollection.CollectionId,
                    Isbn = ScannedIsbn,
                    Skip = _loaded,
                    Take = PageSize,
                },
                new CallContext(new CallOptions(cancellationToken: token)));

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (reset)
            {
                Results.Clear();
            }

            foreach (var book in page.Books)
            {
                Results.Add(new BrowseRowViewModel(book, _openDetail));
            }

            _loaded = Results.Count;
            IsUnreachable = false;
            IsRevoked = false;
            TotalCount = page.TotalCount;
            IsOwned = ScannedIsbn is not null && Results.Count > 0;
        }
        catch (Exception ex) when (
            ex is RpcException or HttpRequestException or IOException or OperationCanceledException)
        {
            if (!token.IsCancellationRequested)
            {
                Explain();
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(HasMore));
            OnPropertyChanged(nameof(ShowsEmpty));
            OnPropertyChanged(nameof(ResultCount));
            LoadMoreCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Which of the two failures this was. The connection knows; the screen only has to say it.</summary>
    private void Explain()
    {
        IsRevoked = _connection.Status == ConnectionStatus.Revoked;
        IsUnreachable = !IsRevoked;
    }

    /// <summary>The filter is only worth offering when there is something to filter by, so a library with no
    /// collections simply does not show one.</summary>
    private async Task LoadCollectionsAsync()
    {
        try
        {
            var connection = await _connection.EnsureConnectedAsync();
            if (connection is null)
            {
                return;
            }

            var list = await connection.Service.ListCollectionsAsync();

            var selected = SelectedCollection.CollectionId;
            _rebuildingFilter = true;
            try
            {
                Collections.Clear();
                Collections.Add(CollectionFilterViewModel.All());
                foreach (var collection in list.Collections)
                {
                    Collections.Add(CollectionFilterViewModel.For(collection));
                }

                SelectedCollection = Collections.FirstOrDefault(c => c.CollectionId == selected) ?? Collections[0];
            }
            finally
            {
                _rebuildingFilter = false;
            }

            OnPropertyChanged(nameof(HasCollections));
        }
        catch (Exception ex) when (ex is RpcException or HttpRequestException or IOException)
        {
            // The list still works without the filter; the query below reports the connection.
        }
    }

    public bool HasCollections => Collections.Count > 1;

    private CancellationToken Restart()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();
        return _pending.Token;
    }
}
