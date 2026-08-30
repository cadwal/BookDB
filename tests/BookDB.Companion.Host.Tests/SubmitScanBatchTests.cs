using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Contracts;
using BookDB.Logic.Services;
using Grpc.Core;
using ProtoBuf.Grpc;
using Xunit;

namespace BookDB.Companion.Host.Tests;

/// <summary>
/// The upload half of the streaming protocol, over the wire: headers open items, images follow, and an
/// item is stored only once it is complete — so a stream that stops mid-item leaves that item unwritten.
/// </summary>
public sealed class SubmitScanBatchTests
{
    private static ScanUpload Header(string itemId, string isbn, string? batchId = null)
        => new() { ClientItemId = itemId, BatchId = batchId ?? "", Header = new ScanItemHeader { Isbn = isbn } };

    private static ScanUpload Image(string itemId, ScanImageType type, params byte[] jpeg)
        => new() { ClientItemId = itemId, Image = new ScanImage { Type = type, Jpeg = jpeg } };

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

    [Fact]
    public async Task EachCompletedItemIsTakenIntoTheLibrary()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var result = await client.SubmitScanBatchAsync(Stream(
            Header("a", "9780306406157", "batch-1"),
            Image("a", ScanImageType.FrontCover, 1, 2),
            Header("b", "9789100120115"),
            Image("b", ScanImageType.FrontCover, 3, 4)));

        Assert.Equal("batch-1", result.BatchId);
        Assert.Equal(2, result.AcceptedItems);
        Assert.Equal(2, harness.Intake.Taken.Count);
        Assert.Equal("9780306406157", harness.Intake.Taken[0].Isbn);
        Assert.Equal([1, 2], harness.Intake.Taken[0].Images.Single().Jpeg);
    }

    [Fact]
    public async Task AllOfAnItemsImagesArriveGroupedUnderIt()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        await client.SubmitScanBatchAsync(Stream(
            Header("a", "9780306406157"),
            Image("a", ScanImageType.FrontCover, 1),
            Image("a", ScanImageType.BackCover, 2),
            Image("a", ScanImageType.Spine, 3)));

        var item = Assert.Single(harness.Intake.Taken);
        Assert.Equal(
            [BookImageType(ScanImageType.FrontCover), BookImageType(ScanImageType.BackCover), BookImageType(ScanImageType.Spine)],
            item.Images.Select(i => i.ImageTypeId));

        static int BookImageType(ScanImageType type) => (int)type;
    }

    [Fact]
    public async Task AnItemMayBeSubmittedFromItsIsbnAlone()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var result = await client.SubmitScanBatchAsync(Stream(Header("a", "9780306406157")));

        Assert.Equal(1, result.AcceptedItems);
        Assert.Empty(Assert.Single(harness.Intake.Taken).Images);
    }

    [Fact]
    public async Task TheServerMintsABatchIdWhenThePhoneDidNotNameOne()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var result = await client.SubmitScanBatchAsync(Stream(Header("a", "9780306406157")));

        Assert.False(string.IsNullOrEmpty(result.BatchId));
    }

    [Fact]
    public async Task AnEmptyUploadTakesNothingIn()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var result = await client.SubmitScanBatchAsync(Stream());

        Assert.Equal(0, result.AcceptedItems);
        Assert.Empty(harness.Intake.Taken);
    }

    [Fact]
    public async Task AnImageBeforeItsHeaderIsRejected()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        var error = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.SubmitScanBatchAsync(Stream(Image("a", ScanImageType.FrontCover, 1))));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Empty(harness.Intake.Taken);
    }

    [Fact]
    public async Task AFailedItemDoesNotCountAsAccepted()
    {
        await using var harness = new CompanionHostHarness();
        harness.Intake.Handle = item => new CompanionIntakeResult(
            item.ClientItemId, CompanionIntakeOutcome.Failed, null, null, CompanionIntakeFailure.InvalidIsbn);
        var client = await ConnectedAsync(harness);

        var result = await client.SubmitScanBatchAsync(Stream(Header("a", "not-an-isbn")));

        Assert.Equal(0, result.AcceptedItems);
    }

    [Fact]
    public async Task AnItemIsNotTakenInUntilItsLastImageHasArrived()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);

        // The first item must not be finalized until the second item's header proves it is complete.
        await client.SubmitScanBatchAsync(Stream(
            Header("a", "9780306406157"),
            Image("a", ScanImageType.FrontCover, 1),
            Image("a", ScanImageType.BackCover, 2),
            Header("b", "9789100120115")));

        Assert.Equal(2, harness.Intake.Taken.Count);
        Assert.Equal(2, harness.Intake.Taken[0].Images.Count);
    }

    [Fact]
    public async Task AnOversizedFrameIsRefusedWithoutTakingTheItemIn()
    {
        await using var harness = new CompanionHostHarness();
        var client = await ConnectedAsync(harness);
        byte[] huge = new byte[5 * 1024 * 1024];

        // What the sender sees of the refusal is not guaranteed. It may arrive as a send failure wrapping
        // ResourceExhausted, or the write may never complete at all: a frame the server refuses mid-stream
        // leaves the writer on a flow-control window nothing reopens, which is why the phone bounds its own
        // wait rather than trusting the transport to report it. Guaranteed, and what this pins: the call
        // never succeeds, and the item is never taken in.
        using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await client.SubmitScanBatchAsync(
                Stream(Header("a", "9780306406157"), Image("a", ScanImageType.FrontCover, huge)),
                new CallContext(new CallOptions(cancellationToken: bounded.Token))));

        Assert.Empty(harness.Intake.Taken);
    }
}
