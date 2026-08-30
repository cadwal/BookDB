using System;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>First-run pairing: scan the code shown on the computer, name this device, and pair. On success it
/// tells the shell so it can move on to the hub.</summary>
public sealed partial class PairingViewModel : PageViewModel
{
    private readonly IBarcodeScanner _scanner;
    private readonly IPairingCoordinator _coordinator;
    private readonly INavigator _navigator;

    private string? _scannedCode;

    public PairingViewModel(
        IBarcodeScanner scanner,
        IPairingCoordinator coordinator,
        IDeviceSettings deviceSettings,
        INavigator navigator)
    {
        _scanner = scanner;
        _coordinator = coordinator;
        _navigator = navigator;
        CameraAccess = new CameraAccessViewModel(deviceSettings);
    }

    public override string Title => Resources.Pairing_Title;

    public CameraAccessViewModel CameraAccess { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PairCommand))]
    private bool _hasScannedCode;

    [ObservableProperty]
    private string? _statusMessage;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        var scan = await _scanner.ScanOnceAsync(BarcodeKind.PairingCode);
        if (CameraAccess.Explains(scan))
        {
            StatusMessage = null;
            return;
        }

        if (string.IsNullOrEmpty(scan.Text))
        {
            StatusMessage = Resources.Pairing_ScanCancelled;
            return;
        }

        // Refused here rather than at the end of a pairing attempt: any QR in the room will decode, and being
        // told to name the device first only to be turned away teaches nothing about which code to point at.
        if (!PairingPayload.TryParse(scan.Text, out _))
        {
            _scannedCode = null;
            HasScannedCode = false;
            StatusMessage = Resources.Pairing_Result_InvalidCode;
            return;
        }

        _scannedCode = scan.Text;
        HasScannedCode = true;
        StatusMessage = Resources.Pairing_Scanned;
    }

    private bool CanScan() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPair))]
    private async Task PairAsync()
    {
        IsBusy = true;
        StatusMessage = Resources.Pairing_Busy;
        try
        {
            var outcome = await _coordinator.PairAsync(_scannedCode!, DeviceName.Trim());
            StatusMessage = Describe(outcome);
            if (outcome == PairingOutcome.Paired)
                _navigator.ShowHub();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanPair() => HasScannedCode && !IsBusy && !string.IsNullOrWhiteSpace(DeviceName);

    private static string Describe(PairingOutcome outcome) => outcome switch
    {
        PairingOutcome.Paired => Resources.Pairing_Result_Paired,
        PairingOutcome.InvalidCode => Resources.Pairing_Result_InvalidCode,
        PairingOutcome.CodeExpired => Resources.Pairing_Result_CodeExpired,
        PairingOutcome.DeviceLimitReached => Resources.Pairing_Result_DeviceLimitReached,
        PairingOutcome.IncompatibleVersion => Resources.Pairing_Result_IncompatibleVersion,
        PairingOutcome.CannotConnect => Resources.Pairing_Result_CannotConnect,
        _ => Resources.Pairing_Result_Failed,
    };
}
