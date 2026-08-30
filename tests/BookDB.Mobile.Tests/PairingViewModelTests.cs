using System;
using System.Threading.Tasks;
using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.ViewModels;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The pairing screen's behaviour, driven with a fake scanner and a fake coordinator (the real
/// coordinator's wire behaviour is covered by the end-to-end host test). Messages are compared against the
/// resource strings so they hold in any UI culture.</summary>
public class PairingViewModelTests
{
    /// <summary>A code with the shape the scan step checks for. Only the shape matters here — the certificate
    /// inside it is never opened until the coordinator dials the computer, which these tests fake.</summary>
    private static string ValidCode() => new PairingPayload
    {
        Endpoint = "192.168.1.20:7443",
        ServerThumbprints = ["6E:34:0B:9C:FF:B3:7A:98"],
        ClientPfxBase64 = "MIIDdTCCAl2gAwIBAgIJAKm",
        IssuedAtUtc = DateTimeOffset.UtcNow,
    }.ToQrString();

    private static PairingViewModel New(
        FakeBarcodeScanner scanner,
        FakePairingCoordinator coordinator,
        out FakeNavigator navigator,
        FakeDeviceSettings? deviceSettings = null)
    {
        var shell = new FakeNavigator();
        navigator = shell;
        return new PairingViewModel(scanner, coordinator, deviceSettings ?? new FakeDeviceSettings(), shell);
    }

    [Fact]
    public void CannotPair_UntilACodeIsScannedAndTheDeviceIsNamed()
    {
        var vm = New(new FakeBarcodeScanner(), new FakePairingCoordinator(), out _);

        Assert.False(vm.PairCommand.CanExecute(null));

        vm.DeviceName = "Kitchen tablet"; // named but not scanned yet
        Assert.False(vm.PairCommand.CanExecute(null));
    }

    [Fact]
    public async Task ScanningACode_EnablesPairingOnceNamed()
    {
        var vm = New(new FakeBarcodeScanner { Next = ValidCode() }, new FakePairingCoordinator(), out _);

        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.HasScannedCode);
        Assert.Equal(Resources.Pairing_Scanned, vm.StatusMessage);

        vm.DeviceName = "Kitchen tablet";
        Assert.True(vm.PairCommand.CanExecute(null));
    }

    /// <summary>The screen has to say what it is scanning for, or the camera is configured for book numbers
    /// and cannot see a pairing code at all — the defect UAT found on real hardware.</summary>
    [Fact]
    public async Task Scanning_AsksTheCameraForAPairingCode()
    {
        var scanner = new FakeBarcodeScanner { Next = ValidCode() };
        var vm = New(scanner, new FakePairingCoordinator(), out _);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(BarcodeKind.PairingCode, scanner.LastKind);
    }

    [Fact]
    public async Task ScanningNothing_ReportsThatNoCodeWasRead()
    {
        var vm = New(new FakeBarcodeScanner { Next = null }, new FakePairingCoordinator(), out _);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.HasScannedCode);
        Assert.Equal(Resources.Pairing_ScanCancelled, vm.StatusMessage);
    }

    [Fact]
    public async Task Pairing_PassesTheScannedCodeAndTrimmedNameToTheCoordinator()
    {
        var coordinator = new FakePairingCoordinator();
        var code = ValidCode();
        var vm = New(new FakeBarcodeScanner { Next = code }, coordinator, out _);
        await vm.ScanCommand.ExecuteAsync(null);
        vm.DeviceName = "  Kitchen tablet  ";

        await vm.PairCommand.ExecuteAsync(null);

        Assert.Equal(code, coordinator.LastCode);
        Assert.Equal("Kitchen tablet", coordinator.LastName);
    }

    /// <summary>Any QR in the room decodes. One that is not a pairing code is refused where it was scanned,
    /// rather than accepted, named, and turned away at the end of a pairing attempt.</summary>
    [Theory]
    [InlineData("BookDB")]
    [InlineData("https://example.com")]
    [InlineData("{}")]
    public async Task ScanningSomethingThatIsNotAPairingCode_SaysSoAndLeavesPairingDisabled(string text)
    {
        var vm = New(new FakeBarcodeScanner { Next = text }, new FakePairingCoordinator(), out _);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.False(vm.HasScannedCode);
        Assert.Equal(Resources.Pairing_Result_InvalidCode, vm.StatusMessage);

        vm.DeviceName = "Kitchen tablet";
        Assert.False(vm.PairCommand.CanExecute(null));
    }

    [Fact]
    public async Task PairingSuccessfully_ReportsSuccessAndSendsTheShellToTheHub()
    {
        var vm = New(new FakeBarcodeScanner { Next = ValidCode() }, new FakePairingCoordinator { Outcome = PairingOutcome.Paired }, out var navigator);
        await vm.ScanCommand.ExecuteAsync(null);
        vm.DeviceName = "Kitchen tablet";

        await vm.PairCommand.ExecuteAsync(null);

        Assert.Equal(Resources.Pairing_Result_Paired, vm.StatusMessage);
        Assert.Equal(1, navigator.HubCount);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(PairingOutcome.InvalidCode)]
    [InlineData(PairingOutcome.CodeExpired)]
    [InlineData(PairingOutcome.DeviceLimitReached)]
    [InlineData(PairingOutcome.IncompatibleVersion)]
    [InlineData(PairingOutcome.CannotConnect)]
    [InlineData(PairingOutcome.Failed)]
    public async Task AFailedOutcome_ShowsAMessageAndStaysOnThePairingScreen(PairingOutcome outcome)
    {
        var vm = New(new FakeBarcodeScanner { Next = ValidCode() }, new FakePairingCoordinator { Outcome = outcome }, out var navigator);
        await vm.ScanCommand.ExecuteAsync(null);
        vm.DeviceName = "Kitchen tablet";

        await vm.PairCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
        Assert.NotEqual(Resources.Pairing_Result_Paired, vm.StatusMessage);
        Assert.Equal(0, navigator.HubCount);
    }

    /// <summary>Pairing is the one screen with no way round the camera, so a refusal here has to name the
    /// cure rather than read as a cancelled scan.</summary>
    [Fact]
    public async Task ARefusedCamera_ExplainsItselfInsteadOfReadingAsACancelledScan()
    {
        var settings = new FakeDeviceSettings();
        var scanner = new FakeBarcodeScanner { NextScan = BarcodeScan.PermissionDenied };
        var vm = New(scanner, new FakePairingCoordinator(), out _, settings);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.CameraAccess.IsVisible);
        Assert.Equal(Resources.Camera_PermissionDenied, vm.CameraAccess.Message);
        Assert.NotEqual(Resources.Pairing_ScanCancelled, vm.StatusMessage);
        Assert.False(vm.HasScannedCode);

        vm.CameraAccess.OpenSettingsCommand.Execute(null);
        Assert.Equal(1, settings.OpenCount);
    }
}
