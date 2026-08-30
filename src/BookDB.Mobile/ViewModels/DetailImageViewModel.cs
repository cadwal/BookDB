using BookDB.Contracts;
using BookDB.Mobile.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Mobile.ViewModels;

/// <summary>One of a book's photos on the detail view. It appears named but empty and fills in when its bytes
/// arrive, because the images are fetched one after another rather than all at once.</summary>
public sealed partial class DetailImageViewModel : ViewModelBase
{
    public DetailImageViewModel(ScanImageType type) => Type = type;

    public ScanImageType Type { get; }

    /// <summary>The capture slots already name these four; a photo is the same thing whether it is being taken
    /// or being looked at.</summary>
    public string Label => Type switch
    {
        ScanImageType.BackCover => Resources.Builder_SlotBackCover,
        ScanImageType.Spine => Resources.Builder_SlotSpine,
        ScanImageType.DustJacket => Resources.Builder_SlotDustJacket,
        _ => Resources.Builder_SlotFrontCover,
    };

    [ObservableProperty]
    private byte[]? _jpeg;
}
