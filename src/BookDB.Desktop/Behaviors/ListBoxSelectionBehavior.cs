using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;
using BookDB.Desktop.Messages;
using BookDB.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Messaging;

namespace BookDB.Desktop.Behaviors;

/// <summary>
/// Syncs the cover-grid ListBox multi-selection into the shared <see cref="BookListViewModel"/> and
/// sends a <see cref="BookSelectedMessage"/> on the single-selection — the grid's mirror of
/// <see cref="DataGridSelectionBehavior"/> so both views feed the same SelectedBooks/toolbar state.
/// </summary>
public class ListBoxSelectionBehavior : Behavior<ListBox>
{
    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not null)
            AssociatedObject.SelectionChanged += OnSelectionChanged;
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject is not null)
            AssociatedObject.SelectionChanged -= OnSelectionChanged;
        base.OnDetaching();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AssociatedObject?.DataContext is not BookListViewModel vm) return;

        vm.UpdateSelectedBooks(AssociatedObject.SelectedItems as IList ?? new List<object>());

        var selected = AssociatedObject.SelectedItem as BookRowViewModel;
        WeakReferenceMessenger.Default.Send(new BookSelectedMessage(selected?.BookId));
    }
}
