using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class LookupEntryRow : ObservableObject
{
    public int Id { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    public LookupEntryRow() { }

    public LookupEntryRow(int id, string name)
    {
        Id = id;
        Name = name;
    }
}
