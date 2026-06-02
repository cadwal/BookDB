using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Sent when a book row is selected or activated.
/// Null BookId means no book is selected (deselected).
/// OpenInEditMode true means the detail panel should enter edit mode immediately.
/// </summary>
public sealed class BookSelectedMessage : ValueChangedMessage<int?>
{
    public bool OpenInEditMode { get; }

    public BookSelectedMessage(int? bookId, bool openInEditMode = false) : base(bookId)
    {
        OpenInEditMode = openInEditMode;
    }
}
