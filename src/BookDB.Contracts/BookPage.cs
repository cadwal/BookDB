using System.Collections.Generic;
using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class BookPage
{
    [ProtoMember(1)] public List<BookSummary> Books { get; set; } = [];
    [ProtoMember(2)] public int TotalCount { get; set; }
}
