using ProtoBuf;

namespace BookDB.Contracts;

/// <summary>
/// Answer to a scanned ISBN that creates nothing: whether the library already holds it and, either way,
/// whatever title the desktop could resolve for the wizard's confidence line.
/// </summary>
[ProtoContract]
public sealed class IsbnPreview
{
    [ProtoMember(1)] public bool InLibrary { get; set; }
    [ProtoMember(2)] public int? BookId { get; set; }
    [ProtoMember(3)] public string? Title { get; set; }
    [ProtoMember(4)] public string? Authors { get; set; }
    [ProtoMember(5)] public IsbnPreviewSource Source { get; set; }
}
