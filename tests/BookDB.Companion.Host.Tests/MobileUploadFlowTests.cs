using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Logic.Services;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// End to end, both ends real: the phone's own staging store and upload service drive a live mutual-TLS
/// host over loopback. What is being proved is the durability rule — a book leaves the phone's storage only
/// once the desktop says it has it — against the real streaming protocol rather than a stub of it.
/// </summary>
public sealed class MobileUploadFlowTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"bookdb_upload_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Hands the upload service the harness's live client, standing in for the reconnect machinery
    /// that has its own tests.</summary>
    private sealed class LiveConnection : IConnectionService, ICompanionConnection
    {
        private readonly Func<IBookScannerService?> _resolve;

        public LiveConnection(Func<IBookScannerService?> resolve) => _resolve = resolve;

        public ConnectionStatus Status => _resolve() is null ? ConnectionStatus.Offline : ConnectionStatus.Connected;

        public ServerInfo? ServerInfo => null;

        public IBookScannerService Service => _resolve()!;

        public event EventHandler? StatusChanged { add { } remove { } }

        public Task<ICompanionConnection?> EnsureConnectedAsync(CancellationToken ct = default) =>
            Task.FromResult<ICompanionConnection?>(_resolve() is null ? null : this);

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

        public void Reset() { }

        public void Dispose() { }
    }

    private static async Task<IBookScannerService> ConnectedAsync(CompanionHostHarness harness)
    {
        await harness.StartAsync();
        var client = harness.ConnectWith(harness.CreatePayload());
        await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Phone" });
        return client;
    }

    private static StagedBook Book(string id, string isbn, params ScanImageType[] types) => new()
    {
        ClientItemId = id,
        Isbn = isbn,
        Images = [.. types.Select(type => new StagedImage { Type = type, Jpeg = Jpeg((byte)type) })],
    };

    /// <summary>A real JPEG: the staging store re-encodes what it is given to make the tray thumbnail.</summary>
    private static byte[] Jpeg(byte tint)
    {
        using var bitmap = new SkiaSharp.SKBitmap(40, 60);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(new SkiaSharp.SKColor(tint, 0x40, 0x80));
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        return encoded.ToArray();
    }

    [Fact]
    public async Task AStagedStack_GoesAcrossWithItsPhotosAndLeavesTheTrayEmpty()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var staging = new FileStagingStore(_dir);
        staging.Save(Book("aaa1", "9780306406157", ScanImageType.FrontCover, ScanImageType.BackCover));
        staging.Save(Book("bbb2", "9789100120115", ScanImageType.FrontCover));

        var seen = new List<BatchItemStatus>();
        var upload = new UploadService(staging, new LiveConnection(() => client));

        var result = await upload.RunAsync(seen.Add, TestContext.Current.CancellationToken);

        Assert.Equal(UploadResult.Completed, result);
        Assert.Empty(staging.Items);

        Assert.Equal(2, harness.Intake.Taken.Count);
        Assert.Equal("9780306406157", harness.Intake.Taken[0].Isbn);
        Assert.Equal(2, harness.Intake.Taken[0].Images.Count);
        Assert.Equal("9789100120115", harness.Intake.Taken[1].Isbn);
        Assert.Single(harness.Intake.Taken[1].Images);

        // Every book was reported as stored, which is what made it safe to drop.
        Assert.Equal(
            ["aaa1", "bbb2"],
            seen.Where(status => status.State == BatchItemState.Saved).Select(status => status.ClientItemId).Distinct().Order());
    }

    /// <summary>Nothing was sent and nothing was lost; sending again once the computer is back gets it all
    /// across. This is the plug being pulled and put back in.</summary>
    [Fact]
    public async Task WithNoComputerToSendTo_NothingIsLost_AndSendingAgainGetsItAcross()
    {
        await using var harness = new CompanionHostHarness();

        var staging = new FileStagingStore(_dir);
        staging.Save(Book("aaa1", "9780306406157", ScanImageType.FrontCover));

        IBookScannerService? client = null;
        var upload = new UploadService(staging, new LiveConnection(() => client));

        Assert.Equal(UploadResult.Offline, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Single(staging.Items);
        Assert.Empty(harness.Intake.Taken);

        client = await ConnectedAsync(harness);

        Assert.Equal(UploadResult.Completed, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Empty(staging.Items);
        Assert.Equal("9780306406157", Assert.Single(harness.Intake.Taken).Isbn);
    }

    /// <summary>A book the desktop refuses stays on the phone, and its neighbour still goes.</summary>
    [Fact]
    public async Task ARefusedBook_StaysStagedWhileTheRestGoes()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => item.Isbn == "9789100120115"
            ? new CompanionIntakeResult(item.ClientItemId, CompanionIntakeOutcome.Failed, null, null, CompanionIntakeFailure.InvalidIsbn)
            : new CompanionIntakeResult(item.ClientItemId, CompanionIntakeOutcome.Created, 1, 100, CompanionIntakeFailure.None);

        var client = await ConnectedAsync(harness);

        var staging = new FileStagingStore(_dir);
        staging.Save(Book("aaa1", "9780306406157", ScanImageType.FrontCover));
        staging.Save(Book("bbb2", "9789100120115"));

        var seen = new List<BatchItemStatus>();
        var upload = new UploadService(staging, new LiveConnection(() => client));
        await upload.RunAsync(seen.Add, TestContext.Current.CancellationToken);

        Assert.Equal("9789100120115", Assert.Single(staging.Items).Isbn);
        Assert.Contains(seen, status => status.ClientItemId == "bbb2" && status.Failure == BatchItemFailure.InvalidIsbn);
    }

    /// <summary>An ISBN-only scan of a book the library already has adds nothing — and still leaves the tray,
    /// because the desktop did answer for it.</summary>
    [Fact]
    public async Task ABookTheLibraryAlreadyHas_IsReportedAsSuchAndStillLeavesTheTray()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.AlreadyOwned, 7, null, CompanionIntakeFailure.None);

        var client = await ConnectedAsync(harness);

        var staging = new FileStagingStore(_dir);
        staging.Save(Book("aaa1", "9780306406157"));

        var seen = new List<BatchItemStatus>();
        var upload = new UploadService(staging, new LiveConnection(() => client));

        Assert.Equal(UploadResult.Completed, await upload.RunAsync(seen.Add, TestContext.Current.CancellationToken));
        Assert.Empty(staging.Items);
        Assert.Contains(seen, status => status.State == BatchItemState.AlreadyOwned);
    }
}
