using System.Collections.Generic;
using ProtoBuf;

namespace BookDB.Contracts;

[ProtoContract]
public sealed class CollectionList
{
    [ProtoMember(1)] public List<CollectionRef> Collections { get; set; } = [];
}
