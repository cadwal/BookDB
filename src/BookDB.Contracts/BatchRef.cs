using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class BatchRef
{
    [ProtoMember(1)] public string BatchId { get; set; } = "";
}
