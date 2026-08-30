using ProtoBuf;

namespace BookDB.Contracts;

/// <summary>One collection a browse query may be narrowed to.</summary>
[ProtoContract]
public sealed class CollectionRef
{
    [ProtoMember(1)] public int CollectionId { get; set; }
    [ProtoMember(2)] public string Name { get; set; } = "";
    [ProtoMember(3)] public int BookCount { get; set; }
}
