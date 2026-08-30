using BookDB.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Mobile.ViewModels;

/// <summary>One row of the book's checklist: which cover it is, whether it is expected or a nice-to-have, and
/// the sized JPEG once one has been accepted for it.</summary>
public sealed partial class CaptureSlotViewModel : ViewModelBase
{
    public CaptureSlotViewModel(ScanImageType type, string label, bool isOptional)
    {
        Type = type;
        Label = label;
        IsOptional = isOptional;
    }

    public ScanImageType Type { get; }

    public string Label { get; }

    /// <summary>Spine and dust jacket: offered, never asked for.</summary>
    public bool IsOptional { get; }

    /// <summary>Already sized to the computer's capture settings — what gets staged, not what the camera
    /// returned.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private byte[]? _jpeg;

    public bool HasImage => Jpeg is not null;
}
