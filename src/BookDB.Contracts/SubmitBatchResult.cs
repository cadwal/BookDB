using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class SubmitBatchResult
{
    [ProtoMember(1)] public string BatchId { get; set; } = "";
    [ProtoMember(2)] public int AcceptedItems { get; set; }
}
