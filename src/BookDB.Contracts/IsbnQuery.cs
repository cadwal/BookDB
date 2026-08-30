using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class IsbnQuery
{
    [ProtoMember(1)] public string Isbn { get; set; } = "";
}
