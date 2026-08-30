using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using BookDB.Companion.Host;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// The rotating pairing code. A code is valid for a couple of minutes; this keeps one on screen, replaces
/// it the moment it lapses, and watches for the device that claims it.
/// </summary>
public sealed partial class PairingDialogViewModel : ObservableObject, IDisposable
{
    private readonly CompanionHostManager _manager;
    private readonly TimeProvider _clock;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private CompanionPairingOffer? _offer;

    public Action<bool?>? CloseDialog { get; set; }

    public PairingDialogViewModel(CompanionHostManager manager, TimeProvider clock)
    {
        _manager = manager;
        _clock = clock;
        _timer.Tick += (_, _) => Tick();
    }

    public ObservableCollection<string> Addresses { get; } = [];

    /// <summary>Identity of the code currently on screen — what a claim by a device will register under.</summary>
    public string? CodeThumbprint => _offer?.Thumbprint;

    /// <summary>True once a device has claimed the code, which swaps the QR for the confirmation.</summary>
    [ObservableProperty]
    private bool _isPaired;

    [ObservableProperty]
    private string? _pairedDeviceName;

    /// <summary>The code as an encoded PNG; the view turns it into an image, so this stays platform-free.</summary>
    [ObservableProperty]
    private byte[]? _qrPng;

    [ObservableProperty]
    private string? _selectedAddress;

    [ObservableProperty]
    private string _expiryText = string.Empty;

    [ObservableProperty]
    private string _deviceCountText = string.Empty;

    /// <summary>Set when there is no code to show at all — no network address, or the host is not running.</summary>
    [ObservableProperty]
    private string? _errorText;

    public bool HasError => ErrorText is not null;

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>Re-mints on a different address: the code names one endpoint, so changing it needs a new code.</summary>
    partial void OnSelectedAddressChanged(string? value)
    {
        if (value is not null && !IsPaired)
        {
            MintOffer();
        }
    }

    public void Start()
    {
        Addresses.Clear();
        foreach (var address in LocalAddresses.Candidates())
        {
            Addresses.Add(address);
        }

        UpdateDeviceCount();

        // Checked before anything is minted: a code that the desktop already knows it will refuse should
        // never reach a screen, let alone a phone that has to scan it to find out.
        if (_manager.Registry.IsAtCapacity)
        {
            ErrorText = string.Format(
                CultureInfo.CurrentCulture, Resources.Devices_AtLimit, DeviceRegistry.MaxDevices);
            return;
        }

        if (Addresses.Count == 0)
        {
            ErrorText = Resources.Pairing_NoNetwork;
            return;
        }

        // Assigning this mints the first code through OnSelectedAddressChanged.
        SelectedAddress = Addresses[0];
        _timer.Start();
    }

    /// <summary>
    /// Leaving takes the code with it. An unclaimed offer outliving the dialog is a live code with nothing on
    /// screen to say so — and a device claiming it afterwards pairs into a list that has already been read.
    /// </summary>
    public void Stop()
    {
        _timer.Stop();

        if (!IsPaired && _offer is not null)
        {
            _manager.RunningHost?.WithdrawPairing(_offer.Thumbprint);
            _offer = null;
        }
    }

    /// <summary>One second of the dialog's life: the countdown, the renewal, and the claim check.</summary>
    public void Tick()
    {
        if (IsPaired || _offer is null)
        {
            return;
        }

        // The claim is the device's doing, so the only way to see it is to look.
        var device = _manager.Registry.Find(_offer.Thumbprint);
        if (device is not null)
        {
            IsPaired = true;
            PairedDeviceName = device.Name;
            UpdateDeviceCount();
            _timer.Stop();
            return;
        }

        var remaining = _offer.ExpiresAtUtc - _clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            // Expired unclaimed: silently replace it, so a user who looked away still has a working code.
            MintOffer();
            return;
        }

        ExpiryText = string.Format(
            CultureInfo.CurrentCulture,
            Resources.Pairing_ExpiresIn,
            remaining.Minutes,
            remaining.Seconds);
    }

    [RelayCommand]
    private void PairAnother()
    {
        IsPaired = false;
        PairedDeviceName = null;

        // The device just paired may have been the last slot.
        if (_manager.Registry.IsAtCapacity)
        {
            ErrorText = string.Format(
                CultureInfo.CurrentCulture, Resources.Devices_AtLimit, DeviceRegistry.MaxDevices);
            return;
        }

        MintOffer();
        _timer.Start();
    }

    [RelayCommand]
    private void Close() => CloseDialog?.Invoke(true);

    public void Dispose() => Stop();

    private void MintOffer()
    {
        var host = _manager.RunningHost;
        if (host is null || SelectedAddress is null)
        {
            ErrorText = Resources.Pairing_NotRunning;
            return;
        }

        try
        {
            // The code being replaced leaves the screen, so it leaves circulation with it — a renewal or a
            // change of address must not quietly leave a second claimable code behind.
            if (_offer is not null)
            {
                host.WithdrawPairing(_offer.Thumbprint);
            }

            _offer = host.BeginPairing(SelectedAddress);
            QrPng = PairingQrRenderer.ToPng(_offer.Payload.ToQrString());
            ErrorText = null;
            Tick();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Pairing code could not be created");
            ErrorText = Resources.Pairing_NotRunning;
        }
    }

    private void UpdateDeviceCount()
        => DeviceCountText = string.Format(
            CultureInfo.CurrentCulture,
            Resources.Devices_CountOfMax,
            _manager.Registry.Count,
            DeviceRegistry.MaxDevices);
}
