using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class ImageTypeButtonViewModel : ObservableObject
{
    public int BookImageTypeId { get; init; }
    public string Label { get; init; } = string.Empty;
    public long? BitmapSizeBytes { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _bitmap;

    partial void OnBitmapChanging(Bitmap? oldValue, Bitmap? newValue)
    {
        if (oldValue is not null && !ReferenceEquals(oldValue, newValue))
            oldValue.Dispose();
    }
}
