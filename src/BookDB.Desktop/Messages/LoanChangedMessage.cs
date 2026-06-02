using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

public sealed class LoanChangedMessage : ValueChangedMessage<int>
{
    public LoanChangedMessage(int bookId) : base(bookId) { }
}
