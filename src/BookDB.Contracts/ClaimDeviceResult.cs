using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class ClaimDeviceResult
{
    [ProtoMember(1)] public ClaimOutcome Outcome { get; set; }

    /// <summary>Both counts travel so the phone can say "5 of 5 devices" without hardcoding the cap.</summary>
    [ProtoMember(2)] public int RegisteredDeviceCount { get; set; }
    [ProtoMember(3)] public int MaxDevices { get; set; }
}
