using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

public sealed class BookCountChangedMessage : ValueChangedMessage<(int FilteredTotal, int GrandTotal)>
{
    public BookCountChangedMessage(int filteredTotal, int grandTotal)
        : base((filteredTotal, grandTotal)) { }
}
