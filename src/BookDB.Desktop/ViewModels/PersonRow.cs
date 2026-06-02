using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class PersonRow : ObservableObject
{
    public int PersonId { get; init; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _sortName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBioData))]
    private string? _bio;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBioData))]
    private string? _birthDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBioData))]
    private string? _birthPlace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBioData))]
    private string? _deathDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBioData))]
    private string? _deathPlace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBioData))]
    private string? _website;

    public bool HasBioData =>
        Bio != null || BirthDate != null || BirthPlace != null
        || DeathDate != null || DeathPlace != null || Website != null;

    public PersonRow() { }

    public PersonRow(int personId, string displayName, string sortName)
    {
        PersonId = personId;
        DisplayName = displayName;
        SortName = sortName;
    }
}
