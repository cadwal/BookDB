using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Staging;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace BookDB.Mobile.Services;

/// <summary>
/// Streams the staged books to the computer and watches what it makes of them. The two run at once on
/// purpose — the phone names the batch precisely so results can land while the photos are still going up,
/// and each book can leave the tray the moment the computer says it has it.
/// </summary>
public sealed class UploadService : IUploadService
{
    /// <summary>The status stream can drop while the upload is still accounted for. Re-opening it replays
    /// what the computer already knows, which is exactly what its snapshot is for; a couple of tries is
    /// enough to tell a blip from a computer that has gone away.</summary>
    private const int ReconnectAttempts = 2;

    /// <summary>
    /// How long one frame may sit unwritten before the upload is treated as gone. A write that cannot
    /// finish is not always an error the transport reports: a frame the computer refuses mid-stream leaves
    /// the writer waiting on a flow-control window that never reopens, and a link that dies between frames
    /// looks the same from here. Generous against the largest frame on a poor link, since overshooting only
    /// delays an answer while undershooting abandons an upload that was still moving.
    /// </summary>
    private static readonly TimeSpan DefaultWriteStallTimeout = TimeSpan.FromSeconds(60);

    private readonly IStagingStore _staging;
    private readonly IConnectionService _connection;
    private readonly TimeSpan _writeStallTimeout;

    public UploadService(
        IStagingStore staging, IConnectionService connection, TimeSpan? writeStallTimeout = null)
    {
        _staging = staging;
        _connection = connection;
        _writeStallTimeout = writeStallTimeout ?? DefaultWriteStallTimeout;
    }

    public async Task<UploadResult> RunAsync(Action<BatchItemStatus> onStatus, CancellationToken ct = default)
    {
        var items = _staging.Items.ToList();
        if (items.Count == 0)
            return UploadResult.NothingStaged;

        var connection = await _connection.EnsureConnectedAsync(ct);
        if (connection is null)
            return _connection.Status == ConnectionStatus.Revoked ? UploadResult.Refused : UploadResult.Offline;

        var batchId = Guid.NewGuid().ToString("N");
        var outstanding = new HashSet<string>(items.Select(item => item.ClientItemId), StringComparer.Ordinal);
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var watching = WatchAsync(connection.Service, batchId, outstanding, onStatus, run);

        try
        {
            await connection.Service.SubmitScanBatchAsync(
                Messages(items, batchId, run, run.Token),
                new CallContext(new CallOptions(cancellationToken: run.Token)));
        }
        catch (Exception ex) when (IsTransport(ex) || ex is OperationCanceledException)
        {
            // Nothing the computer did not confirm has left the tray, so the books are all still here.
            run.Cancel();
            await watching;
            return UploadResult.Interrupted;
        }

        await watching;

        for (var attempt = 0; outstanding.Count > 0 && attempt < ReconnectAttempts && !ct.IsCancellationRequested; attempt++)
            await WatchAsync(connection.Service, batchId, outstanding, onStatus, run);

        return outstanding.Count == 0 ? UploadResult.Completed : UploadResult.Interrupted;
    }

    /// <summary>Reads statuses until every book is accounted for, then stops the run.</summary>
    private async Task WatchAsync(
        IBookScannerService service,
        string batchId,
        HashSet<string> outstanding,
        Action<BatchItemStatus> onStatus,
        CancellationTokenSource run)
    {
        try
        {
            var statuses = service.StreamBatchStatusAsync(
                new BatchRef { BatchId = batchId },
                new CallContext(new CallOptions(cancellationToken: run.Token)));

            await foreach (var status in statuses.WithCancellation(run.Token))
            {
                onStatus(status);

                if (IsAccepted(status.State))
                {
                    // The one rule that makes an interruption harmless: a book is dropped only once the
                    // computer has it.
                    _staging.Remove(status.ClientItemId);
                }

                if (IsAccepted(status.State) || status.State == BatchItemState.Failed)
                {
                    outstanding.Remove(status.ClientItemId);

                    // Leaving the loop disposes the stream, which is what ends the server's side of it —
                    // cancelling the run here would take the upload down with it.
                    if (outstanding.Count == 0)
                        return;
                }
            }
        }
        catch (Exception ex) when (IsTransport(ex) || ex is OperationCanceledException)
        {
            // Either we stopped it ourselves or the link went; both are answered by what is left outstanding.
        }
    }

    /// <summary>The upload stream: a header opens each book, its photos follow.</summary>
    private async IAsyncEnumerable<ScanUpload> Messages(
        IReadOnlyList<StagedItem> items,
        string batchId,
        CancellationTokenSource run,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Re-armed before every frame and disarmed once the last one is away: resuming here is the only
        // proof the frame before it was actually written, and after that the computer may take as long as
        // it needs to answer.
        try
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();

                run.CancelAfter(_writeStallTimeout);
                yield return new ScanUpload
                {
                    BatchId = batchId,
                    ClientItemId = item.ClientItemId,
                    Header = new ScanItemHeader { Isbn = item.Isbn },
                };

                foreach (var type in item.ImageTypes)
                {
                    // Off the calling thread: this runs on the UI thread, and a batch is a lot of file reads.
                    var jpeg = await Task.Run(() => _staging.ReadImage(item.ClientItemId, type), ct);
                    if (jpeg is null)
                        continue;

                    run.CancelAfter(_writeStallTimeout);
                    yield return new ScanUpload
                    {
                        BatchId = batchId,
                        ClientItemId = item.ClientItemId,
                        Image = new ScanImage { Type = type, Jpeg = jpeg },
                    };
                }
            }
        }
        finally
        {
            run.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>States that mean the computer has the book — everything from here on happens at its end.</summary>
    private static bool IsAccepted(BatchItemState state) => state is
        BatchItemState.Saved or
        BatchItemState.AddedToExisting or
        BatchItemState.AlreadyOwned or
        BatchItemState.Cataloguing or
        BatchItemState.Done or
        BatchItemState.NeedsReview;

    private static bool IsTransport(Exception ex) => ex is RpcException or HttpRequestException or IOException;
}
