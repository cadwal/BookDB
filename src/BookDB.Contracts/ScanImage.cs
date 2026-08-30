using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class ScanImage
{
    [ProtoMember(1)] public ScanImageType Type { get; set; }

    /// <summary>Already cropped and downscaled on the phone to the handshake's capture settings.</summary>
    [ProtoMember(2)] public byte[] Jpeg { get; set; } = [];
}
