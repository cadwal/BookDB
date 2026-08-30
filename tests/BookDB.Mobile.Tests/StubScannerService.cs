using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Services;
using Grpc.Core;
using ProtoBuf.Grpc;

namespace BookDB.Mobile.Tests;

/// <summary>A hand-rolled desktop service for coordinator unit tests: only the handshake and claim are used,
/// and both are configurable. The wire-level round-trip against a real host is covered separately.</summary>
internal sealed class StubScannerService : IBookScannerService
{
    public ServerInfo Info { get; set; } = new() { ContractVersion = ContractVersion.Current, LibraryName = "Library" };
    public ClaimDeviceResult ClaimResult { get; set; } = new() { Outcome = ClaimOutcome.Registered };

    /// <summary>False stands for a computer that is no longer answering at that address.</summary>
    public bool Reachable { get; set; } = true;

    /// <summary>
    /// True stands for a computer that answers but has taken this device off its list — what the host's own
    /// certificate interceptor does, down to the status code.
    /// </summary>
    public bool Revoked { get; set; }

    public ValueTask<ServerInfo> GetServerInfoAsync(CallContext context = default) =>
        !Reachable ? throw new RpcException(new Status(StatusCode.Unavailable, "nothing listening"))
        : Revoked ? throw new RpcException(new Status(StatusCode.PermissionDenied, "Unknown or revoked device."))
        : new(Info);

    public ValueTask<ClaimDeviceResult> ClaimDeviceAsync(ClaimDeviceRequest request, CallContext context = default) => new(ClaimResult);

    /// <summary>What the desktop knows about a scanned number, for the wizard's confidence line.</summary>
    public IsbnPreview Preview { get; set; } = new();

    /// <summary>Set to have the preview call fail the way an unreachable computer does.</summary>
    public bool PreviewFails { get; set; }

    /// <summary>Holds the preview answer until the test lets it go — a computer that is thinking, which is
    /// the state that decides whether the wizard waits for the confidence line or moves on without it.</summary>
    public Task? PreviewGate { get; set; }

    public List<string> Checked { get; } = [];

    public async ValueTask<IsbnPreview> CheckIsbnAsync(IsbnQuery query, CallContext context = default)
    {
        Checked.Add(query.Isbn);

        if (PreviewGate is not null)
            await PreviewGate.WaitAsync(context.CancellationToken);

        return PreviewFails
            ? throw new RpcException(new Status(StatusCode.Unavailable, "nothing listening"))
            : Preview;
    }

    /// <summary>Every message the phone sent, in order — headers and images both.</summary>
    public List<ScanUpload> Uploaded { get; } = [];

    /// <summary>Statuses the test hands back. Written before or during a run; the stream ends when the
    /// channel is completed.</summary>
    public Channel<BatchItemStatus> Statuses { get; } = Channel.CreateUnbounded<BatchItemStatus>();

    /// <summary>
    /// What to report for each item once all of its messages have arrived. A real desktop cannot say it has
    /// stored a book before the book is complete, so reporting on finalization — at the next header, or at
    /// the end of the stream — is the only ordering a test should rely on.
    /// </summary>
    public BatchItemState? AutoState { get; set; }

    /// <summary>Per-item override of <see cref="AutoState"/>.</summary>
    public Dictionary<string, BatchItemState> AutoStates { get; } = [];

    /// <summary>Stands for the link dropping part-way through an upload.</summary>
    public bool SubmitFails { get; set; }

    public int StreamsOpened { get; private set; }

    /// <summary>
    /// How many frames the computer takes before it stops reading without saying so — what a frame it
    /// refuses mid-stream looks like from the sending end: the write never completes and never faults,
    /// because the flow-control window it is waiting on is never reopened.
    /// </summary>
    public int? StopsReadingAfter { get; set; }

    public async ValueTask<SubmitBatchResult> SubmitScanBatchAsync(
        IAsyncEnumerable<ScanUpload> uploads, CallContext context = default)
    {
        string? open = null;

        await foreach (var upload in uploads.WithCancellation(context.CancellationToken))
        {
            Uploaded.Add(upload);

            if (SubmitFails)
                throw new RpcException(new Status(StatusCode.Unavailable, "the link went"));

            if (Uploaded.Count == StopsReadingAfter)
                await Task.Delay(System.Threading.Timeout.Infinite, context.CancellationToken);

            if (upload.Header is not null)
            {
                Finalize(open);
                open = upload.ClientItemId;
            }
        }

        Finalize(open);

        return new SubmitBatchResult
        {
            BatchId = Uploaded.Count > 0 ? Uploaded[0].BatchId : "",
            AcceptedItems = Uploaded.Count(upload => upload.Header is not null),
        };
    }

    private void Finalize(string? clientItemId)
    {
        if (clientItemId is null)
            return;

        if (!AutoStates.TryGetValue(clientItemId, out var state))
        {
            if (AutoState is not { } fallback)
                return;

            state = fallback;
        }

        Statuses.Writer.TryWrite(new BatchItemStatus { ClientItemId = clientItemId, State = state });
    }

    public async IAsyncEnumerable<BatchItemStatus> StreamBatchStatusAsync(
        BatchRef batch, CallContext context = default)
    {
        StreamsOpened++;

        await foreach (var status in Statuses.Reader.ReadAllAsync(context.CancellationToken))
            yield return status;
    }

    /// <summary>Every browse query the phone sent, in order — what paging and filtering are asserted on.</summary>
    public List<BookQuery> Queries { get; } = [];

    /// <summary>Answers each browse query in turn; the last one is repeated once the list runs out, so a test
    /// that only cares about one page need supply only that page.</summary>
    public List<BookPage> Pages { get; } = [];

    /// <summary>Set to have browsing fail the way an unreachable computer does.</summary>
    public bool BrowseFails { get; set; }

    public ValueTask<BookPage> ListBooksAsync(BookQuery query, CallContext context = default)
    {
        Queries.Add(query);

        if (BrowseFails)
            throw new RpcException(new Status(StatusCode.Unavailable, "nothing listening"));

        if (Pages.Count == 0)
            return new ValueTask<BookPage>(new BookPage());

        return new ValueTask<BookPage>(Pages[Math.Min(Queries.Count - 1, Pages.Count - 1)]);
    }

    public List<CollectionRef> Collections { get; } = [];

    public int CollectionCalls { get; private set; }

    public ValueTask<CollectionList> ListCollectionsAsync(CallContext context = default)
    {
        CollectionCalls++;

        if (BrowseFails)
            throw new RpcException(new Status(StatusCode.Unavailable, "nothing listening"));

        return new ValueTask<CollectionList>(new CollectionList { Collections = [.. Collections] });
    }

    /// <summary>Keyed by book id; a book that is not here answers as one deleted since it was listed.</summary>
    public Dictionary<int, BookDetail> Details { get; } = [];

    public ValueTask<BookDetail> GetBookDetailAsync(BookRef book, CallContext context = default)
    {
        if (BrowseFails)
            throw new RpcException(new Status(StatusCode.Unavailable, "nothing listening"));

        return new ValueTask<BookDetail>(Details.TryGetValue(book.BookId, out var detail)
            ? detail
            : new BookDetail { Found = false, BookId = book.BookId });
    }

    public Dictionary<(int BookId, ScanImageType Type), byte[]> Images { get; } = [];

    public List<BookImageRef> ImageReads { get; } = [];

    public ValueTask<ImageBytes> GetBookImageAsync(BookImageRef imageRef, CallContext context = default)
    {
        ImageReads.Add(imageRef);

        if (BrowseFails)
            throw new RpcException(new Status(StatusCode.Unavailable, "nothing listening"));

        return new ValueTask<ImageBytes>(new ImageBytes
        {
            Jpeg = Images.TryGetValue((imageRef.BookId, imageRef.Type), out var bytes) ? bytes : null,
        });
    }
}

internal sealed class StubChannelFactory : ICompanionChannelFactory
{
    private readonly Func<DeviceIdentity, IBookScannerService> _resolve;

    public StubChannelFactory(IBookScannerService service) => _resolve = _ => service;

    public StubChannelFactory(Func<DeviceIdentity, IBookScannerService> resolve) => _resolve = resolve;

    public int ConnectCount { get; private set; }

    public List<string> Dialled { get; } = [];

    public int OpenConnections { get; private set; }

    public ICompanionConnection Connect(DeviceIdentity identity)
    {
        ConnectCount++;
        OpenConnections++;
        Dialled.Add(identity.Endpoint);
        return new StubConnection(_resolve(identity), () => OpenConnections--);
    }

    private sealed class StubConnection : ICompanionConnection
    {
        private readonly Action _onDispose;

        public StubConnection(IBookScannerService service, Action onDispose)
        {
            Service = service;
            _onDispose = onDispose;
        }

        public IBookScannerService Service { get; }

        public void Dispose() => _onDispose();
    }
}
