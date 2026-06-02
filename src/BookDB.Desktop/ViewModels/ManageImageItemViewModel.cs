using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class ManageImageItemViewModel : ObservableObject
{
    public int BookImageId { get; init; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private int _selectedTypeId;

    [ObservableProperty]
    private int _displayOrder;

    public string TypeName { get; set; } = string.Empty;

    public string OrderLabel => $"#{DisplayOrder}";

    public int OriginalTypeId { get; init; }
    public int OriginalDisplayOrder { get; init; }

    public bool IsThumbnailType { get; init; }

    public bool CanMoveUp { get; set; }
    public bool CanMoveDown { get; set; }

    /// <summary>True when SelectedTypeId or DisplayOrder has been staged but not yet flushed to DB.</summary>
    public bool IsDirty { get; set; }

    partial void OnThumbnailChanging(Bitmap? oldValue, Bitmap? newValue)
    {
        if (oldValue is not null && !ReferenceEquals(oldValue, newValue))
            oldValue.Dispose();
    }

    partial void OnDisplayOrderChanged(int value)
    {
        if (value != OriginalDisplayOrder)
            IsDirty = true;
        OnPropertyChanged(nameof(OrderLabel));
    }

    partial void OnSelectedTypeIdChanged(int value)
    {
        if (value != OriginalTypeId)
            IsDirty = true;
    }

    /// <summary>Raises PropertyChanged for CanMoveUp and CanMoveDown so AXAML bindings refresh.</summary>
    public void NotifyCanMoveChanged()
    {
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }
}
