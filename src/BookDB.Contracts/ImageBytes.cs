using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class ImageBytes
{
    [ProtoMember(1)] public byte[]? Jpeg { get; set; }
}
