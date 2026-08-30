using System.Collections.Generic;
using System.Threading.Tasks;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

namespace BookDB.Contracts;

/// <summary>
/// The companion wire contract in full: the interface itself is the schema — no .proto, no generated
/// stubs. The desktop implements it; the phone consumes an emitted proxy over the same assembly.
/// </summary>
[Service]
public interface IBookScannerService
{
    ValueTask<ServerInfo> GetServerInfoAsync(CallContext context = default);

    /// <summary>
    /// Finishes pairing: the caller is already authenticated by the certificate its QR carried, and this
    /// names the device and promotes it to a registered one.
    /// </summary>
    ValueTask<ClaimDeviceResult> ClaimDeviceAsync(ClaimDeviceRequest request, CallContext context = default);

    ValueTask<IsbnPreview> CheckIsbnAsync(IsbnQuery query, CallContext context = default);

    /// <summary>
    /// Per <see cref="ScanUpload.ClientItemId"/> the first message carries <see cref="ScanUpload.Header"/>
    /// and no image; zero or more image messages follow — zero because a book may be staged from its ISBN
    /// alone. The server persists an item only once its last message has arrived, so a dropped stream
    /// leaves nothing behind.
    /// </summary>
    ValueTask<SubmitBatchResult> SubmitScanBatchAsync(IAsyncEnumerable<ScanUpload> uploads, CallContext context = default);

    IAsyncEnumerable<BatchItemStatus> StreamBatchStatusAsync(BatchRef batch, CallContext context = default);

    ValueTask<BookPage> ListBooksAsync(BookQuery query, CallContext context = default);

    /// <summary>The collections a <see cref="BookQuery.CollectionId"/> may name — the phone stores none of
    /// them, so it asks each time it offers the filter.</summary>
    ValueTask<CollectionList> ListCollectionsAsync(CallContext context = default);

    ValueTask<BookDetail> GetBookDetailAsync(BookRef book, CallContext context = default);

    ValueTask<ImageBytes> GetBookImageAsync(BookImageRef imageRef, CallContext context = default);
}
