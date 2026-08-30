using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class BookImageRef
{
    [ProtoMember(1)] public int BookId { get; set; }
    [ProtoMember(2)] public ScanImageType Type { get; set; }

    /// <summary>Downscale target for the returned JPEG; 0 asks for the stored image untouched.</summary>
    [ProtoMember(3)] public int MaxLongEdgePx { get; set; }
}
