using BookDB.Contracts;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.ViewModels;
using Xunit;

namespace BookDB.Mobile.Tests;

/// <summary>The settings screen: what it reports about the pairing, and the two-step way out of it.
/// Text is compared against the resource strings so the assertions hold in any UI culture.</summary>
public class SettingsViewModelTests
{
    private static DeviceIdentity Identity() =>
        new("192.168.1.9:7443", ["AA"], [1, 2, 3], "Kitchen tablet", "instance-1");

    private static SettingsViewModel New(
        out FakeConnectionService connection, out FakeIdentityStore store, out FakeNavigator navigator)
    {
        connection = new FakeConnectionService
        {
            ServerInfo = new ServerInfo
            {
                LibraryName = "Böckerna",
                AppVersion = "4.0.0",
                Capture = new CaptureSettings { MaxLongEdgePx = 1600, JpegQuality = 80 },
            },
        };
        store = new FakeIdentityStore(Identity());
        navigator = new FakeNavigator();
        return new SettingsViewModel(connection, store, navigator);
    }

    [Fact]
    public void ItReportsTheComputerTheDeviceIsPairedWith()
    {
        var settings = New(out var connection, out _, out _);
        connection.Report(ConnectionStatus.Connected);

        Assert.Equal("192.168.1.9:7443", settings.Endpoint);
        Assert.Equal(Resources.Status_Connected, settings.StatusText);
        Assert.Equal("Böckerna", settings.LibraryName);
        Assert.Equal("4.0.0", settings.ComputerAppVersion);
        Assert.False(settings.IsOffline);
    }

    [Fact]
    public void ItReportsTheCaptureParametersTheComputerDictates()
    {
        var settings = New(out _, out _, out _);

        Assert.Contains("1600", settings.LongEdge);
        Assert.Equal("80", settings.JpegQuality);
        Assert.Equal("Kitchen tablet", settings.DeviceName);
        Assert.False(string.IsNullOrWhiteSpace(settings.AppVersion));
    }

    [Fact]
    public void BeforeAnyHandshake_TheComputersFactsReadAsUnknownAndTheHintIsShown()
    {
        var connection = new FakeConnectionService();
        var settings = new SettingsViewModel(connection, new FakeIdentityStore(Identity()), new FakeNavigator());

        Assert.Equal(Resources.Settings_NotAvailable, settings.LibraryName);
        Assert.Equal(Resources.Settings_NotAvailable, settings.LongEdge);
        Assert.Equal(Resources.Settings_NotAvailable, settings.JpegQuality);
        Assert.True(settings.IsOffline);
    }

    /// <summary>
    /// The offline hint sends the user checking a network, a switch and a setting. None of that applies to a
    /// device the computer has removed, and this screen is where the way back — unpair, then pair again — is.
    /// </summary>
    [Fact]
    public void RemovedOnTheComputer_ShowsItsOwnHintRatherThanTheOfflineOne()
    {
        var settings = New(out var connection, out _, out _);

        connection.Report(ConnectionStatus.Revoked);

        Assert.True(settings.IsRevoked);
        Assert.False(settings.IsOffline);
        Assert.Equal(Resources.Status_Revoked, settings.StatusText);
    }

    [Fact]
    public async System.Threading.Tasks.Task CheckingTheConnection_AsksTheServiceAndRepublishesTheStatus()
    {
        var settings = New(out var connection, out _, out _);
        var changed = new System.Collections.Generic.List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await settings.CheckConnectionCommand.ExecuteAsync(null);

        Assert.Equal(1, connection.RefreshCount);
        Assert.Contains(nameof(SettingsViewModel.StatusText), changed);
        Assert.Contains(nameof(SettingsViewModel.LibraryName), changed);
        Assert.False(settings.IsChecking);
    }

    [Fact]
    public void AStatusChangeWhileTheScreenIsOpen_UpdatesIt()
    {
        var settings = New(out var connection, out _, out _);
        Assert.Equal(Resources.Status_Offline, settings.StatusText);

        connection.Report(ConnectionStatus.Connected);

        Assert.Equal(Resources.Status_Connected, settings.StatusText);
    }

    [Fact]
    public void AfterTheScreenIsDisposed_ItStopsFollowingTheConnection()
    {
        var settings = New(out var connection, out _, out _);
        settings.Dispose();
        var changed = 0;
        settings.PropertyChanged += (_, _) => changed++;

        connection.Report(ConnectionStatus.Connected);

        Assert.Equal(0, changed);
    }

    [Fact]
    public void UnpairingTakesTwoTaps_AndTheFirstChangesNothing()
    {
        var settings = New(out var connection, out var store, out var navigator);

        settings.UnpairCommand.Execute(null);

        Assert.True(settings.IsConfirmingUnpair);
        Assert.True(store.HasIdentity);
        Assert.False(connection.WasReset);
        Assert.Equal(0, navigator.PairingCount);
    }

    [Fact]
    public void ConfirmingUnpair_DropsTheIdentityClosesTheLinkAndReturnsToPairing()
    {
        var settings = New(out var connection, out var store, out var navigator);
        settings.UnpairCommand.Execute(null);

        settings.ConfirmUnpairCommand.Execute(null);

        Assert.False(store.HasIdentity);
        Assert.True(connection.WasReset);
        Assert.Equal(1, navigator.PairingCount);
    }

    [Fact]
    public void CancellingUnpair_LeavesThePairingAlone()
    {
        var settings = New(out var connection, out var store, out var navigator);
        settings.UnpairCommand.Execute(null);

        settings.CancelUnpairCommand.Execute(null);

        Assert.False(settings.IsConfirmingUnpair);
        Assert.True(store.HasIdentity);
        Assert.False(connection.WasReset);
        Assert.Equal(0, navigator.PairingCount);
    }
}
