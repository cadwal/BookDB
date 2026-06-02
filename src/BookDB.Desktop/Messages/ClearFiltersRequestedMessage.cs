using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

public sealed class ClearFiltersRequestedMessage : ValueChangedMessage<int>
{
    public ClearFiltersRequestedMessage() : base(0) { }
}
