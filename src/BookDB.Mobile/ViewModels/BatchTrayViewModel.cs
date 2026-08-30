using System;
using System.Collections.ObjectModel;
using System.Globalization;
using BookDB.Mobile.Localization;
using BookDB.Mobile.Services;
using BookDB.Mobile.Staging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BookDB.Mobile.ViewModels;

/// <summary>
/// The books scanned but not yet sent. Nothing here depends on a connection except the sending itself: the
/// tray fills up offline and waits. Uploading is deliberate — there is no silent send on reconnect — so the
/// button says why it cannot be pressed rather than disappearing.
/// </summary>
public sealed partial class BatchTrayViewModel : PageViewModel, IDisposable
{
    private readonly IStagingStore _store;
    private readonly IConnectionService _connection;
    private readonly INavigator _navigator;
    private readonly Func<StagedItem, PageViewModel> _openEditor;
    private readonly Action _upload;

    public BatchTrayViewModel(
        IStagingStore store,
        IConnectionService connection,
        INavigator navigator,
        Func<StagedItem, PageViewModel> openEditor,
        Action upload)
    {
        _store = store;
        _connection = connection;
        _navigator = navigator;
        _openEditor = openEditor;
        _upload = upload;

        _store.Changed += OnStoreChanged;
        _connection.StatusChanged += OnConnectionChanged;
        Refill();
    }

    /// <summary>The shell disposes a page it navigates away from, which is what unhooks these.</summary>
    public void Dispose()
    {
        _store.Changed -= OnStoreChanged;
        _connection.StatusChanged -= OnConnectionChanged;
    }

    public override string Title => Resources.Batch_Title;

    public ObservableCollection<BatchItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    public string CountLine => string.Format(CultureInfo.CurrentCulture, Resources.Batch_CountLine, Items.Count);

    public string UploadLabel => string.Format(CultureInfo.CurrentCulture, Resources.Batch_Upload, Items.Count);

    public bool IsOffline => _connection.Status != ConnectionStatus.Connected;

    public bool IsRevoked => _connection.Status == ConnectionStatus.Revoked;

    /// <summary>Shown instead of leaving a disabled button unexplained.</summary>
    public bool ShowsOfflineReason => IsOffline && !IsRevoked && !IsEmpty;

    /// <summary>The same disabled button, a different reason: waiting for the network will not fix this one.</summary>
    public bool ShowsRevokedReason => IsRevoked && !IsEmpty;

    [RelayCommand(CanExecute = nameof(CanUpload))]
    private void Upload() => _upload();

    private bool CanUpload() => !IsEmpty && !IsOffline;

    private void OnStoreChanged(object? sender, EventArgs e) => Refill();

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(IsRevoked));
        OnPropertyChanged(nameof(ShowsOfflineReason));
        OnPropertyChanged(nameof(ShowsRevokedReason));
        UploadCommand.NotifyCanExecuteChanged();
    }

    private void Refill()
    {
        Items.Clear();
        foreach (var item in _store.Items)
            Items.Add(new BatchItemViewModel(item, Edit, Remove));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountLine));
        OnPropertyChanged(nameof(UploadLabel));
        OnPropertyChanged(nameof(ShowsOfflineReason));
        OnPropertyChanged(nameof(ShowsRevokedReason));
        UploadCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Re-opens the book in the builder, which is where a photo is re-taken, added or dropped.</summary>
    private void Edit(StagedItem item) => _navigator.NavigateTo(_openEditor(item));

    private void Remove(StagedItem item) => _store.Remove(item.ClientItemId);
}
