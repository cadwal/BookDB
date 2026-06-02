using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class CollectionItemViewModel : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}
