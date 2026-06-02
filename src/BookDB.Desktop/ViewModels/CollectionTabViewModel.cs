using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Desktop.Services;
using BookDB.Logic.Services;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace BookDB.Desktop.ViewModels;

/// <summary>
/// Manage-Lookups tab for collections. Adds reordering on top of the shared add/rename/delete
/// behavior, and preserves the stored SortOrder instead of sorting alphabetically. Collection
/// mutations broadcast <see cref="CollectionsChangedMessage"/> so the main-window selector refreshes.
/// </summary>
public partial class CollectionTabViewModel : LookupTabViewModel
{
    private const string DefaultCollectionSettingKey = "DefaultCollectionId";

    private readonly IMessenger _messenger;
    private readonly ISettingsService _settings;

    public CollectionTabViewModel(
        ILookupManagementService service,
        ILookupService lookupService,
        IWindowService windowService,
        IMessenger messenger,
        ISettingsService settings)
        : base(service, lookupService, windowService, "Collection", messenger)
    {
        _messenger = messenger;
        _settings = settings;
    }

    public override bool SupportsMerge => true;

    protected override async Task<IReadOnlyList<LookupEntryRow>> LoadRowsAsync()
    {
        var items = await LookupService.GetCollectionsAsync();
        return items.Select(c => new LookupEntryRow(c.CollectionId, c.Name)).ToList();
    }

    // Preserve the stored order (GetCollectionsAsync already orders by SortOrder) — do not sort by name.
    protected override IEnumerable<LookupEntryRow> SortRows(IEnumerable<LookupEntryRow> rows) => rows;

    protected override Task<int> GetUsageCountAsync(int id) => Service.GetCollectionBookCountAsync(id);

    protected override async Task<int> AddEntryAsync(string name)
    {
        var id = await Service.AddCollectionAsync(name);
        _messenger.Send(new CollectionsChangedMessage());
        return id;
    }

    protected override async Task RenameEntryAsync(int id, string name)
    {
        await Service.RenameCollectionAsync(id, name);
        _messenger.Send(new CollectionsChangedMessage());
    }

    protected override async Task DeleteEntryAsync(int id)
    {
        await Service.DeleteCollectionAsync(id);
        // If the deleted collection was the default, fall back to the first remaining one.
        await ReassignDefaultIfNeededAsync(removedId: id, preferredNewId: null);
        _messenger.Send(new CollectionsChangedMessage());
    }

    protected override async Task PerformMergeAsync(int sourceId, int targetId)
    {
        await Service.MergeCollectionsAsync(sourceId, targetId);
        // If the merged-away collection was the default, hand the default to the merge target.
        await ReassignDefaultIfNeededAsync(removedId: sourceId, preferredNewId: targetId);
        _messenger.Send(new CollectionsChangedMessage());
    }

    /// <summary>
    /// Keeps the "default collection" setting valid after a collection is removed (deleted or
    /// merged away). When <paramref name="preferredNewId"/> is null, falls back to the first
    /// remaining collection by sort order; if none remain, the setting is cleared.
    /// </summary>
    private async Task ReassignDefaultIfNeededAsync(int removedId, int? preferredNewId)
    {
        var current = await _settings.GetAsync(DefaultCollectionSettingKey);
        if (!int.TryParse(current, out var currentId) || currentId != removedId)
            return;

        var newId = preferredNewId;
        if (newId is null)
        {
            var remaining = await LookupService.GetCollectionsAsync();
            newId = remaining.FirstOrDefault()?.CollectionId;
        }
        await _settings.SetAsync(DefaultCollectionSettingKey, newId?.ToString());
    }

    protected override void OnSelectedEntryUpdated()
    {
        base.OnSelectedEntryUpdated();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveUp()
    {
        var sel = SelectedEntry;
        return sel is { Id: > 0 } && Entries.IndexOf(sel) > 0;
    }

    private bool CanMoveDown()
    {
        var sel = SelectedEntry;
        if (sel is not { Id: > 0 }) return false;
        var i = Entries.IndexOf(sel);
        return i >= 0 && i < Entries.Count - 1;
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private Task MoveUpAsync() => MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private Task MoveDownAsync() => MoveAsync(+1);

    private async Task MoveAsync(int delta)
    {
        var sel = SelectedEntry;
        if (sel is null) return;
        var idx = Entries.IndexOf(sel);
        var newIdx = idx + delta;
        if (idx < 0 || newIdx < 0 || newIdx >= Entries.Count) return;

        Entries.Move(idx, newIdx);
        OnPropertyChanged(nameof(FilteredEntries));
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();

        try
        {
            await Service.ReorderCollectionsAsync(Entries.Select(e => e.Id).ToList());
            _messenger.Send(new CollectionsChangedMessage());
        }
        catch (Exception ex)
        {
            StatusMessage = Resources.ManageLookups_ErrorSaveFailed;
            Log.Error(ex, "CollectionTabViewModel: reorder failed");
        }
    }
}
