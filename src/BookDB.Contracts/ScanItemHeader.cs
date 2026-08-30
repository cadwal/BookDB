using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class ScanItemHeader
{
    [ProtoMember(1)] public string Isbn { get; set; } = "";
}
