using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Sent when the Manage Lookups window closes after any rename/delete/merge operation,
/// so the filter panel and other live views can reload their lookup data.
/// </summary>
public sealed class LookupsChangedMessage : ValueChangedMessage<bool>
{
    public LookupsChangedMessage() : base(true) { }
}
