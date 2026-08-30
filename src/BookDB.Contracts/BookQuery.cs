using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class BookQuery
{
    [ProtoMember(1)] public string? Search { get; set; }
    [ProtoMember(2)] public int? CollectionId { get; set; }
    [ProtoMember(3)] public int Skip { get; set; }

    /// <summary>Page size; 0 asks for the server's default. Protobuf cannot tell an unset int from 0, so
    /// the default lives on the server rather than in a property initializer here.</summary>
    [ProtoMember(4)] public int Take { get; set; }

    /// <summary>Exact match from a scanned barcode — the "do I own this?" path, distinct from search.</summary>
    [ProtoMember(5)] public string? Isbn { get; set; }
}
