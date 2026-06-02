using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Sent after one or more books are deleted to signal that BookListViewModel should remove them.
/// </summary>
public sealed class BooksDeletedMessage : ValueChangedMessage<IReadOnlyList<int>>
{
    public BooksDeletedMessage(IReadOnlyList<int> bookIds) : base(bookIds) { }
}
