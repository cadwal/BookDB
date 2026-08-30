using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Logic.Services;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using ProtoBuf.Grpc;

// Import individually: BookDB.Models.Entities also defines a Status type that would collide with gRPC's.
using BookImageTypeId = BookDB.Models.Entities.BookImageTypeId;
using BatchStatus = BookDB.Models.Entities.BatchStatus;

namespace BookDB.Companion.Host;

/// <summary>
/// The desktop side of the contract. Every method here runs behind
/// <see cref="ClientCertificateInterceptor"/>, so the caller is always an authorised device.
/// </summary>
public sealed class CompanionScannerService : IBookScannerService
{
    /// <summary>A device name longer than this is the phone misbehaving; the desktop list has to render it.</summary>
    private const int MaxDeviceNameLength = 64;

    /// <summary>How often the catalogue queue is re-read while a phone is watching an upload.</summary>
    private static readonly TimeSpan QueuePollInterval = TimeSpan.FromSeconds(1);

    private readonly ICompanionEnvironment _environment;
    private readonly IPairingService _pairing;
    private readonly IDeviceRegistry _registry;
    private readonly CompanionLibrary _library;
    private readonly CompanionBatchSessions _sessions;
    private readonly ThumbnailCache _thumbnails;
    private readonly TimeProvider _clock;
    private readonly IHttpContextAccessor _http;

    public CompanionScannerService(
        ICompanionEnvironment environment,
        IPairingService pairing,
        IDeviceRegistry registry,
        CompanionLibrary library,
        CompanionBatchSessions sessions,
        ThumbnailCache thumbnails,
        TimeProvider clock,
        IHttpContextAccessor http)
    {
        _environment = environment;
        _pairing = pairing;
        _registry = registry;
        _library = library;
        _sessions = sessions;
        _thumbnails = thumbnails;
        _clock = clock;
        _http = http;
    }

    /// <summary>The calling device, as established by the interceptor before the call reached this class.</summary>
    internal string CallerThumbprint =>
        _http.HttpContext?.Items[ClientCertificateInterceptor.ThumbprintItemKey] as string
        ?? throw new RpcException(new Status(StatusCode.Internal, "Caller was not authorised."));

    /// <summary>
    /// Everything except the handshake and the claim itself is for registered devices only. A device holding
    /// a live pairing offer can reach the host — it must, in order to claim itself — but until it claims it
    /// does not appear in the device list, which is the only record of who may talk to this library and the
    /// only place the user can revoke from. Without this, whoever photographed the code could read the
    /// library and write to it for the offer's lifetime while leaving no trace anywhere the user can look.
    /// </summary>
    private void RequireRegisteredDevice()
    {
        if (!_registry.IsRegistered(CallerThumbprint))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Device is not registered."));
        }
    }

    public ValueTask<ServerInfo> GetServerInfoAsync(CallContext context = default)
        => new(new ServerInfo
        {
            AppVersion = _environment.AppVersion,
            ContractVersion = ContractVersion.Current,
            LibraryName = _environment.LibraryName,
            InstanceId = _environment.InstanceId,
            Capture = _environment.Capture,
        });

    public ValueTask<ClaimDeviceResult> ClaimDeviceAsync(ClaimDeviceRequest request, CallContext context = default)
    {
        string name = (request.DeviceName ?? "").Trim();
        if (name.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A device name is required."));
        }

        if (name.Length > MaxDeviceNameLength)
        {
            name = name.Substring(0, MaxDeviceNameLength);
        }

        var outcome = _pairing.Claim(CallerThumbprint, name);

        return new ValueTask<ClaimDeviceResult>(new ClaimDeviceResult
        {
            Outcome = ToContract(outcome),
            RegisteredDeviceCount = _registry.Count,
            MaxDevices = DeviceRegistry.MaxDevices,
        });
    }

    public async ValueTask<IsbnPreview> CheckIsbnAsync(IsbnQuery query, CallContext context = default)
    {
        RequireRegisteredDevice();

        var preview = await _library.Preview.PreviewIsbnAsync(query.Isbn ?? "", context.CancellationToken)
            .ConfigureAwait(false);

        return new IsbnPreview
        {
            InLibrary = preview.InLibrary,
            BookId = preview.BookId,
            Title = preview.Title,
            Authors = preview.Authors,
            Source = preview.Origin switch
            {
                IsbnPreviewOrigin.Library => IsbnPreviewSource.Library,
                IsbnPreviewOrigin.Lookup => IsbnPreviewSource.Lookup,
                _ => IsbnPreviewSource.None,
            },
        };
    }

    /// <summary>
    /// Reads the upload stream item by item. An item is taken into the library only once its last photo
    /// has arrived — the next header, or the end of the stream — so a connection that drops mid-item
    /// leaves that item entirely unwritten.
    /// </summary>
    public async ValueTask<SubmitBatchResult> SubmitScanBatchAsync(
        IAsyncEnumerable<ScanUpload> uploads, CallContext context = default)
    {
        RequireRegisteredDevice();

        var cancellationToken = context.CancellationToken;
        CompanionBatchSession? session = null;
        string batchId = "";
        PendingItem? open = null;
        int accepted = 0;

        await foreach (var upload in uploads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (session is null)
            {
                // The phone names the batch so it can watch results while still uploading; if it did not,
                // one is minted here and the results are only observable once the upload finishes.
                batchId = string.IsNullOrEmpty(upload.BatchId) ? Guid.NewGuid().ToString("N") : upload.BatchId;
                session = _sessions.GetOrCreate(batchId);
            }
            else if (!string.IsNullOrEmpty(upload.BatchId) && upload.BatchId != batchId)
            {
                throw Invalid("Every message in one upload must carry the same batch id.");
            }

            if (upload.Header is not null)
            {
                accepted += await FinalizeAsync(session, open, cancellationToken).ConfigureAwait(false);
                open = new PendingItem(upload.ClientItemId, upload.Header.Isbn ?? "");
                session.Report(open.ClientItemId, BatchItemState.Received, _clock.GetUtcNow(), isbn: open.Isbn);
            }
            else if (upload.Image is not null)
            {
                if (open is null || open.ClientItemId != upload.ClientItemId)
                {
                    throw Invalid("An image arrived before its item's header.");
                }

                open.Images.Add(new CompanionScanImage((int)upload.Image.Type, upload.Image.Jpeg ?? []));
            }
            else
            {
                throw Invalid("An upload message must carry either a header or an image.");
            }
        }

        accepted += await FinalizeAsync(session, open, cancellationToken).ConfigureAwait(false);

        return new SubmitBatchResult { BatchId = batchId, AcceptedItems = accepted };
    }

    /// <summary>
    /// Sends what is already known about the batch, then everything that happens next — including the
    /// catalogue queue's own progress, which is read back from the queue rows while anyone is listening.
    /// </summary>
    public async IAsyncEnumerable<BatchItemStatus> StreamBatchStatusAsync(
        BatchRef batch, CallContext context = default)
    {
        RequireRegisteredDevice();

        var cancellationToken = context.CancellationToken;
        var session = _sessions.GetOrCreate(batch.BatchId ?? "");

        using var watcher = session.Watch();

        await PollQueueAsync(session, cancellationToken).ConfigureAwait(false);
        foreach (var status in session.Snapshot())
        {
            yield return status;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            while (watcher.Reader.TryRead(out var status))
            {
                yield return status;
            }

            if (!await WaitForWorkAsync(watcher, cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }

            if (!session.IsSettled())
            {
                await PollQueueAsync(session, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Waits for the next update or for the poll timer, whichever comes first.</summary>
    private static async Task<bool> WaitForWorkAsync(CompanionBatchSession.Watcher watcher, CancellationToken ct)
    {
        try
        {
            var next = watcher.Reader.WaitToReadAsync(ct).AsTask();
            await Task.WhenAny(next, Task.Delay(QueuePollInterval, ct)).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            // The phone hung up; ending the stream is the whole response.
            return false;
        }
    }

    private async Task PollQueueAsync(CompanionBatchSession session, CancellationToken ct)
    {
        var tracked = session.TrackedQueueItems();
        if (tracked.Count == 0)
        {
            return;
        }

        var rows = await _library.QueueStatus
            .GetStatusesAsync([.. tracked.Select(t => t.BatchQueueItemId)], ct)
            .ConfigureAwait(false);
        var byId = rows.ToDictionary(row => row.BatchQueueItemId);
        var now = _clock.GetUtcNow();

        foreach (var (clientItemId, queueItemId) in tracked)
        {
            if (!byId.TryGetValue(queueItemId, out var row) || ToItemState(row.Status) is not { } state)
            {
                continue;
            }

            session.Report(
                clientItemId,
                state,
                now,
                bookId: row.BookId,
                failure: state == BatchItemState.Failed ? BatchItemFailure.CatalogueFailed : BatchItemFailure.None,
                failureDetail: state == BatchItemState.Failed ? row.FailureCode : null);
        }
    }

    private async Task<int> FinalizeAsync(
        CompanionBatchSession? session, PendingItem? item, CancellationToken ct)
    {
        if (session is null || item is null)
        {
            return 0;
        }

        var result = await _library.Intake
            .IntakeAsync(new CompanionScanItem(item.ClientItemId, item.Isbn, item.Images), ct)
            .ConfigureAwait(false);

        if (result.BatchQueueItemId is { } queueItemId)
        {
            session.TrackQueueItem(item.ClientItemId, queueItemId);
        }

        session.Report(
            item.ClientItemId,
            ToItemState(result.Outcome),
            _clock.GetUtcNow(),
            isbn: item.Isbn,
            bookId: result.BookId,
            failure: ToContract(result.Failure));

        return result.Outcome == CompanionIntakeOutcome.Failed ? 0 : 1;
    }

    private static BatchItemState ToItemState(CompanionIntakeOutcome outcome) => outcome switch
    {
        CompanionIntakeOutcome.Created => BatchItemState.Saved,
        CompanionIntakeOutcome.AddedToExisting => BatchItemState.AddedToExisting,
        CompanionIntakeOutcome.AlreadyOwned => BatchItemState.AlreadyOwned,
        _ => BatchItemState.Failed,
    };

    /// <summary>Null for a queue status that says nothing new to the phone, such as a still-pending item.</summary>
    private static BatchItemState? ToItemState(string queueStatus) => queueStatus switch
    {
        BatchStatus.Processing => BatchItemState.Cataloguing,
        BatchStatus.Done or BatchStatus.AutoAccepted or BatchStatus.Skipped => BatchItemState.Done,
        BatchStatus.PendingReview => BatchItemState.NeedsReview,
        BatchStatus.Failed => BatchItemState.Failed,
        _ => null,
    };

    private static BatchItemFailure ToContract(CompanionIntakeFailure failure) => failure switch
    {
        CompanionIntakeFailure.InvalidIsbn => BatchItemFailure.InvalidIsbn,
        CompanionIntakeFailure.SaveFailed => BatchItemFailure.SaveFailed,
        _ => BatchItemFailure.None,
    };

    private static RpcException Invalid(string message)
        => new(new Status(StatusCode.InvalidArgument, message));

    /// <summary>One item's messages, held only until the item is complete enough to store.</summary>
    private sealed class PendingItem
    {
        public PendingItem(string clientItemId, string isbn)
        {
            ClientItemId = clientItemId;
            Isbn = isbn;
        }

        public string ClientItemId { get; }
        public string Isbn { get; }
        public List<CompanionScanImage> Images { get; } = [];
    }

    public async ValueTask<BookPage> ListBooksAsync(BookQuery query, CallContext context = default)
    {
        RequireRegisteredDevice();

        var cancellationToken = context.CancellationToken;
        var page = await _library.Browse
            .BrowseAsync(query.Search, query.CollectionId, query.Isbn, query.Skip, query.Take, cancellationToken)
            .ConfigureAwait(false);

        var books = new List<BookSummary>(page.Books.Count);
        foreach (var book in page.Books)
        {
            books.Add(new BookSummary
            {
                BookId = book.BookId,
                Title = book.Title,
                Authors = book.Authors,
                Isbn = book.Isbn,
                Year = book.Year,
                Thumbnail = book.HasCover
                    ? await ThumbnailAsync(book.BookId, cancellationToken).ConfigureAwait(false)
                    : null,
            });
        }

        return new BookPage { Books = books, TotalCount = page.TotalCount };
    }

    public async ValueTask<CollectionList> ListCollectionsAsync(CallContext context = default)
    {
        RequireRegisteredDevice();

        var collections = await _library.Browse
            .GetCollectionsAsync(context.CancellationToken).ConfigureAwait(false);

        return new CollectionList
        {
            Collections = [.. collections.Select(collection => new CollectionRef
            {
                CollectionId = collection.CollectionId,
                Name = collection.Name,
                BookCount = collection.BookCount,
            })],
        };
    }

    public async ValueTask<BookDetail> GetBookDetailAsync(BookRef book, CallContext context = default)
    {
        RequireRegisteredDevice();

        var detail = await _library.Browse
            .GetDetailAsync(book.BookId, context.CancellationToken).ConfigureAwait(false);

        if (detail is null)
        {
            return new BookDetail { Found = false, BookId = book.BookId };
        }

        return new BookDetail
        {
            Found = true,
            BookId = detail.BookId,
            Title = detail.Title,
            Subtitle = detail.Subtitle,
            Authors = detail.Authors,
            Series = detail.Series,
            Publisher = detail.Publisher,
            PubDate = detail.PubDate,
            Format = detail.Format,
            FormatKey = detail.FormatKey,
            Language = detail.Language,
            LanguageKey = detail.LanguageKey,
            Pages = detail.Pages,
            Isbn = detail.Isbn,
            Collection = detail.Collection,
            Comments = detail.Comments,

            // The wire image types carry the desktop's own numbers, so this is a cast, not a mapping.
            Images = [.. detail.ImageTypeIds.Select(typeId => (ScanImageType)typeId)],
        };
    }

    public async ValueTask<ImageBytes> GetBookImageAsync(BookImageRef imageRef, CallContext context = default)
    {
        RequireRegisteredDevice();

        // The wire image types deliberately carry the desktop's own image-type numbers.
        byte[]? stored = await _library.Browse
            .GetImageAsync(imageRef.BookId, (int)imageRef.Type, context.CancellationToken)
            .ConfigureAwait(false);

        return new ImageBytes
        {
            Jpeg = stored is null ? null : ImageDownscaler.Downscale(stored, imageRef.MaxLongEdgePx),
        };
    }

    /// <summary>
    /// Reads and shrinks one cover, remembering the result: a phone paging up and down a long library
    /// otherwise re-reads and re-decodes the same images over and over.
    /// </summary>
    private async Task<byte[]?> ThumbnailAsync(int bookId, CancellationToken cancellationToken)
    {
        var cached = _thumbnails.TryGet(bookId, ImageDownscaler.ThumbnailLongEdgePx);
        if (cached is not null)
        {
            return cached;
        }

        byte[]? cover = await _library.Browse
            .GetImageAsync(bookId, BookImageTypeId.FrontCover, cancellationToken)
            .ConfigureAwait(false);
        if (cover is null)
        {
            return null;
        }

        byte[] thumbnail = ImageDownscaler.Downscale(cover, ImageDownscaler.ThumbnailLongEdgePx);
        _thumbnails.Set(bookId, ImageDownscaler.ThumbnailLongEdgePx, thumbnail);
        return thumbnail;
    }

    private static ClaimOutcome ToContract(PairingClaimOutcome outcome) => outcome switch
    {
        PairingClaimOutcome.Registered => ClaimOutcome.Registered,
        PairingClaimOutcome.AlreadyRegistered => ClaimOutcome.AlreadyRegistered,
        PairingClaimOutcome.UnknownOrExpired => ClaimOutcome.UnknownOrExpired,
        PairingClaimOutcome.AlreadyUsed => ClaimOutcome.AlreadyUsed,
        PairingClaimOutcome.DeviceLimitReached => ClaimOutcome.DeviceLimitReached,
        _ => ClaimOutcome.Unknown,
    };

    private static RpcException NotYetServed()
        => new(new Status(StatusCode.Unimplemented, "This operation is not served by this desktop version."));
}
