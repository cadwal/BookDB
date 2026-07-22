using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Row-level view model for an inline contributor entry in the Contributors &amp; Admin
/// tab. Bound as an item inside parent VM's Contributors ObservableCollection; one instance
/// per (person, role) pair. The name field is the shared person type-ahead
/// (<see cref="PersonSuggestionRowViewModel"/>), so the editor reuses-or-creates people with
/// the lend flow's semantics.
/// </summary>
public partial class ContributorRowViewModel : PersonSuggestionRowViewModel
{
    public ContributorRowViewModel(PersonSuggestionProvider provider) : base(provider)
    {
    }

    [ObservableProperty]
    private int? _roleId;

    /// <summary>True if this row was added in the current edit session (not loaded from DB).</summary>
    public bool IsNew { get; init; }
}
