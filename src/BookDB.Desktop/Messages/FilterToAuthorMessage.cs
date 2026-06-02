using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Requests the filter panel to set the Author facet to the specified person ID,
/// clearing all other selections. Sent from ManageLookupsWindow's Person tab.
/// </summary>
public sealed class FilterToAuthorMessage : ValueChangedMessage<int>
{
    public FilterToAuthorMessage(int personId) : base(personId) { }
}
