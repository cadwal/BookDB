using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Data.Interfaces;
using BookDB.Desktop.Helpers;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Base VM for all lookup tabs (Location, Owner, Language, Publisher, Series).
/// Publisher/Series/Location/Owner/Language extend this with merge support by overriding
/// SupportsMerge and PerformMergeAsync. Person has its own VM because of display/sort name pairing.
/// </summary>
public partial class LookupTabViewModel : ObservableObject
{
    protected readonly ILookupManagementService Service;
    protected readonly ILookupService LookupService;
    protected readonly IWindowService WindowService;
    protected readonly string TableName; // "Publisher" | "Series" | "Location" | "Owner" | "Language"
    private readonly IMessenger _messenger;

    public ObservableCollection<LookupEntryRow> Entries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredEntries))]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private LookupEntryRow? _selectedEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _editName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private int _usedByCount;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private int _addFocusSequence;

    public bool HasSelection => SelectedEntry is not null;

    public virtual bool SupportsMerge => false;

    private AsyncRelayCommand? _mergeIntoCommandBacking;

    [ObservableProperty]
    private bool _isMerging;

    /// <summary>
    /// Non-null for any tab where SupportsMerge = true; null otherwise (hides the Merge button).
    /// </summary>
    public virtual System.Windows.Input.ICommand? MergeIntoCommand =>
        SupportsMerge
            ? _mergeIntoCommandBacking ??= new AsyncRelayCommand(MergeIntoAsync, CanMergeInto)
            : null;

    private bool CanMergeInto() => !IsMerging && SelectedEntry is { Id: > 0 };

    partial void OnIsMergingChanged(bool value) => _mergeIntoCommandBacking?.NotifyCanExecuteChanged();

    public IEnumerable<LookupEntryRow> FilteredEntries =>
        string.IsNullOrWhiteSpace(FilterText)
            ? Entries
            : Entries.Where(e => e.Name?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true);

    public LookupTabViewModel(
        ILookupManagementService service,
        ILookupService lookupService,
        IWindowService windowService,
        string tableName,
        IMessenger messenger)
    {
        Service = service;
        LookupService = lookupService;
        WindowService = windowService;
        TableName = tableName;
        _messenger = messenger;
    }

    // Set by ManageLookupsViewModel after construction (the tabs are not built through DI). When a lookup write
    // fails on a dropped remote connection, route it to the shared status-bar indicator instead of reporting it
    // as a generic save/delete/merge error.
    public IConnectionHealthMonitor? ConnectionMonitor { get; set; }
    public IConnectionFailureClassifier? ConnectionClassifier { get; set; }

    // Returns true when the failure was a connection loss (already reported to the monitor), so the caller can
    // show the connection-lost message rather than its operation-specific error. The dependencies are
    // property-injected, so this no-ops until ManageLookupsViewModel has wired them.
    private bool ReportIfConnectionLoss(Exception ex) =>
        ConnectionMonitor is not null && ConnectionClassifier is not null
        && ConnectionMonitor.ReportIfConnectionLoss(ConnectionClassifier, ex);

    public virtual async Task LoadAsync()
    {
        Entries.Clear();
        var rows = await LoadRowsAsync().ConfigureAwait(true);
        foreach (var r in SortRows(rows))
            Entries.Add(r);
        OnPropertyChanged(nameof(FilteredEntries));
    }

    /// <summary>
    /// Display order for the entry list. Default is case-insensitive by name; tabs with an
    /// explicit order (e.g. Collections) override this to preserve their stored order.
    /// </summary>
    protected virtual IEnumerable<LookupEntryRow> SortRows(IEnumerable<LookupEntryRow> rows) =>
        rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-subclass override — Location/Owner/Language call GetAllAsync on their entity.</summary>
    protected virtual Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync() =>
        throw new NotImplementedException("Override in subclass");

    protected virtual Task<int> GetUsageCountAsync(int id) =>
        throw new NotImplementedException("Override in subclass");

    protected virtual Task<int> AddEntryAsync(string name) =>
        throw new NotImplementedException("Override in subclass");

    protected virtual Task RenameEntryAsync(int id, string name) =>
        throw new NotImplementedException("Override in subclass");

    protected virtual Task DeleteEntryAsync(int id) =>
        throw new NotImplementedException("Override in subclass");

    partial void OnSelectedEntryChanged(LookupEntryRow? value)
    {
        StatusMessage = null;
        if (value is null)
        {
            EditName = string.Empty;
            UsedByCount = 0;
        }
        else
        {
            EditName = value.Name;
            _ = LoadUsageCountAsync(value);
        }
        OnSelectedEntryUpdated();
    }

    /// <summary>Notifies the merge command when selection changes.</summary>
    protected virtual void OnSelectedEntryUpdated() =>
        _mergeIntoCommandBacking?.NotifyCanExecuteChanged();

    private async Task LoadUsageCountAsync(LookupEntryRow entry)
    {
        try
        {
            UsedByCount = entry.Id > 0 ? await GetUsageCountAsync(entry.Id) : 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LookupTabViewModel({Table}): usage count failed for {Id}", TableName, entry.Id);
            UsedByCount = 0;
        }
    }

    [RelayCommand]
    private void Add()
    {
        var transient = new LookupEntryRow(0, string.Empty);
        Entries.Add(transient);
        SelectedEntry = transient;
        EditName = string.Empty;
        AddFocusSequence++;
    }

    private bool CanSave() => SelectedEntry is not null && !string.IsNullOrWhiteSpace(EditName);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (SelectedEntry is null) return;
        var name = EditName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = Resources.ManageLookups_ErrorEmptyName;
            return;
        }
        try
        {
            if (SelectedEntry.Id == 0)
            {
                var newId = await AddEntryAsync(name).ConfigureAwait(true);
                await LoadAsync();
                SelectedEntry = Entries.FirstOrDefault(e => e.Id == newId);
            }
            else
            {
                await RenameEntryAsync(SelectedEntry.Id, name).ConfigureAwait(true);
                SelectedEntry.Name = name;
                // Re-apply the tab's display order after the rename.
                var snapshot = SortRows(Entries).ToList();
                Entries.Clear();
                foreach (var r in snapshot) Entries.Add(r);
                OnPropertyChanged(nameof(FilteredEntries));
            }
            StatusMessage = null;
            _messenger.Send(new LookupsChangedMessage());
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = string.Format(Resources.ManageLookups_ErrorDuplicateName, TableName);
            Log.Error(ex, "LookupTabViewModel({Table}): save failed", TableName);
        }
        catch (Exception ex)
        {
            StatusMessage = ReportIfConnectionLoss(ex)
                ? Resources.StatusBar_Connection_Lost
                : Resources.ManageLookups_ErrorSaveFailed;
            Log.Error(ex, "LookupTabViewModel({Table}): save failed unexpectedly", TableName);
        }
    }

    private bool CanCancel() => SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (SelectedEntry is null) return;
        if (SelectedEntry.Id == 0)
        {
            // discard transient row
            Entries.Remove(SelectedEntry);
            SelectedEntry = null;
            EditName = string.Empty;
        }
        else
        {
            EditName = SelectedEntry.Name;
        }
        StatusMessage = null;
    }

    private bool CanDelete() => SelectedEntry is { Id: > 0 } && UsedByCount == 0;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedEntry is null || SelectedEntry.Id == 0) return;
        var confirmMessage = string.Format(Resources.ManageLookups_DeleteConfirm, SelectedEntry.Name);
        var confirmed = await WindowService.ShowDeleteConfirmationAsync(confirmMessage);
        if (confirmed != true) return;
        try
        {
            await DeleteEntryAsync(SelectedEntry.Id).ConfigureAwait(true);
            Entries.Remove(SelectedEntry);
            SelectedEntry = null;
            OnPropertyChanged(nameof(FilteredEntries));
        }
        catch (Exception ex)
        {
            StatusMessage = ReportIfConnectionLoss(ex)
                ? Resources.StatusBar_Connection_Lost
                : Resources.ManageLookups_ErrorDeleteFailed;
            Log.Error(ex, "LookupTabViewModel({Table}): delete failed", TableName);
        }
    }

    private async Task MergeIntoAsync()
    {
        if (SelectedEntry is null || SelectedEntry.Id == 0) return;
        var source = SelectedEntry;
        try
        {
            IsMerging = true;
            var targetId = await WindowService.ShowMergeTargetPickerAsync(
                source.Name,
                source.Id,
                Entries.ToList());
            if (targetId is null) return;
            await PerformMergeAsync(source.Id, targetId.Value);
            await LoadAsync();
            SelectedEntry = Entries.FirstOrDefault(e => e.Id == targetId.Value);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            StatusMessage = ReportIfConnectionLoss(ex)
                ? Resources.StatusBar_Connection_Lost
                : Resources.ManageLookups_ErrorMergeFailed;
            Log.Error(ex, "LookupTabViewModel({Table}): merge failed", TableName);
        }
        finally
        {
            IsMerging = false;
        }
    }

    /// <summary>
    /// Override in merge-capable subclasses to call the appropriate service method.
    /// Any subclass that sets <see cref="SupportsMerge"/> to <c>true</c> MUST override this method.
    /// </summary>
    protected virtual Task PerformMergeAsync(int sourceId, int targetId) =>
        throw new NotImplementedException($"{GetType().Name} overrides SupportsMerge but did not override PerformMergeAsync.");
}
