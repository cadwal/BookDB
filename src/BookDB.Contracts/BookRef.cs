using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class BookRef
{
    [ProtoMember(1)] public int BookId { get; set; }
}
