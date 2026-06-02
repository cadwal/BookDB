using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BookDB.Desktop.Localization;
using BookDB.Desktop.Messages;
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
        {
            var item = new CollectionItemViewModel
            {
                Id = collection.CollectionId,
                Name = collection.Name,
                IsSelected = selectedIds.Contains(collection.CollectionId)
            };

            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CollectionItemViewModel.IsSelected))
                    OnItemSelectionChanged();
            };

            CollectionItems.Add(item);
        }

        _lastValidSelection = CollectionItems.Where(c => c.IsSelected).ToList();
        UpdateSummary();

        var initialIds = new System.Collections.Generic.HashSet<int>(
            _lastValidSelection.Select(c => c.Id));
        Messenger.Send(new CollectionSelectionChangedMessage(initialIds));
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
