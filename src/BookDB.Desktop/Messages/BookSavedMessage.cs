using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Sent after a book is added or edited to signal that BookListViewModel should refresh.
/// </summary>
public sealed class BookSavedMessage : ValueChangedMessage<int>
{
    public BookSavedMessage(int bookId) : base(bookId) { }
}
