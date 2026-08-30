using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>The shell: it owns the navigation frame (the current page and the back stack) and the connection
/// status the chip reflects. A fresh, unpaired device starts on the pairing page; once paired it starts —
/// and, after a successful pair, resets — to the hub. Pages push onto the stack through
/// <see cref="INavigator"/>; the shell is the only thing that mutates it.</summary>
public sealed partial class MainViewModel : ViewModelBase, INavigator
{
    private readonly Stack<PageViewModel> _backStack = new();
    private readonly IIdentityStore _identityStore;
    private readonly IConnectionService _connectionService;
    private readonly PageFactories _pages;

    private DispatcherTimer? _heartbeat;

    [ObservableProperty]
    private PageViewModel _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ConnectionStatus _connection = ConnectionStatus.Offline;

    public MainViewModel(IIdentityStore identityStore, IConnectionService connection, PageFactories pages)
    {
        _identityStore = identityStore;
        _connectionService = connection;
        _pages = pages;

        _connectionService.StatusChanged += (_, _) => Connection = _connectionService.Status;
        _currentPage = identityStore.HasIdentity ? pages.Hub(this) : pages.Pairing(this);
    }

    public bool CanGoBack => _backStack.Count > 0;

    /// <summary>A device with no computer to be connected to has no connection to report; the chip would read
    /// Offline on the one screen where that says nothing.</summary>
    public bool ShowsConnection => _identityStore.HasIdentity;

    public string StatusText => Connection switch
    {
        ConnectionStatus.Connected => Resources.Status_Connected,
        ConnectionStatus.Reconnecting => Resources.Status_Reconnecting,
        ConnectionStatus.Revoked => Resources.Status_Revoked,
        _ => Resources.Status_Offline,
    };

    public void NavigateTo(PageViewModel page)
    {
        _backStack.Push(CurrentPage);
        CurrentPage = page;
        OnPropertyChanged(nameof(CanGoBack));
        GoBackCommand.NotifyCanExecuteChanged();
    }

    public void NavigateTo(HubDestination destination) => NavigateTo(destination switch
    {
        HubDestination.Settings => _pages.Settings(this),
        HubDestination.Browse => _pages.Browse(this),
        HubDestination.Scan => _pages.Scan(this),
        _ => _pages.Batch(this),
    });

    public void Replace(PageViewModel page)
    {
        var leaving = CurrentPage;
        CurrentPage = page;
        Discard(leaving);
    }

    public void ShowHub() => ResetTo(_pages.Hub(this));

    public void ShowPairing() => ResetTo(_pages.Pairing(this));

    /// <summary>Checks the link to the paired computer, which is how the chip catches up after a start, a
    /// change of network, or a computer that has gone away.</summary>
    [RelayCommand]
    private async Task RefreshConnectionAsync()
    {
        if (_identityStore.HasIdentity)
            await _connectionService.RefreshAsync();
    }

    /// <summary>
    /// Starts the periodic re-check that keeps the chip honest when nobody is asking for anything — without
    /// it a computer that has been switched off still reads as Connected. Only the running app starts it: it
    /// ticks on the UI thread, which is also what keeps the status updates there.
    /// </summary>
    public void StartConnectionHeartbeat(TimeSpan? interval = null)
    {
        _heartbeat?.Stop();
        _heartbeat = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(20) };
        _heartbeat.Tick += (_, _) => RefreshConnectionCommand.Execute(null);
        _heartbeat.Start();
        RefreshConnectionCommand.Execute(null);
    }

    /// <summary>
    /// The platform's own Back — Android's button or gesture. True means it was consumed by going back a
    /// page; false leaves it to the OS, which at the top of the stack means leaving the app. Without this the
    /// system gesture skips the shell entirely and closes the app from wherever the user happens to be.
    /// </summary>
    public bool HandleSystemBack()
    {
        if (!CanGoBack)
            return false;

        GoBack();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (_backStack.Count == 0)
            return;

        var leaving = CurrentPage;
        CurrentPage = _backStack.Pop();
        Discard(leaving);
        OnPropertyChanged(nameof(CanGoBack));
        GoBackCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Drops the history along with the current page, so Back cannot return to a flow the device has
    /// finished (or, after unpairing, to screens it can no longer serve).</summary>
    private void ResetTo(PageViewModel page)
    {
        var leaving = CurrentPage;
        foreach (var stacked in _backStack)
            Discard(stacked);

        _backStack.Clear();
        CurrentPage = page;
        Discard(leaving);
        OnPropertyChanged(nameof(CanGoBack));

        // Pairing and unpairing are the only ways the identity changes, and both land here.
        OnPropertyChanged(nameof(ShowsConnection));
        GoBackCommand.NotifyCanExecuteChanged();
    }

    /// <summary>A page that is not coming back is disposed, so anything it subscribed to lets go of it.</summary>
    private static void Discard(PageViewModel page) => (page as IDisposable)?.Dispose();
}
