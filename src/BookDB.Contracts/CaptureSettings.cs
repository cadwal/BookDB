using ProtoBuf;

namespace BookDB.Contracts;

/// <summary>
/// Capture parameters the desktop dictates and the phone applies to every crop before staging. Values are
/// always populated by the host; 0 means "not supplied" and the phone falls back to its own clamps —
/// property initializers would be invisible to protobuf, which cannot distinguish unset from zero.
/// </summary>
[ProtoContract]
public sealed class CaptureSettings
{
    [ProtoMember(1)] public int MaxLongEdgePx { get; set; }
    [ProtoMember(2)] public int JpegQuality { get; set; }
}
