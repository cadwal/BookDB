using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Sent when collections are added, renamed, deleted, or reordered in the Manage Lookups
/// window, so the main-window collection selector can reload and preserve the current selection.
/// </summary>
public sealed class CollectionsChangedMessage : ValueChangedMessage<bool>
{
    public CollectionsChangedMessage() : base(true) { }
}
