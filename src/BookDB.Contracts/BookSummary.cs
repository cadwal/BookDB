using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class BookSummary
{
    [ProtoMember(1)] public int BookId { get; set; }
    [ProtoMember(2)] public string Title { get; set; } = "";
    [ProtoMember(3)] public string? Authors { get; set; }
    [ProtoMember(4)] public string? Isbn { get; set; }
    [ProtoMember(5)] public int? Year { get; set; }

    /// <summary>List-sized JPEG, downscaled by the desktop — the library holds no generated thumbnails.</summary>
    [ProtoMember(6)] public byte[]? Thumbnail { get; set; }
}
