using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using BookDB.Logic.Services;
using BookDB.Models.Entities;
using Grpc.Core;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The status half of the streaming protocol: a phone watching an upload sees each item saved, then the
/// catalogue queue's own progress read back from the rows, and a reconnecting phone catches up from a
/// snapshot before it sees anything live.
/// </summary>
public sealed class StreamBatchStatusTests
{
    private static ScanUpload Header(string itemId, string isbn, string batchId)
        => new() { ClientItemId = itemId, BatchId = batchId, Header = new ScanItemHeader { Isbn = isbn } };

    private static async IAsyncEnumerable<ScanUpload> Stream(params ScanUpload[] uploads)
    {
        foreach (var upload in uploads)
        {
            yield return upload;
            await Task.Yield();
        }
    }

    private static async Task<IBookScannerService> ConnectedAsync(CompanionHostHarness harness)
    {
        await harness.StartAsync();
        var client = harness.ConnectWith(harness.CreatePayload());
        await client.ClaimDeviceAsync(new ClaimDeviceRequest { DeviceName = "Phone" });
        return client;
    }

    /// <summary>Reads the stream until every wanted client item has reached a state the predicate accepts.</summary>
    private static async Task<Dictionary<string, BatchItemStatus>> ReadUntilAsync(
        IBookScannerService client,
        string batchId,
        IReadOnlyCollection<string> itemIds,
        Func<BatchItemStatus, bool> done)
    {
        var latest = new Dictionary<string, BatchItemStatus>(StringComparer.Ordinal);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await foreach (var status in client.StreamBatchStatusAsync(
            new BatchRef { BatchId = batchId }, new ProtoBuf.Grpc.CallContext(new CallOptions(cancellationToken: timeout.Token))))
        {
            latest[status.ClientItemId] = status;
            if (itemIds.All(id => latest.TryGetValue(id, out var s) && done(s)))
            {
                return latest;
            }
        }

        return latest;
    }

    [Fact]
    public async Task WatchingAnUploadShowsEachItemBeingSaved()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.Created, 10, null, CompanionIntakeFailure.None);
        var client = await ConnectedAsync(harness);
        await client.SubmitScanBatchAsync(Stream(Header("a", "9780306406157", "batch-1")));

        var statuses = await ReadUntilAsync(
            client, "batch-1", ["a"], s => s.State == BatchItemState.Saved);

        Assert.Equal(BatchItemState.Saved, statuses["a"].State);
        Assert.Equal(10, statuses["a"].BookId);
    }

    [Fact]
    public async Task AnAddedToExistingItemIsReportedAsSuch()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.AddedToExisting, 7, null, CompanionIntakeFailure.None);
        var client = await ConnectedAsync(harness);
        await client.SubmitScanBatchAsync(Stream(Header("a", "9780306406157", "batch-1")));

        var statuses = await ReadUntilAsync(
            client, "batch-1", ["a"], s => s.State != BatchItemState.Received);

        Assert.Equal(BatchItemState.AddedToExisting, statuses["a"].State);
    }

    [Fact]
    public async Task TheCatalogueQueuesProgressIsFollowedThroughToDone()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.Created, 10, 100, CompanionIntakeFailure.None);
        harness.QueueStatus.Rows[100] = new CompanionQueueItemStatus(100, BatchStatus.Processing, null, 10);
        var client = await ConnectedAsync(harness);
        await client.SubmitScanBatchAsync(Stream(Header("a", "9780306406157", "batch-1")));

        // The queue advances underneath the phone while it watches.
        var advancing = TestContext.Current.CancellationToken;
        _ = Task.Run(async () =>
        {
            await Task.Delay(200, advancing);
            harness.QueueStatus.Rows[100] = new CompanionQueueItemStatus(100, BatchStatus.Done, null, 10);
        }, advancing);

        var statuses = await ReadUntilAsync(
            client, "batch-1", ["a"], s => s.State == BatchItemState.Done);

        Assert.Equal(BatchItemState.Done, statuses["a"].State);
    }

    [Fact]
    public async Task AnItemThatNeedsReviewIsReportedThatWay()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.Created, 10, 100, CompanionIntakeFailure.None);
        harness.QueueStatus.Rows[100] = new CompanionQueueItemStatus(100, BatchStatus.PendingReview, null, 10);
        var client = await ConnectedAsync(harness);
        await client.SubmitScanBatchAsync(Stream(Header("a", "9780306406157", "batch-1")));

        var statuses = await ReadUntilAsync(
            client, "batch-1", ["a"], s => s.State is BatchItemState.NeedsReview);

        Assert.Equal(BatchItemState.NeedsReview, statuses["a"].State);
    }

    [Fact]
    public async Task AFailedCataloguingIsReportedWithItsReason()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.Created, 10, 100, CompanionIntakeFailure.None);
        harness.QueueStatus.Rows[100] = new CompanionQueueItemStatus(100, BatchStatus.Failed, "NoResults", 10);
        var client = await ConnectedAsync(harness);
        await client.SubmitScanBatchAsync(Stream(Header("a", "9780306406157", "batch-1")));

        var statuses = await ReadUntilAsync(
            client, "batch-1", ["a"], s => s.State == BatchItemState.Failed);

        Assert.Equal(BatchItemState.Failed, statuses["a"].State);
        Assert.Equal(BatchItemFailure.CatalogueFailed, statuses["a"].Failure);
        Assert.Equal("NoResults", statuses["a"].FailureDetail);
    }

    [Fact]
    public async Task AReconnectingPhoneCatchesUpFromASnapshot()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.Created, 10, 100, CompanionIntakeFailure.None);
        harness.QueueStatus.Rows[100] = new CompanionQueueItemStatus(100, BatchStatus.Done, null, 10);
        var client = await ConnectedAsync(harness);

        // Upload finishes with nobody watching; a phone that reconnects afterwards must still learn the outcome.
        await client.SubmitScanBatchAsync(Stream(Header("a", "9780306406157", "batch-1")));

        var statuses = await ReadUntilAsync(
            client, "batch-1", ["a"], s => s.State == BatchItemState.Done);

        Assert.Equal(BatchItemState.Done, statuses["a"].State);
    }
}
