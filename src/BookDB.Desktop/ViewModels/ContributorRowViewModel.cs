using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Row-level view model for an inline contributor entry in the Contributors &amp; Admin
/// tab. Bound as an item inside parent VM's Contributors ObservableCollection.
/// Per D-A01 (CONTEXT.md): one instance per (person, role) pair.
/// </summary>
public partial class ContributorRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _personName = string.Empty;

    [ObservableProperty]
    private int? _roleId;

    /// <summary>True if this row was added in the current edit session (not loaded from DB).</summary>
    public bool IsNew { get; init; }
}
