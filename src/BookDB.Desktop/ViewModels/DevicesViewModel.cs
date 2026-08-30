using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using BookDB.Companion.Host;
using BookDB.Help;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Desktop.ViewModels;

/// <summary>One paired device as the list shows it. Timestamps are formatted for display, not sorting.</summary>
public sealed record PairedDeviceRow(string Name, string Thumbprint, string PairedAt, string LastUsed);

/// <summary>
/// The Devices section of Maintenance: which phones and tablets are paired with this computer, removing
/// one, and getting a pairing code for a new one.
/// </summary>
public sealed partial class DevicesViewModel : ObservableObject
{
    private readonly CompanionHostManager _manager;
    private readonly IWindowService _windowService;

    public DevicesViewModel(CompanionHostManager manager, IWindowService windowService)
    {
        _manager = manager;
        _windowService = windowService;
    }

    public ObservableCollection<PairedDeviceRow> Devices { get; } = [];

    [ObservableProperty]
    private string _countText = string.Empty;

    /// <summary>With the companion off there is nothing to pair against, so the tab says where to turn it on.</summary>
    public bool IsCompanionOff => !_manager.Current.Enabled;

    public bool HasNoDevices => Devices.Count == 0;

    /// <summary>
    /// True with every slot taken. A code minted now is one the desktop already knows it will refuse, so the
    /// refusal belongs here — where the list the user has to remove from is on screen — rather than on the
    /// phone after it has scanned, named itself and asked.
    /// </summary>
    public bool IsAtDeviceLimit => _manager.Registry.IsAtCapacity;

    public bool CanPair => !IsCompanionOff && !IsAtDeviceLimit;

    public string AtLimitText => string.Format(
        CultureInfo.CurrentCulture, Resources.Devices_AtLimit, DeviceRegistry.MaxDevices);

    public void Load()
    {
        Devices.Clear();
        foreach (var device in _manager.Registry.List())
        {
            Devices.Add(new PairedDeviceRow(
                device.Name,
                device.Thumbprint,
                device.PairedAtUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
                Describe(device.LastUsedUtc)));
        }

        CountText = string.Format(
            CultureInfo.CurrentCulture, Resources.Devices_CountOfMax, Devices.Count, DeviceRegistry.MaxDevices);
        OnPropertyChanged(nameof(IsCompanionOff));
        OnPropertyChanged(nameof(HasNoDevices));
        OnPropertyChanged(nameof(IsAtDeviceLimit));
        OnPropertyChanged(nameof(CanPair));
    }

    [RelayCommand]
    private void OpenCompanionHelp() => _windowService.OpenHelpWindow(HelpTab.Companion);

    [RelayCommand]
    private async Task ShowPairingCodeAsync()
    {
        await _windowService.ShowPairingDialogAsync();
        // A device may have been paired while the dialog was open.
        Load();
        _manager.NotifyDevicesChanged();
    }

    [RelayCommand]
    private async Task RemoveAsync(PairedDeviceRow? device)
    {
        if (device is null)
        {
            return;
        }

        // Revoking is not undoable — the device has to be paired again from scratch — so it is confirmed.
        // With its own words: the shared dialog otherwise offers to keep a book nobody mentioned.
        var confirmed = await _windowService.ShowDeleteConfirmationAsync(
            string.Format(CultureInfo.CurrentCulture, Resources.Devices_RemoveConfirm, device.Name),
            Resources.Devices_Remove,
            Resources.Common_Cancel);
        if (confirmed == true)
        {
            _manager.Registry.Revoke(device.Thumbprint);
            Load();
            _manager.NotifyDevicesChanged();
        }
    }

    /// <summary>A coarse "last seen" — the exact minute is never what the user is asking.</summary>
    private static string Describe(DateTimeOffset lastUsedUtc)
    {
        var age = DateTimeOffset.UtcNow - lastUsedUtc;

        return age switch
        {
            _ when age < TimeSpan.FromMinutes(2) => Resources.Devices_LastUsed_JustNow,
            _ when age < TimeSpan.FromHours(1) =>
                string.Format(CultureInfo.CurrentCulture, Resources.Devices_LastUsed_MinutesAgo, (int)age.TotalMinutes),
            _ when age < TimeSpan.FromDays(1) =>
                string.Format(CultureInfo.CurrentCulture, Resources.Devices_LastUsed_HoursAgo, (int)age.TotalHours),
            _ => lastUsedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture),
        };
    }
}
