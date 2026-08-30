using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>What this device is paired to, what the computer dictates about capture, and the way out:
/// re-check the connection, or unpair and start over with a fresh pairing code. Everything here is read-only
/// — capture parameters belong to the computer, and the computer's device list is the authoritative revoke.</summary>
public sealed partial class SettingsViewModel : PageViewModel, IDisposable
{
    private readonly IConnectionService _connection;
    private readonly IIdentityStore _identityStore;
    private readonly INavigator _navigator;

    public SettingsViewModel(IConnectionService connection, IIdentityStore identityStore, INavigator navigator)
    {
        _connection = connection;
        _identityStore = identityStore;
        _navigator = navigator;
        _connection.StatusChanged += OnConnectionChanged;
    }

    /// <summary>The shell disposes a page it navigates away from, which is what unhooks this.</summary>
    public void Dispose() => _connection.StatusChanged -= OnConnectionChanged;

    private void OnConnectionChanged(object? sender, EventArgs e) => Refreshed();

    public override string Title => Resources.Settings_Title;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckConnectionCommand))]
    private bool _isChecking;

    /// <summary>The confirm step of unpairing: the second tap is the one that drops the identity.</summary>
    [ObservableProperty]
    private bool _isConfirmingUnpair;

    public string Endpoint => Value(_identityStore.Load()?.Endpoint);

    public string DeviceName => Value(_identityStore.Load()?.DeviceName);

    public string StatusText => _connection.Status switch
    {
        ConnectionStatus.Connected => Resources.Status_Connected,
        ConnectionStatus.Reconnecting => Resources.Status_Reconnecting,
        ConnectionStatus.Revoked => Resources.Status_Revoked,
        _ => Resources.Status_Offline,
    };

    public bool IsOffline => _connection.Status == ConnectionStatus.Offline;

    /// <summary>Removed on the computer. The hint for being offline would send the user checking a network
    /// that is working; this screen is also where the way back — unpair, then pair again — already lives.</summary>
    public bool IsRevoked => _connection.Status == ConnectionStatus.Revoked;

    public string LibraryName => Value(_connection.ServerInfo?.LibraryName);

    public string ComputerAppVersion => Value(_connection.ServerInfo?.AppVersion);

    public string LongEdge => _connection.ServerInfo is { Capture.MaxLongEdgePx: > 0 } info
        ? string.Format(CultureInfo.CurrentCulture, Resources.Settings_PixelsValue, info.Capture.MaxLongEdgePx)
        : Resources.Settings_NotAvailable;

    public string JpegQuality => _connection.ServerInfo is { Capture.JpegQuality: > 0 } info
        ? info.Capture.JpegQuality.ToString(CultureInfo.CurrentCulture)
        : Resources.Settings_NotAvailable;

    public string AppVersion { get; } =
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? Resources.Settings_NotAvailable;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckConnectionAsync()
    {
        IsChecking = true;
        try
        {
            await _connection.RefreshAsync();
        }
        finally
        {
            IsChecking = false;
            Refreshed();
        }
    }

    private bool CanCheck() => !IsChecking;

    [RelayCommand]
    private void Unpair() => IsConfirmingUnpair = true;

    [RelayCommand]
    private void CancelUnpair() => IsConfirmingUnpair = false;

    [RelayCommand]
    private void ConfirmUnpair()
    {
        _connection.Reset();
        _identityStore.Clear();
        _navigator.ShowPairing();
    }

    /// <summary>The handshake feeds most of this screen, so a re-check republishes all of it at once.</summary>
    private void Refreshed()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(IsRevoked));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(LibraryName));
        OnPropertyChanged(nameof(ComputerAppVersion));
        OnPropertyChanged(nameof(LongEdge));
        OnPropertyChanged(nameof(JpegQuality));
    }

    private static string Value(string? text) =>
        string.IsNullOrEmpty(text) ? Resources.Settings_NotAvailable : text;
}
