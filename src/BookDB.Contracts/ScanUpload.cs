using ProtoBuf;

namespace BookDB.Contracts;

/// <summary>
/// One message of the upload stream. Exactly one of <see cref="Header"/> and <see cref="Image"/> is set:
/// the header opens an item, the images that follow belong to it until the next header for the same
/// <see cref="ClientItemId"/>.
/// </summary>
[ProtoContract]
public sealed class ScanUpload
{
    /// <summary>Phone-minted id that correlates an item's header, its images, and its status rows.</summary>
    [ProtoMember(1)] public string ClientItemId { get; set; } = "";
    [ProtoMember(2)] public ScanItemHeader? Header { get; set; }
    [ProtoMember(3)] public ScanImage? Image { get; set; }

    /// <summary>
    /// Phone-minted id for the upload as a whole, so the phone can watch the status stream while the
    /// upload is still running and know which items are safe to drop from its tray. Left empty, the
    /// server mints one and returns it, and per-item results are only observable afterwards.
    /// </summary>
    [ProtoMember(4)] public string BatchId { get; set; } = "";
}
