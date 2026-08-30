using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>
/// What a screen that scans says when the camera says no. A refusal is not an error to report and forget:
/// once it is refused for good the app may not ask again, so the screen has to keep offering the one route
/// that still works — the device's own settings — until the camera comes back.
/// </summary>
public sealed partial class CameraAccessViewModel : ObservableObject
{
    private readonly IDeviceSettings _settings;

    public CameraAccessViewModel(IDeviceSettings settings) => _settings = settings;

    /// <summary>Why the camera cannot be used, or null while there is nothing the matter with it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Message))]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    [NotifyPropertyChangedFor(nameof(CanOpenSettings))]
    [NotifyCanExecuteChangedFor(nameof(OpenSettingsCommand))]
    private BarcodeScanOutcome? _refusal;

    public bool IsVisible => Refusal is not null;

    /// <summary>Only a refused permission has a settings page behind it; a camera that is simply busy or
    /// missing is not something the user can grant their way out of.</summary>
    public bool CanOpenSettings => Refusal == BarcodeScanOutcome.PermissionDenied;

    public string Message => Refusal switch
    {
        BarcodeScanOutcome.PermissionDenied => Resources.Camera_PermissionDenied,
        BarcodeScanOutcome.CameraUnavailable => Resources.Camera_Unavailable,
        _ => string.Empty,
    };

    /// <summary>Records a scan that ended without a read, and answers whether it was the camera's doing —
    /// the caller's own "nothing was read" message is for the other case.</summary>
    public bool Explains(BarcodeScan scan)
    {
        Refusal = scan.Outcome is BarcodeScanOutcome.PermissionDenied or BarcodeScanOutcome.CameraUnavailable
            ? scan.Outcome
            : null;

        return Refusal is not null;
    }

    [RelayCommand(CanExecute = nameof(CanOpenSettings))]
    private void OpenSettings() => _settings.OpenAppSettings();
}
