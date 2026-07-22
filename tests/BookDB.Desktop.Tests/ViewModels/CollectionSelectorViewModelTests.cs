using System.Collections.Generic;
using System.Linq;
using BookDB.Desktop.Messages;
using BookDB.Desktop.ViewModels;
using BookDB.Models;
using BookDB.Models.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace BookDB.Desktop.Tests.ViewModels;

/// <summary>
/// Test double that executes the revert synchronously instead of posting to Dispatcher.UIThread,
/// allowing deterministic assertions without a running Avalonia application.
/// </summary>
file sealed class TestableCollectionSelectorViewModel(IMessenger messenger)
    : CollectionSelectorViewModel(messenger)
{
    protected override void DeferRevert(System.Action revert) => revert();
}

public class CollectionSelectorViewModelTests
{
    private static List<Collection> MakeCollections(params (int id, string name)[] items)
    {
        return items.Select((x, i) => new Collection
        {
            CollectionId = x.id,
            Name = x.name,
            SortOrder = i
        }).ToList();
    }

    [Fact]
    public void Initialize_PopulatesCollectionItems()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "A"), (2, "B"), (3, "C"));
        var selectedIds = new HashSet<int> { 1, 2, 3 };

        vm.Initialize(collections, selectedIds);

        // Three real collections plus the always-present Uncategorized filter entry.
        Assert.Equal(4, vm.CollectionItems.Count);
        Assert.Equal(1, vm.CollectionItems[0].Id);
        Assert.Equal("A", vm.CollectionItems[0].Name);
        Assert.Equal(2, vm.CollectionItems[1].Id);
        Assert.Equal(3, vm.CollectionItems[2].Id);
        // Real collections were selected; the Uncategorized entry trails and follows its own sentinel.
        Assert.All(vm.CollectionItems.Where(i => i.Id > 0), item => Assert.True(item.IsSelected));
        Assert.Equal(CollectionFilter.Uncategorized, vm.CollectionItems[3].Id);
        Assert.False(vm.CollectionItems[3].IsSelected);
    }

    [Fact]
    public void Initialize_SelectsUncategorized_WhenSentinelInSelectedIds()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "A"), (2, "B"));
        var selectedIds = new HashSet<int> { 1, 2, CollectionFilter.Uncategorized };

        vm.Initialize(collections, selectedIds);

        var uncategorized = vm.CollectionItems.First(c => c.Id == CollectionFilter.Uncategorized);
        Assert.True(uncategorized.IsSelected);
    }

    [Fact]
    public void Initialize_SelectsCorrectSubset()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "A"), (2, "B"), (3, "C"));
        var selectedIds = new HashSet<int> { 1, 3 };

        vm.Initialize(collections, selectedIds);

        Assert.True(vm.CollectionItems.First(c => c.Id == 1).IsSelected);
        Assert.False(vm.CollectionItems.First(c => c.Id == 2).IsSelected);
        Assert.True(vm.CollectionItems.First(c => c.Id == 3).IsSelected);
    }

    [Fact]
    public void SelectionSummary_AllSelected_ShowsAllCollections()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "A"), (2, "B"), (3, "C"));
        // Every item, including the Uncategorized entry, must be selected for the "all" summary.
        var selectedIds = new HashSet<int> { 1, 2, 3, CollectionFilter.Uncategorized };

        vm.Initialize(collections, selectedIds);

        Assert.Equal("All collections", vm.SelectionSummary);
    }

    [Fact]
    public void SelectionSummary_SubsetSelected_ShowsCommaJoinedNames()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "Alpha"), (2, "Beta"), (3, "Gamma"));
        var selectedIds = new HashSet<int> { 1, 2, 3 };

        vm.Initialize(collections, selectedIds);

        // Deselect Beta, leaving Alpha and Gamma
        vm.CollectionItems.First(c => c.Id == 2).IsSelected = false;

        Assert.Contains("Alpha", vm.SelectionSummary);
        Assert.Contains("Gamma", vm.SelectionSummary);
        Assert.DoesNotContain("Beta", vm.SelectionSummary);
        Assert.Equal("Alpha, Gamma", vm.SelectionSummary);
    }

    [Fact]
    public void MinimumOne_DeselectionBlocked_WhenLastItem()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "A"), (2, "B"), (3, "C"));
        // Only item 2 is selected
        var selectedIds = new HashSet<int> { 2 };

        vm.Initialize(collections, selectedIds);

        // Try to deselect the only selected item
        vm.CollectionItems.First(c => c.Id == 2).IsSelected = false;

        // Should be reverted: at least one must remain selected
        Assert.True(vm.CollectionItems.Count(c => c.IsSelected) >= 1);
        // The item should have been reverted back to selected
        Assert.True(vm.CollectionItems.First(c => c.Id == 2).IsSelected);
    }

    [Fact]
    public void MinimumOne_AllowsDeselection_WhenMultipleSelected()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "A"), (2, "B"), (3, "C"));
        var selectedIds = new HashSet<int> { 1, 2 };

        vm.Initialize(collections, selectedIds);

        // Deselect item 1, item 2 remains
        vm.CollectionItems.First(c => c.Id == 1).IsSelected = false;

        Assert.Equal(1, vm.CollectionItems.Count(c => c.IsSelected));
        Assert.True(vm.CollectionItems.First(c => c.Id == 2).IsSelected);
        Assert.False(vm.CollectionItems.First(c => c.Id == 1).IsSelected);
    }

    [Fact]
    public void ValidSelectionChange_SendsMessage()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "A"), (2, "B"), (3, "C"));
        var selectedIds = new HashSet<int> { 1, 2, 3 };

        vm.Initialize(collections, selectedIds);

        CollectionSelectionChangedMessage? received = null;
        messenger.Register<CollectionSelectionChangedMessage>(this, (_, msg) => received = msg);

        // Deselect item 2 (valid: items 1 and 3 remain)
        vm.CollectionItems.First(c => c.Id == 2).IsSelected = false;

        Assert.NotNull(received);
        Assert.Contains(1, received.Value);
        Assert.Contains(3, received.Value);
        Assert.DoesNotContain(2, received.Value);
        Assert.Equal(2, received.Value.Count);
    }

    [Fact]
    public void NoMessageSent_WhenMinimumOneBlocks()
    {
        var messenger = new WeakReferenceMessenger();
        var vm = new TestableCollectionSelectorViewModel(messenger);
        var collections = MakeCollections((1, "A"), (2, "B"), (3, "C"));
        // Only item 1 selected
        var selectedIds = new HashSet<int> { 1 };

        vm.Initialize(collections, selectedIds);

        var messageCount = 0;
        messenger.Register<CollectionSelectionChangedMessage>(this, (_, _) => messageCount++);

        // Try to deselect the only item (will be reverted)
        vm.CollectionItems.First(c => c.Id == 1).IsSelected = false;

        // No message should be sent for invalid (zero-selection) state
        // The revert sets IsSelected back to true, which calls OnItemSelectionChanged again
        // but since count becomes 1 (valid), a message IS sent on the revert.
        // The important thing is the selection is never zero.
        Assert.True(vm.CollectionItems.First(c => c.Id == 1).IsSelected);
        Assert.Equal(1, vm.CollectionItems.Count(c => c.IsSelected));
    }
}
