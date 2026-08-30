using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class ServerInfo
{
    [ProtoMember(1)] public string AppVersion { get; set; } = "";
    [ProtoMember(2)] public int ContractVersion { get; set; }
    [ProtoMember(3)] public string LibraryName { get; set; } = "";
    [ProtoMember(4)] public CaptureSettings Capture { get; set; } = new CaptureSettings();

    /// <summary>
    /// Identifies this desktop install across address changes, so a phone that rediscovers a beacon on a
    /// new IP can tell "same desktop, new address" from "a different desktop".
    /// </summary>
    [ProtoMember(5)] public string InstanceId { get; set; } = "";
}
