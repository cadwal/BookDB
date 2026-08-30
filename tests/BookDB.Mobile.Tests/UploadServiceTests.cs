using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>
/// Sending the batch, with a stub desktop on the other end. The rule everything else hangs off is that a
/// book leaves the tray only once the computer says it has it — so most of these check what is *still*
/// staged after a run that went wrong.
/// </summary>
public class UploadServiceTests
{
    private sealed class StubConnection : ICompanionConnection
    {
        public StubConnection(IBookScannerService service) => Service = service;

        public IBookScannerService Service { get; }

        public void Dispose() { }
    }

    private static UploadService New(
        out FakeStagingStore staging,
        out StubScannerService desktop,
        out FakeConnectionService connection,
        bool offline = false,
        TimeSpan? writeStallTimeout = null)
    {
        staging = new FakeStagingStore();
        desktop = new StubScannerService();
        connection = new FakeConnectionService();

        if (!offline)
            connection.Connection = new StubConnection(desktop);

        return new UploadService(staging, connection, writeStallTimeout);
    }

    private static StagedBook Book(string id, string isbn, params ScanImageType[] types) => new()
    {
        ClientItemId = id,
        Isbn = isbn,
        Images = [.. types.Select(type => new StagedImage { Type = type, Jpeg = [(byte)type, 9] })],
    };

    private static BatchItemStatus Status(string id, BatchItemState state, BatchItemFailure failure = BatchItemFailure.None)
        => new() { ClientItemId = id, State = state, Failure = failure };

    [Fact]
    public async Task WithNothingStaged_ThereIsNothingToSend()
    {
        var upload = New(out _, out var desktop, out _);

        Assert.Equal(UploadResult.NothingStaged, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Empty(desktop.Uploaded);
    }

    /// <summary>
    /// A computer that refuses this device is not one that cannot be reached, and the run has to say which:
    /// the offline wording promises the batch will go when the network comes back, and it never will.
    /// </summary>
    [Fact]
    public async Task RemovedOnTheComputer_TheRunSaysItWasRefusedRatherThanOffline()
    {
        var upload = New(out var staging, out var desktop, out var connection, offline: true);
        connection.Report(ConnectionStatus.Revoked);
        staging.Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover));

        Assert.Equal(
            UploadResult.Refused, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Empty(desktop.Uploaded);
        Assert.Single(staging.Items);
    }

    [Fact]
    public async Task Offline_NothingIsAttemptedAndNothingIsLost()
    {
        var upload = New(out var staging, out var desktop, out _, offline: true);
        staging.Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover));

        Assert.Equal(UploadResult.Offline, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Empty(desktop.Uploaded);
        Assert.Single(staging.Items);
    }

    [Fact]
    public async Task EachBook_GoesUpAsAHeaderThenItsPhotos()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover, ScanImageType.BackCover));
        desktop.AutoState = BatchItemState.Saved;

        await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken);

        Assert.Equal(3, desktop.Uploaded.Count);
        Assert.Equal("9780441013593", desktop.Uploaded[0].Header!.Isbn);
        Assert.Null(desktop.Uploaded[0].Image);
        Assert.Equal(ScanImageType.FrontCover, desktop.Uploaded[1].Image!.Type);
        Assert.Equal(ScanImageType.BackCover, desktop.Uploaded[2].Image!.Type);
        Assert.All(desktop.Uploaded, message => Assert.Equal("aaa1", message.ClientItemId));
    }

    /// <summary>The phone names the batch so it can watch results while the photos are still going up.</summary>
    [Fact]
    public async Task EveryMessage_CarriesTheSameBatchId()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "1111111111111", ScanImageType.FrontCover));
        staging.Save(Book("bbb2", "2222222222222"));
        desktop.AutoState = BatchItemState.Saved;

        await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken);

        var batchId = desktop.Uploaded[0].BatchId;
        Assert.NotEmpty(batchId);
        Assert.All(desktop.Uploaded, message => Assert.Equal(batchId, message.BatchId));
    }

    [Fact]
    public async Task AnIsbnOnlyBook_GoesUpAsAHeaderAlone()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        desktop.AutoState = BatchItemState.AlreadyOwned;

        Assert.Equal(UploadResult.Completed, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Single(desktop.Uploaded);
    }

    [Theory]
    [InlineData(BatchItemState.Saved)]
    [InlineData(BatchItemState.AddedToExisting)]
    [InlineData(BatchItemState.AlreadyOwned)]
    [InlineData(BatchItemState.Cataloguing)]
    [InlineData(BatchItemState.Done)]
    [InlineData(BatchItemState.NeedsReview)]
    public async Task OnceTheComputerHasTheBook_ItLeavesTheTray(BatchItemState state)
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover));
        desktop.AutoState = state;

        Assert.Equal(UploadResult.Completed, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Empty(staging.Items);
    }

    /// <summary>Received means the header arrived, not that anything was stored — dropping the book then
    /// would lose it if the stream died a moment later.</summary>
    [Fact]
    public async Task MerelyReceived_IsNotEnoughToDropABook()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        desktop.Statuses.Writer.TryWrite(Status("aaa1", BatchItemState.Received));
        desktop.Statuses.Writer.Complete();

        Assert.Equal(UploadResult.Interrupted, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Single(staging.Items);
    }

    [Fact]
    public async Task ABookTheComputerRefused_StaysStagedAndTheRunStillFinishes()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "1111111111111"));
        staging.Save(Book("bbb2", "2222222222222"));
        desktop.Statuses.Writer.TryWrite(Status("aaa1", BatchItemState.Saved));
        desktop.Statuses.Writer.TryWrite(Status("bbb2", BatchItemState.Failed, BatchItemFailure.InvalidIsbn));

        Assert.Equal(UploadResult.Completed, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Equal("2222222222222", Assert.Single(staging.Items).Isbn);
    }

    [Fact]
    public async Task TheLinkDroppingMidUpload_LosesNothing()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "1111111111111", ScanImageType.FrontCover));
        staging.Save(Book("bbb2", "2222222222222"));
        desktop.SubmitFails = true;

        Assert.Equal(UploadResult.Interrupted, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));
        Assert.Equal(2, staging.Items.Count);
    }

    /// <summary>Sending again after an interruption picks up exactly what is left — the confirmed books are
    /// already gone and are not sent twice.</summary>
    [Fact]
    public async Task SendingAgain_OnlyRetriesWhatIsStillStaged()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "1111111111111"));
        staging.Save(Book("bbb2", "2222222222222"));
        desktop.Statuses.Writer.TryWrite(Status("aaa1", BatchItemState.Saved));
        desktop.Statuses.Writer.TryWrite(Status("bbb2", BatchItemState.Failed));
        await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken);
        desktop.Uploaded.Clear();

        desktop.Statuses.Writer.TryWrite(Status("bbb2", BatchItemState.Saved));
        Assert.Equal(UploadResult.Completed, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));

        Assert.Equal("2222222222222", Assert.Single(desktop.Uploaded).Header!.Isbn);
        Assert.Empty(staging.Items);
    }

    /// <summary>The status stream can drop while the upload itself was fine; re-opening it replays what the
    /// computer already knows.</summary>
    [Fact]
    public async Task AStatusStreamThatDrops_IsReopened()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "1111111111111"));
        desktop.Statuses.Writer.Complete();

        Assert.Equal(UploadResult.Interrupted, await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken));

        // One for the run itself and one per re-open attempt.
        Assert.Equal(3, desktop.StreamsOpened);
        Assert.Single(staging.Items);
    }

    [Fact]
    public async Task EveryStatus_IsReportedAsItArrives()
    {
        var upload = New(out var staging, out var desktop, out _);
        staging.Save(Book("aaa1", "9780441013593"));
        desktop.Statuses.Writer.TryWrite(Status("aaa1", BatchItemState.Received));
        desktop.Statuses.Writer.TryWrite(Status("aaa1", BatchItemState.Saved));

        var seen = new List<BatchItemState>();
        await upload.RunAsync(status => seen.Add(status.State), TestContext.Current.CancellationToken);

        Assert.Equal([BatchItemState.Received, BatchItemState.Saved], seen);
    }

    /// <summary>
    /// A computer that stops reading part-way through says nothing about it, so the send neither finishes
    /// nor fails — the one shape of failure that used to leave the phone on the sending screen for good.
    /// </summary>
    [Fact]
    public async Task WhenTheComputerStopsReadingPartWay_TheRunEndsInsteadOfWaitingForever()
    {
        var upload = New(
            out var staging, out var desktop, out _, writeStallTimeout: TimeSpan.FromMilliseconds(200));
        staging.Save(Book("aaa1", "9780441013593", ScanImageType.FrontCover));
        desktop.StopsReadingAfter = 1;

        var result = await upload.RunAsync(_ => { }, TestContext.Current.CancellationToken);

        Assert.Equal(UploadResult.Interrupted, result);
        Assert.Single(staging.Items);
    }
}
