using System;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Staging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;

namespace BookDB.Mobile.ViewModels;

/// <summary>The home hub: the two primary jobs (Scan, Browse), a way into Settings, and — only while there is
/// something waiting — the way into the batch.</summary>
public sealed partial class HubViewModel : PageViewModel, IDisposable
{
    private readonly INavigator _navigator;
    private readonly IStagingStore _staging;

    public HubViewModel(INavigator navigator, IStagingStore staging)
    {
        _navigator = navigator;
        _staging = staging;
        _staging.Changed += OnStagingChanged;
    }

    public void Dispose() => _staging.Changed -= OnStagingChanged;

    public override string Title => Resources.App_Title;

    public int BatchCount => _staging.Items.Count;

    /// <summary>Nothing staged, nothing to offer — the batch row is simply absent until there is one.</summary>
    public bool HasBatch => BatchCount > 0;

    public string BatchLine => string.Format(CultureInfo.CurrentCulture, Resources.Hub_BatchItems, BatchCount);

    [RelayCommand]
    private void Scan() => _navigator.NavigateTo(HubDestination.Scan);

    [RelayCommand]
    private void Browse() => _navigator.NavigateTo(HubDestination.Browse);

    [RelayCommand]
    private void OpenBatch() => _navigator.NavigateTo(HubDestination.Batch);

    [RelayCommand]
    private void OpenSettings() => _navigator.NavigateTo(HubDestination.Settings);

    private void OnStagingChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(BatchCount));
        OnPropertyChanged(nameof(HasBatch));
        OnPropertyChanged(nameof(BatchLine));
    }
}
