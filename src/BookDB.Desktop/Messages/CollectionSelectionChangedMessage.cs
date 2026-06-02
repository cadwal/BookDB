using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

public sealed class CollectionSelectionChangedMessage
    : ValueChangedMessage<IReadOnlySet<int>>
{
    public CollectionSelectionChangedMessage(IReadOnlySet<int> collectionIds)
        : base(collectionIds) { }
}
