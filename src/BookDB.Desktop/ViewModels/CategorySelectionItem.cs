using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class CategorySelectionItem : ObservableObject
{
    public int CategoryId { get; init; }
    public string Name { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
}
