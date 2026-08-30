using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class ClaimDeviceRequest
{
    /// <summary>The name the user typed on the phone; it is what the desktop's device list shows.</summary>
    [ProtoMember(1)] public string DeviceName { get; set; } = "";
}
