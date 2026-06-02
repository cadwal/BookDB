using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Carries the matching BookIds from AdvancedSearchViewModel back to BookListViewModel.
/// When BookListViewModel receives this, it filters its displayed books to only those
/// with BookIds in the list.
/// </summary>
public sealed class AdvancedSearchResultMessage : ValueChangedMessage<IReadOnlyList<long>>
{
    public AdvancedSearchResultMessage(IReadOnlyList<long> bookIds) : base(bookIds) { }
}
