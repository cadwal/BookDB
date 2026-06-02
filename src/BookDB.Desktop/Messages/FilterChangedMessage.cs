using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BookDB.Desktop.Messages;

/// <summary>
/// Carries the current facet filter state from FilterPanelViewModel to BookListViewModel.
/// FacetSelections: map of facet name (e.g. "Format") to selected value IDs.
/// </summary>
public record FilterState(
    IReadOnlyDictionary<string, IReadOnlySet<int>> FacetSelections,
    bool IsLoanedOut = false);

public sealed class FilterChangedMessage : ValueChangedMessage<FilterState>
{
    public FilterChangedMessage(FilterState value) : base(value) { }
}
