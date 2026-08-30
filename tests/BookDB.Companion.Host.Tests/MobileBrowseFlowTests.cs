using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Logic.Services;
using BookDB.Mobile.Services;
using BookDB.Mobile.ViewModels;
using SkiaSharp;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// End to end, both ends real: the phone's own browse and detail screens read a library over a live
/// mutual-TLS host. What is being proved is that the read path holds up through the actual wire — paging,
/// the collection filter, the ownership question, and covers arriving shrunk — rather than against a stub.
/// </summary>
public sealed class MobileBrowseFlowTests
{
    /// <summary>Hands the screens the harness's live client, standing in for the reconnect machinery that
    /// has its own tests.</summary>
    private sealed class LiveConnection : IConnectionService, ICompanionConnection
    {
        private readonly IBookScannerService _client;

        public LiveConnection(IBookScannerService client) => _client = client;

        public ConnectionStatus Status => ConnectionStatus.Connected;

        public ServerInfo? ServerInfo => null;

        public IBookScannerService Service => _client;

        public event EventHandler? StatusChanged { add { } remove { } }

        public Task<ICompanionConnection?> EnsureConnectedAsync(CancellationToken ct = default) =>
            Task.FromResult<ICompanionConnection?>(this);

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Reset() { }

        public void Dispose() { }
    }

    private static async Task<IConnectionService> ConnectedAsync(CompanionHostHarness harness)
    {
        await harness.StartAsync();
        var client = harness.ConnectWith(harness.CreatePayload());
        await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Phone" });
        return new LiveConnection(client);
    }

    private static BrowseViewModel Browse(
        IConnectionService connection, FakeScanner scanner, List<(int BookId, string Title)> opened) =>
        new(connection, scanner, new FakeDeviceSettings(), (id, title) => opened.Add((id, title)),
            _ => Task.CompletedTask);

    private sealed class FakeScanner : IBarcodeScanner
    {
        public string? Next { get; set; }

        public Task<BarcodeScan> ScanOnceAsync(BarcodeKind kind, CancellationToken ct = default) =>
            Task.FromResult(Next is null ? BarcodeScan.Nothing : BarcodeScan.Of(Next));
    }

    private sealed class FakeDeviceSettings : IDeviceSettings
    {
        public void OpenAppSettings() { }
    }

    private static BrowseBook Book(int id, string title, string? isbn = null, bool hasCover = false) =>
        new(id, title, null, isbn, null, hasCover);

    /// <summary>A real JPEG, since the point is that the desktop decodes and shrinks it on the way out.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    [Fact]
    public async Task ThePhoneListsTheLibraryAndItsCollectionsOverTheWire()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Page = new BrowsePage([Book(1, "Kallocain"), Book(2, "Aniara")], 2);
        harness.Browse.Collections.Add(new BrowseCollection(3, "Fiction", 412));
        var vm = Browse(await ConnectedAsync(harness), new FakeScanner(), []);

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Kallocain", "Aniara"], vm.Results.Select(r => r.Title));
        Assert.True(vm.HasCollections);
        Assert.Equal(2, vm.TotalCount);
        Assert.False(vm.IsUnreachable);
    }

    [Fact]
    public async Task ScanningABookTheLibraryHasAnswersOwnedOverTheWire()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Page = new BrowsePage([Book(7, "Dune", "9780441013593")], 1);
        var scanner = new FakeScanner { Next = "978-0-441-01359-3" };
        var vm = Browse(await ConnectedAsync(harness), scanner, []);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.IsOwned);
        Assert.Equal("Dune", Assert.Single(vm.Results).Title);
        Assert.Equal("9780441013593", harness.Browse.Queries[^1].Isbn);
    }

    [Fact]
    public async Task ScanningABookTheLibraryLacksAnswersNotOwnedOverTheWire()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Page = new BrowsePage([], 0);
        var scanner = new FakeScanner { Next = "9780441013593" };
        var vm = Browse(await ConnectedAsync(harness), scanner, []);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ShowsBanner);
        Assert.False(vm.IsOwned);
    }

    [Fact]
    public async Task ARowThumbnailArrivesShrunkRatherThanAsTheStoredCover()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Page = new BrowsePage([Book(1, "Kallocain", hasCover: true)], 1);
        harness.Browse.Images[(1, 0)] = Jpeg(2000, 3000);
        var vm = Browse(await ConnectedAsync(harness), new FakeScanner(), []);

        await vm.LoadCommand.ExecuteAsync(null);

        byte[] thumbnail = Assert.Single(vm.Results).Thumbnail!;
        using var decoded = SKBitmap.Decode(thumbnail);
        Assert.Equal(ImageDownscaler.ThumbnailLongEdgePx, decoded.Height);
    }

    [Fact]
    public async Task OpeningARowLoadsThatBooksDetailAndItsPhotosOverTheWire()
    {
        await using var harness = new CompanionHostHarness();
        harness.Browse.Page = new BrowsePage([Book(7, "Kallocain")], 1);
        harness.Browse.Details[7] = new BrowseDetail(
            7, "Kallocain", null, "Karin Boye", null, "Bonniers", "1940", null, null, 191,
            "9780306406157", "Fiction", null, [0]);
        harness.Browse.Images[(7, 0)] = Jpeg(2000, 3000);

        var connection = await ConnectedAsync(harness);
        var opened = new List<(int BookId, string Title)>();
        var browse = Browse(connection, new FakeScanner(), opened);
        await browse.LoadCommand.ExecuteAsync(null);
        Assert.Single(browse.Results).OpenCommand.Execute(null);

        var (bookId, title) = Assert.Single(opened);
        var detail = new BookDetailViewModel(connection, bookId, title);
        await detail.LoadCommand.ExecuteAsync(null);

        Assert.False(detail.IsMissing);
        Assert.Equal("Kallocain", detail.BookTitle);
        Assert.Equal(6, detail.Fields.Count);
        var photo = Assert.Single(detail.Images);
        using var decoded = SKBitmap.Decode(photo.Jpeg!);
        Assert.Equal(BookDetailViewModel.ImageLongEdgePx, decoded.Height);
    }

    [Fact]
    public async Task ABookDeletedBetweenListingAndOpeningSaysSoRatherThanFailing()
    {
        await using var harness = new CompanionHostHarness();
        var detail = new BookDetailViewModel(await ConnectedAsync(harness), 7, "Kallocain");

        await detail.LoadCommand.ExecuteAsync(null);

        Assert.True(detail.IsMissing);
        Assert.False(detail.IsUnreachable);
    }
}
