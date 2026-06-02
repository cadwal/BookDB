using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class CleanupProposalRow : ObservableObject
{
    public int PersonId { get; init; }
    public string CurrentDisplayName { get; init; } = string.Empty;
    public string? SplitGroupId { get; init; }
    public bool IsSplitRow => SplitGroupId is not null;

    [ObservableProperty]
    private string _proposedDisplayName = string.Empty;

    [ObservableProperty]
    private string _suggestedSortName = string.Empty;

    [ObservableProperty]
    private bool _applyChecked = true;
}
