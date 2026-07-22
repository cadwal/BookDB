using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class CleanupProposalRow : ObservableObject
{
    public int PersonId { get; init; }
    public string CurrentDisplayName { get; init; } = string.Empty;
    public string CurrentSortName { get; init; } = string.Empty;
    public string? SplitGroupId { get; init; }
    public bool IsSplitRow => SplitGroupId is not null;

    // A split shows one grid row per fragment; the second+ fragments are continuations of the same source
    // entry. They blank the current-name cell and hide the (whole-group) ignore button so the split reads
    // as one unit rather than as several unrelated rows repeating the same name.
    public bool IsSplitContinuation { get; init; }
    public string CurrentNameDisplay => IsSplitContinuation ? string.Empty : CurrentDisplayName;

    [ObservableProperty]
    private string _proposedDisplayName = string.Empty;

    [ObservableProperty]
    private string _suggestedSortName = string.Empty;

    [ObservableProperty]
    private bool _applyChecked = true;
}

/// <summary>A persisted, ignored cleanup proposal shown in the ignored list, with a localized kind label.</summary>
public sealed record IgnoredProposalRow(int IgnoreId, string PersonDisplayName, string KindLabel, string ProposedContent);
