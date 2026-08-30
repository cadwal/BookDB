using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class BatchItemStatus
{
    [ProtoMember(1)] public string ClientItemId { get; set; } = "";
    [ProtoMember(2)] public string Isbn { get; set; } = "";
    [ProtoMember(3)] public BatchItemState State { get; set; }
    [ProtoMember(4)] public int? BookId { get; set; }
    [ProtoMember(5)] public string? Title { get; set; }
    [ProtoMember(6)] public BatchItemFailure Failure { get; set; }

    /// <summary>
    /// Untranslated diagnostic text for the detail view. The phone renders <see cref="Failure"/> in its own
    /// language and treats this as an addendum, never as the explanation itself.
    /// </summary>
    [ProtoMember(7)] public string? FailureDetail { get; set; }
}
