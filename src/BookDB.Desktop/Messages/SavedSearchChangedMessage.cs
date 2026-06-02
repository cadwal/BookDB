using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

public sealed class SavedSearchChangedMessage : ValueChangedMessage<int>
{
    public SavedSearchChangedMessage() : base(0) { }
}
