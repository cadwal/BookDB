using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.ViewModels;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>
/// Browsing the library from the phone, driven by a fake computer: what a search sends, how paging grows the
/// list, what the collection filter does, and — the point of the screen — what a scanned barcode answers.
/// </summary>
public class BrowseViewModelTests
{
    private const string Dune = "9780441013593";

    private sealed class StubConnection : ICompanionConnection
    {
        public StubConnection(IBookScannerService service) => Service = service;

        public IBookScannerService Service { get; }

        public void Dispose() { }
    }

    /// <summary>The debounce is driven by the test rather than by the clock: it completes at once, so a
    /// search reaches the fake computer without anything waiting on a timer.</summary>
    private static BrowseViewModel New(
        StubScannerService desktop,
        out FakeBarcodeScanner scanner,
        out List<(int BookId, string Title)> opened,
        bool offline = false,
        bool revoked = false,
        FakeDeviceSettings? deviceSettings = null)
    {
        scanner = new FakeBarcodeScanner();
        var taken = new List<(int, string)>();
        opened = taken;

        var connection = new FakeConnectionService();
        if (revoked)
        {
            connection.Report(ConnectionStatus.Revoked);
        }
        else if (!offline)
        {
            connection.Connection = new StubConnection(desktop);
        }

        return new BrowseViewModel(
            connection,
            scanner,
            deviceSettings ?? new FakeDeviceSettings(),
            (id, title) => taken.Add((id, title)),
            _ => Task.CompletedTask);
    }

    private static BookPage Page(int totalCount, params (int Id, string Title)[] books) => new()
    {
        TotalCount = totalCount,
        Books = [.. books.Select(b => new BookSummary { BookId = b.Id, Title = b.Title })],
    };

    [Fact]
    public async Task OpeningTheScreen_ListsTheLibraryAndOffersItsCollections()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(2, (1, "Kallocain"), (2, "Aniara")));
        desktop.Collections.Add(new CollectionRef { CollectionId = 3, Name = "Fiction", BookCount = 412 });
        var vm = New(desktop, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Kallocain", "Aniara"], vm.Results.Select(r => r.Title));
        Assert.Equal(2, vm.TotalCount);
        Assert.True(vm.HasCollections);
        Assert.Equal(2, vm.Collections.Count);
        Assert.Null(vm.SelectedCollection.CollectionId);
        Assert.False(vm.ShowsEmpty);
    }

    [Fact]
    public async Task ALibraryWithNoCollections_DoesNotOfferAFilter()
    {
        var desktop = new StubScannerService();
        var vm = New(desktop, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.HasCollections);
        Assert.Single(vm.Collections);
    }

    [Fact]
    public async Task TypingASearch_SendsItOnceTheTypingSettles()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(1, (1, "Kallocain")));
        var vm = New(desktop, out _, out _);

        vm.SearchText = "kallo";
        await WaitForAsync(() => desktop.Queries.Count == 1);

        Assert.Equal("kallo", desktop.Queries[0].Search);
        Assert.Equal(0, desktop.Queries[0].Skip);
        Assert.Equal("Kallocain", Assert.Single(vm.Results).Title);
    }

    [Fact]
    public async Task ChoosingACollection_NarrowsTheQueryAndStartsFromTheTop()
    {
        var desktop = new StubScannerService();
        desktop.Collections.Add(new CollectionRef { CollectionId = 3, Name = "Fiction", BookCount = 412 });
        var vm = New(desktop, out _, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        vm.SelectedCollection = vm.Collections.Single(c => c.CollectionId == 3);
        await WaitForAsync(() => desktop.Queries.Count == 2);

        Assert.Equal(3, desktop.Queries[^1].CollectionId);
        Assert.Equal(0, desktop.Queries[^1].Skip);
    }

    /// <summary>Rebuilding the filter list restores the same choice, which must not be read as the user
    /// changing it — that would fire a second query for every load.</summary>
    [Fact]
    public async Task LoadingTheFilter_DoesNotCountAsChoosingOne()
    {
        var desktop = new StubScannerService();
        desktop.Collections.Add(new CollectionRef { CollectionId = 3, Name = "Fiction", BookCount = 412 });
        var vm = New(desktop, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(desktop.Queries);
    }

    [Fact]
    public async Task ShowingMore_AppendsTheNextPageRatherThanReplacingTheList()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(3, (1, "One"), (2, "Two")));
        desktop.Pages.Add(Page(3, (3, "Three")));
        var vm = New(desktop, out _, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasMore);
        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(["One", "Two", "Three"], vm.Results.Select(r => r.Title));
        Assert.Equal(2, desktop.Queries[^1].Skip);
        Assert.False(vm.HasMore);
    }

    [Fact]
    public async Task Scanning_AsksTheCameraForABookNumber()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(0));
        var vm = New(desktop, out var scanner, out _);
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(BarcodeKind.BookNumber, scanner.LastKind);
    }

    [Fact]
    public async Task AScannedBarcodeTheLibraryHas_AnswersOwnedAndShowsTheBook()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(1, (7, "Dune")));
        var vm = New(desktop, out var scanner, out _);
        scanner.Next = "978-0-441-01359-3";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowsBanner);
        Assert.True(vm.IsOwned);
        Assert.Equal(Dune, vm.ScannedIsbn);
        Assert.Equal("Dune", Assert.Single(vm.Results).Title);
        Assert.Equal(Dune, desktop.Queries[^1].Isbn);
    }

    [Fact]
    public async Task AScannedBarcodeTheLibraryLacks_AnswersNotOwnedWithoutTheEmptyState()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(0));
        var vm = New(desktop, out var scanner, out _);
        scanner.Next = Dune;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowsBanner);
        Assert.False(vm.IsOwned);

        // The banner has already said it: a "nothing matched" underneath it would only muddle the answer.
        Assert.False(vm.ShowsEmpty);
    }

    [Fact]
    public async Task TheOwnershipQuestionIsAskedAsAnExactIsbnRatherThanAsASearch()
    {
        var desktop = new StubScannerService();
        var vm = New(desktop, out var scanner, out _);
        vm.SearchText = "kallocain";
        await WaitForAsync(() => desktop.Queries.Count == 1);

        scanner.Next = Dune;
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Null(desktop.Queries[^1].Search);
        Assert.Equal(Dune, desktop.Queries[^1].Isbn);
    }

    [Fact]
    public async Task ClearingTheScan_GoesBackToBrowsing()
    {
        var desktop = new StubScannerService();
        var vm = New(desktop, out var scanner, out _);
        scanner.Next = Dune;
        await vm.ScanCommand.ExecuteAsync(null);

        await vm.ClearScanCommand.ExecuteAsync(null);

        Assert.False(vm.ShowsBanner);
        Assert.False(vm.IsOwned);
        Assert.Null(desktop.Queries[^1].Isbn);
    }

    [Fact]
    public async Task ABarcodeThatIsNotAnIsbn_SaysSoWithoutAsking()
    {
        var desktop = new StubScannerService();
        var vm = New(desktop, out var scanner, out _);
        scanner.Next = "012345678905";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.ShowsBanner);
        Assert.Empty(desktop.Queries);
        Assert.NotNull(vm.Message);
    }

    /// <summary>Misprinted ISBNs exist on real books, so a failed check digit is still asked about — only a
    /// barcode that is not an ISBN-shaped number at all is refused.</summary>
    [Fact]
    public async Task AnIsbnWithAWrongCheckDigit_IsStillAskedAbout()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(0));
        var vm = New(desktop, out var scanner, out _);
        scanner.Next = "9780441013594";

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowsBanner);
        Assert.Equal("9780441013594", Assert.Single(desktop.Queries).Isbn);
    }

    /// <summary>Searching by title is unaffected by a refused camera, so the screen keeps working and only
    /// the barcode question goes away — and it says why rather than doing nothing when tapped.</summary>
    [Fact]
    public async Task ARefusedCamera_ExplainsItselfAndLeavesSearchWorking()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(1, (7, "Dune")));
        var settings = new FakeDeviceSettings();
        var vm = New(desktop, out var scanner, out _, deviceSettings: settings);
        scanner.NextScan = BarcodeScan.PermissionDenied;

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.CameraAccess.IsVisible);
        Assert.Equal(Resources.Camera_PermissionDenied, vm.CameraAccess.Message);
        Assert.Null(vm.Message);
        Assert.False(vm.ShowsBanner);
        Assert.Empty(desktop.Queries);

        vm.CameraAccess.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, settings.OpenCount);

        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Single(vm.Results);
    }

    [Fact]
    public async Task AnUnreachableComputer_IsAnExplanationRatherThanAnEmptyLibrary()
    {
        var vm = New(new StubScannerService(), out _, out _, offline: true);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsUnreachable);
        Assert.False(vm.IsRevoked);
        Assert.False(vm.ShowsEmpty);
        Assert.Empty(vm.Results);
    }

    /// <summary>
    /// Removed on the computer is not the same failure as an unreachable one, and must not be reported as it:
    /// the offline screen's Try again is an offer that cannot be honoured, and it was all the user got.
    /// </summary>
    [Fact]
    public async Task ADeviceRemovedOnTheComputer_IsToldSoRatherThanOfferedAnotherTry()
    {
        var vm = New(new StubScannerService(), out _, out _, revoked: true);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsRevoked);
        Assert.False(vm.IsUnreachable);
        Assert.False(vm.ShowsEmpty);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task AComputerThatFailsMidQuery_IsReportedAsUnreachable()
    {
        var desktop = new StubScannerService { BrowseFails = true };
        var vm = New(desktop, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsUnreachable);
    }

    [Fact]
    public async Task AComputerThatComesBack_RefillsTheListOnRetry()
    {
        var desktop = new StubScannerService { BrowseFails = true };
        var vm = New(desktop, out _, out _);
        await vm.LoadCommand.ExecuteAsync(null);

        desktop.BrowseFails = false;
        desktop.Pages.Add(Page(1, (1, "Kallocain")));
        await vm.RetryCommand.ExecuteAsync(null);

        Assert.False(vm.IsUnreachable);
        Assert.Single(vm.Results);
    }

    [Fact]
    public async Task AnEmptyLibraryShowsTheEmptyStateRatherThanNothingAtAll()
    {
        var desktop = new StubScannerService();
        var vm = New(desktop, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.ShowsEmpty);
    }

    [Fact]
    public async Task TappingARow_OpensThatBookWithTheTitleAlreadyKnown()
    {
        var desktop = new StubScannerService();
        desktop.Pages.Add(Page(1, (7, "Dune")));
        var vm = New(desktop, out _, out var opened);
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Single(vm.Results).OpenCommand.Execute(null);

        Assert.Equal((7, "Dune"), Assert.Single(opened));
    }

    [Fact]
    public async Task ThePageSizeAskedForIsTheOneTheDesktopExpects()
    {
        var desktop = new StubScannerService();
        var vm = New(desktop, out _, out _);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(BrowseViewModel.PageSize, Assert.Single(desktop.Queries).Take);
    }

    /// <summary>A change to the search text starts its query without anything to await, so the assertion has
    /// to wait for it rather than assume it has already run.</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
