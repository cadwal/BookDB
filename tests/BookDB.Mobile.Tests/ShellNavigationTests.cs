using System;
using System.Linq;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using BookDB.Mobile.ViewModels;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The shell's navigation frame: a paired device starts on the hub, the two primary jobs and
/// Settings each push a page, Back returns, and the status chip reflects the connection state. Assertions
/// compare against the resource strings so they hold in whatever UI culture the machine runs.</summary>
public class ShellNavigationTests
{
    /// <summary>A page that reports whether the shell let go of it when navigating away.</summary>
    private sealed class TrackedPage : PageViewModel, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public override string Title => "tracked";

        public void Dispose() => IsDisposed = true;
    }

    private static MainViewModel Shell(
        FakeIdentityStore store,
        FakeConnectionService? connection = null,
        Func<INavigator, PageViewModel>? settings = null,
        Func<INavigator, PageViewModel>? browse = null,
        Func<INavigator, PageViewModel>? scan = null,
        Func<INavigator, PageViewModel>? batch = null,
        Func<INavigator, PageViewModel>? pairing = null,
        IStagingStore? staging = null)
    {
        var stagingStore = staging ?? new FakeStagingStore();

        return new MainViewModel(store, connection ?? new FakeConnectionService(), new PageFactories
        {
            Pairing = pairing ?? (navigator => new PairingViewModel(
                new FakeBarcodeScanner(), new FakePairingCoordinator(), new FakeDeviceSettings(), navigator)),
            Hub = navigator => new HubViewModel(navigator, stagingStore),
            Settings = settings ?? (_ => new StubPage(Resources.Settings_Title)),
            Browse = browse ?? (_ => new StubPage(Resources.Browse_Title)),
            Scan = scan ?? (_ => new StubPage(Resources.Scan_Title)),
            Batch = batch ?? (_ => new StubPage(Resources.Batch_Title)),
        });
    }

    private static MainViewModel PairedShell() => Shell(new FakeIdentityStore(paired: true));

    [Fact]
    public void StartsOnTheHub_WithNothingToGoBackTo()
    {
        var shell = PairedShell();

        Assert.IsType<HubViewModel>(shell.CurrentPage);
        Assert.False(shell.CanGoBack);
        Assert.False(shell.GoBackCommand.CanExecute(null));
    }

    [Fact]
    public void ScanFromTheHub_OpensTheIsbnScreen()
    {
        var scan = new IsbnScanViewModel(
            new FakeBarcodeScanner(), new FakeConnectionService(), new FakeStagingStore(),
            new FakeDeviceSettings(), _ => { });
        var shell = Shell(new FakeIdentityStore(paired: true), scan: _ => scan);
        var hub = Assert.IsType<HubViewModel>(shell.CurrentPage);

        hub.ScanCommand.Execute(null);

        Assert.Same(scan, shell.CurrentPage);
        Assert.True(shell.CanGoBack);
    }

    [Fact]
    public void BrowseFromTheHub_OpensTheBrowseScreen()
    {
        var browse = new BrowseViewModel(
            new FakeConnectionService(), new FakeBarcodeScanner(), new FakeDeviceSettings(), (_, _) => { });
        var shell = Shell(new FakeIdentityStore(paired: true), browse: _ => browse);
        var hub = Assert.IsType<HubViewModel>(shell.CurrentPage);

        hub.BrowseCommand.Execute(null);

        Assert.Same(browse, shell.CurrentPage);
        Assert.True(shell.CanGoBack);
    }

    [Fact]
    public void SettingsFromTheHub_OpensTheSettingsScreen()
    {
        var settings = new SettingsViewModel(
            new FakeConnectionService(), new FakeIdentityStore(paired: true), new FakeNavigator());
        var shell = Shell(new FakeIdentityStore(paired: true), settings: _ => settings);
        var hub = Assert.IsType<HubViewModel>(shell.CurrentPage);

        hub.OpenSettingsCommand.Execute(null);

        Assert.Same(settings, shell.CurrentPage);
    }

    [Fact]
    public void GoBack_ReturnsToTheHub_AndCannotGoBackFurther()
    {
        var shell = PairedShell();
        var hub = Assert.IsType<HubViewModel>(shell.CurrentPage);
        hub.ScanCommand.Execute(null);

        shell.GoBackCommand.Execute(null);

        Assert.Same(hub, shell.CurrentPage);
        Assert.False(shell.CanGoBack);
    }

    /// <summary>
    /// Android's Back — button or gesture — is handed to the shell, and the shell answers whether it used it.
    /// A press it does not consume belongs to the OS, which at the hub means leaving the app; the app had no
    /// answer for the system gesture at all, so it left from wherever the user happened to be.
    /// </summary>
    [Fact]
    public void SystemBack_GoesBackAPageAndSaysItWasUsed()
    {
        var page = new TrackedPage();
        var shell = PairedShell();
        var hub = shell.CurrentPage;
        shell.NavigateTo(page);

        Assert.True(shell.HandleSystemBack());

        Assert.Same(hub, shell.CurrentPage);
        Assert.True(page.IsDisposed);
    }

    [Fact]
    public void SystemBack_AtTheTopOfTheStack_IsLeftToTheOperatingSystem()
    {
        var shell = PairedShell();
        var hub = shell.CurrentPage;

        Assert.False(shell.HandleSystemBack());

        Assert.Same(hub, shell.CurrentPage);
    }

    [Fact]
    public void APageLeftBehind_IsDisposedSoItStopsListening()
    {
        var page = new TrackedPage();
        var shell = PairedShell();
        shell.NavigateTo(page);

        shell.GoBackCommand.Execute(null);

        Assert.True(page.IsDisposed);
    }

    /// <summary>
    /// The shell disposing a page it leaves is only worth anything if the pages that hold something take
    /// part. Every page that owns work in flight — a query, a photo fetch, a read waiting out its hold —
    /// must be disposable, or leaving the screen leaves that work running against a computer nobody is
    /// looking at, holding the page and its images alive until it answers.
    /// </summary>
    [Fact]
    public void EveryPageThatOwnsWorkInFlight_CanBeDisposedByTheShell()
    {
        var connection = new FakeConnectionService();
        var navigator = new FakeNavigator();

        PageViewModel[] pages =
        [
            new IsbnScanViewModel(
                new FakeBarcodeScanner(), connection, new FakeStagingStore(), new FakeDeviceSettings(), _ => { }),
            new BrowseViewModel(connection, new FakeBarcodeScanner(), new FakeDeviceSettings(), (_, _) => { }),
            new BookDetailViewModel(connection, 7, "Dune"),
            new SettingsViewModel(connection, new FakeIdentityStore(paired: true), navigator),
            new BatchTrayViewModel(
                new FakeStagingStore(), connection, navigator, _ => new StubPage(), () => { }),
            new HubViewModel(navigator, new FakeStagingStore()),
        ];

        var notDisposable = pages.Where(page => page is not IDisposable).Select(page => page.GetType().Name);
        Assert.Empty(notDisposable);

        // And disposing twice — which the shell can do when a reset follows a navigation — is harmless.
        foreach (var page in pages)
        {
            ((IDisposable)page).Dispose();
            ((IDisposable)page).Dispose();
        }
    }

    [Fact]
    public void TheChip_StartsOfflineAndFollowsTheConnectionService()
    {
        var connection = new FakeConnectionService();
        var shell = Shell(new FakeIdentityStore(paired: true), connection);
        Assert.Equal(ConnectionStatus.Offline, shell.Connection);
        Assert.Equal(Resources.Status_Offline, shell.StatusText);

        connection.Report(ConnectionStatus.Reconnecting);
        Assert.Equal(Resources.Status_Reconnecting, shell.StatusText);

        connection.Report(ConnectionStatus.Connected);
        Assert.Equal(Resources.Status_Connected, shell.StatusText);
    }

    /// <summary>An unpaired device has no computer to be connected to, so the chip would read Offline on the
    /// one screen where that means nothing at all.</summary>
    [Fact]
    public async System.Threading.Tasks.Task TheChip_IsAbsentUntilThereIsAComputerToBeConnectedTo()
    {
        var store = new FakeIdentityStore(paired: false);
        var scanner = new FakeBarcodeScanner { Next = "code" };
        var coordinator = new FakePairingCoordinator { Outcome = PairingOutcome.Paired, Store = store };
        var shell = Shell(
            store,
            pairing: navigator => new PairingViewModel(scanner, coordinator, new FakeDeviceSettings(), navigator));

        Assert.False(shell.ShowsConnection);

        var pairing = Assert.IsType<PairingViewModel>(shell.CurrentPage);
        await pairing.ScanCommand.ExecuteAsync(null);
        pairing.DeviceName = "Kitchen tablet";
        await pairing.PairCommand.ExecuteAsync(null);

        Assert.True(shell.ShowsConnection);
    }

    [Fact]
    public async System.Threading.Tasks.Task RefreshingTheConnection_OnlyAsksWhenTheDeviceIsPaired()
    {
        var connection = new FakeConnectionService();
        var unpaired = Shell(new FakeIdentityStore(paired: false), connection);

        await unpaired.RefreshConnectionCommand.ExecuteAsync(null);
        Assert.Equal(0, connection.RefreshCount);

        var paired = Shell(new FakeIdentityStore(paired: true), connection);
        await paired.RefreshConnectionCommand.ExecuteAsync(null);
        Assert.Equal(1, connection.RefreshCount);
    }

    /// <summary>The top bar shows whatever the current page calls itself, so every page has to have a name —
    /// asserted on the pages themselves, since a stand-in would only prove the stand-in.</summary>
    [Fact]
    public void EachPageReportsATitleForTheTopBar()
    {
        var connection = new FakeConnectionService();
        var staging = new FakeStagingStore();
        var navigator = new FakeNavigator();

        Assert.Equal(Resources.App_Title, new HubViewModel(navigator, staging).Title);
        Assert.Equal(
            Resources.Pairing_Title,
            new PairingViewModel(
                new FakeBarcodeScanner(), new FakePairingCoordinator(), new FakeDeviceSettings(), navigator).Title);
        Assert.Equal(
            Resources.Scan_Title,
            new IsbnScanViewModel(
                new FakeBarcodeScanner(), connection, staging, new FakeDeviceSettings(), _ => { }).Title);
        Assert.Equal(
            Resources.Browse_Title,
            new BrowseViewModel(
                connection, new FakeBarcodeScanner(), new FakeDeviceSettings(), (_, _) => { }).Title);
        Assert.Equal(
            Resources.Settings_Title,
            new SettingsViewModel(connection, new FakeIdentityStore(paired: true), navigator).Title);
        Assert.Equal(
            Resources.Batch_Title,
            new BatchTrayViewModel(staging, connection, navigator, _ => new StubPage(), () => { }).Title);
    }

    /// <summary>The batch is only offered once there is one, and the count on the hub follows the store.</summary>
    [Fact]
    public void TheHubOffersTheBatch_OnlyOnceSomethingIsWaiting()
    {
        var staging = new FakeStagingStore();
        var tray = new StubPage(Resources.Batch_Title);
        var shell = Shell(new FakeIdentityStore(paired: true), staging: staging, batch: _ => tray);
        var hub = Assert.IsType<HubViewModel>(shell.CurrentPage);

        Assert.False(hub.HasBatch);
        Assert.Equal(0, hub.BatchCount);

        staging.Save(new StagedBook { ClientItemId = "a1", Isbn = "9780441013593", Images = [] });

        Assert.True(hub.HasBatch);
        Assert.Equal(1, hub.BatchCount);

        hub.OpenBatchCommand.Execute(null);

        Assert.Same(tray, shell.CurrentPage);
    }

    /// <summary>How the wizard loops from one book to the next: the finished builder is swapped out, not
    /// stacked on, so a stack of books leaves a single step to go back through.</summary>
    [Fact]
    public void ReplacingThePage_KeepsTheBackStackWhereItWas()
    {
        var shell = PairedShell();
        var first = new TrackedPage();
        shell.NavigateTo(first);
        var second = new TrackedPage();

        shell.Replace(second);

        Assert.Same(second, shell.CurrentPage);
        Assert.True(first.IsDisposed);
        Assert.True(shell.CanGoBack);

        shell.GoBackCommand.Execute(null);

        Assert.IsType<HubViewModel>(shell.CurrentPage);
        Assert.False(shell.CanGoBack);
    }

    [Fact]
    public void AnUnpairedDevice_StartsOnThePairingPage()
    {
        var shell = Shell(new FakeIdentityStore(paired: false));

        Assert.IsType<PairingViewModel>(shell.CurrentPage);
        Assert.False(shell.CanGoBack);
    }

    [Fact]
    public async System.Threading.Tasks.Task PairingSuccessfully_ResetsTheShellToTheHub()
    {
        var scanner = new FakeBarcodeScanner { Next = "code" };
        var coordinator = new FakePairingCoordinator { Outcome = PairingOutcome.Paired };
        var shell = Shell(
            new FakeIdentityStore(paired: false),
            pairing: navigator => new PairingViewModel(
                scanner, coordinator, new FakeDeviceSettings(), navigator));
        var pairing = Assert.IsType<PairingViewModel>(shell.CurrentPage);

        await pairing.ScanCommand.ExecuteAsync(null);
        pairing.DeviceName = "Kitchen tablet";
        await pairing.PairCommand.ExecuteAsync(null);

        Assert.IsType<HubViewModel>(shell.CurrentPage);
        Assert.False(shell.CanGoBack);
    }

    [Fact]
    public void Unpairing_TakesTheShellBackToPairingWithNoHistory()
    {
        var store = new FakeIdentityStore(paired: true);
        var connection = new FakeConnectionService();
        var shell = Shell(store, connection, navigator => new SettingsViewModel(connection, store, navigator));
        Assert.IsType<HubViewModel>(shell.CurrentPage);
        ((HubViewModel)shell.CurrentPage).OpenSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(shell.CurrentPage);

        settings.UnpairCommand.Execute(null);
        settings.ConfirmUnpairCommand.Execute(null);

        Assert.IsType<PairingViewModel>(shell.CurrentPage);
        Assert.False(shell.CanGoBack);
        Assert.False(store.HasIdentity);
        Assert.True(connection.WasReset);
    }
}
