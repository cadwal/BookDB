using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
using BookDB.Models;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.ViewModels;

public partial class CollectionSelectorViewModel : ObservableRecipient
{
    [ObservableProperty]
    private bool _isDropdownOpen;

    [ObservableProperty]
    private string _selectionSummary = string.Empty;

    public ObservableCollection<CollectionItemViewModel> CollectionItems { get; } = [];

    private IReadOnlyList<CollectionItemViewModel> _lastValidSelection = [];

    public CollectionSelectorViewModel(IMessenger messenger)
        : base(messenger)
    {
        IsActive = true;
    }

    public void Initialize(IReadOnlyList<Collection> collections, IReadOnlySet<int> selectedIds)
    {
        CollectionItems.Clear();

        foreach (var collection in collections)
            AddItem(collection.CollectionId, collection.Name, selectedIds.Contains(collection.CollectionId));

        // Uncategorized: a filter-only pseudo-entry for books with no collection. It is never a real
        // collection (so it never appears as a move-to target), and books with no collection are shown only
        // when it is selected — keeping orphaned books findable and cleanable without leaking into every view.
        AddItem(CollectionFilter.Uncategorized, Resources.CollectionSelector_Uncategorized,
            selectedIds.Contains(CollectionFilter.Uncategorized));

        _lastValidSelection = CollectionItems.Where(c => c.IsSelected).ToList();
        UpdateSummary();

        var initialIds = new System.Collections.Generic.HashSet<int>(
            _lastValidSelection.Select(c => c.Id));
        Messenger.Send(new CollectionSelectionChangedMessage(initialIds));
    }

    private void AddItem(int id, string name, bool isSelected)
    {
        var item = new CollectionItemViewModel { Id = id, Name = name, IsSelected = isSelected };
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CollectionItemViewModel.IsSelected))
                OnItemSelectionChanged();
        };
        CollectionItems.Add(item);
    }

    private void OnItemSelectionChanged()
    {
        var selected = CollectionItems.Where(c => c.IsSelected).ToList();

        if (selected.Count == 0)
        {
            var itemsToRevert = _lastValidSelection;
            DeferRevert(() =>
            {
                foreach (var item in itemsToRevert)
                    item.IsSelected = true;
            });
            return;
        }

        _lastValidSelection = selected;
        UpdateSummary();

        var selectedIds = new System.Collections.Generic.HashSet<int>(selected.Select(c => c.Id));
        Messenger.Send(new CollectionSelectionChangedMessage(selectedIds));
    }

    protected virtual void DeferRevert(Action revert)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(revert, Avalonia.Threading.DispatcherPriority.Normal);
    }

    private void UpdateSummary()
    {
        if (CollectionItems.Count > 0 && CollectionItems.All(c => c.IsSelected))
            SelectionSummary = Resources.CollectionSelector_AllSelected;
        else
            SelectionSummary = string.Join(", ", CollectionItems.Where(c => c.IsSelected).Select(c => c.Name));
    }
}
